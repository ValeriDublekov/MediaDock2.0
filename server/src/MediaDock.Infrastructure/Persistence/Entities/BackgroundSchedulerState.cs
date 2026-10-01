namespace MediaDock.Infrastructure.Persistence.Entities;

public sealed class BackgroundSchedulerState
{
    public string ScheduleName { get; set; } = string.Empty;
    public DateTimeOffset LastEvaluatedSlotUtc { get; set; }
}