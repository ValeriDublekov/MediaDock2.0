using System.Text.Json;
using MediaDock.Application.GoldenGlobes;
using MediaDock.Application.Ingestion;
using MediaDock.Application.OscarAwards;
using MediaDock.Application.PersonalRatings;
using MediaDock.Infrastructure.GoldenGlobes;
using MediaDock.Infrastructure.Metadata;
using MediaDock.Infrastructure.OscarAwards;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace MediaDock.Api.BackgroundJobs;

internal interface IBackgroundJobHandler
{
    Task<BackgroundJobExecutionResult> ExecuteAsync(
        BackgroundJobExecutionContext context,
        CancellationToken cancellationToken);
}

internal sealed record BackgroundJobExecutionResult(string Status, object? Summary = null);

internal sealed class BackgroundJobExecutionContext(
    BackgroundJob job,
    Func<CancellationToken, Task> loadProviderSettingsAsync,
    Func<string, string, CancellationToken, Task> setStageAsync,
    Func<string, CancellationToken, Task> updateStageAsync,
    Func<IngestionProgressUpdate, CancellationToken, Task> saveIngestionProgressAsync)
{
    public BackgroundJob Job { get; } = job;

    public Task LoadProviderSettingsAsync(CancellationToken cancellationToken) =>
        loadProviderSettingsAsync(cancellationToken);

    public Task SetStageAsync(string stage, string message, CancellationToken cancellationToken) =>
        setStageAsync(stage, message, cancellationToken);

    public Task UpdateStageAsync(string stage, CancellationToken cancellationToken) =>
        updateStageAsync(stage, cancellationToken);

    public Task SaveIngestionProgressAsync(
        IngestionProgressUpdate progress,
        CancellationToken cancellationToken) =>
        saveIngestionProgressAsync(progress, cancellationToken);
}

