using System.ComponentModel.DataAnnotations;

namespace MediaDock.Api.Recommendations;

public sealed record RecommendationSettingsResponse(
    int MedianPeriodYears,
    bool RequiresVoteRecalculation,
    DateTimeOffset? LastVoteRecalculationAt,
    int? LastVoteRecalculationPeriodYears,
    IReadOnlyList<RecommendationAwardWeightResponse> AwardWeights);

public sealed record RecommendationAwardWeightResponse(
    string AwardSource,
    string CategoryKey,
    decimal WinPoints,
    decimal NominationPoints);

public sealed record UpdateRecommendationSettingsRequest
{
    [Range(1, int.MaxValue)]
    public int MedianPeriodYears { get; init; } = 5;

    [Required]
    public UpdateRecommendationAwardWeightRequest[] AwardWeights { get; init; } = [];
}

public sealed record UpdateRecommendationAwardWeightRequest
{
    [Required, MaxLength(32)]
    public string AwardSource { get; init; } = string.Empty;

    [Required, MaxLength(128)]
    public string CategoryKey { get; init; } = string.Empty;

    public decimal WinPoints { get; init; }

    public decimal NominationPoints { get; init; }
}

public sealed record VoteWeightRecalculationResponse(
    DateTimeOffset CalculatedAt,
    long DurationMilliseconds,
    int MedianPeriodYears,
    int MovieCount,
    int MoviesWithVotesCount,
    int PeriodCount,
    int GlobalFallbackCount,
    int MissingVoteCount);