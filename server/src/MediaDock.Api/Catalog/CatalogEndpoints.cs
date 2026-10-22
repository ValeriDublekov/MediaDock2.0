using MediaDock.Api.Common;
using MediaDock.Api.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MediaDock.Api.Catalog;

internal static class CatalogEndpoints
{
    public static WebApplication MapCatalogEndpoints(this WebApplication app)
    {
        app.MapGet("/api/catalog", async Task<Ok<PageResponse<CatalogTitleResponse>>> (
            [AsParameters] CatalogQuery query,
            ICatalogApiService catalogService,
            CancellationToken cancellationToken) =>
        {
            var result = await catalogService.GetCatalogAsync(query, cancellationToken);
            return TypedResults.Ok(result);
        })
            .WithName("GetCatalog")
            .WithSummary("Search and filter catalog titles.")
            .WithDescription("Returns a stable page of titles, newest last-seen first.")
            .Produces<PageResponse<CatalogTitleResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapGet("/api/titles/{id:long}", async Task<Ok<TitleDetailsResponse>> (
            long id,
            ICatalogApiService catalogService,
            CancellationToken cancellationToken) =>
        {
            var result = await catalogService.GetTitleAsync(id, cancellationToken);
            return TypedResults.Ok(result);
        })
            .WithName("GetTitleById")
            .WithSummary("Get full details for one catalog title.")
            .WithDescription("Returns title metadata and the current occurrence count.")
            .Produces<TitleDetailsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/api/titles/{id:long}/occurrences", async Task<Ok<PageResponse<OccurrenceResponse>>> (
            long id,
            [AsParameters] OccurrencesQuery query,
            ICatalogApiService catalogService,
            CancellationToken cancellationToken) =>
        {
            var result = await catalogService.GetOccurrencesAsync(id, query, cancellationToken);
            return TypedResults.Ok(result);
        })
            .WithName("GetTitleOccurrences")
            .WithSummary("List feed occurrences for a catalog title.")
            .WithDescription("Returns a stable page of occurrences, newest last-seen first.")
            .Produces<PageResponse<OccurrenceResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapDelete("/api/titles/{id:long}", async Task<Ok<DeleteTitleResponse>> (
            long id,
            ICatalogApiService catalogService,
            CancellationToken cancellationToken,
            bool deleteImdbInfo = false) =>
        {
            var result = await catalogService.DeleteTitleAsync(id, deleteImdbInfo, cancellationToken);
            return TypedResults.Ok(result);
        })
            .WithName("DeleteCatalogTitle")
            .WithSummary("Delete a torrent catalog title.")
            .WithDescription("Removes the title and its releases. IMDb metadata is deleted only when requested and no other records use it.")
            .RequireAuthorization(AccessPolicies.Administrator)
            .Produces<DeleteTitleResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}