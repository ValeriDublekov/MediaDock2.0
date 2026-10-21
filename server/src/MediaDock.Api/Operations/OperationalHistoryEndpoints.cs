using MediaDock.Api.Catalog;
using MediaDock.Api.Common;
using MediaDock.Api.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MediaDock.Api.Operations;

internal static class OperationalHistoryEndpoints
{
    public static WebApplication MapOperationalHistoryEndpoints(this WebApplication app)
    {
        app.MapGet("/api/parse-logs", async Task<Ok<PageResponse<ParseLogResponse>>> (
            [AsParameters] ParseLogQuery query,
            IOperationalHistoryApiService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetParseLogsAsync(query, cancellationToken);
            return TypedResults.Ok(result);
        })
            .WithName("GetParseLogs")
            .WithSummary("Search paginated parse-log history.")
            .WithDescription("Returns a stable page of recent parser and ingestion decisions.")
            .RequireAuthorization(AccessPolicies.Administrator)
            .Produces<PageResponse<ParseLogResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapGet("/api/scan-runs", async Task<Ok<PageResponse<ScanRunResponse>>> (
            [AsParameters] ScanRunQuery query,
            IOperationalHistoryApiService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetScanRunsAsync(query, cancellationToken);
            return TypedResults.Ok(result);
        })
            .WithName("GetScanRuns")
            .WithSummary("List paginated scan history.")
            .WithDescription("Returns a stable page of recent scan summaries; this endpoint does not start scans.")
            .RequireAuthorization(AccessPolicies.Administrator)
            .Produces<PageResponse<ScanRunResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapDelete("/api/operation-history", async Task<Ok<OperationalHistoryCleanupResponse>> (
            [AsParameters] OperationalHistoryCleanupQuery query,
            IOperationalHistoryApiService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DeleteOldHistoryAsync(query.OlderThanDays, cancellationToken);
            return TypedResults.Ok(result);
        })
            .WithName("DeleteOldOperationalHistory")
            .WithSummary("Delete operational history older than the requested age.")
            .WithDescription("Deletes old parse logs and completed scan summaries while preserving active scans and their parse logs.")
            .RequireAuthorization(AccessPolicies.Administrator)
            .Produces<OperationalHistoryCleanupResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }
}