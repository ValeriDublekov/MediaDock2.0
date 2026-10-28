using System.ComponentModel.DataAnnotations;

namespace MediaDock.Api.Favorites;

/// <summary>Filters the shared favorite title list.</summary>
public sealed record FavoriteQuery
{
    [RegularExpression("^(all|to_watch|to_download)$")]
    public string? Status { get; init; }

    [Range(1, 1_000_000)]
    public int? Page { get; init; }

    [Range(1, 100)]
    public int? PageSize { get; init; }
}

/// <summary>Adds an enriched title from a supported source.</summary>
public sealed record CreateFavoriteRequest
{
    [Required, RegularExpression("^[Tt][Tt][0-9]{7,10}$")]
    public string ImdbId { get; init; } = string.Empty;

    [Required, RegularExpression("^(oscar|catalog|recommendation)$")]
    public string? From { get; init; }
}

/// <summary>Changes only the supplied favorite markers.</summary>
public sealed record UpdateFavoriteRequest
{
    public bool? ToWatch { get; init; }
    public bool? ToDownload { get; init; }
}

/// <summary>A favorite title and its current Oscar and torrent summaries.</summary>
public sealed record FavoriteMovieResponse(
    string ImdbId,
    long? TorrentMovieId,
    string Title,
    int? Year,
    string MediaType,
    string? MetadataStatus,
    decimal? ImdbRating,
    long? ImdbVotes,
    decimal? Metascore,
    IReadOnlyList<string> Genres,
    IReadOnlyList<string> Countries,
    string? Director,
    string? Plot,
    string? Runtime,
    string? Awards,
    string? BoxOffice,
    string? PosterUrl,
    bool ToWatch,
    bool ToDownload,
    bool AddedFromOscar,
    bool AddedFromCatalog,
    bool AddedFromRecommendations,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int OscarFilmCount,
    int NominationCount,
    int WinCount,
    int OccurrenceCount,
    DateTimeOffset? LastSeenAt);