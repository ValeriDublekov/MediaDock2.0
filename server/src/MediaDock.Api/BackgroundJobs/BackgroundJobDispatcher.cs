using System.Data.Common;
using System.Text.Json;
using MediaDock.Application.Ingestion;
using MediaDock.Application.GoldenGlobes;
using MediaDock.Application.OscarAwards;
using MediaDock.Infrastructure.Ingestion;
using MediaDock.Infrastructure.OscarAwards;
using MediaDock.Infrastructure.GoldenGlobes;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MediaDock.Api.BackgroundJobs;

internal sealed class BackgroundJobDispatcher(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<BackgroundJobDispatcher> logger) : BackgroundService
{
    private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(2), timeProvider);
    private bool _recoveredInterruptedJobs;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            do
            {
                try
                {
                    await ProcessNextAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        "Background job dispatcher iteration failed ({ErrorType}).",
                        exception.GetType().Name);
                }
            }
            while (await _timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public override void Dispose()
    {
        _timer.Dispose();
        base.Dispose();
    }

    private async Task ProcessNextAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<MediaDockDbContext>();
        var scanLock = services.GetRequiredService<PostgresAdvisoryScanLock>();
        var lockLease = await scanLock.TryAcquireAsync(stoppingToken);
        if (lockLease is null)
        {
            return;
        }

        await using (lockLease)
        {
            if (!_recoveredInterruptedJobs)
            {
                await RecoverInterruptedJobsAsync(dbContext, stoppingToken);
                _recoveredInterruptedJobs = true;
            }

            await services.GetRequiredService<BackgroundJobScheduler>().EnqueueDueSlotAsync(stoppingToken);
            var job = await ClaimNextAsync(dbContext, timeProvider.GetUtcNow(), stoppingToken);
            if (job is not null)
            {
                await ExecuteJobAsync(services, dbContext, job, stoppingToken);
            }
        }
    }

    private async Task RecoverInterruptedJobsAsync(
        MediaDockDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var interruptedJobs = await dbContext.BackgroundJobs
            .Where(job => job.Status == "running")
            .ToListAsync(cancellationToken);
        if (interruptedJobs.Count == 0)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        foreach (var job in interruptedJobs)
        {
            job.Status = "failed";
            job.FinishedAt = now;
            job.ErrorCode = "interrupted";
            job.CurrentStage = "interrupted";
            job.CurrentSource = null;
            job.ProgressUpdatedAt = now;
            job.InputBytes = null;
            job.Events.Add(new BackgroundJobEvent
            {
                OccurredAt = now,
                Level = "warning",
                EventCode = "job_interrupted",
                Message = "The previous API process stopped before this job completed."
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task<BackgroundJob?> ClaimNextAsync(
        MediaDockDbContext dbContext,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var connection = dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = dbContext.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = """
            UPDATE background_jobs
            SET status = 'running', started_at = @started_at, current_stage = 'starting',
                progress_updated_at = @started_at
            WHERE id = (
                SELECT id FROM background_jobs
                WHERE status = 'queued'
                ORDER BY enqueued_at, id
                FOR UPDATE SKIP LOCKED
                LIMIT 1
            )
            RETURNING id;
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "started_at";
        parameter.Value = now;
        command.Parameters.Add(parameter);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (result is null or DBNull)
        {
            return null;
        }

        var jobId = Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
        dbContext.ChangeTracker.Clear();
        var job = await dbContext.BackgroundJobs.SingleAsync(item => item.Id == jobId, cancellationToken);
        job.Events.Add(new BackgroundJobEvent
        {
            OccurredAt = now,
            Level = "information",
            EventCode = "job_started",
            Message = "Background job started."
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return job;
    }

    private async Task ExecuteJobAsync(
        IServiceProvider services,
        MediaDockDbContext dbContext,
        BackgroundJob job,
        CancellationToken stoppingToken)
    {
        try
        {
            if (job.JobType == "oscar_import")
            {
                await ExecuteOscarImportAsync(services, dbContext, job, stoppingToken);
                return;
            }

            if (job.JobType == "golden_globe_import")
            {
                await ExecuteGoldenGlobeImportAsync(services, dbContext, job, stoppingToken);
                return;
            }

            if (job.JobType == "oscar_enrichment")
            {
                await ExecuteOscarEnrichmentAsync(services, dbContext, job, stoppingToken);
                return;
            }
            if (job.JobType == "golden_globe_enrichment")
            {
                await ExecuteGoldenGlobeEnrichmentAsync(services, dbContext, job, stoppingToken);
                return;
            }

            var failedRecheck = IsFailedRecheck(job.ResultSummary);
            await LoadProviderSettingsAsync(services, dbContext, stoppingToken);
            var ingestion = services.GetRequiredService<RssIngestionService>();
            if (failedRecheck)
            {
                var recheck = await ingestion.RecheckFailedAsync(
                    stoppingToken,
                    (progress, token) => SaveIngestionProgressAsync(dbContext, job, progress, token));
                var recheckStatus = recheck.Run.Summary.Status switch
                {
                    "failed" => "failed",
                    "partial" => "partial",
                    _ => "succeeded"
                };
                await CompleteJobAsync(
                    dbContext,
                    job,
                    recheckStatus,
                    new
                    {
                        scanRunId = recheck.Run.RunId,
                        rss = recheck.Run.Summary,
                        recheck = new
                        {
                            recheck.RetryableEntriesSelected,
                            recheck.EntriesUnavailable
                        }
                    },
                    null,
                    CancellationToken.None);
                return;
            }

            var result = await ingestion.RunAsync(
                job.Trigger,
                stoppingToken,
                (progress, token) => SaveIngestionProgressAsync(dbContext, job, progress, token));

            var overallStatus = result.Summary.Status switch
            {
                "failed" => "failed",
                "partial" => "partial",
                _ => "succeeded"
            };
            await CompleteJobAsync(
                dbContext,
                job,
                overallStatus,
                new { scanRunId = result.RunId, rss = result.Summary },
                null,
                CancellationToken.None);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Background job {JobId} was interrupted during API shutdown.", job.Id);
            await CompleteJobAsync(
                dbContext,
                job,
                "failed",
                null,
                "interrupted",
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            var errorCode = exception is InvalidDataException ? "invalid_dataset" : "operation_failed";
            logger.LogError(
                exception,
                "Background job {JobId} failed ({ErrorType}); safe code {ErrorCode}.",
                job.Id,
                exception.GetType().Name,
                errorCode);
            // The import may have left the request-scoped DbContext with failed tracked
            // entities or a broken transaction. Persist the failure through a fresh context
            // so the original exception is not replaced by a generic fallback message.
            await MarkJobFailedWithFreshContextAsync(
                job.Id,
                errorCode,
                exception.Message,
                exception.GetType().FullName,
                exception.InnerException?.Message);
        }
    }

    private static bool IsFailedRecheck(string? resultSummary)
    {
        if (resultSummary is null)
        {
            return false;
        }

        using var document = JsonDocument.Parse(resultSummary);
        return document.RootElement.TryGetProperty("operation", out var operation)
            && operation.GetString() == "failed_recheck";
    }

    private async Task MarkJobFailedWithFreshContextAsync(
        long jobId,
        string errorCode,
        string? errorMessage = null,
        string? exceptionType = null,
        string? innerError = null)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MediaDockDbContext>();
            var job = await dbContext.BackgroundJobs.SingleOrDefaultAsync(item => item.Id == jobId);
            if (job is null || job.Status is not ("queued" or "running")) return;
            var now = timeProvider.GetUtcNow();
            job.Status = "failed";
            job.FinishedAt = now;
            job.CurrentStage = "failed";
            job.ErrorCode = errorCode;
            job.ProgressUpdatedAt = now;
            job.ResultSummary = JsonSerializer.Serialize(new
            {
                error = errorMessage ?? "The job failed before its error details could be persisted.",
                exceptionType,
                innerError
            });
            job.InputBytes = null;
            await dbContext.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not recover failed state for background job {JobId}.", jobId);
        }
    }

    private async Task LoadProviderSettingsAsync(
        IServiceProvider services,
        MediaDockDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var settings = await dbContext.Settings.AsNoTracking()
            .Where(item => item.Id == 1)
            .Select(item => new
            {
                item.OmdbApiKey,
                item.OmdbDailyRequestLimit
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (settings is null || string.IsNullOrWhiteSpace(settings.OmdbApiKey)
            || settings.OmdbDailyRequestLimit <= 0)
        {
            throw new InvalidOperationException("Provider settings are missing or invalid.");
        }

        services.GetRequiredService<IngestionProviderSettings>().Configure(
            settings.OmdbApiKey,
            settings.OmdbDailyRequestLimit);
    }

    private async Task ExecuteOscarEnrichmentAsync(
        IServiceProvider services,
        MediaDockDbContext dbContext,
        BackgroundJob job,
        CancellationToken cancellationToken)
    {
        await LoadProviderSettingsAsync(services, dbContext, cancellationToken);
        await SetStageAsync(dbContext, job, "oscar_enrichment", "Oscar enrichment started.", cancellationToken);
        var enrichment = await services.GetRequiredService<OscarEnrichmentService>()
            .RunAsync(job.Trigger, cancellationToken);
        var status = enrichment.Status == OscarEnrichmentRunStatuses.Succeeded ? "succeeded" : "partial";
        await CompleteJobAsync(
            dbContext,
            job,
            status,
            new { enrichment.RunId, enrichment.Status, enrichment.Summary },
            null,
            CancellationToken.None);
    }

    private async Task ExecuteGoldenGlobeEnrichmentAsync(IServiceProvider services, MediaDockDbContext dbContext, BackgroundJob job, CancellationToken cancellationToken)
    {
        await LoadProviderSettingsAsync(services, dbContext, cancellationToken);
        await SetStageAsync(dbContext, job, "golden_globe_enrichment", "Golden Globes enrichment started.", cancellationToken);
        var result = await services.GetRequiredService<GoldenGlobeEnrichmentService>().RunAsync(cancellationToken);
        await CompleteJobAsync(dbContext, job, result.Status, result.Summary, null, CancellationToken.None);
    }

    private async Task ExecuteOscarImportAsync(
        IServiceProvider services,
        MediaDockDbContext dbContext,
        BackgroundJob job,
        CancellationToken cancellationToken)
    {
        if (job.InputBytes is null || job.ResultSummary is null)
        {
            throw new InvalidDataException("The queued import payload is unavailable.");
        }

        using var options = JsonDocument.Parse(job.ResultSummary);
        var yearAfter = options.RootElement.GetProperty("yearAfter").GetInt32();
        await SetStageAsync(dbContext, job, "oscar_import", "Oscar dataset import started.", cancellationToken);
        var summary = await services.GetRequiredService<OscarDatasetImporter>()
            .ImportAsync(job.InputBytes, yearAfter, cancellationToken);
        await CompleteJobAsync(dbContext, job, "succeeded", summary, null, CancellationToken.None);
    }

    private async Task ExecuteGoldenGlobeImportAsync(IServiceProvider services, MediaDockDbContext dbContext, BackgroundJob job, CancellationToken cancellationToken)
    {
        if (job.InputBytes is null || job.ResultSummary is null) throw new InvalidDataException("The queued import payload is unavailable.");
        using var options = JsonDocument.Parse(job.ResultSummary);
        var yearAfter = options.RootElement.GetProperty("yearAfter").GetInt32();
        await SetStageAsync(dbContext, job, "golden_globe_import", "Golden Globes dataset import started.", cancellationToken);
        job.CurrentStage = "golden_globe_parsing";
        job.ProgressUpdatedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        var summary = await services.GetRequiredService<GoldenGlobeDatasetImporter>().ImportAsync(job.InputBytes, yearAfter, cancellationToken);
        await CompleteJobAsync(dbContext, job, "succeeded", summary, null, CancellationToken.None);
    }

    private async Task SaveIngestionProgressAsync(
        MediaDockDbContext dbContext,
        BackgroundJob job,
        IngestionProgressUpdate progress,
        CancellationToken cancellationToken)
    {
        job.ScanRunId = progress.ScanRunId;
        job.CurrentStage = progress.Stage;
        job.CurrentSource = progress.Source;
        job.ProgressUpdatedAt = timeProvider.GetUtcNow();
        job.ResultSummary = JsonSerializer.Serialize(new
        {
            feedsProcessed = progress.FeedsProcessed,
            entriesSeen = progress.EntriesSeen,
            knownEntriesSkipped = progress.KnownEntriesSkipped,
            titlesCreated = progress.TitlesCreated,
            occurrencesCreated = progress.OccurrencesCreated,
            cacheHits = progress.CacheHits,
            omdbRequests = progress.OmdbRequests,
            ignoredEntries = progress.IgnoredEntries,
            errorCount = progress.ErrorCount
        });
        var eventData = new
        {
            progress.Source,
            progress.FeedsProcessed,
            progress.EntriesSeen,
            progress.KnownEntriesSkipped,
            progress.TitlesCreated,
            progress.OccurrencesCreated,
            progress.CacheHits,
            progress.OmdbRequests,
            progress.IgnoredEntries,
            progress.ErrorCount
        };
        var message = progress.Stage switch
        {
            "started" => "RSS scan started.",
            "source_started" => "Configured feed started.",
            "source_completed" => "Configured feed completed.",
            "completed" => "RSS scan completed.",
            _ => "RSS scan progress updated."
        };
        job.Events.Add(new BackgroundJobEvent
        {
            OccurredAt = job.ProgressUpdatedAt.Value,
            Level = progress.ErrorCount > 0 ? "warning" : "information",
            EventCode = progress.Stage,
            Message = message,
            DataJson = JsonSerializer.Serialize(eventData)
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SetStageAsync(
        MediaDockDbContext dbContext,
        BackgroundJob job,
        string stage,
        string message,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        job.CurrentStage = stage;
        job.CurrentSource = null;
        job.ProgressUpdatedAt = now;
        job.Events.Add(new BackgroundJobEvent
        {
            OccurredAt = now,
            Level = "information",
            EventCode = "stage_started",
            Message = message
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task CompleteJobAsync(
        MediaDockDbContext dbContext,
        BackgroundJob job,
        string status,
        object? summary,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        job.Status = status;
        job.FinishedAt = now;
        job.CurrentStage = status;
        job.CurrentSource = null;
        job.ProgressUpdatedAt = now;
        job.ErrorCode = errorCode;
        job.ResultSummary = summary is null ? null : JsonSerializer.Serialize(summary);
        job.InputBytes = null;
        job.Events.Add(new BackgroundJobEvent
        {
            OccurredAt = now,
            Level = status == "failed" ? "error" : status == "partial" ? "warning" : "information",
            EventCode = status == "failed" ? errorCode ?? "operation_failed" : $"job_{status}",
            Message = status switch
            {
                "succeeded" => "Background job completed.",
                "partial" => "Background job completed with partial results.",
                _ => "Background job failed or was interrupted."
            }
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
