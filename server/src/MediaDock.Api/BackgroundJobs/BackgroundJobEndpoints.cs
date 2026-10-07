using MediaDock.Api.Common;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MediaDock.Api.BackgroundJobs;

internal static class BackgroundJobEndpoints
{
    public static WebApplication MapBackgroundJobEndpoints(this WebApplication app, int maximumUploadBytes)
    {
        app.MapGet("/api/background-jobs/active", async (BackgroundJobApiService service, CancellationToken token) =>
        {
            var job = await service.GetActiveAsync(token);
            return job is null ? Results.NoContent() : Results.Ok(job);
        })
            .WithName("GetActiveBackgroundJob")
            .Produces<BackgroundJobResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status204NoContent);

        app.MapPost("/api/background-jobs/scans", async (BackgroundJobApiService service, CancellationToken token) =>
        {
            var result = await service.EnqueueScanAsync(token);
            if (!result.Accepted)
            {
                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "An RSS scan is already queued or running.",
                    Detail = "Wait for the active scan to finish before requesting another scan."
                };
                problem.Extensions["activeJobId"] = result.Job.Id;
                problem.Extensions["activeJobStatusUrl"] = $"/api/background-jobs/{result.Job.Id}";
                return Results.Conflict(problem);
            }

            var statusUrl = $"/api/background-jobs/{result.Job.Id}";
            return Results.Accepted(statusUrl, new BackgroundJobAcceptedResponse(result.Job.Id, result.Job.Status, statusUrl));
        })
            .WithName("EnqueueBackgroundScan")
            .WithSummary("Queue a manual RSS scan.")
            .Produces<BackgroundJobAcceptedResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapPost("/api/background-jobs/recheck-failed", async (BackgroundJobApiService service, CancellationToken token) =>
        {
            var result = await service.EnqueueFailedRecheckAsync(token);
            if (!result.Accepted)
            {
                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "An RSS job is already queued or running.",
                    Detail = "Wait for the active RSS job to finish before requesting a failed-entry recheck."
                };
                problem.Extensions["activeJobId"] = result.Job.Id;
                problem.Extensions["activeJobStatusUrl"] = $"/api/background-jobs/{result.Job.Id}";
                return Results.Conflict(problem);
            }

            var statusUrl = $"/api/background-jobs/{result.Job.Id}";
            return Results.Accepted(statusUrl, new BackgroundJobAcceptedResponse(result.Job.Id, result.Job.Status, statusUrl));
        })
            .WithName("EnqueueFailedEntryRecheck")
            .WithSummary("Queue a recheck of the latest retryable torrent entries.")
            .Produces<BackgroundJobAcceptedResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapPost("/api/background-jobs/oscar-enrichment", async (BackgroundJobApiService service, CancellationToken token) =>
        {
            var result = await service.EnqueueOscarEnrichmentAsync(token);
            if (!result.Accepted)
            {
                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Oscar enrichment is already queued or running.",
                    Detail = "Wait for the active Oscar enrichment job to finish before requesting another."
                };
                problem.Extensions["activeJobId"] = result.Job.Id;
                problem.Extensions["activeJobStatusUrl"] = $"/api/background-jobs/{result.Job.Id}";
                return Results.Conflict(problem);
            }

            var statusUrl = $"/api/background-jobs/{result.Job.Id}";
            return Results.Accepted(statusUrl, new BackgroundJobAcceptedResponse(result.Job.Id, result.Job.Status, statusUrl));
        })
            .WithName("EnqueueOscarEnrichment")
            .WithSummary("Queue a manual Oscar film metadata enrichment run.")
            .Produces<BackgroundJobAcceptedResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapPost("/api/background-jobs/golden-globe-enrichment", async (BackgroundJobApiService service, CancellationToken token) =>
        {
            var result = await service.EnqueueGoldenGlobeEnrichmentAsync(token);
            if (!result.Accepted)
            {
                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Golden Globes enrichment is already queued or running.",
                    Detail = "Wait for the active Golden Globes enrichment job to finish before requesting another."
                };
                problem.Extensions["activeJobId"] = result.Job.Id;
                problem.Extensions["activeJobStatusUrl"] = $"/api/background-jobs/{result.Job.Id}";
                return Results.Conflict(problem);
            }
            var statusUrl = $"/api/background-jobs/{result.Job.Id}";
            return Results.Accepted(statusUrl, new BackgroundJobAcceptedResponse(result.Job.Id, result.Job.Status, statusUrl));
        }).WithName("EnqueueGoldenGlobeEnrichment").WithSummary("Queue a manual Golden Globes metadata enrichment run.");

        app.MapPost("/api/background-jobs/oscar-import", async (
            [FromForm] OscarImportForm form,
            BackgroundJobApiService service,
            CancellationToken token) =>
        {
            var job = await service.EnqueueOscarImportAsync(form, token);
            var statusUrl = $"/api/background-jobs/{job.Id}";
            return Results.Accepted(statusUrl, new BackgroundJobAcceptedResponse(job.Id, job.Status, statusUrl));
        })
            .WithName("EnqueueOscarImport")
            .WithSummary("Queue a CSV or TSV Oscar dataset import.")
            .WithMetadata(new RequestSizeLimitAttribute(maximumUploadBytes + 1024 * 1024))
            .DisableAntiforgery()
            .Produces<BackgroundJobAcceptedResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapPost("/api/background-jobs/golden-globe-import", async (
            [FromForm] GoldenGlobeImportForm form,
            BackgroundJobApiService service,
            CancellationToken token) =>
        {
            var job = await service.EnqueueGoldenGlobeImportAsync(form, token);
            var statusUrl = $"/api/background-jobs/{job.Id}";
            return Results.Accepted(statusUrl, new BackgroundJobAcceptedResponse(job.Id, job.Status, statusUrl));
        })
            .WithName("EnqueueGoldenGlobeImport")
            .WithSummary("Queue a CSV or TSV Golden Globes dataset import.")
            .WithMetadata(new RequestSizeLimitAttribute(maximumUploadBytes + 1024 * 1024))
            .DisableAntiforgery()
            .Produces<BackgroundJobAcceptedResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapGet("/api/background-jobs/{id:long}", async (
            long id,
            BackgroundJobApiService service,
            CancellationToken token) => Results.Ok(await service.GetAsync(id, token)))
            .WithName("GetBackgroundJob")
            .Produces<BackgroundJobResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/api/background-jobs/{id:long}/events", async (
            long id,
            [AsParameters] BackgroundJobEventQuery query,
            BackgroundJobApiService service,
            CancellationToken token) => Results.Ok(await service.GetEventsAsync(id, query, token)))
            .WithName("GetBackgroundJobEvents")
            .Produces<BackgroundJobEventsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
