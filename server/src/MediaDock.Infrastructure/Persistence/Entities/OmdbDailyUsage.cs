namespace MediaDock.Infrastructure.Persistence.Entities;

public sealed class OmdbDailyUsage
{
    public DateOnly UtcDate { get; set; }
    public int TotalRequests { get; set; }
    public int TorrentRequests { get; set; }
    public int OscarRequests { get; set; }
    public bool ProviderQuotaExceeded { get; set; }
    public bool DailyRequestLimitReached { get; set; }
    public string? LastErrorCode { get; set; }
    public DateTimeOffset? LastProviderRequestAt { get; set; }
    public DateTimeOffset? LastProviderSuccessAt { get; set; }
    public DateTimeOffset? LastProviderErrorAt { get; set; }
    public DateTimeOffset? ProviderQuotaExceededAt { get; set; }
    public DateTimeOffset? QuotaRecoveryAttemptedAt { get; set; }
    public DateTimeOffset? QuotaRecoverySucceededAt { get; set; }
}