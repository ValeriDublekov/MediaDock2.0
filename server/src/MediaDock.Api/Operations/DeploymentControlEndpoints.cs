using System.Net;

namespace MediaDock.Api.Operations;

internal static class DeploymentControlEndpoints
{
    public static WebApplication MapDeploymentControlEndpoints(this WebApplication app)
    {
        app.MapGet("/api/deployment", async (
            DeploymentControlApiService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await service.GetStatusAsync(cancellationToken));
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                return Results.Problem(
                    title: "Deployment control is unavailable.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
            .WithName("GetDeploymentStatus")
            .WithSummary("Get deployment status and recent output.")
            .Produces<DeploymentStatusResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        app.MapPost("/api/deployment/run", async (
            DeploymentActionRequest request,
            DeploymentControlApiService service,
            CancellationToken cancellationToken) =>
        {
            if (request.Action is not ("check" or "retry_failed_gate"))
            {
                return Results.Problem(
                    title: "Unsupported deployment action.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var (statusCode, response) = await service.StartAsync(request.Action, cancellationToken);
                if (statusCode == HttpStatusCode.Accepted)
                {
                    return Results.Accepted(value: response);
                }

                return Results.Problem(
                    title: response?.Message ?? "The deployment control returned no result.",
                    statusCode: (int)statusCode);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                return Results.Problem(
                    title: "Deployment control is unavailable.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
            .WithName("StartDeployment")
            .WithSummary("Run an immediate deployment check or retry a failed staging gate.")
            .Produces<DeploymentActionResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }
}