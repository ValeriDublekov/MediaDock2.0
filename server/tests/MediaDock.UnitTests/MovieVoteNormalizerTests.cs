using MediaDock.Application.Recommendations;

namespace MediaDock.UnitTests;

public sealed class MovieVoteNormalizerTests
{
    [Fact]
    public void NormalizeUsesHalfOpenYearPeriodsAndOddAndEvenMedians()
    {
        var movies = new[]
        {
            new MovieVoteNormalizationInput("tt00000001", 1999, 10),
            new MovieVoteNormalizationInput("tt00000002", 2000, 10),
            new MovieVoteNormalizationInput("tt00000003", 2004, 90),
            new MovieVoteNormalizationInput("tt00000004", 2005, 10),
            new MovieVoteNormalizationInput("tt00000005", 2006, 20),
            new MovieVoteNormalizationInput("tt00000006", 2009, 90),
            new MovieVoteNormalizationInput("tt00000007", 2010, 100)
        };

        var results = MovieVoteNormalizer.Normalize(movies, 5).ToDictionary(result => result.ImdbId);

        Assert.Equal(1995, results["tt00000001"].PeriodStart);
        Assert.Equal(2000, results["tt00000002"].PeriodStart);
        Assert.Equal(2005, results["tt00000004"].PeriodStart);
        Assert.Equal(2010, results["tt00000007"].PeriodStart);
        Assert.Equal(Math.Log10(2d), results["tt00000001"].NormalizedVoteWeight, 12);
        Assert.Equal(Math.Log10(1.2d), results["tt00000002"].NormalizedVoteWeight, 12);
        Assert.Equal(Math.Log10(2.8d), results["tt00000003"].NormalizedVoteWeight, 12);
        Assert.Equal(Math.Log10(1.5d), results["tt00000004"].NormalizedVoteWeight, 12);
        Assert.Equal(Math.Log10(2d), results["tt00000005"].NormalizedVoteWeight, 12);
        Assert.Equal(Math.Log10(5.5d), results["tt00000006"].NormalizedVoteWeight, 12);
        Assert.Equal(Math.Log10(2d), results["tt00000007"].NormalizedVoteWeight, 12);
    }

    [Fact]
    public void NormalizeUsesGlobalMedianForMissingYearsIncludingZeroVotes()
    {
        var movies = new[]
        {
            new MovieVoteNormalizationInput("tt00000001", 2020, 0),
            new MovieVoteNormalizationInput("tt00000002", 2021, 100),
            new MovieVoteNormalizationInput("tt00000003", null, 0),
            new MovieVoteNormalizationInput("tt00000004", null, 1000)
        };

        var results = MovieVoteNormalizer.Normalize(movies, 5).ToDictionary(result => result.ImdbId);

        Assert.Equal(2020, results["tt00000001"].PeriodStart);
        Assert.Equal(0d, results["tt00000001"].NormalizedVoteWeight);
        Assert.True(results["tt00000003"].UsedGlobalMedian);
        Assert.True(results["tt00000004"].UsedGlobalMedian);
        Assert.Equal(0d, results["tt00000003"].NormalizedVoteWeight);
        Assert.Equal(Math.Log10(21d), results["tt00000004"].NormalizedVoteWeight, 12);
    }

    [Fact]
    public void NormalizeReturnsZeroForMissingVotesAndUsesOneForZeroMedian()
    {
        var movies = new[]
        {
            new MovieVoteNormalizationInput("tt00000001", 2020, 0),
            new MovieVoteNormalizationInput("tt00000002", 2021, 0),
            new MovieVoteNormalizationInput("tt00000003", 2022, null),
            new MovieVoteNormalizationInput("tt00000004", null, 0)
        };

        var results = MovieVoteNormalizer.Normalize(movies, 5).ToDictionary(result => result.ImdbId);

        Assert.All(results.Values, result => Assert.Equal(0d, result.NormalizedVoteWeight));
        Assert.Equal(2020, results["tt00000003"].PeriodStart);
        Assert.False(results["tt00000003"].UsedGlobalMedian);
        Assert.Null(results["tt00000004"].PeriodStart);
        Assert.True(results["tt00000004"].UsedGlobalMedian);
    }

    [Fact]
    public void NormalizeUsesFloatingPointDivisionWhenVotesEqualMedian()
    {
        var movies = new[]
        {
            new MovieVoteNormalizationInput("tt00000001", 2024, 100),
            new MovieVoteNormalizationInput("tt00000002", 2024, 100)
        };

        var results = MovieVoteNormalizer.Normalize(movies, 5);

        Assert.All(results, result => Assert.Equal(Math.Log10(2d), result.NormalizedVoteWeight, 12));
    }

    [Fact]
    public void NormalizeRequiresPositivePeriod()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MovieVoteNormalizer.Normalize([], 0));
    }
}