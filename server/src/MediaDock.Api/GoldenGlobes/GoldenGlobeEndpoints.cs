using MediaDock.Api.Common;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

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
        app.MapGet("/api/golden-globes/categories", async Task<Ok<IReadOnlyList<string>>>(
            IGoldenGlobeApiService service,
            CancellationToken cancellationToken) =>
                TypedResults.Ok(await service.GetCategoriesAsync(cancellationToken)))
            .WithName("GetGoldenGlobeCategories")
            .WithSummary("List distinct Golden Globes award categories.")
            .Produces<IReadOnlyList<string>>(StatusCodes.Status200OK);
        app.MapPut("/api/golden-globes/imdb-link", async Task<Ok<GoldenGlobeImdbLinkResponse>>(
            [FromBody] GoldenGlobeImdbLinkRequest request,
            IGoldenGlobeApiService service,
            CancellationToken cancellationToken) =>
                TypedResults.Ok(await service.SetImdbIdAsync(request, cancellationToken)))
            .WithName("SetGoldenGlobeImdbId")
            .WithSummary("Manually link or clear an IMDb ID and queue its metadata refresh.")
            .Produces<GoldenGlobeImdbLinkResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
        return app;
    }
}
