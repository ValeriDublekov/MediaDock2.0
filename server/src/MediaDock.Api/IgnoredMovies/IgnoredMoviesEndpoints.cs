using System.Security.Claims;
using MediaDock.Api.Authentication;
using MediaDock.Api.Common;
using MediaDock.Api.Middleware;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MediaDock.Api.IgnoredMovies;

internal static class IgnoredMoviesEndpoints
{
    public static WebApplication MapIgnoredMoviesEndpoints(this WebApplication app)
    {
        app.MapGet("/api/ignored-movies", async Task<Ok<PageResponse<IgnoredMovieResponse>>> (
            [AsParameters] IgnoredMoviesQuery query,
            ClaimsPrincipal principal,
            CurrentUserContext currentUserContext,
            IgnoredMoviesApiService service,
            CancellationToken cancellationToken) =>
        {
            var currentUser = await ResolveCurrentUserAsync(principal, currentUserContext, cancellationToken);
            return TypedResults.Ok(await service.GetAsync(currentUser.Id, query, cancellationToken));
        })
            .WithName("GetIgnoredMovies")
            .WithSummary("List the current user's ignored movies.")
            .RequireAuthorization(AccessPolicies.OwnerScopedIgnoredMovies)
            .Produces<PageResponse<IgnoredMovieResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapPost("/api/ignored-movies", async Task<Ok<IgnoredMovieResponse>> (
            CreateIgnoredMovieRequest request,
            ClaimsPrincipal principal,
            CurrentUserContext currentUserContext,
            IgnoredMoviesApiService service,
            CancellationToken cancellationToken) =>
        {
            var currentUser = await ResolveCurrentUserAsync(principal, currentUserContext, cancellationToken);
            return TypedResults.Ok(await service.AddAsync(currentUser.Id, request, cancellationToken));
        })
            .WithName("AddIgnoredMovie")
            .WithSummary("Add an existing IMDb movie to the current user's ignore list.")
            .RequireAuthorization(AccessPolicies.OwnerScopedIgnoredMovies)
            .Produces<IgnoredMovieResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapDelete("/api/ignored-movies/{imdbId}", async Task<NoContent> (
            string imdbId,
            ClaimsPrincipal principal,
            CurrentUserContext currentUserContext,
            IgnoredMoviesApiService service,
            CancellationToken cancellationToken) =>
        {
            var currentUser = await ResolveCurrentUserAsync(principal, currentUserContext, cancellationToken);
            await service.DeleteAsync(currentUser.Id, imdbId, cancellationToken);
            return TypedResults.NoContent();
        })
            .WithName("DeleteIgnoredMovie")
            .WithSummary("Remove a movie from the current user's ignore list.")
            .RequireAuthorization(AccessPolicies.OwnerScopedIgnoredMovies)
            .Produces(StatusCodes.Status204NoContent)
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