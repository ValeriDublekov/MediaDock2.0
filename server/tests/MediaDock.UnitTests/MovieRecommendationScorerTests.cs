using MediaDock.Application.Recommendations;

namespace MediaDock.UnitTests;

public sealed class MovieRecommendationScorerTests
{
    [Fact]
    public void BuildTasteProfileCountsDistinctWinnerMoviesAndClampsMultipliers()
    {
        var profile = MovieRecommendationScorer.BuildTasteProfile(
            [6, 6, 6],
            [
                new("tt1000001", "oscar", "threshold", 10),
                new("tt1000002", "oscar", "threshold", 10),
                new("tt1000003", "oscar", "high", 10),
                new("tt1000004", "oscar", "high", 10),
                new("tt1000005", "oscar", "high", 10),
                new("tt1000003", "oscar", "high", 10),
                new("tt1000006", "oscar", "low", 1),
                new("tt1000007", "oscar", "low", 1),
                new("tt1000008", "oscar", "low", 1)
            ]);

        Assert.Equal(6m, profile.GlobalAverage);
        Assert.Equal(2, profile.Categories[new MovieRecommendationCategoryKey("oscar", "threshold")].RatedWinnerCount);
        Assert.Equal(1m, profile.GetMultiplier("oscar", "threshold"));
        Assert.Equal(3, profile.Categories[new MovieRecommendationCategoryKey("oscar", "high")].RatedWinnerCount);
        Assert.Equal(1.5m, profile.GetMultiplier("oscar", "high"));
        Assert.Equal(0.5m, profile.GetMultiplier("oscar", "low"));
        Assert.Equal(1m, profile.GetMultiplier("golden_globes", "high"));
    }

    [Fact]
    public void EmptyPersonalRatingsUseNeutralTasteMultipliers()
    {
        var profile = MovieRecommendationScorer.BuildTasteProfile([], []);

        Assert.Null(profile.GlobalAverage);
        Assert.Empty(profile.Categories);
        Assert.Equal(1m, profile.GetMultiplier("oscar", "BEST PICTURE"));
    }

    [Fact]
    public void ScoreUsesPriorMeanForMissingVotesAndCapsPopularityAndAwards()
    {
        var candidate = Candidate(
            imdbRating: 9m,
            imdbVotes: null,
            normalizedVoteWeight: 12d,
            awardEvents: Enumerable.Range(2020, 4)
                .Select(year => new MovieRecommendationAwardEvent("oscar", "BEST PICTURE", "Best Picture", year, true))
                .ToArray());
        var weights = new[] { new MovieRecommendationAwardWeight("oscar", "BEST PICTURE", 3m, 1m) };

        var score = MovieRecommendationScorer.Score(
            candidate,
            7m,
            MovieRecommendationScorer.BuildTasteProfile([], []),
            weights,
            new MovieRecommendationCoefficients(0.5m, 0.25m, 0.25m));

        Assert.Equal(7m, score.RatingComponent);
        Assert.Equal(10m, score.PopularityComponent);
        Assert.Equal(10m, score.AwardComponent);
        Assert.Equal(4, score.AwardEvents.Count);
        Assert.Equal(8.5m, score.FinalScore);
        Assert.InRange(score.FinalScore, 0m, 10m);
    }

    [Fact]
    public void ScoreDeduplicatesAwardEventsAndUsesSourceQualifiedOutcomePoints()
    {
        var candidate = Candidate(
            imdbRating: 8m,
            imdbVotes: 1000,
            normalizedVoteWeight: 0.5d,
            awardEvents:
            [
                new("oscar", "BEST PICTURE", "Best Picture", 2025, false),
                new("oscar", "BEST PICTURE", "Best Picture", 2025, true),
                new("golden_globes", "BEST PICTURE", "Best Motion Picture", 2025, false),
                new("golden_globes", "award_other", "Unmapped Award", 2025, false)
            ]);
        var weights = new[]
        {
            new MovieRecommendationAwardWeight("oscar", "BEST PICTURE", 3m, 1m),
            new MovieRecommendationAwardWeight("golden_globes", "BEST PICTURE", 2m, 0.5m)
        };

        var score = MovieRecommendationScorer.Score(
            candidate,
            6m,
            MovieRecommendationScorer.BuildTasteProfile([], []),
            weights,
            new MovieRecommendationCoefficients(0.5m, 0.25m, 0.25m));

        Assert.Equal(7m, score.RatingComponent);
        Assert.Equal(0.5m, score.PopularityComponent);
        Assert.Equal(3.5m, score.AwardComponent);
        Assert.Equal(3, score.AwardEvents.Count);
        Assert.Equal(3m, score.AwardEvents.Single(item => item.AwardSource == "oscar").BasePoints);
        Assert.Equal(0.5m, score.AwardEvents.Single(item =>
            item.AwardSource == "golden_globes" && item.CategoryKey == "BEST PICTURE").BasePoints);
        Assert.Equal(0m, score.AwardEvents.Single(item => item.CategoryKey == "award_other").AdjustedPoints);
        Assert.Equal(4.5m, score.FinalScore);
    }

    [Fact]
    public void ScoreRequiresBoundedCoefficientsSummingToOne()
    {
        var candidate = Candidate(8m, 1000, 0.5d, []);
        var profile = MovieRecommendationScorer.BuildTasteProfile([], []);

        Assert.Throws<ArgumentException>(() => MovieRecommendationScorer.Score(
            candidate,
            7m,
            profile,
            [],
            new MovieRecommendationCoefficients(0.5m, 0.25m, 0.3m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => MovieRecommendationScorer.Score(
            candidate,
            7m,
            profile,
            [],
            new MovieRecommendationCoefficients(1.1m, 0m, -0.1m)));
    }

    private static MovieRecommendationCandidate Candidate(
        decimal imdbRating,
        long? imdbVotes,
        double normalizedVoteWeight,
        IReadOnlyList<MovieRecommendationAwardEvent> awardEvents) =>
        new(
            "tt2000001",
            "Recommendation Candidate",
            2025,
            imdbRating,
            imdbVotes,
            normalizedVoteWeight,
            "https://example.com/poster.jpg",
            ["Drama"],
            awardEvents);
}