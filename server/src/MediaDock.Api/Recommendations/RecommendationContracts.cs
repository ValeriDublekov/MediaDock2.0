using System.ComponentModel.DataAnnotations;

namespace MediaDock.Api.Recommendations;

public sealed record RecommendationsQuery
{
    [Range(1, 1_000_000)]
    public int? Page { get; init; }

    [Range(1, 100)]
    public int? PageSize { get; init; }

    [Required]
    [Range(typeof(decimal), "0", "1")]
    public decimal? RatingWeight { get; init; }

    [Required]
    [Range(typeof(decimal), "0", "1")]
    public decimal? VotesWeight { get; init; }

    [Required]
    [Range(typeof(decimal), "0", "1")]
    public decimal? AwardsWeight { get; init; }
}

public sealed record RecommendationResponse(
    string ImdbId,
    string Title,
    int? Year,
    string MediaType,
    decimal ImdbRating,
    long? ImdbVotes,
    string? PosterUrl,
    IReadOnlyList<string> Genres,
    decimal RatingComponent,
    decimal PopularityComponent,
    decimal AwardComponent,
    decimal RatingWeight,
    decimal VotesWeight,
    decimal AwardsWeight,
    decimal FinalScore,
    IReadOnlyList<RecommendationAwardEventResponse> AwardEvents);

public sealed record RecommendationAwardEventResponse(
    string AwardSource,
    string CategoryKey,
    string CategoryName,
    int? Ceremony,
    int? CeremonyYear,
    string Outcome,
    decimal BasePoints,
    decimal TasteMultiplier,
    decimal AdjustedPoints);