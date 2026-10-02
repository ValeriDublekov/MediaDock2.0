using MediaDock.Application.Metadata;
using MediaDock.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.Infrastructure.Metadata;

public sealed class PostgresOmdbRequestBudget(MediaDockDbContext dbContext) : IOmdbRequestBudget
{
    private const int DailyRequestSafetyBuffer = 50;

    public async Task<bool> TryReserveAsync(
        DateOnly utcDate,
        OmdbRequestPurpose requestPurpose,
        int dailyRequestLimit,
        int oscarDailyRequestLimit,
        CancellationToken cancellationToken = default)
    {
        if (requestPurpose is not (OmdbRequestPurpose.RssIngestion or OmdbRequestPurpose.OscarEnrichment))
        {
            throw new ArgumentOutOfRangeException(nameof(requestPurpose));
        }

        if (dailyRequestLimit <= 0 || oscarDailyRequestLimit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dailyRequestLimit));
        }

        var isOscarRequest = requestPurpose == OmdbRequestPurpose.OscarEnrichment;
        if (isOscarRequest && oscarDailyRequestLimit == 0)
        {
            return false;
        }

        var effectiveDailyRequestLimit = dailyRequestLimit - DailyRequestSafetyBuffer;
        if (effectiveDailyRequestLimit <= 0)
        {
            return false;
        }

        var oscarIncrement = isOscarRequest ? 1 : 0;
        var affectedRows = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO omdb_daily_usage (utc_date, total_requests, oscar_requests, daily_request_limit_reached)
            VALUES ({utcDate}, 1, {oscarIncrement}, 1 >= {effectiveDailyRequestLimit})
            ON CONFLICT (utc_date) DO UPDATE
            SET total_requests = omdb_daily_usage.total_requests + 1,
                oscar_requests = omdb_daily_usage.oscar_requests + EXCLUDED.oscar_requests,
                daily_request_limit_reached = omdb_daily_usage.total_requests + 1 >= {effectiveDailyRequestLimit}
            WHERE omdb_daily_usage.total_requests < {effectiveDailyRequestLimit}
                            AND NOT omdb_daily_usage.provider_quota_exceeded
              AND ({!isOscarRequest} OR omdb_daily_usage.oscar_requests < {oscarDailyRequestLimit});
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
}