internal sealed class RssScanBackgroundJobHandler(IServiceProvider services) : IBackgroundJobHandler
{
    public async Task<BackgroundJobExecutionResult> ExecuteAsync(
        BackgroundJobExecutionContext context,
        CancellationToken cancellationToken)
    {
        await context.LoadProviderSettingsAsync(cancellationToken);
        var ingestion = services.GetRequiredService<RssIngestionService>();
        if (IsFailedRecheck(context.Job.ResultSummary))
        {
            var recheck = await ingestion.RecheckFailedAsync(
                cancellationToken,
                (progress, token) => context.SaveIngestionProgressAsync(progress, token));
            var status = GetStatus(recheck.Run.Summary.Status);
            return new BackgroundJobExecutionResult(status, new
            {
                scanRunId = recheck.Run.RunId,
                rss = recheck.Run.Summary,
                recheck = new
                {
                    recheck.RetryableEntriesSelected,
                    recheck.EntriesUnavailable
                }
            });
        }

        var result = await ingestion.RunAsync(
            context.Job.Trigger,
            cancellationToken,
            (progress, token) => context.SaveIngestionProgressAsync(progress, token));
        return new BackgroundJobExecutionResult(
            GetStatus(result.Summary.Status),
            new { scanRunId = result.RunId, rss = result.Summary });
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

    private static string GetStatus(string status) => status switch
    {
        "failed" => "failed",
        "partial" => "partial",
        _ => "succeeded"
    };
}

internal sealed class OscarImportBackgroundJobHandler(OscarDatasetImporter importer) : IBackgroundJobHandler
{
    public async Task<BackgroundJobExecutionResult> ExecuteAsync(
        BackgroundJobExecutionContext context,
        CancellationToken cancellationToken)
    {
        var job = context.Job;
        if (job.InputBytes is null || job.ResultSummary is null)
        {
            throw new InvalidDataException("The queued import payload is unavailable.");
        }

        using var options = JsonDocument.Parse(job.ResultSummary);
        var yearAfter = options.RootElement.GetProperty("yearAfter").GetInt32();
        await context.SetStageAsync("oscar_import", "Oscar dataset import started.", cancellationToken);
        var summary = await importer.ImportAsync(job.InputBytes, yearAfter, cancellationToken);
        return new BackgroundJobExecutionResult("succeeded", summary);
    }
}

internal sealed class GoldenGlobeImportBackgroundJobHandler(
    GoldenGlobeDatasetImporter importer) : IBackgroundJobHandler
{
    public async Task<BackgroundJobExecutionResult> ExecuteAsync(
        BackgroundJobExecutionContext context,
        CancellationToken cancellationToken)
    {
        var job = context.Job;
        if (job.InputBytes is null || job.ResultSummary is null)
        {
            throw new InvalidDataException("The queued import payload is unavailable.");
        }

        using var options = JsonDocument.Parse(job.ResultSummary);
        var yearAfter = options.RootElement.GetProperty("yearAfter").GetInt32();
        await context.SetStageAsync("golden_globe_import", "Golden Globes dataset import started.", cancellationToken);
        await context.UpdateStageAsync("golden_globe_parsing", cancellationToken);
        var summary = await importer.ImportAsync(job.InputBytes, yearAfter, cancellationToken);
        return new BackgroundJobExecutionResult("succeeded", summary);
    }
}

internal sealed class ImdbTitleImportBackgroundJobHandler(
    ImdbTitleDatasetImporter importer,
    BackgroundJobApiService backgroundJobs) : IBackgroundJobHandler
{
    public async Task<BackgroundJobExecutionResult> ExecuteAsync(
        BackgroundJobExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (context.Job.InputBytes is null)
        {
            throw new InvalidDataException("The queued IMDb import payload is unavailable.");
        }

        await context.SetStageAsync("imdb_title_import", "IMDb title dataset import started.", cancellationToken);
        var summary = await importer.ImportAsync(context.Job.InputBytes, cancellationToken);
        var enrichmentJob = await backgroundJobs.EnqueueImdbCatalogEnrichmentAsync("manual", cancellationToken);
        return new BackgroundJobExecutionResult("succeeded", new
        {
            import = summary,
            enrichmentJobId = enrichmentJob?.Id
        });
    }
}

internal sealed class OscarEnrichmentBackgroundJobHandler(IServiceProvider services)
    : IBackgroundJobHandler
{
    public async Task<BackgroundJobExecutionResult> ExecuteAsync(
        BackgroundJobExecutionContext context,
        CancellationToken cancellationToken)
    {
        await context.LoadProviderSettingsAsync(cancellationToken);
        await context.SetStageAsync(
            "oscar_enrichment",
            "Oscar enrichment started.",
            cancellationToken);
        var enrichmentService = services.GetRequiredService<OscarEnrichmentService>();
        var enrichment = await enrichmentService.RunAsync(context.Job.Trigger, cancellationToken);
        var status = enrichment.Status == OscarEnrichmentRunStatuses.Succeeded
            ? "succeeded"
            : "partial";
        return new BackgroundJobExecutionResult(
            status,
            new { enrichment.RunId, enrichment.Status, enrichment.Summary });
    }
}

internal sealed class GoldenGlobeEnrichmentBackgroundJobHandler(IServiceProvider services) : IBackgroundJobHandler
{
    public async Task<BackgroundJobExecutionResult> ExecuteAsync(
        BackgroundJobExecutionContext context,
        CancellationToken cancellationToken)
    {
        await context.LoadProviderSettingsAsync(cancellationToken);
        await context.SetStageAsync(
            "golden_globe_enrichment",
            "Golden Globes enrichment started.",
            cancellationToken);
        var enrichmentService = services.GetRequiredService<GoldenGlobeEnrichmentService>();
        var result = await enrichmentService.RunAsync(cancellationToken);
        return new BackgroundJobExecutionResult(result.Status, result.Summary);
    }
}

internal sealed class ImdbTitleEnrichmentBackgroundJobHandler(IServiceProvider services) : IBackgroundJobHandler
{
    public async Task<BackgroundJobExecutionResult> ExecuteAsync(
        BackgroundJobExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (context.Job.OwnerUserId is not long ownerUserId)
        {
            throw new InvalidDataException("The queued IMDb enrichment owner is unavailable.");
        }

        await context.LoadProviderSettingsAsync(cancellationToken);
        await context.SetStageAsync(
            "imdb_title_enrichment",
            "IMDb metadata enrichment started.",
            cancellationToken);
        var enrichmentService = services.GetRequiredService<ImdbTitleEnrichmentService>();
        var result = await enrichmentService.RunAsync(ownerUserId, cancellationToken);
        return new BackgroundJobExecutionResult(result.Status);
    }
}

internal sealed class ImdbCatalogEnrichmentBackgroundJobHandler(IServiceProvider services)
    : IBackgroundJobHandler
{
    public async Task<BackgroundJobExecutionResult> ExecuteAsync(
        BackgroundJobExecutionContext context,
        CancellationToken cancellationToken)
    {
        await context.LoadProviderSettingsAsync(cancellationToken);
        await context.SetStageAsync(
            "imdb_catalog_enrichment",
            "IMDb catalog enrichment started.",
            cancellationToken);
        var enrichmentService = services.GetRequiredService<ImdbTitleEnrichmentService>();
        var result = await enrichmentService.RunCatalogAsync(cancellationToken);
        return new BackgroundJobExecutionResult(result.Status, result.Summary);
    }
}

internal sealed class GoldenGlobeManualRefreshBackgroundJobHandler(IServiceProvider services)
    : IBackgroundJobHandler
{
    public async Task<BackgroundJobExecutionResult> ExecuteAsync(
        BackgroundJobExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (context.Job.ResultSummary is null)
        {
            throw new InvalidDataException("The queued refresh payload is unavailable.");
        }

        using var payload = JsonDocument.Parse(context.Job.ResultSummary);
        var root = payload.RootElement;
        var candidate = new GoldenGlobeManualRefreshCandidate(
            root.GetProperty("title").GetString()
                ?? throw new InvalidDataException("The queued refresh title is unavailable."),
            root.GetProperty("ceremonyYear").GetInt32(),
            root.GetProperty("sourceType").GetString()
                ?? throw new InvalidDataException("The queued refresh type is unavailable."),
            root.GetProperty("imdbId").GetString()
                ?? throw new InvalidDataException("The queued refresh IMDb ID is unavailable."),
            root.GetProperty("linkVersion").GetInt32(),
            root.GetProperty("attemptCount").GetInt32());
        await context.LoadProviderSettingsAsync(cancellationToken);
        await context.SetStageAsync(
            "golden_globe_manual_refresh",
            "Manual Golden Globes metadata refresh started.",
            cancellationToken);
        var enrichmentService = services.GetRequiredService<GoldenGlobeEnrichmentService>();
        var result = await enrichmentService.RefreshManualAsync(candidate, cancellationToken);
        var status = !result.Applied || result.Status == GoldenGlobeEnrichmentStatuses.Enriched
            ? "succeeded"
            : "partial";
        return new BackgroundJobExecutionResult(status, new
        {
            result.ImdbId,
            outcome = result.Applied ? result.Status : "superseded",
            errorCode = result.ErrorCode,
            result.HttpAttempts,
            result.CacheHit
        });
    }
}