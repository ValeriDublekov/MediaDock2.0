using System.ComponentModel.DataAnnotations;

namespace MediaDock.Api.Recommendations;

public sealed record RecommendationSettingsResponse(
    int MedianPeriodYears,
    decimal BayesianDefaultC,
    decimal BayesianMRatio,
    decimal OscarWinPoints,
    decimal OscarNomPoints,
    decimal GgWinPoints,
    decimal GgNomPoints,
    bool RequiresVoteRecalculation,
    DateTimeOffset? LastVoteRecalculationAt,
    int? LastVoteRecalculationPeriodYears,
    IReadOnlyList<RecommendationAwardWeightResponse> AwardWeights);

public sealed record RecommendationAwardWeightResponse(
    string AwardSource,
    string CategoryKey,
    decimal WinPoints,
    decimal NominationPoints,
    bool IsOverride);

public sealed record UpdateRecommendationSettingsRequest
{
    [Range(1, int.MaxValue)]
    public int MedianPeriodYears { get; init; } = 5;

    public decimal BayesianDefaultC { get; init; } = 5.5m;

    public decimal BayesianMRatio { get; init; } = 0.25m;

    public decimal OscarWinPoints { get; init; } = 3m;

    public decimal OscarNomPoints { get; init; } = 1m;

    public decimal GgWinPoints { get; init; } = 2m;

    public decimal GgNomPoints { get; init; } = 0.5m;

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

    public bool? IsOverride { get; init; }
}

public sealed record VoteWeightRecalculationResponse(
    DateTimeOffset CalculatedAt,
    long DurationMilliseconds,
    int MedianPeriodYears,
    int MovieCount,
    int MoviesWithVotesCount,
    int PeriodCount,
    int FallbackMedianCount,
    int MissingVoteCount);