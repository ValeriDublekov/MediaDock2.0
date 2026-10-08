using MediaDock.Api.Common;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MediaDock.Api.OscarAwards;

internal static class OscarEndpoints
{
    public static WebApplication MapOscarEndpoints(this WebApplication app)
    {
        app.MapGet("/api/oscars", async Task<Ok<PageResponse<OscarFilmResponse>>> (
            [AsParameters] OscarCatalogQuery query,
            IOscarApiService oscarService,
            CancellationToken cancellationToken) =>
        {
            var result = await oscarService.GetOscarFilmsAsync(query, cancellationToken);
            return TypedResults.Ok(result);
        })
            .WithName("GetOscarFilms")
            .WithSummary("Search and filter Oscar films.")
            .WithDescription("Returns a stable page of Oscar films, newest film year first.")
            .Produces<PageResponse<OscarFilmResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapGet("/api/oscars/categories", async Task<Ok<IReadOnlyList<string>>>(
            IOscarApiService oscarService,
            CancellationToken cancellationToken) =>
                TypedResults.Ok(await oscarService.GetCategoriesAsync(cancellationToken)))
            .WithName("GetOscarCategories")
            .WithSummary("List distinct Oscar nomination categories.")
            .Produces<IReadOnlyList<string>>(StatusCodes.Status200OK);

        app.MapGet("/api/oscars/{id:long}", async Task<Ok<OscarFilmResponse>> (
            long id,
            IOscarApiService oscarService,
            CancellationToken cancellationToken) =>
        {
            var result = await oscarService.GetOscarFilmAsync(id, cancellationToken);
            return TypedResults.Ok(result);
        })
            .WithName("GetOscarFilmById")
            .WithSummary("Get Oscar nominations and metadata for one film.")
            .Produces<OscarFilmResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/api/titles/{titleId:long}/oscars", async Task<Ok<IReadOnlyList<OscarFilmResponse>>> (
            long titleId, IOscarApiService oscarService, CancellationToken cancellationToken) =>
            TypedResults.Ok(await oscarService.GetByTitleAsync(titleId, cancellationToken)))
            .WithName("GetTitleOscars")
            .WithSummary("List all Oscar records and nominations for one title.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}