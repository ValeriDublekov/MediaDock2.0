namespace MediaDock.Application.Recommendations;

public readonly record struct MovieRecommendationCategoryKey(string AwardSource, string CategoryKey);

public sealed record MovieRecommendationRatedWinner(
    string ImdbId,
    string AwardSource,
    string CategoryKey,
    int Rating);

public sealed record MovieRecommendationAwardEvent(
    string AwardSource,
    string CategoryKey,
    string CategoryName,
    int EventId,
    bool IsWinner);

public sealed record MovieRecommendationAwardWeight(
    string AwardSource,
    string CategoryKey,
    decimal WinPoints,
    decimal NominationPoints);

public sealed record MovieRecommendationCoefficients(
    decimal Rating,
    decimal Votes,
    decimal Awards);

public sealed record MovieRecommendationCandidate(
    string ImdbId,
    string Title,
    int? Year,
    decimal ImdbRating,
    long? ImdbVotes,
    double NormalizedVoteWeight,
    string? PosterUrl,
    IReadOnlyList<string> Genres,
    IReadOnlyList<MovieRecommendationAwardEvent> AwardEvents);

public sealed record MovieRecommendationCategoryTaste(
    int RatedWinnerCount,
    decimal AverageRating,
    decimal Multiplier);

public sealed record MovieRecommendationTasteProfile(
    decimal? GlobalAverage,
    IReadOnlyDictionary<MovieRecommendationCategoryKey, MovieRecommendationCategoryTaste> Categories)
{
    public decimal GetMultiplier(string awardSource, string categoryKey) =>
        Categories.TryGetValue(new MovieRecommendationCategoryKey(awardSource, categoryKey), out var taste)
            ? taste.Multiplier
            : 1m;
}

public sealed record MovieRecommendationAwardContribution(
    string AwardSource,
    string CategoryKey,
    string CategoryName,
    int EventId,
    string Outcome,
    decimal BasePoints,
    decimal TasteMultiplier,
    decimal AdjustedPoints);

public sealed record MovieRecommendationScore(
    decimal RatingComponent,
    decimal PopularityComponent,
    decimal AwardComponent,
    decimal RatingWeight,
    decimal VotesWeight,
    decimal AwardsWeight,
    decimal FinalScore,
    IReadOnlyList<MovieRecommendationAwardContribution> AwardEvents);

public static class MovieRecommendationScorer
{
    public const decimal RatingPriorStrength = 1000m;
    public const int MinimumRatedWinnerCount = 3;

    public static MovieRecommendationTasteProfile BuildTasteProfile(
        IReadOnlyCollection<int> personalRatings,
        IReadOnlyCollection<MovieRecommendationRatedWinner> ratedWinners)
    {
        ArgumentNullException.ThrowIfNull(personalRatings);
        ArgumentNullException.ThrowIfNull(ratedWinners);

        decimal? globalAverage = personalRatings.Count == 0
            ? null
            : personalRatings.Average(rating => (decimal)rating);
        var distinctWinners = ratedWinners
            .GroupBy(winner => new
            {
                winner.ImdbId,
                Category = new MovieRecommendationCategoryKey(winner.AwardSource, winner.CategoryKey)
            })
            .Select(group => group.First());
        var categories = new Dictionary<MovieRecommendationCategoryKey, MovieRecommendationCategoryTaste>();

        foreach (var group in distinctWinners.GroupBy(winner =>
                     new MovieRecommendationCategoryKey(winner.AwardSource, winner.CategoryKey)))
        {
            var ratedWinnerCount = group.Count();
            var averageRating = group.Average(winner => (decimal)winner.Rating);
            var multiplier = ratedWinnerCount < MinimumRatedWinnerCount
                || globalAverage is null
                || globalAverage == 0m
                ? 1m
                : Math.Clamp(averageRating / globalAverage.Value, 0.5m, 1.5m);
            categories[group.Key] = new MovieRecommendationCategoryTaste(
                ratedWinnerCount,
                averageRating,
                multiplier);
        }

        return new MovieRecommendationTasteProfile(globalAverage, categories);
    }

    public static MovieRecommendationScore Score(
        MovieRecommendationCandidate candidate,
        decimal ratingPriorMean,
        MovieRecommendationTasteProfile tasteProfile,
        IReadOnlyCollection<MovieRecommendationAwardWeight> awardWeights,
        MovieRecommendationCoefficients coefficients)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(tasteProfile);
        ArgumentNullException.ThrowIfNull(awardWeights);
        ValidateCoefficients(coefficients);

