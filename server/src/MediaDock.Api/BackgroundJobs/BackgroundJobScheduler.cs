using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.Api.BackgroundJobs;

public sealed class BackgroundJobScheduler(MediaDockDbContext dbContext, TimeProvider timeProvider)
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
        var trackedState = await dbContext.BackgroundSchedulerStates
            .SingleAsync(item => item.ScheduleName == ScheduleName, cancellationToken);
        trackedState.LastEvaluatedSlotUtc = dueSlot;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
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