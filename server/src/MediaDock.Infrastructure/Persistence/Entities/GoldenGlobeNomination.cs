namespace MediaDock.Infrastructure.Persistence.Entities;

public sealed class GoldenGlobeNomination
{
    public long Id { get; set; }
    public string ImportKey { get; set; } = string.Empty;
    public int Year { get; set; }
    public bool Winner { get; set; }
    public long AwardId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string NomineeType { get; set; } = "movie";
    public string? ImdbId { get; set; }
    public string EnrichmentStatus { get; set; } = "pending";
    public int EnrichmentAttemptCount { get; set; }
    public DateTimeOffset? LastEnrichmentAttemptAt { get; set; }
    public DateTimeOffset? NextEnrichmentAttemptAt { get; set; }
    public string? LastEnrichmentError { get; set; }

    public GoldenGlobeAward Award { get; set; } = null!;
}
