using System.ComponentModel.DataAnnotations;

namespace MediaDock.Api.GoldenGlobes;

public sealed record GoldenGlobeCatalogQuery
{
    [Range(1, 1_000_000)] public int? Page { get; init; }
    [Range(1, 100)] public int? PageSize { get; init; }
    [MaxLength(200)] public string? Search { get; init; }
    [Range(1800, 2200)] public int? YearFrom { get; init; }
    [Range(1800, 2200)] public int? YearTo { get; init; }
    [MaxLength(100)] public string? Award { get; init; }
    [RegularExpression("^(winner|nominee)$")] public string? Result { get; init; }
    [RegularExpression("^(pending|enriched|problem|not_found|temporary_error)$")] public string? EnrichmentStatus { get; init; }
}

public sealed record GoldenGlobeFilmResponse(
    string Id,
    string Title,
    int Year,
    string? ImdbId,
    string? PosterUrl,
    string? EnrichmentStatus,
    string? EnrichmentError,
    IReadOnlyList<GoldenGlobeNominationResponse> Nominations);

public sealed record GoldenGlobeNominationResponse(
    long Id,
    int Year,
    string Award,
    bool IsWinner);
