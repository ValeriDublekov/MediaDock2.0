using MediaDock.Api.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MediaDock.Api.Administration;

internal static class MovieTablesDashboardEndpoints
{
    public static WebApplication MapMovieTablesDashboardEndpoints(this WebApplication app)
    {
        app.MapGet("/api/admin/movie-tables-dashboard", async Task<Ok<MovieTablesDashboardResponse>> (
            IMovieTablesDashboardApiService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetDashboardSummaryAsync(cancellationToken);
            return TypedResults.Ok(result);
        })
            .WithName("GetMovieTablesDashboard")
            .WithSummary("Retrieve status and counts of system movie tables.")
            .WithDescription("Returns total counts, type breakdowns, and enrichment states for IMDb, Golden Globes, and Oscar movie tables.")
            .RequireAuthorization(AccessPolicies.Administrator)
            .Produces<MovieTablesDashboardResponse>(StatusCodes.Status200OK);

        return app;
    }
}