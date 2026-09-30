using MediaDock.Api.Common;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MediaDock.Api.Favorites;

internal static class FavoriteEndpoints
{
    public static WebApplication MapFavoriteEndpoints(this WebApplication app)
    {
        app.MapGet("/api/favorites", async Task<Ok<PageResponse<FavoriteMovieResponse>>> (
            [AsParameters] FavoriteQuery query, FavoriteApiService service, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.GetAsync(query, cancellationToken)))
            .WithName("GetFavorites").WithSummary("List favorite movies and their current sources.")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapPost("/api/favorites", async Task<Ok<FavoriteMovieResponse>> (
            CreateFavoriteRequest request, FavoriteApiService service, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.AddAsync(request, cancellationToken)))
            .WithName("AddFavorite").WithSummary("Add a movie from a verified catalog.")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        app.MapPatch("/api/favorites/{titleId:long}", async Task<Ok<FavoriteMovieResponse>> (
            long titleId, UpdateFavoriteRequest request, FavoriteApiService service, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.UpdateAsync(titleId, request, cancellationToken)))
            .WithName("UpdateFavorite").WithSummary("Update favorite movie markers.")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        app.MapDelete("/api/favorites/{titleId:long}", async Task<NoContent> (
            long titleId, FavoriteApiService service, CancellationToken cancellationToken) =>
        {
            await service.DeleteAsync(titleId, cancellationToken);
            return TypedResults.NoContent();
        }).WithName("DeleteFavorite").WithSummary("Remove a movie from favorites.");

        return app;
    }
}