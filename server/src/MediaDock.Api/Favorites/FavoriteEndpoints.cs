using System.Security.Claims;
using MediaDock.Api.Common;
using MediaDock.Api.Authentication;
using MediaDock.Api.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MediaDock.Api.Favorites;

internal static class FavoriteEndpoints
{
    public static WebApplication MapFavoriteEndpoints(this WebApplication app)
    {
        app.MapGet("/api/favorites", async Task<Ok<PageResponse<FavoriteMovieResponse>>> (
            [AsParameters] FavoriteQuery query,
            ClaimsPrincipal principal,
            CurrentUserContext currentUserContext,
            FavoriteApiService service,
            CancellationToken cancellationToken) =>
        {
            var currentUser = await ResolveCurrentUserAsync(principal, currentUserContext, cancellationToken);
            return TypedResults.Ok(await service.GetAsync(currentUser.Id, query, cancellationToken));
        })
            .WithName("GetFavorites").WithSummary("List favorite movies and their current sources.")
            .RequireAuthorization(AccessPolicies.OwnerScopedFavorites)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapPost("/api/favorites", async Task<Ok<FavoriteMovieResponse>> (
            CreateFavoriteRequest request,
            ClaimsPrincipal principal,
            CurrentUserContext currentUserContext,
            FavoriteApiService service,
            CancellationToken cancellationToken) =>
        {
            var currentUser = await ResolveCurrentUserAsync(principal, currentUserContext, cancellationToken);
            return TypedResults.Ok(await service.AddAsync(currentUser.Id, request, cancellationToken));
        })
            .WithName("AddFavorite").WithSummary("Add a movie from a verified catalog.")
            .RequireAuthorization(AccessPolicies.OwnerScopedFavorites)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapPatch("/api/favorites/{titleId:long}", async Task<Ok<FavoriteMovieResponse>> (
            long titleId,
            UpdateFavoriteRequest request,
            ClaimsPrincipal principal,
            CurrentUserContext currentUserContext,
            FavoriteApiService service,
            CancellationToken cancellationToken) =>
        {
            var currentUser = await ResolveCurrentUserAsync(principal, currentUserContext, cancellationToken);
            return TypedResults.Ok(await service.UpdateAsync(currentUser.Id, titleId, request, cancellationToken));
        })
            .WithName("UpdateFavorite").WithSummary("Update favorite movie markers.")
            .RequireAuthorization(AccessPolicies.OwnerScopedFavorites)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapDelete("/api/favorites/{titleId:long}", async Task<NoContent> (
            long titleId,
            ClaimsPrincipal principal,
            CurrentUserContext currentUserContext,
            FavoriteApiService service,
            CancellationToken cancellationToken) =>
        {
            var currentUser = await ResolveCurrentUserAsync(principal, currentUserContext, cancellationToken);
            await service.DeleteAsync(currentUser.Id, titleId, cancellationToken);
            return TypedResults.NoContent();
        }).WithName("DeleteFavorite").WithSummary("Remove a movie from favorites.")
            .RequireAuthorization(AccessPolicies.OwnerScopedFavorites)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<CurrentUser> ResolveCurrentUserAsync(
        ClaimsPrincipal principal,
        CurrentUserContext currentUserContext,
        CancellationToken cancellationToken) =>
        await currentUserContext.ResolveAsync(principal, cancellationToken)
        ?? throw new ApiForbiddenException("An active linked user is required.");
}