using System.ComponentModel.DataAnnotations;

namespace MediaDock.Api.ImdbTitles;

/// <summary>Filters and pages the canonical IMDb title catalog.</summary>
public sealed record ImdbTitlesQuery
{
    [Range(1, 1_000_000)]
    public int? Page { get; init; }

    [Range(1, 100)]
    public int? PageSize { get; init; }

    [MaxLength(200)]
    public string? Search { get; init; }

    [MaxLength(32)]
    public string? MediaType { get; init; }

    public bool? MediaTypeIsNull { get; init; }

    [MaxLength(100)]
    public string[]? Genres { get; init; }

    public bool? GenreFilter { get; init; }

    [MaxLength(100)]
    public string[]? Countries { get; init; }

    public bool? CountryFilter { get; init; }

    [Range(1800, 2200)]
    public int? YearFrom { get; init; }

    [Range(1800, 2200)]
    public int? YearTo { get; init; }

    [Range(typeof(decimal), "0", "10")]
    public decimal? ImdbRatingFrom { get; init; }

    [Range(typeof(decimal), "0", "10")]
    public decimal? ImdbRatingTo { get; init; }

    [RegularExpression("^(pending|enriched|not_found|temporary_error)$")]
    public string? MetadataStatus { get; init; }

    [RegularExpression("^(year|rating|votes)$")]
    public string? Sort { get; init; }

    public bool? ShowIgnored { get; init; }

    public bool? ShowWatched { get; init; }
}

/// <summary>A page of canonical IMDb rows and catalog-wide filter options.</summary>
public sealed record ImdbTitlesPageResponse(
    IReadOnlyList<ImdbTitleCatalogItem> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    ImdbTitleYearBounds YearBounds,
    ImdbTitleRatingBounds ImdbRatingBounds,
    IReadOnlyList<string> MediaTypes,
    bool HasUntypedTitles,
    IReadOnlyList<string> Genres,
    IReadOnlyList<string> MetadataStatuses);

public sealed record DeleteImdbTitleResponse(int UnlinkedTorrentTitles);

/// <summary>Canonical IMDb metadata and the active user's personal state for one title.</summary>
public sealed record ImdbTitleCatalogItem(
    string ImdbId,
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
    string MetadataStatus,
    bool IsFavorite,
    bool ToWatch,
    bool ToDownload,
    bool IsIgnored,
    int? PersonalRating);

/// <summary>Minimum and maximum non-null release years in the canonical catalog.</summary>
public sealed record ImdbTitleYearBounds(int? MinYear, int? MaxYear);

/// <summary>Minimum and maximum non-null IMDb ratings in the canonical catalog.</summary>
public sealed record ImdbTitleRatingBounds(decimal? MinRating, decimal? MaxRating);

