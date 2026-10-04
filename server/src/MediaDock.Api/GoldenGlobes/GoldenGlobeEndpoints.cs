using MediaDock.Api.Common;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MediaDock.Api.GoldenGlobes;

internal static class GoldenGlobeEndpoints
{
    public static WebApplication MapGoldenGlobeEndpoints(this WebApplication app)
    {
        app.MapGet("/api/golden-globes", async Task<Ok<PageResponse<GoldenGlobeFilmResponse>>>([AsParameters] GoldenGlobeCatalogQuery query, IGoldenGlobeApiService service, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.GetFilmsAsync(query, cancellationToken)))
            .WithName("GetGoldenGlobeFilms")
            .WithSummary("Search and filter Golden Globes films.")
            .Produces<PageResponse<GoldenGlobeFilmResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);
        return app;
    }
}
