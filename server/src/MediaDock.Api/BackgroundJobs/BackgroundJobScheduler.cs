using MediaDock.Infrastructure.Persistence;
using MediaDock.Application.GoldenGlobes;
using MediaDock.Application.Metadata;
using MediaDock.Application.OscarAwards;
using MediaDock.Application.PersonalRatings;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace MediaDock.Api.BackgroundJobs;

public sealed class BackgroundJobScheduler(
    MediaDockDbContext dbContext,
    IOscarEnrichmentRepository oscarEnrichmentRepository,
    IGoldenGlobeEnrichmentRepository goldenGlobeEnrichmentRepository,
    IImdbTitleEnrichmentRepository imdbTitleEnrichmentRepository,
    TimeProvider timeProvider)
{
    private const string ScheduleName = "rss_ingestion";
    private const string ImdbRatingScheduleName = "imdb_rating_refresh";
    private static readonly TimeZoneInfo SofiaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Sofia");

    public async Task EnqueueDueSlotAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var initialCheckpoint = GetLatestDueSlot(now);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO background_scheduler_state (schedule_name, last_evaluated_slot_utc)
            VALUES ({ScheduleName}, {initialCheckpoint})
            ON CONFLICT (schedule_name) DO NOTHING;
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE background_scheduler_state SET schedule_name = schedule_name WHERE schedule_name = 'rss_ingestion';",
            cancellationToken);
        var state = await dbContext.BackgroundSchedulerStates.AsNoTracking()
            .SingleAsync(item => item.ScheduleName == ScheduleName, cancellationToken);

        await EnqueueQuotaRecoveryIfDueAsync(now, cancellationToken);
        await EnqueueImdbRatingRefreshIfDueAsync(now, cancellationToken);

        var activeRssJob = await dbContext.BackgroundJobs.AsNoTracking()
            .AnyAsync(job => job.JobType == "rss_scan" && (job.Status == "queued" || job.Status == "running"), cancellationToken);
        if (activeRssJob)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var dueSlot = GetDueSlots(state.LastEvaluatedSlotUtc, now).LastOrDefault();
        if (dueSlot == default)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var job = new BackgroundJob
        {
            JobType = "rss_scan",
            Trigger = "schedule",
            Status = "queued",
            EnqueuedAt = now,
            ScheduledSlotUtc = dueSlot,
            CurrentStage = "queued"
        };
        job.Events.Add(new BackgroundJobEvent
        {
            OccurredAt = now,
            Level = "information",
            EventCode = "job_scheduled",
            Message = "Scheduled RSS scan queued.",
            DataJson = "{}"
        });
        dbContext.BackgroundJobs.Add(job);

        if (await HasDailyOmdbBudgetAsync(now, cancellationToken))
        {
            await EnqueueEligibleEnrichmentJobsAsync(now, "schedule", cancellationToken);
        }

        var trackedState = await dbContext.BackgroundSchedulerStates
            .SingleAsync(item => item.ScheduleName == ScheduleName, cancellationToken);
        trackedState.LastEvaluatedSlotUtc = dueSlot;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task EnqueueEnrichmentJobsAfterQuotaRecoveryAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (await HasDailyOmdbBudgetAsync(now, cancellationToken))
        {
            await EnqueueEligibleEnrichmentJobsAsync(now, "quota_recovery", cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task EnqueueImdbRatingRefreshIfDueAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var latestSlot = GetLatestImdbRatingSlot(now);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO background_scheduler_state (schedule_name, last_evaluated_slot_utc)
            VALUES ({ImdbRatingScheduleName}, {latestSlot})
            ON CONFLICT (schedule_name) DO NOTHING;
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE background_scheduler_state SET schedule_name = schedule_name WHERE schedule_name = {ImdbRatingScheduleName};",
            cancellationToken);
        var state = await dbContext.BackgroundSchedulerStates
            .SingleAsync(item => item.ScheduleName == ImdbRatingScheduleName, cancellationToken);
        if (latestSlot <= state.LastEvaluatedSlotUtc || await dbContext.BackgroundJobs.AsNoTracking()
                .AnyAsync(job => job.JobType == "imdb_rating_refresh"
                    && (job.Status == "queued" || job.Status == "running"), cancellationToken))
        {
            return;
        }

        var job = new BackgroundJob
        {
            JobType = "imdb_rating_refresh",
            Trigger = "schedule",
            Status = "queued",
            EnqueuedAt = now,
            ScheduledSlotUtc = latestSlot,
            CurrentStage = "queued"
        };
        job.Events.Add(new BackgroundJobEvent
        {
            OccurredAt = now,
            Level = "information",
            EventCode = "job_scheduled",
            Message = "Scheduled IMDb ratings refresh queued.",
            DataJson = "{}"
        });
        dbContext.BackgroundJobs.Add(job);
        state.LastEvaluatedSlotUtc = latestSlot;
    }

    private async Task EnqueueQuotaRecoveryIfDueAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var quotaEvent = await dbContext.OmdbDailyUsage.AsNoTracking()
            .Where(item => item.ProviderQuotaExceededAt != null)
            .OrderByDescending(item => item.ProviderQuotaExceededAt)
            .Select(item => new
            {
                item.UtcDate,
                item.ProviderQuotaExceededAt,
                item.QuotaRecoveryAttemptedAt
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (quotaEvent?.ProviderQuotaExceededAt is not DateTimeOffset quotaAt)
        {
            return;
        }

        var lastSuccessAt = await dbContext.OmdbDailyUsage.AsNoTracking()
            .Where(item => item.LastProviderSuccessAt != null)
            .OrderByDescending(item => item.LastProviderSuccessAt)
            .Select(item => item.LastProviderSuccessAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (lastSuccessAt >= quotaAt)
        {
            return;
        }

        var lastErrorAt = await dbContext.OmdbDailyUsage.AsNoTracking()
            .Where(item => item.LastProviderErrorAt != null)
            .OrderByDescending(item => item.LastProviderErrorAt)
            .Select(item => item.LastProviderErrorAt)
            .FirstOrDefaultAsync(cancellationToken);
        var lastAttemptAt = await dbContext.OmdbDailyUsage.AsNoTracking()
            .Where(item => item.QuotaRecoveryAttemptedAt != null)
            .OrderByDescending(item => item.QuotaRecoveryAttemptedAt)
            .Select(item => item.QuotaRecoveryAttemptedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var retryAfter = quotaAt;
        if (lastErrorAt > retryAfter)
        {
            retryAfter = lastErrorAt.Value;
        }
        if (lastAttemptAt > retryAfter)
        {
            retryAfter = lastAttemptAt.Value;
        }

        var dueAt = GetNextQuotaRecoverySlot(retryAfter);
        if (now < dueAt || await dbContext.BackgroundJobs.AsNoTracking()
                .AnyAsync(job => job.JobType == "omdb_quota_recovery"
                    && (job.Status == "queued" || job.Status == "running"), cancellationToken))
        {
            return;
        }

        var job = new BackgroundJob
        {
            JobType = "omdb_quota_recovery",
            Trigger = "schedule",
            Status = "queued",
            EnqueuedAt = now,
            CurrentStage = "queued",
            ResultSummary = JsonSerializer.Serialize(new { quotaUtcDate = quotaEvent.UtcDate.ToString("yyyy-MM-dd") })
        };
        job.Events.Add(new BackgroundJobEvent
        {
            OccurredAt = now,
            Level = "information",
            EventCode = "omdb_quota_recovery_scheduled",
            Message = "OMDb quota recovery probe queued.",
            DataJson = "{}"
        });
        dbContext.BackgroundJobs.Add(job);
    }

    private async Task EnqueueEligibleEnrichmentJobsAsync(
        DateTimeOffset now,
        string trigger,
        CancellationToken cancellationToken)
    {
        await EnqueueEnrichmentIfEligibleAsync(
            "oscar_enrichment",
            trigger,
            trigger == "schedule" ? "Scheduled Oscar enrichment queued." : "Oscar enrichment requeued after OMDb quota recovery.",
            (await oscarEnrichmentRepository.GetEligibleCandidatesAsync(now, cancellationToken)).Count > 0,
            now,
            cancellationToken);
        await EnqueueEnrichmentIfEligibleAsync(
            "golden_globe_enrichment",
            trigger,
            trigger == "schedule" ? "Scheduled Golden Globes enrichment queued." : "Golden Globes enrichment requeued after OMDb quota recovery.",
            (await goldenGlobeEnrichmentRepository.GetEligibleCandidatesAsync(now, cancellationToken)).Count > 0,
            now,
            cancellationToken);
        await EnqueueEnrichmentIfEligibleAsync(
            "imdb_catalog_enrichment",
            trigger,
            trigger == "schedule" ? "Scheduled IMDb catalog enrichment queued." : "IMDb catalog enrichment requeued after OMDb quota recovery.",
            (await imdbTitleEnrichmentRepository.GetEligibleCatalogCandidatesAsync(now, 1, cancellationToken)).Count > 0,
            now,
            cancellationToken);
    }

    private async Task EnqueueEnrichmentIfEligibleAsync(
        string jobType,
        string trigger,
        string message,
        bool hasEligibleCandidates,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!hasEligibleCandidates || await dbContext.BackgroundJobs.AsNoTracking()
                .AnyAsync(job => job.JobType == jobType
                    && (job.Status == "queued" || job.Status == "running"), cancellationToken))
        {
            return;
        }

        var job = new BackgroundJob
        {
            JobType = jobType,
            Trigger = trigger,
            Status = "queued",
            EnqueuedAt = now,
            CurrentStage = "queued"
        };
        job.Events.Add(new BackgroundJobEvent
        {
            OccurredAt = now,
            Level = "information",
            EventCode = "job_scheduled",
            Message = message,
            DataJson = "{}"
        });
        dbContext.BackgroundJobs.Add(job);
    }

    private async Task<bool> HasDailyOmdbBudgetAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var dailyRequestLimit = await dbContext.Settings.AsNoTracking()
            .Where(settings => settings.Id == 1)
            .Select(settings => settings.OmdbDailyRequestLimit)
            .SingleOrDefaultAsync(cancellationToken);
        dailyRequestLimit = Math.Min(dailyRequestLimit, OmdbRequestBudgetLimits.MaximumDailyRequests);
        if (dailyRequestLimit <= 0)
        {
            return false;
        }

        var utcDate = DateOnly.FromDateTime(now.UtcDateTime);
        var usage = await dbContext.OmdbDailyUsage.AsNoTracking()
            .Where(item => item.UtcDate == utcDate)
            .Select(item => new { item.TotalRequests, item.TorrentRequests, item.ProviderQuotaExceeded })
            .SingleOrDefaultAsync(cancellationToken);
        return usage is null
            || !usage.ProviderQuotaExceeded
                && usage.TotalRequests < dailyRequestLimit
                && usage.TotalRequests - usage.TorrentRequests < OmdbRequestBudgetLimits.MaximumDailyEnrichmentRequests;
    }

    public static IReadOnlyList<DateTimeOffset> GetDueSlots(DateTimeOffset after, DateTimeOffset through)
    {
        if (through <= after)
        {
            return [];
        }

        var firstLocalDate = TimeZoneInfo.ConvertTime(after, SofiaTimeZone).Date.AddDays(-1);
        var lastLocalDate = TimeZoneInfo.ConvertTime(through, SofiaTimeZone).Date;
        var slots = new List<DateTimeOffset>();
        for (var date = firstLocalDate; date <= lastLocalDate; date = date.AddDays(1))
        {
            foreach (var hour in new[] { 7, 18 })
            {
                var slot = ConvertLocalSlotToUtc(date.AddHours(hour));
                if (slot > after && slot <= through)
                {
                    slots.Add(slot);
                }
            }
        }

        return slots.Order().ToArray();
    }

    public static DateTimeOffset GetNextQuotaRecoverySlot(DateTimeOffset after)
    {
        var localTime = TimeZoneInfo.ConvertTime(after, SofiaTimeZone);
        var slot = localTime.Date.AddHours(9);
        if (localTime.TimeOfDay >= TimeSpan.FromHours(9))
        {
            slot = slot.AddDays(1);
        }

        return ConvertLocalSlotToUtc(slot);
    }

    public static DateTimeOffset GetLatestImdbRatingSlot(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, SofiaTimeZone);
        var daysSinceWednesday = ((int)local.DayOfWeek - (int)DayOfWeek.Wednesday + 7) % 7;
        var localSlot = local.Date.AddDays(-daysSinceWednesday).AddHours(3);
        var slot = ConvertLocalSlotToUtc(localSlot);
        return slot <= now ? slot : ConvertLocalSlotToUtc(localSlot.AddDays(-7));
    }

    private static DateTimeOffset GetLatestDueSlot(DateTimeOffset now) =>
        GetDueSlots(now.AddDays(-3), now).Last();

    private static DateTimeOffset ConvertLocalSlotToUtc(DateTime localTime)
    {
        localTime = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
        while (SofiaTimeZone.IsInvalidTime(localTime))
        {
            localTime = localTime.AddMinutes(1);
        }

        var utcTime = SofiaTimeZone.IsAmbiguousTime(localTime)
            ? new DateTimeOffset(localTime, SofiaTimeZone.GetAmbiguousTimeOffsets(localTime).Max()).UtcDateTime
            : TimeZoneInfo.ConvertTimeToUtc(localTime, SofiaTimeZone);
        return new DateTimeOffset(utcTime, TimeSpan.Zero);
    }
}