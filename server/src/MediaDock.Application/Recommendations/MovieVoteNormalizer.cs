namespace MediaDock.Application.Recommendations;

public sealed record MovieVoteNormalizationInput(string ImdbId, int? Year, long? TotalVotes);

public sealed record MovieVoteNormalizationResult(
    string ImdbId,
    double NormalizedVoteWeight,
    int? PeriodStart,
    bool UsedGlobalMedian);

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
        var globalMedian = MedianOrNull(movies
            .Where(movie => movie.TotalVotes.HasValue)
            .Select(movie => movie.TotalVotes!.Value));

        return movies.Select(movie =>
        {
            int? periodStart = movie.Year is int year
                ? GetPeriodStart(year, medianPeriodYears)
                : null;
            if (movie.TotalVotes is not long totalVotes)
            {
                return new MovieVoteNormalizationResult(movie.ImdbId, 0d, periodStart, false);
            }

            var median = periodStart is int start
                ? periodMedians[start]
                : globalMedian ?? 0d;
            var divisor = median == 0d ? 1d : median;
            var normalizedWeight = Math.Log10(totalVotes / divisor + 1d);
            return new MovieVoteNormalizationResult(
                movie.ImdbId,
                normalizedWeight,
                periodStart,
                movie.Year is null);
        }).ToArray();
    }

    private static int GetPeriodStart(int year, int medianPeriodYears) =>
        year - year % medianPeriodYears;

    private static double Median(IEnumerable<long> values) =>
        MedianOrNull(values) ?? throw new InvalidOperationException("A median requires at least one value.");

    private static double? MedianOrNull(IEnumerable<long> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0)
        {
            return null;
        }

        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? sorted[middle - 1] / 2d + sorted[middle] / 2d
            : sorted[middle];
    }
}