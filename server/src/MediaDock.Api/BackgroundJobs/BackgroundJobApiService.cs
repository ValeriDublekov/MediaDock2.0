using System.Text.Json;
using MediaDock.Api.Middleware;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediaDock.Api.BackgroundJobs;

internal sealed class BackgroundJobApiService(
    MediaDockDbContext dbContext,
    TimeProvider timeProvider,
    IConfiguration configuration)
{
    private const int DefaultMaximumUploadBytes = 10 * 1024 * 1024;
    private readonly int _maximumUploadBytes = Math.Clamp(
        configuration.GetValue("BackgroundJobs:MaxUploadBytes", DefaultMaximumUploadBytes),
        1,
        100 * 1024 * 1024);

    public async Task<BackgroundJobResponse?> GetActiveAsync(CancellationToken cancellationToken)
    {
        var job = await dbContext.BackgroundJobs.AsNoTracking()
            .Where(job => job.Status == "queued" || job.Status == "running")
            .OrderByDescending(job => job.EnqueuedAt)
            .Select(job => new BackgroundJobStatusProjection(
                job.Id,
                job.JobType,
                job.Trigger,
                job.Status,
                job.EnqueuedAt,
                job.StartedAt,
                job.FinishedAt,
                job.CurrentStage,
                job.CurrentSource,
                job.ProgressUpdatedAt,
                job.ErrorCode,
                job.ResultSummary,
                job.ScanRunId,
                job.InputFileName))
            .FirstOrDefaultAsync(cancellationToken);
        return job is null ? null : ToResponse(job);
    }

    public async Task<BackgroundJobResponse> GetAsync(long id, CancellationToken cancellationToken)
    {
        var job = await dbContext.BackgroundJobs.AsNoTracking()
            .Where(job => job.Id == id)
            .Select(job => new BackgroundJobStatusProjection(
                job.Id,
                job.JobType,
                job.Trigger,
                job.Status,
                job.EnqueuedAt,
                job.StartedAt,
                job.FinishedAt,
                job.CurrentStage,
                job.CurrentSource,
                job.ProgressUpdatedAt,
                job.ErrorCode,
                job.ResultSummary,
                job.ScanRunId,
                job.InputFileName))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ApiNotFoundException("Background job not found.");
        return ToResponse(job);
    }

    public async Task<BackgroundJobEventsResponse> GetEventsAsync(
        long id,
        BackgroundJobEventQuery query,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.BackgroundJobs.AnyAsync(job => job.Id == id, cancellationToken))
        {
            throw new ApiNotFoundException("Background job not found.");
        }

        var records = await dbContext.BackgroundJobEvents.AsNoTracking()
            .Where(jobEvent => jobEvent.JobId == id && jobEvent.Id > query.AfterId)
            .OrderBy(jobEvent => jobEvent.Id)
            .Take(query.PageSize)
            .Select(jobEvent => new
            {
                jobEvent.Id,
                jobEvent.OccurredAt,
                jobEvent.Level,
                jobEvent.EventCode,
                jobEvent.Message,
                jobEvent.DataJson
            })
            .ToListAsync(cancellationToken);
        var events = records.Select(jobEvent => new BackgroundJobEventResponse(
            jobEvent.Id,
            jobEvent.OccurredAt,
            jobEvent.Level,
            jobEvent.EventCode,
            jobEvent.Message,
            ParseJson(jobEvent.DataJson))).ToArray();

        return new BackgroundJobEventsResponse(events, events.Length == 0 ? query.AfterId : events[^1].Id);
    }

    public async Task<EnqueuedBackgroundJob> EnqueueScanAsync(CancellationToken cancellationToken)
        => await EnqueueRssJobAsync(false, cancellationToken);

    public async Task<EnqueuedBackgroundJob> EnqueueFailedRecheckAsync(CancellationToken cancellationToken)
        => await EnqueueRssJobAsync(true, cancellationToken);

    private async Task<EnqueuedBackgroundJob> EnqueueRssJobAsync(
        bool failedRecheck,
        CancellationToken cancellationToken)
    {
        var active = await dbContext.BackgroundJobs.AsNoTracking()
            .Where(job => job.JobType == "rss_scan" && (job.Status == "queued" || job.Status == "running"))
            .OrderByDescending(job => job.EnqueuedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (active is not null)
        {
            return new EnqueuedBackgroundJob(ToResponse(active), false);
        }

        var now = timeProvider.GetUtcNow();
        var job = new BackgroundJob
        {
            JobType = "rss_scan",
            Trigger = "manual",
            Status = "queued",
            EnqueuedAt = now,
            CurrentStage = failedRecheck ? "recheck_queued" : null,
            ResultSummary = failedRecheck
                ? JsonSerializer.Serialize(new { operation = "failed_recheck" })
                : null
        };
        job.Events.Add(new BackgroundJobEvent
        {
            OccurredAt = now,
            Level = "information",
            EventCode = "job_queued",
            Message = failedRecheck ? "Failed torrent recheck queued." : "Manual scan queued."
        });
        dbContext.BackgroundJobs.Add(job);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new EnqueuedBackgroundJob(ToResponse(job), true);
        }
        catch (DbUpdateException exception) when (IsActiveScanConflict(exception))
        {
            dbContext.ChangeTracker.Clear();
            var existing = await dbContext.BackgroundJobs.AsNoTracking()
                .Where(item => item.JobType == "rss_scan" && (item.Status == "queued" || item.Status == "running"))
                .OrderByDescending(item => item.EnqueuedAt)
                .SingleAsync(cancellationToken);
            return new EnqueuedBackgroundJob(ToResponse(existing), false);
        }
    }

    public async Task<EnqueuedBackgroundJob> EnqueueOscarEnrichmentAsync(CancellationToken cancellationToken)
    {
        var active = await dbContext.BackgroundJobs.AsNoTracking()
            .Where(job => job.JobType == "oscar_enrichment" && (job.Status == "queued" || job.Status == "running"))
            .OrderByDescending(job => job.EnqueuedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (active is not null)
        {
            return new EnqueuedBackgroundJob(ToResponse(active), false);
        }

        var now = timeProvider.GetUtcNow();
        var job = new BackgroundJob
        {
            JobType = "oscar_enrichment",
            Trigger = "manual",
            Status = "queued",
            EnqueuedAt = now,
            CurrentStage = "queued"
        };
        job.Events.Add(new BackgroundJobEvent
        {
            OccurredAt = now,
            Level = "information",
            EventCode = "job_queued",
            Message = "Oscar enrichment queued."
        });
        dbContext.BackgroundJobs.Add(job);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new EnqueuedBackgroundJob(ToResponse(job), true);
        }
        catch (DbUpdateException exception) when (IsActiveOscarEnrichmentConflict(exception))
        {
            dbContext.ChangeTracker.Clear();
            var existing = await dbContext.BackgroundJobs.AsNoTracking()
                .Where(item => item.JobType == "oscar_enrichment" && (item.Status == "queued" || item.Status == "running"))
                .OrderByDescending(item => item.EnqueuedAt)
                .SingleAsync(cancellationToken);
            return new EnqueuedBackgroundJob(ToResponse(existing), false);
        }
    }

    public async Task<BackgroundJobResponse> EnqueueOscarImportAsync(
        OscarImportForm form,
        CancellationToken cancellationToken)
    {
        if (form.File is not { } file || file.Length == 0)
        {
            throw ValidationError("file", "Select a non-empty CSV or TSV file.");
        }

        if (file.Length > _maximumUploadBytes)
        {
            throw ValidationError("file", $"The upload exceeds the {_maximumUploadBytes}-byte limit.");
        }

        if (form.YearAfter is < 0 or >= 9999)
        {
            throw ValidationError("yearAfter", "YearAfter must be between 0 and 9998.");
        }

        var fileName = SanitizeFileName(file.FileName);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not (".csv" or ".tsv")
            || file.ContentType is not ("text/csv" or "application/csv" or "text/tab-separated-values"
                or "application/vnd.ms-excel" or "application/octet-stream"))
        {
            throw ValidationError("file", "Only CSV or tab-separated CSV uploads are supported.");
        }

        await using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0 || buffer.Length > _maximumUploadBytes)
        {
            throw ValidationError("file", "The upload is empty or exceeds the configured size limit.");
        }

        var now = timeProvider.GetUtcNow();
        var job = new BackgroundJob
        {
            JobType = "oscar_import",
            Trigger = "manual",
            Status = "queued",
            EnqueuedAt = now,
            CurrentStage = "queued",
            InputFileName = fileName,
            InputContentType = file.ContentType,
            InputBytes = buffer.ToArray(),
            ResultSummary = JsonSerializer.Serialize(new { yearAfter = form.YearAfter })
        };
        job.Events.Add(new BackgroundJobEvent
        {
            OccurredAt = now,
            Level = "information",
            EventCode = "job_queued",
            Message = "Oscar dataset import queued."
        });
        dbContext.BackgroundJobs.Add(job);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(job);
    }

    public async Task<EnqueuedBackgroundJob> EnqueueGoldenGlobeEnrichmentAsync(CancellationToken cancellationToken)
    {
        var active = await dbContext.BackgroundJobs.AsNoTracking().Where(job => job.JobType == "golden_globe_enrichment" && (job.Status == "queued" || job.Status == "running")).OrderByDescending(job => job.EnqueuedAt).FirstOrDefaultAsync(cancellationToken);
        if (active is not null) return new(ToResponse(active), false);
        var now = timeProvider.GetUtcNow();
        var job = new BackgroundJob { JobType = "golden_globe_enrichment", Trigger = "manual", Status = "queued", EnqueuedAt = now, CurrentStage = "queued" };
        job.Events.Add(new BackgroundJobEvent { OccurredAt = now, Level = "information", EventCode = "job_queued", Message = "Golden Globes enrichment queued." });
        dbContext.BackgroundJobs.Add(job);
        try { await dbContext.SaveChangesAsync(cancellationToken); return new(ToResponse(job), true); }
        catch (DbUpdateException exception) when (IsActiveGoldenGlobeEnrichmentConflict(exception))
        {
            dbContext.ChangeTracker.Clear();
            var existing = await dbContext.BackgroundJobs.AsNoTracking().Where(item => item.JobType == "golden_globe_enrichment" && (item.Status == "queued" || item.Status == "running")).OrderByDescending(item => item.EnqueuedAt).SingleAsync(cancellationToken);
            return new(ToResponse(existing), false);
        }
    }

    public async Task<BackgroundJobResponse> EnqueueGoldenGlobeImportAsync(
        GoldenGlobeImportForm form,
        CancellationToken cancellationToken)
    {
        if (form.File is not { } file || file.Length == 0) throw ValidationError("file", "Select a non-empty CSV or TSV file.");
        if (file.Length > _maximumUploadBytes) throw ValidationError("file", $"The upload exceeds the {_maximumUploadBytes}-byte limit.");
        if (form.YearAfter is < 0 or >= 9999) throw ValidationError("yearAfter", "YearAfter must be between 0 and 9998.");
        var fileName = SanitizeFileName(file.FileName);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not (".csv" or ".tsv")) throw ValidationError("file", "Only CSV or tab-separated CSV uploads are supported.");
        await using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var job = new BackgroundJob { JobType = "golden_globe_import", Trigger = "manual", Status = "queued", EnqueuedAt = now, CurrentStage = "queued", InputFileName = fileName, InputContentType = file.ContentType, InputBytes = buffer.ToArray(), ResultSummary = JsonSerializer.Serialize(new { yearAfter = form.YearAfter }) };
        job.Events.Add(new BackgroundJobEvent { OccurredAt = now, Level = "information", EventCode = "job_queued", Message = "Golden Globes dataset import queued." });
        dbContext.BackgroundJobs.Add(job); await dbContext.SaveChangesAsync(cancellationToken); return ToResponse(job);
    }

    private static BackgroundJobResponse ToResponse(BackgroundJobStatusProjection job) => new(
        job.Id,
        job.JobType,
        job.Trigger,
        job.Status,
        job.EnqueuedAt,
        job.StartedAt,
        job.FinishedAt,
        job.CurrentStage,
        job.CurrentSource,
        job.ProgressUpdatedAt,
        job.ErrorCode,
        ParseJson(job.ResultSummary),
        job.ScanRunId,
        job.InputFileName);

    private static BackgroundJobResponse ToResponse(BackgroundJob job) => new(
        job.Id,
        job.JobType,
        job.Trigger,
        job.Status,
        job.EnqueuedAt,
        job.StartedAt,
        job.FinishedAt,
        job.CurrentStage,
        job.CurrentSource,
        job.ProgressUpdatedAt,
        job.ErrorCode,
        ParseJson(job.ResultSummary),
        job.ScanRunId,
        job.InputFileName);

    private sealed record BackgroundJobStatusProjection(
        long Id,
        string JobType,
        string Trigger,
        string Status,
        DateTimeOffset EnqueuedAt,
        DateTimeOffset? StartedAt,
        DateTimeOffset? FinishedAt,
        string? CurrentStage,
        string? CurrentSource,
        DateTimeOffset? ProgressUpdatedAt,
        string? ErrorCode,
        string? ResultSummary,
        long? ScanRunId,
        string? InputFileName);

    private static JsonElement? ParseJson(string? value)
    {
        if (value is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }

    private static ApiValidationException ValidationError(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });

    private static string SanitizeFileName(string? fileName)
    {
        var safeName = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/'));
        safeName = new string(safeName.Where(character => !char.IsControl(character)).ToArray()).Trim();
        return safeName.Length <= 255 ? safeName : safeName[^255..];
    }

    private static bool IsActiveScanConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_background_jobs_active_rss_scan"
        };

    private static bool IsActiveOscarEnrichmentConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_background_jobs_active_oscar_enrichment"
        };

    private static bool IsActiveGoldenGlobeEnrichmentConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_background_jobs_active_golden_globe_enrichment" };
}
