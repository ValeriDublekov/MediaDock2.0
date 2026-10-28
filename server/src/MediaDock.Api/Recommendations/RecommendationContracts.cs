using System.ComponentModel.DataAnnotations;

namespace MediaDock.Api.Recommendations;

internal static class RecommendationMediaTypes
{
    public const string Movie = "movie";
    public const string Series = "series";
    public const string Pattern = "^(movie|series)$";

    public static bool IsSupported(string? mediaType) => mediaType is Movie or Series;
}

public sealed record RecommendationsQuery
{
    [RegularExpression(RecommendationMediaTypes.Pattern)]
    public string? MediaType { get; init; }

    [Range(1, 1_000_000)]
    public int? Page { get; init; }

    [Range(1, 100)]
    public int? PageSize { get; init; }

    [Range(1800, 2200)]
    public int? YearFrom { get; init; }

    [Range(1800, 2200)]
    public int? YearTo { get; init; }

    public string[]? Genres { get; init; }

    public bool? ExcludeWatched { get; init; }

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

public sealed record RecommendationMediaTypeQuery
{
    [RegularExpression(RecommendationMediaTypes.Pattern)]
    public string? MediaType { get; init; }
}

public sealed record MarkRecommendationWatchedRequest
{
    public int Rating { get; init; }

    [RegularExpression(RecommendationMediaTypes.Pattern)]
    public string MediaType { get; init; } = RecommendationMediaTypes.Movie;
}

public sealed record RecommendationResponse(
    string ImdbId,
    string Title,
    int? Year,
    string MediaType,
    bool IsWatched,
    decimal ImdbRating,
    long? ImdbVotes,
    string? PosterUrl,
    IReadOnlyList<string> Genres,
    decimal? Metascore,
    IReadOnlyList<string> Countries,
    string? Director,
    string? Plot,
    string? Runtime,
    string? Awards,
    string? BoxOffice,
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