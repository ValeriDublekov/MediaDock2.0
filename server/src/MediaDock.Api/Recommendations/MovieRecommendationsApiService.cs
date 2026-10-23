using System.Data;
using MediaDock.Api.Common;
using MediaDock.Api.Middleware;
using MediaDock.Application.Recommendations;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.Api.Recommendations;

internal sealed class MovieRecommendationsApiService(MediaDockDbContext dbContext)
{
    public async Task<PageResponse<RecommendationResponse>> GetAsync(
        long userId,
        RecommendationsQuery query,
        CancellationToken cancellationToken)
    {
        if (query.RatingWeight is not decimal ratingWeight
            || query.VotesWeight is not decimal votesWeight
            || query.AwardsWeight is not decimal awardsWeight)
        {
            throw ValidationError("weights", "All three recommendation coefficients are required.");
        }

        var coefficients = new MovieRecommendationCoefficients(ratingWeight, votesWeight, awardsWeight);
        try
        {
            MovieRecommendationScorer.ValidateCoefficients(coefficients);
        }
        catch (ArgumentException exception)
        {
            throw ValidationError("weights", exception.Message);
        }

        if (query.YearFrom is { } yearFrom
            && query.YearTo is { } yearTo
            && yearFrom > yearTo)
        {
            throw ValidationError(nameof(query.YearTo), "YearTo must be greater than or equal to YearFrom.");
        }

        var page = query.Page ?? 1;
        var pageSize = query.PageSize ?? 25;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);

        var allCandidates = await GetEligibleCandidatesQuery(userId)
            .Select(title => new CandidateTitle(
                title.ImdbId,
                title.TitleText!.Trim(),
                title.Year,
                title.ImdbRating!.Value,
                title.ImdbVotes,
                title.NormalizedVoteWeight,
                title.PosterUrl,
                title.Genres))
            .ToListAsync(cancellationToken);