        if (ratingPriorMean is < 0m or > 10m)
        {
            throw new ArgumentOutOfRangeException(nameof(ratingPriorMean));
        }

        if (candidate.ImdbRating is < 0m or > 10m)
        {
            throw new ArgumentOutOfRangeException(nameof(candidate), "IMDb rating must be between 0 and 10.");
        }

        if (!double.IsFinite(candidate.NormalizedVoteWeight))
        {
            throw new ArgumentOutOfRangeException(nameof(candidate), "Normalized vote weight must be finite.");
        }

        var voteCount = Math.Max(candidate.ImdbVotes ?? 0L, 0L);
        var ratingComponent = candidate.ImdbVotes is null
            ? ratingPriorMean
            : (candidate.ImdbRating * voteCount + ratingPriorMean * RatingPriorStrength)
                / (voteCount + RatingPriorStrength);
        var popularityComponent = (decimal)Math.Clamp(candidate.NormalizedVoteWeight, 0d, 10d);
        var configuredWeights = awardWeights.ToDictionary(
            weight => new MovieRecommendationCategoryKey(weight.AwardSource, weight.CategoryKey));
        var awardContributions = GetDistinctAwardEvents(candidate.AwardEvents)
            .Select(awardEvent =>
            {
                var category = new MovieRecommendationCategoryKey(awardEvent.AwardSource, awardEvent.CategoryKey);
                var configuredWeight = configuredWeights.GetValueOrDefault(category);
                var basePoints = awardEvent.IsWinner
                    ? configuredWeight?.WinPoints ?? 0m
                    : configuredWeight?.NominationPoints ?? 0m;
                var tasteMultiplier = tasteProfile.GetMultiplier(awardEvent.AwardSource, awardEvent.CategoryKey);
                return new MovieRecommendationAwardContribution(
                    awardEvent.AwardSource,
                    awardEvent.CategoryKey,
                    awardEvent.CategoryName,
                    awardEvent.EventId,
                    awardEvent.IsWinner ? "win" : "nomination",
                    basePoints,
                    tasteMultiplier,
                    basePoints * tasteMultiplier);
            })
            .ToArray();
        var awardComponent = Math.Min(10m, awardContributions.Sum(contribution => contribution.AdjustedPoints));
        var finalScore = ratingComponent * coefficients.Rating
            + popularityComponent * coefficients.Votes
            + awardComponent * coefficients.Awards;

        return new MovieRecommendationScore(
            ratingComponent,
            popularityComponent,
            awardComponent,
            coefficients.Rating,
            coefficients.Votes,
            coefficients.Awards,
            finalScore,
            awardContributions);
    }

    public static void ValidateCoefficients(MovieRecommendationCoefficients coefficients)
    {
        if (coefficients.Rating is < 0m or > 1m
            || coefficients.Votes is < 0m or > 1m
            || coefficients.Awards is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(coefficients), "Each coefficient must be between 0 and 1.");
        }

        if (coefficients.Rating + coefficients.Votes + coefficients.Awards != 1m)
        {
            throw new ArgumentException("The recommendation coefficients must sum to 1.0.", nameof(coefficients));
        }
    }

    private static IEnumerable<MovieRecommendationAwardEvent> GetDistinctAwardEvents(
        IReadOnlyList<MovieRecommendationAwardEvent> awardEvents) =>
        awardEvents
            .GroupBy(awardEvent => new AwardEventKey(
                awardEvent.AwardSource,
                awardEvent.CategoryKey,
                awardEvent.EventId))
            .Select(group =>
            {
                var first = group
                    .OrderBy(awardEvent => awardEvent.CategoryName, StringComparer.Ordinal)
                    .First();
                return first with { IsWinner = group.Any(awardEvent => awardEvent.IsWinner) };
            })
            .OrderBy(awardEvent => awardEvent.EventId)
            .ThenBy(awardEvent => awardEvent.AwardSource, StringComparer.Ordinal)
            .ThenBy(awardEvent => awardEvent.CategoryKey, StringComparer.Ordinal)
            .ThenBy(awardEvent => awardEvent.CategoryName, StringComparer.Ordinal);

    private readonly record struct AwardEventKey(string AwardSource, string CategoryKey, int EventId);
}