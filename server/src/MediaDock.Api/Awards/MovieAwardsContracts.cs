using System.ComponentModel.DataAnnotations;

namespace MediaDock.Api.Awards;

public sealed record MovieAwardsQuery
{
    [Required]
    [MaxLength(1299)]
    public string? ImdbIds { get; init; }
}

public sealed record MovieAwardRecognitionResponse(
    long Id,
    string ImdbId,
    string Source,
    int? FilmYear,
    int? CeremonyYear,
    int? Ceremony,
    string Award,
    string? Name,
    string? Nominees,
    string? Detail,
    bool IsWinner);