        var candidateYears = allCandidates
            .Where(candidate => candidate.Year.HasValue)
            .Select(candidate => candidate.Year!.Value)
            .ToArray();
        var yearBounds = candidateYears.Length == 0
            ? null
            : new YearBounds(candidateYears.Min(), candidateYears.Max());
        var selectedGenres = (query.Genres ?? [])
            .Where(genre => !string.IsNullOrWhiteSpace(genre))
            .Select(genre => genre.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = allCandidates
            .Where(candidate =>
                (query.YearFrom is null || (candidate.Year.HasValue && candidate.Year.Value >= query.YearFrom.Value))
                && (query.YearTo is null || (candidate.Year.HasValue && candidate.Year.Value <= query.YearTo.Value))
                && (selectedGenres.Count == 0 || candidate.Genres.Any(selectedGenres.Contains)))
            .ToList();

        if (candidates.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return CreatePage([], page, pageSize, 0, yearBounds);
        }

        var ratingRows = await dbContext.PersonalRatings
            .AsNoTracking()
            .Where(rating => rating.UserId == userId)
            .Select(rating => new UserRating(rating.ImdbId, rating.Rating))
            .ToListAsync(cancellationToken);
        var ratingsByImdbId = ratingRows.ToDictionary(rating => rating.ImdbId);
        var ratedOscarMovies = await dbContext.OscarNominations
            .AsNoTracking()
            .Where(nomination => nomination.OscarMovie != null
                && nomination.OscarMovie.ImdbId != null
                && nomination.OscarMovie.ImdbTitle != null
                && nomination.OscarMovie.ImdbTitle.MediaType == "movie"
                && dbContext.PersonalRatings.Any(rating =>
                    rating.UserId == userId && rating.ImdbId == nomination.OscarMovie.ImdbId))
            .Select(nomination => new RatedWinnerReference(
                nomination.OscarMovie!.ImdbId!,
                "oscar",
                nomination.CanonicalCategory))
            .ToListAsync(cancellationToken);
        var ratedGoldenGlobeMovies = await dbContext.GoldenGlobeNominations
            .AsNoTracking()
            .Where(nomination => nomination.GoldenGlobeTitle != null
                && nomination.GoldenGlobeTitle.NomineeType == "movie"
                && nomination.GoldenGlobeTitle.ImdbId != null
                && nomination.GoldenGlobeTitle.ImdbTitle != null
                && nomination.GoldenGlobeTitle.ImdbTitle.MediaType == "movie"
                && dbContext.PersonalRatings.Any(rating =>
                    rating.UserId == userId && rating.ImdbId == nomination.GoldenGlobeTitle.ImdbId))
            .Select(nomination => new RatedWinnerReference(
                nomination.GoldenGlobeTitle!.ImdbId!,
                "golden_globes",
                nomination.Award.CategoryKey))
            .ToListAsync(cancellationToken);
        var tasteProfile = MovieRecommendationScorer.BuildTasteProfile(
            ratingRows.Select(rating => rating.Rating).ToArray(),
            ratedOscarMovies.Concat(ratedGoldenGlobeMovies)
                .Select(awardMovie => new MovieRecommendationRatedAwardMovie(
                    awardMovie.ImdbId,
                    awardMovie.AwardSource,
                    awardMovie.CategoryKey,
                    ratingsByImdbId[awardMovie.ImdbId].Rating))
                .ToArray());

        var recommendationSettings = await dbContext.GeneralRecommendationSettings
            .AsNoTracking()
            .SingleAsync(settings => settings.Id == 1, cancellationToken);
        var voteInputs = await dbContext.ImdbTitles
            .AsNoTracking()
            .Where(title => title.MediaType == "movie")
            .Select(title => new MovieVoteNormalizationInput(title.ImdbId, title.Year, title.ImdbVotes))
            .ToListAsync(cancellationToken);
        var periodDataByImdbId = MovieVoteNormalizer.Normalize(voteInputs, recommendationSettings.MedianPeriodYears)
            .ToDictionary(result => result.ImdbId, StringComparer.Ordinal);
        var scoringSettings = new MovieRecommendationScoringSettings(
            recommendationSettings.BayesianDefaultC,
            recommendationSettings.BayesianMRatio,
            recommendationSettings.OscarWinPoints,
            recommendationSettings.OscarNomPoints,
            recommendationSettings.GgWinPoints,
            recommendationSettings.GgNomPoints);

        var candidateIds = candidates.Select(candidate => candidate.ImdbId).ToArray();
        var oscarEvents = await dbContext.OscarNominations
            .AsNoTracking()
            .Where(nomination => nomination.OscarMovie != null
                && nomination.OscarMovie.ImdbId != null
                && candidateIds.Contains(nomination.OscarMovie.ImdbId))
            .Select(nomination => new AwardEventReference(
                nomination.OscarMovie!.ImdbId!,
                "oscar",
                nomination.CanonicalCategory,
                nomination.Category,
                nomination.Ceremony,
                nomination.IsWinner))
            .ToListAsync(cancellationToken);
        var goldenGlobeEvents = await dbContext.GoldenGlobeNominations
            .AsNoTracking()
            .Where(nomination => nomination.GoldenGlobeTitle != null
                && nomination.GoldenGlobeTitle.NomineeType == "movie"
                && nomination.GoldenGlobeTitle.ImdbId != null
                && nomination.GoldenGlobeTitle.ImdbTitle != null
                && nomination.GoldenGlobeTitle.ImdbTitle.MediaType == "movie"
                && candidateIds.Contains(nomination.GoldenGlobeTitle.ImdbId))
            .Select(nomination => new AwardEventReference(
                nomination.GoldenGlobeTitle!.ImdbId!,
                "golden_globes",
                nomination.Award.CategoryKey,
                nomination.Award.Name,
                nomination.GoldenGlobeTitle.Year,
                nomination.Winner))
            .ToListAsync(cancellationToken);
        var awardEventsByImdbId = oscarEvents.Concat(goldenGlobeEvents)
            .GroupBy(awardEvent => awardEvent.ImdbId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<MovieRecommendationAwardEvent>)group
                    .Select(awardEvent => new MovieRecommendationAwardEvent(
                        awardEvent.AwardSource,
                        awardEvent.CategoryKey,
                        awardEvent.CategoryName,
                        awardEvent.EventId,
                        awardEvent.IsWinner))
                    .ToArray());
        var awardWeights = await dbContext.RecommendationAwardWeights
            .AsNoTracking()
            .Select(weight => new MovieRecommendationAwardWeight(
                weight.AwardSource,
                weight.CategoryKey,
                weight.WinPoints,
                weight.NominationPoints,
                weight.IsOverride))
            .ToListAsync(cancellationToken);

        var scoredCandidates = candidates
            .Select(candidate =>
            {
                var awardEvents = awardEventsByImdbId.GetValueOrDefault(candidate.ImdbId)
                    ?? Array.Empty<MovieRecommendationAwardEvent>();
                var periodMedianVotes = periodDataByImdbId.TryGetValue(candidate.ImdbId, out var periodData)
                    ? periodData.PeriodMedianVotes
                    : MovieRecommendationScorer.FallbackPeriodMedianVotes;
                var scoringCandidate = new MovieRecommendationCandidate(
                    candidate.ImdbId,
                    candidate.Title,
                    candidate.Year,
                    candidate.ImdbRating,
                    candidate.ImdbVotes,
                    candidate.NormalizedVoteWeight,
                    candidate.PosterUrl,
                    candidate.Genres,
                    awardEvents,
                    periodMedianVotes);
                var score = MovieRecommendationScorer.Score(
                    scoringCandidate,
                    scoringSettings,
                    tasteProfile,
                    awardWeights,
                    coefficients);
                return new RecommendationResponse(
                    candidate.ImdbId,
                    candidate.Title,
                    candidate.Year,
                    "movie",
                    candidate.ImdbRating,
                    candidate.ImdbVotes,
                    candidate.PosterUrl,
                    candidate.Genres,
                    score.RatingComponent,
                    score.PopularityComponent,
                    score.AwardComponent,
                    score.RatingWeight,
                    score.VotesWeight,
                    score.AwardsWeight,
                    score.FinalScore,
                    score.AwardEvents
                        .Select(awardEvent => new RecommendationAwardEventResponse(
                            awardEvent.AwardSource,
                            awardEvent.CategoryKey,
                            awardEvent.CategoryName,
                            awardEvent.AwardSource == "oscar" ? awardEvent.EventId : null,
                            awardEvent.AwardSource == "golden_globes" ? awardEvent.EventId : null,
                            awardEvent.Outcome,
                            awardEvent.BasePoints,
                            awardEvent.TasteMultiplier,
                            awardEvent.AdjustedPoints))
                        .ToArray());
            })
            .OrderByDescending(recommendation => recommendation.FinalScore)
            .ThenBy(recommendation => recommendation.ImdbId, StringComparer.Ordinal)
            .ToArray();

