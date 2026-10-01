namespace MediaDock.Infrastructure.Persistence.Entities;

public sealed class OmdbDailyUsage
{
    public DateOnly UtcDate { get; set; }
    public int TotalRequests { get; set; }
    public int OscarRequests { get; set; }
    public bool ProviderQuotaExceeded { get; set; }
    public bool DailyRequestLimitReached { get; set; }
    public string? LastErrorCode { get; set; }
}