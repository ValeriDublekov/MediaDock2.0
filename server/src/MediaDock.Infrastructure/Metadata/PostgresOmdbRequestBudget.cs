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
        CancellationToken cancellationToken = default)
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
            INSERT INTO omdb_daily_usage (utc_date, total_requests, torrent_requests, oscar_requests, daily_request_limit_reached)
            VALUES ({utcDate}, 1, {torrentIncrement}, {oscarIncrement}, 1 >= {dailyRequestLimit})
            ON CONFLICT (utc_date) DO UPDATE
            SET total_requests = omdb_daily_usage.total_requests + 1,
                torrent_requests = omdb_daily_usage.torrent_requests + EXCLUDED.torrent_requests,
                oscar_requests = omdb_daily_usage.oscar_requests + EXCLUDED.oscar_requests,
                daily_request_limit_reached = omdb_daily_usage.total_requests + 1 >= {dailyRequestLimit}
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
        CancellationToken cancellationToken = default)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE omdb_daily_usage SET last_error_code = {errorCode}, provider_quota_exceeded = provider_quota_exceeded OR {providerQuotaExceeded} WHERE utc_date = {utcDate};",
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