        await transaction.CommitAsync(cancellationToken);
        return CreatePage(
            scoredCandidates.Skip((page - 1) * pageSize).Take(pageSize).ToArray(),
            page,
            pageSize,
            scoredCandidates.Length,
            yearBounds);
    }

    public async Task<bool> MarkWatchedAsync(
        long userId,
        string imdbId,
        int rating,
        CancellationToken cancellationToken)
    {
        if (rating is < 1 or > 10)
        {
            throw ValidationError("rating", "Rating must be between 1 and 10.");
        }

        var existingRating = await dbContext.PersonalRatings
            .AsNoTracking()
            .Where(item => item.UserId == userId && item.ImdbId == imdbId)
            .Select(item => new { item.Rating, item.IsManuallyAdded })
            .SingleOrDefaultAsync(cancellationToken);
        if (existingRating is not null)
        {
            return existingRating.IsManuallyAdded && existingRating.Rating == rating;
        }

        var isEligible = await GetEligibleCandidatesQuery(userId)
            .AnyAsync(title => title.ImdbId == imdbId, cancellationToken);
        if (!isEligible)
        {
            return false;
        }

        dbContext.PersonalRatings.Add(new PersonalRating
        {
            UserId = userId,
            ImdbId = imdbId,
            Rating = rating,
            IsManuallyAdded = true,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<string>> GetGenresAsync(CancellationToken cancellationToken) =>
        await dbContext.ImdbTitles
            .AsNoTracking()
            .SelectMany(title => title.Genres)
            .Where(genre => genre != string.Empty)
            .Distinct()
            .OrderBy(genre => genre)
            .ToListAsync(cancellationToken);

    private IQueryable<ImdbTitle> GetEligibleCandidatesQuery(long userId) =>
        dbContext.ImdbTitles
            .AsNoTracking()
            .Where(title => title.MetadataStatus == "enriched"
                && title.MediaType == "movie"
                && title.TitleText != null
                && title.TitleText.Trim() != string.Empty
                && title.ImdbRating.HasValue
                && !dbContext.PersonalRatings.Any(rating =>
                    rating.UserId == userId && rating.ImdbId == title.ImdbId)
                && !dbContext.UserIgnoredMovies.Any(ignored =>
                    ignored.UserId == userId && ignored.ImdbId == title.ImdbId)
                && !dbContext.ImdbFavoriteMovies.Any(favorite =>
                    favorite.UserId == userId && favorite.ImdbId == title.ImdbId && favorite.ToWatch)
                && (dbContext.OscarNominations.Any(nomination =>
                        nomination.OscarMovie != null && nomination.OscarMovie.ImdbId == title.ImdbId)
                    || dbContext.GoldenGlobeNominations.Any(nomination =>
                        nomination.GoldenGlobeTitle != null
                        && nomination.GoldenGlobeTitle.NomineeType == "movie"
                        && nomination.GoldenGlobeTitle.ImdbId == title.ImdbId
                        && nomination.GoldenGlobeTitle.ImdbTitle != null
                        && nomination.GoldenGlobeTitle.ImdbTitle.MediaType == "movie")));

    private static PageResponse<RecommendationResponse> CreatePage(
        IReadOnlyList<RecommendationResponse> items,
        int page,
        int pageSize,
        int totalCount,
        YearBounds? yearBounds = null) =>
        new(items, page, pageSize, totalCount, totalCount == 0 ? 0 : (totalCount + pageSize - 1) / pageSize, yearBounds);

    private static ApiValidationException ValidationError(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });

    private sealed record CandidateTitle(
        string ImdbId,
        string Title,
        int? Year,
        decimal ImdbRating,
        long? ImdbVotes,
        double NormalizedVoteWeight,
        string? PosterUrl,
        string[] Genres);

    private sealed record UserRating(string ImdbId, int Rating);

    private sealed record RatedWinnerReference(string ImdbId, string AwardSource, string CategoryKey);

    private sealed record AwardEventReference(
        string ImdbId,
        string AwardSource,
        string CategoryKey,
        string CategoryName,
        int EventId,
        bool IsWinner);
}