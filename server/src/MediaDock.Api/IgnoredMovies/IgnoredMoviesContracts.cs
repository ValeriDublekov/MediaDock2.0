using System.ComponentModel.DataAnnotations;

namespace MediaDock.Api.IgnoredMovies;

public sealed record IgnoredMoviesQuery
{
    [Range(1, 1_000_000)]
    public int? Page { get; init; }

    [Range(1, 100)]
    public int? PageSize { get; init; }
}

public sealed record CreateIgnoredMovieRequest
{
    [Required, RegularExpression("^[Tt][Tt][0-9]{7,10}$")]
    public string ImdbId { get; init; } = string.Empty;
}

public sealed record IgnoredMovieResponse(
    string ImdbId,
    string Title,
    int? Year,
    string? MediaType,
    decimal? ImdbRating,
    string? PosterUrl,
    DateTimeOffset CreatedAt);