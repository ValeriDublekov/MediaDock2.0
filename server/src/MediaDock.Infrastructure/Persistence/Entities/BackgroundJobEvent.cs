namespace MediaDock.Infrastructure.Persistence.Entities;

public sealed class BackgroundJobEvent
{
    public long Id { get; set; }
    public long JobId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string Level { get; set; } = string.Empty;
    public string EventCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? DataJson { get; set; }

    public BackgroundJob Job { get; set; } = null!;
}