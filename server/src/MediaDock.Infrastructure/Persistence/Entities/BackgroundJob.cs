namespace MediaDock.Infrastructure.Persistence.Entities;

public sealed class BackgroundJob
{
    public long Id { get; set; }
    public string JobType { get; set; } = string.Empty;
    public string Trigger { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset EnqueuedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public DateTimeOffset? ScheduledSlotUtc { get; set; }
    public long? ScanRunId { get; set; }
    public string? CurrentStage { get; set; }
    public string? CurrentSource { get; set; }
    public DateTimeOffset? ProgressUpdatedAt { get; set; }
    public string? ErrorCode { get; set; }
    public string? ResultSummary { get; set; }
    public string? InputFileName { get; set; }
    public string? InputContentType { get; set; }
    public byte[]? InputBytes { get; set; }

    public ScanRun? ScanRun { get; set; }
    public ICollection<BackgroundJobEvent> Events { get; } = new List<BackgroundJobEvent>();
}