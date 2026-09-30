using System.ComponentModel.DataAnnotations;

namespace MediaDock.Api.Favorites;

/// <summary>Filters the shared favorite movie list.</summary>
public sealed record FavoriteQuery
{
    [RegularExpression("^(all|to_watch|to_download)$")]
    public string? Status { get; init; }

    [Range(1, 1_000_000)]
    public int? Page { get; init; }

    [Range(1, 100)]
    public int? PageSize { get; init; }
}

/// <summary>Adds a movie from a verified catalog.</summary>
public sealed record CreateFavoriteRequest
{
    [Range(1L, long.MaxValue)]
    public long TitleId { get; init; }

    [Required, RegularExpression("^(oscar|catalog)$")]
    public string? From { get; init; }
}

/// <summary>Changes only the supplied favorite markers.</summary>
public sealed record UpdateFavoriteRequest
{
    public bool? ToWatch { get; init; }
    public bool? ToDownload { get; init; }
}

/// <summary>A movie and its current favorite, Oscar and torrent summaries.</summary>
public sealed record FavoriteMovieResponse(
    long TitleId,
    string Title,
    int? Year,
    string MediaType,
    decimal? ImdbRating,
    string? PosterUrl,
    bool ToWatch,
    bool ToDownload,
    bool AddedFromOscar,
    bool AddedFromCatalog,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int OscarFilmCount,
    int NominationCount,
    int WinCount,
    int OccurrenceCount,
    DateTimeOffset? LastSeenAt);