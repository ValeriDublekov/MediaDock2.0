namespace MediaDock.Infrastructure.Persistence.Entities;

public sealed class RssItemProcessingState
{
    public long SourceId { get; set; }
    public string SourceItemKey { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public string Disposition { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}