using System.Diagnostics;
using MediaDock.Api.Middleware;
using MediaDock.Application.Recommendations;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediaDock.Api.Recommendations;

internal interface IRecommendationSettingsApiService
{
    Task<RecommendationSettingsResponse> GetSettingsAsync(CancellationToken cancellationToken);

    Task<RecommendationSettingsResponse> UpdateSettingsAsync(
        UpdateRecommendationSettingsRequest request,
        CancellationToken cancellationToken);

    Task<VoteWeightRecalculationResponse> RecalculateVoteWeightsAsync(CancellationToken cancellationToken);
}

internal sealed class RecommendationSettingsApiService(MediaDockDbContext dbContext)
    : IRecommendationSettingsApiService
{
    private static readonly TimeSpan RecalculationTimeout = TimeSpan.FromSeconds(10);

    public async Task<RecommendationSettingsResponse> GetSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await dbContext.GeneralRecommendationSettings
            .AsNoTracking()
            .SingleAsync(value => value.Id == 1, cancellationToken);
        var configuredWeights = await dbContext.RecommendationAwardWeights
            .AsNoTracking()
            .OrderBy(weight => weight.AwardSource)
            .ThenBy(weight => weight.CategoryKey)
            .Select(weight => new
            {
                weight.AwardSource,
                weight.CategoryKey,
                weight.WinPoints,
                weight.NominationPoints,
                weight.IsOverride
            })
            .ToArrayAsync(cancellationToken);
        var weights = configuredWeights.Select(weight =>
        {
            var (defaultWinPoints, defaultNominationPoints) = weight.AwardSource == "oscar"
                ? (settings.OscarWinPoints, settings.OscarNomPoints)
                : (settings.GgWinPoints, settings.GgNomPoints);
            return new RecommendationAwardWeightResponse(
                weight.AwardSource,
                weight.CategoryKey,
                weight.IsOverride ? weight.WinPoints : defaultWinPoints,
                weight.IsOverride ? weight.NominationPoints : defaultNominationPoints,
                weight.IsOverride);
        }).ToArray();

        return new RecommendationSettingsResponse(
            settings.MedianPeriodYears,
            settings.BayesianDefaultC,
            settings.BayesianMRatio,
            settings.OscarWinPoints,
            settings.OscarNomPoints,
            settings.GgWinPoints,
            settings.GgNomPoints,
            settings.RequiresVoteRecalculation,
            settings.LastVoteRecalculationAt,
            settings.LastVoteRecalculationPeriodYears,
            weights);
    }

    public async Task<RecommendationSettingsResponse> UpdateSettingsAsync(
        UpdateRecommendationSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, List<string>>();
        if (request.MedianPeriodYears <= 0)
        {
            AddError(errors, nameof(request.MedianPeriodYears), "The median period must be positive.");
        }

        if (request.BayesianDefaultC is < 0m or > 10m)
        {
            AddError(errors, nameof(request.BayesianDefaultC), "The Bayesian default rating must be between 0 and 10.");
        }

        if (request.BayesianMRatio is <= 0m or > 1m)
        {
            AddError(errors, nameof(request.BayesianMRatio), "The Bayesian m ratio must be greater than 0 and at most 1.");
        }

        if (request.OscarWinPoints < 0m || request.OscarNomPoints < 0m)
        {
            AddError(errors, nameof(request.OscarWinPoints), "Oscar award points cannot be negative.");
        }

        if (request.GgWinPoints < 0m || request.GgNomPoints < 0m)
        {
            AddError(errors, nameof(request.GgWinPoints), "Golden Globes award points cannot be negative.");
        }

        if (request.AwardWeights is null)
        {
            AddError(errors, nameof(request.AwardWeights), "Award weights are required.");
        }
        else
        {
            var seenWeights = new HashSet<(string AwardSource, string CategoryKey)>();
            foreach (var update in request.AwardWeights)
            {
                if (update is null)
                {
                    AddError(errors, nameof(request.AwardWeights), "Award weight entries cannot be null.");
                    continue;
                }

                if (update.AwardSource is not ("oscar" or "golden_globes"))
                {
                    AddError(errors, nameof(request.AwardWeights), "Award source must be oscar or golden_globes.");
                }

                if (string.IsNullOrWhiteSpace(update.CategoryKey))
                {
                    AddError(errors, nameof(request.AwardWeights), "Category keys cannot be empty.");
                }

                if (update.WinPoints < 0m || update.NominationPoints < 0m)
                {
                    AddError(errors, nameof(request.AwardWeights), "Award points cannot be negative.");
                }

                if (!seenWeights.Add((update.AwardSource, update.CategoryKey)))
                {
                    AddError(errors, nameof(request.AwardWeights), "Award source and category pairs must be unique.");
                }
            }
        }

        ThrowIfInvalid(errors);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var settings = await dbContext.GeneralRecommendationSettings
            .SingleAsync(value => value.Id == 1, cancellationToken);
        var weights = await dbContext.RecommendationAwardWeights
            .ToDictionaryAsync(weight => (weight.AwardSource, weight.CategoryKey), cancellationToken);

        foreach (var update in request.AwardWeights)
        {
            if (!weights.TryGetValue((update.AwardSource, update.CategoryKey), out var weight))
            {
                AddError(
                    errors,
                    nameof(request.AwardWeights),
                    $"No configured award weight exists for {update.AwardSource}/{update.CategoryKey}.");
                continue;
            }

            weight.WinPoints = update.WinPoints;
            weight.NominationPoints = update.NominationPoints;
            weight.IsOverride = update.IsOverride ?? true;
        }

        ThrowIfInvalid(errors);

        settings.MedianPeriodYears = request.MedianPeriodYears;
        settings.BayesianDefaultC = request.BayesianDefaultC;
        settings.BayesianMRatio = request.BayesianMRatio;
        settings.OscarWinPoints = request.OscarWinPoints;
        settings.OscarNomPoints = request.OscarNomPoints;
        settings.GgWinPoints = request.GgWinPoints;
        settings.GgNomPoints = request.GgNomPoints;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetSettingsAsync(cancellationToken);
    }

    public async Task<VoteWeightRecalculationResponse> RecalculateVoteWeightsAsync(
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(RecalculationTimeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
        var stopwatch = Stopwatch.StartNew();
        var transactionCommitted = false;

        try
        {
            var settingsRowsUpdated = await dbContext.GeneralRecommendationSettings
                .Where(value => value.Id == 1)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(value => value.RequiresVoteRecalculation, true),
                    linkedCancellation.Token);
            if (settingsRowsUpdated != 1)
            {
                throw new InvalidOperationException("The recommendation settings singleton is missing.");
            }

            await using var transaction = await dbContext.Database.BeginTransactionAsync(linkedCancellation.Token);
            await dbContext.Database.ExecuteSqlRawAsync(
                "LOCK TABLE imdb_titles IN SHARE ROW EXCLUSIVE MODE",
                linkedCancellation.Token);
            var settings = await dbContext.GeneralRecommendationSettings
                .FromSqlRaw("SELECT * FROM general_recommendation_settings WHERE id = 1 FOR UPDATE")
                .SingleAsync(linkedCancellation.Token);
            var periodYears = settings.MedianPeriodYears;
            var movies = await dbContext.ImdbTitles
                .AsNoTracking()
                .Where(title => title.MediaType == "movie")
                .Select(title => new MovieVoteNormalizationInput(title.ImdbId, title.Year, title.ImdbVotes))
                .ToListAsync(linkedCancellation.Token);
            var normalizedMovies = MovieVoteNormalizer.Normalize(movies, periodYears);

            if (normalizedMovies.Count > 0)
            {
                var imdbIds = normalizedMovies.Select(movie => movie.ImdbId).ToArray();
                var weights = normalizedMovies.Select(movie => movie.NormalizedVoteWeight).ToArray();
                var updatedMovieCount = await dbContext.Database.ExecuteSqlRawAsync(
                    """
                    UPDATE imdb_titles AS title
                    SET normalized_vote_weight = vote_weight_rows.normalized_vote_weight
                    FROM unnest(@imdb_ids, @normalized_vote_weights)
                        AS vote_weight_rows(imdb_id, normalized_vote_weight)
                    WHERE title.imdb_id = vote_weight_rows.imdb_id
                      AND title.media_type = 'movie'
                    """,
                    [
                        new NpgsqlParameter("imdb_ids", imdbIds),
                        new NpgsqlParameter("normalized_vote_weights", weights)
                    ],
                    linkedCancellation.Token);
                if (updatedMovieCount != normalizedMovies.Count)
                {
                    throw new InvalidOperationException("Not every movie vote weight was updated.");
                }
            }

            var calculatedAt = DateTimeOffset.UtcNow;
            settings.LastVoteRecalculationAt = calculatedAt;
            settings.LastVoteRecalculationPeriodYears = periodYears;
            settings.RequiresVoteRecalculation = false;
            await dbContext.SaveChangesAsync(linkedCancellation.Token);
            await transaction.CommitAsync(linkedCancellation.Token);
            transactionCommitted = true;
            stopwatch.Stop();

            return new VoteWeightRecalculationResponse(
                calculatedAt,
                stopwatch.ElapsedMilliseconds,
                periodYears,
                movies.Count,
                movies.Count(movie => movie.TotalVotes.HasValue),
                normalizedMovies.Where(movie => movie.PeriodStart.HasValue)
                    .Select(movie => movie.PeriodStart)
                    .Distinct()
                    .Count(),
                normalizedMovies.Count(movie => movie.UsedFallbackMedian),
                movies.Count(movie => !movie.TotalVotes.HasValue));
        }
        catch (OperationCanceledException exception)
            when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested && !transactionCommitted)
        {
            throw new ApiTimeoutException("Vote recalculation exceeded the 10-second execution limit.", exception);
        }
    }

    private static void AddError(Dictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var messages))
        {
            messages = [];
            errors.Add(key, messages);
        }

        messages.Add(message);
    }

    private static void ThrowIfInvalid(Dictionary<string, List<string>> errors)
    {
        if (errors.Count > 0)
        {
            throw new ApiValidationException(errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray()));
        }
    }
}