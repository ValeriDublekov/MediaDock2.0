using System.Data;
using MediaDock.Api.Common;
using MediaDock.Api.Middleware;
using MediaDock.Application.Recommendations;
using MediaDock.Infrastructure.Persistence;
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

        var page = query.Page ?? 1;
        var pageSize = query.PageSize ?? 25;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);

        var candidates = await dbContext.ImdbTitles
            .AsNoTracking()
            .Where(title => title.MetadataStatus == "enriched"
                && title.MediaType == "movie"
                && title.TitleText != null
                && title.TitleText.Trim() != string.Empty
                && title.ImdbRating.HasValue
                && !dbContext.PersonalRatings.Any(rating =>
                    rating.UserId == userId && rating.ImdbId == title.ImdbId)
                && !dbContext.ImdbFavoriteMovies.Any(favorite =>
                    favorite.UserId == userId && favorite.ImdbId == title.ImdbId && favorite.ToWatch)
                && (dbContext.OscarNominations.Any(nomination =>
                        nomination.OscarMovie != null && nomination.OscarMovie.ImdbId == title.ImdbId)
                    || dbContext.GoldenGlobeNominations.Any(nomination =>
                        nomination.GoldenGlobeTitle != null
                        && nomination.GoldenGlobeTitle.NomineeType == "movie"
                        && nomination.GoldenGlobeTitle.ImdbId == title.ImdbId
                        && nomination.GoldenGlobeTitle.ImdbTitle != null
                        && nomination.GoldenGlobeTitle.ImdbTitle.MediaType == "movie")))
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

        if (candidates.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return CreatePage([], page, pageSize, 0);
        }

        var ratingRows = await dbContext.PersonalRatings
            .AsNoTracking()
            .Where(rating => rating.UserId == userId)
            .Select(rating => new UserRating(rating.ImdbId, rating.Rating))
            .ToListAsync(cancellationToken);
        var ratingsByImdbId = ratingRows.ToDictionary(rating => rating.ImdbId);
        var oscarWinners = await dbContext.OscarNominations
            .AsNoTracking()
            .Where(nomination => nomination.IsWinner
                && nomination.OscarMovie != null
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
        var goldenGlobeWinners = await dbContext.GoldenGlobeNominations
            .AsNoTracking()
            .Where(nomination => nomination.Winner
                && nomination.GoldenGlobeTitle != null
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
            oscarWinners.Concat(goldenGlobeWinners)
                .Select(winner => new MovieRecommendationRatedWinner(
                    winner.ImdbId,
                    winner.AwardSource,
                    winner.CategoryKey,
                    ratingsByImdbId[winner.ImdbId].Rating))
                .ToArray());

        var ratingPriorMean = await dbContext.ImdbTitles
            .AsNoTracking()
            .Where(title => title.MetadataStatus == "enriched"
                && title.MediaType == "movie"
                && title.ImdbRating.HasValue)
            .AverageAsync(title => title.ImdbRating, cancellationToken);
        if (ratingPriorMean is not decimal priorMean)
        {
            throw new InvalidOperationException("An IMDb rating prior is required when recommendation candidates exist.");
        }

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
                weight.NominationPoints))
            .ToListAsync(cancellationToken);

        var scoredCandidates = candidates
            .Select(candidate =>
            {
                var awardEvents = awardEventsByImdbId.GetValueOrDefault(candidate.ImdbId)
                    ?? Array.Empty<MovieRecommendationAwardEvent>();
                var scoringCandidate = new MovieRecommendationCandidate(
                    candidate.ImdbId,
                    candidate.Title,
                    candidate.Year,
                    candidate.ImdbRating,
                    candidate.ImdbVotes,
                    candidate.NormalizedVoteWeight,
                    candidate.PosterUrl,
                    candidate.Genres,
                    awardEvents);
                var score = MovieRecommendationScorer.Score(
                    scoringCandidate,
                    priorMean,
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
            scoredCandidates.Length);
    }

    private static PageResponse<RecommendationResponse> CreatePage(
        IReadOnlyList<RecommendationResponse> items,
        int page,
        int pageSize,
        int totalCount) =>
        new(items, page, pageSize, totalCount, totalCount == 0 ? 0 : (totalCount + pageSize - 1) / pageSize);

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