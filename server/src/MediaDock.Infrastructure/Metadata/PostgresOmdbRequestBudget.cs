using MediaDock.Application.Metadata;
using MediaDock.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.Infrastructure.Metadata;

public sealed class PostgresOmdbRequestBudget(MediaDockDbContext dbContext) : IOmdbRequestBudget
{
    public async Task<bool> TryReserveAsync(
        DateOnly utcDate,
        OmdbRequestPurpose requestPurpose,
        int dailyRequestLimit,
        CancellationToken cancellationToken = default,
        DateTimeOffset? requestAt = null)
    {
        if (requestPurpose is not (OmdbRequestPurpose.RssIngestion
            or OmdbRequestPurpose.OscarEnrichment
            or OmdbRequestPurpose.GoldenGlobeEnrichment
            or OmdbRequestPurpose.ImdbTitleEnrichment
            or OmdbRequestPurpose.InteractiveLookup
            or OmdbRequestPurpose.InteractiveDiagnostic))
        {
            throw new ArgumentOutOfRangeException(nameof(requestPurpose));
        }

        if (dailyRequestLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dailyRequestLimit));
        }

        dailyRequestLimit = Math.Min(dailyRequestLimit, OmdbRequestBudgetLimits.MaximumDailyRequests);
        var isTorrentRequest = requestPurpose == OmdbRequestPurpose.RssIngestion;
        var bypassBudget = requestPurpose == OmdbRequestPurpose.InteractiveDiagnostic;
        var torrentIncrement = isTorrentRequest ? 1 : 0;
        var oscarIncrement = requestPurpose == OmdbRequestPurpose.OscarEnrichment ? 1 : 0;
        var affectedRows = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO omdb_daily_usage (utc_date, total_requests, torrent_requests, oscar_requests, daily_request_limit_reached, last_provider_request_at)
            VALUES ({utcDate}, 1, {torrentIncrement}, {oscarIncrement}, 1 >= {dailyRequestLimit}, {requestAt ?? DateTimeOffset.UtcNow})
            ON CONFLICT (utc_date) DO UPDATE
            SET total_requests = omdb_daily_usage.total_requests + 1,
                torrent_requests = omdb_daily_usage.torrent_requests + EXCLUDED.torrent_requests,
                oscar_requests = omdb_daily_usage.oscar_requests + EXCLUDED.oscar_requests,
                daily_request_limit_reached = omdb_daily_usage.total_requests + 1 >= {dailyRequestLimit},
                last_provider_request_at = EXCLUDED.last_provider_request_at
                WHERE {bypassBudget}
                   OR (
                    omdb_daily_usage.total_requests < {dailyRequestLimit}
                AND NOT omdb_daily_usage.provider_quota_exceeded
                AND ({isTorrentRequest} OR omdb_daily_usage.total_requests - omdb_daily_usage.torrent_requests < {OmdbRequestBudgetLimits.MaximumDailyEnrichmentRequests})
                   );
            """,
            cancellationToken);

        return affectedRows == 1;
    }

    public async Task RecordProviderErrorAsync(
        DateOnly utcDate,
        string errorCode,
        bool providerQuotaExceeded,
        CancellationToken cancellationToken = default,
        DateTimeOffset? errorAt = null)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE omdb_daily_usage SET last_error_code = {errorCode}, last_provider_error_at = {errorAt ?? DateTimeOffset.UtcNow}, provider_quota_exceeded = provider_quota_exceeded OR {providerQuotaExceeded}, provider_quota_exceeded_at = CASE WHEN {providerQuotaExceeded} THEN {errorAt ?? DateTimeOffset.UtcNow} ELSE provider_quota_exceeded_at END, quota_recovery_attempted_at = CASE WHEN {providerQuotaExceeded} THEN NULL ELSE quota_recovery_attempted_at END, quota_recovery_succeeded_at = CASE WHEN {providerQuotaExceeded} THEN NULL ELSE quota_recovery_succeeded_at END WHERE utc_date = {utcDate};",
            cancellationToken);
    }

    public async Task RecordProviderSuccessAsync(
        DateOnly utcDate,
        CancellationToken cancellationToken = default,
        DateTimeOffset? completedAt = null)
    {
        var successAt = completedAt ?? DateTimeOffset.UtcNow;
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE omdb_daily_usage SET last_provider_success_at = {successAt}, last_error_code = NULL WHERE utc_date = {utcDate};",
            cancellationToken);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE omdb_daily_usage SET quota_recovery_succeeded_at = {successAt} WHERE provider_quota_exceeded_at <= {successAt} AND (quota_recovery_succeeded_at IS NULL OR quota_recovery_succeeded_at < provider_quota_exceeded_at);",
            cancellationToken);
    }

    public async Task MarkQuotaRecoveryAttemptedAsync(
        DateOnly utcDate,
        DateTimeOffset attemptedAt,
        CancellationToken cancellationToken = default)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE omdb_daily_usage SET quota_recovery_attempted_at = {attemptedAt} WHERE utc_date = {utcDate};",
            cancellationToken);
    }

    public async Task MarkQuotaRecoverySucceededAsync(
        DateOnly utcDate,
        DateTimeOffset succeededAt,
        CancellationToken cancellationToken = default)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE omdb_daily_usage SET quota_recovery_succeeded_at = {succeededAt} WHERE utc_date = {utcDate};",
            cancellationToken);
    }

    public async Task ResetDailyUsageAsync(
        DateOnly utcDate,
        CancellationToken cancellationToken = default)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE omdb_daily_usage
            SET total_requests = 0,
                torrent_requests = 0,
                oscar_requests = 0,
                provider_quota_exceeded = FALSE,
                daily_request_limit_reached = FALSE,
                last_error_code = NULL
            WHERE utc_date = {utcDate};
            """,
            cancellationToken);
    }
}
