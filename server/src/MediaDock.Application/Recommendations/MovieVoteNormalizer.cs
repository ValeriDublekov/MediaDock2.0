namespace MediaDock.Application.Recommendations;

public sealed record MovieVoteNormalizationInput(string ImdbId, int? Year, long? TotalVotes);

public sealed record MovieVoteNormalizationResult(
    string ImdbId,
    double NormalizedVoteWeight,
    int? PeriodStart,
    decimal PeriodMedianVotes,
    bool UsedFallbackMedian);

public static class MovieVoteNormalizer
{
    public static IReadOnlyList<MovieVoteNormalizationResult> Normalize(
        IReadOnlyList<MovieVoteNormalizationInput> movies,
        int medianPeriodYears)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(medianPeriodYears);

        var periodMedians = movies
            .Where(movie => movie.Year.HasValue && movie.TotalVotes.HasValue)
            .GroupBy(movie => GetPeriodStart(movie.Year!.Value, medianPeriodYears))
            .ToDictionary(group => group.Key, group => Median(group.Select(movie => movie.TotalVotes!.Value)));

        return movies.Select(movie =>
        {
            int? periodStart = movie.Year is int year
                ? GetPeriodStart(year, medianPeriodYears)
                : null;
            decimal? periodMedian = periodStart is int start && periodMedians.TryGetValue(start, out var median)
                ? median
                : null;
            var usedFallbackMedian = !periodMedian.HasValue || periodMedian.Value < 1m;
            var effectiveMedian = usedFallbackMedian ? 1000m : periodMedian!.Value;
            var normalizedWeight = 0d;
            if (movie.TotalVotes is long totalVotes)
            {
                var voteCount = Math.Max(totalVotes, 0L);
                var rawIndex = voteCount / (double)effectiveMedian;
                normalizedWeight = Math.Min(10d, Math.Log10(rawIndex + 1d) * 3.33d);
            }

            return new MovieVoteNormalizationResult(
                movie.ImdbId,
                normalizedWeight,
                periodStart,
                effectiveMedian,
                usedFallbackMedian);
        }).ToArray();
    }

    private static int GetPeriodStart(int year, int medianPeriodYears) =>
        year - year % medianPeriodYears;

    private static decimal Median(IEnumerable<long> values) =>
        MedianOrNull(values) ?? throw new InvalidOperationException("A median requires at least one value.");

    private static decimal? MedianOrNull(IEnumerable<long> values)
    {
        var sorted = values.Select(value => Math.Max(value, 0L)).Order().ToArray();
        if (sorted.Length == 0)
        {
            return null;
        }

        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? sorted[middle - 1] / 2m + sorted[middle] / 2m
            : sorted[middle];
    }
}