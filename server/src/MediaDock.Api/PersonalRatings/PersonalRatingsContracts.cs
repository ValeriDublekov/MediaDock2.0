using System.ComponentModel.DataAnnotations;

namespace MediaDock.Api.PersonalRatings;

/// <summary>Validated sort and pagination parameters for personal IMDb ratings.</summary>
public sealed record PersonalRatingsQuery
{
    [Range(1, 1_000_000)]
    public int? Page { get; init; }

    [Range(1, 100)]
    public int? PageSize { get; init; }

    [RegularExpression("^(asc|desc)$")]
    public string? Sort { get; init; }
}

public sealed class PersonalRatingsImportForm
{
    public IFormFile? File { get; set; }
}

public sealed record PersonalRatingResponse(
    string ImdbId,
    int Rating,
    string Title,
    int? Year,
    string? MediaType,
    decimal? ImdbRating,
    long? ImdbVotes,
    decimal? Metascore,
    IReadOnlyList<string> Genres,
    IReadOnlyList<string> Countries,
    string? Director,
    string? Plot,
    string? PosterUrl,
    string? Runtime,
    string? Awards,
    string? BoxOffice,
    string MetadataStatus);

public sealed record PersonalRatingsImportResponse(
    int RatingsInFile,
    int Added,
    int Updated,
    int Unchanged,
    int TotalRatings,
    DateTimeOffset ImportedAt,
    IReadOnlyList<PersonalRatingsImportError> Errors);

public sealed record PersonalRatingsImportError(string Id, string Message);

public sealed record PersonalRatingsEnrichmentAcceptedResponse(long JobId, string Status);