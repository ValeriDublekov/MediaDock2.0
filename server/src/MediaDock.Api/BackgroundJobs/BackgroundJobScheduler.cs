using MediaDock.Infrastructure.Persistence;
using MediaDock.Application.GoldenGlobes;
using MediaDock.Application.Metadata;
using MediaDock.Application.OscarAwards;
using MediaDock.Application.PersonalRatings;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.Api.BackgroundJobs;

public sealed class BackgroundJobScheduler(
    MediaDockDbContext dbContext,
    IOscarEnrichmentRepository oscarEnrichmentRepository,
    IGoldenGlobeEnrichmentRepository goldenGlobeEnrichmentRepository,
    IImdbTitleEnrichmentRepository imdbTitleEnrichmentRepository,
    TimeProvider timeProvider)
{
    private const string ScheduleName = "rss_ingestion";
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

        var activeRssJob = await dbContext.BackgroundJobs.AsNoTracking()
            .AnyAsync(job => job.JobType == "rss_scan" && (job.Status == "queued" || job.Status == "running"), cancellationToken);
        if (activeRssJob)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var dueSlot = GetDueSlots(state.LastEvaluatedSlotUtc, now).LastOrDefault();
        if (dueSlot == default)
        {
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
            await EnqueueScheduledEnrichmentIfEligibleAsync(
                "oscar_enrichment",
                "Scheduled Oscar enrichment queued.",
                (await oscarEnrichmentRepository.GetEligibleCandidatesAsync(now, cancellationToken)).Count > 0,
                now,
                cancellationToken);
            await EnqueueScheduledEnrichmentIfEligibleAsync(
                "golden_globe_enrichment",
                "Scheduled Golden Globes enrichment queued.",
                (await goldenGlobeEnrichmentRepository.GetEligibleCandidatesAsync(now, cancellationToken)).Count > 0,
                now,
                cancellationToken);
            await EnqueueScheduledEnrichmentIfEligibleAsync(
                "imdb_catalog_enrichment",
                "Scheduled IMDb catalog enrichment queued.",
                (await imdbTitleEnrichmentRepository.GetEligibleCatalogCandidatesAsync(now, 1, cancellationToken)).Count > 0,
                now,
                cancellationToken);
        }

        var trackedState = await dbContext.BackgroundSchedulerStates
            .SingleAsync(item => item.ScheduleName == ScheduleName, cancellationToken);
        trackedState.LastEvaluatedSlotUtc = dueSlot;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task EnqueueScheduledEnrichmentIfEligibleAsync(
        string jobType,
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
            Trigger = "schedule",
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