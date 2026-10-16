namespace MediaDock.Infrastructure.Persistence.Entities;

public sealed class ImdbTitle
{
    public string ImdbId { get; set; } = string.Empty;
    public string? TitleText { get; set; }
    public int? Year { get; set; }
    public string? MediaType { get; set; }
    public decimal? ImdbRating { get; set; }
    public long? ImdbVotes { get; set; }
    public decimal? Metascore { get; set; }
    public string[] Genres { get; set; } = Array.Empty<string>();
    public string[] Countries { get; set; } = Array.Empty<string>();
    public string? Director { get; set; }
    public string? Plot { get; set; }
    public string? PosterUrl { get; set; }
    public string? Runtime { get; set; }
    public string? Awards { get; set; }
    public string? BoxOffice { get; set; }
    public string MetadataStatus { get; set; } = "pending";
    public int EnrichmentAttemptCount { get; set; }
    public DateTimeOffset? LastEnrichmentAttemptAt { get; set; }
    public DateTimeOffset? NextEnrichmentAttemptAt { get; set; }
    public string? LastEnrichmentError { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}