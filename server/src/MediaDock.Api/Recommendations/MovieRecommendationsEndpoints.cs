using System.Security.Claims;
using MediaDock.Api.Authentication;
using MediaDock.Api.Common;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace MediaDock.Api.Recommendations;

internal static class MovieRecommendationsEndpoints
{
    public static WebApplication MapMovieRecommendationsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/recommendations/genres", async Task<Ok<IReadOnlyList<string>>> (
            ClaimsPrincipal principal,
            CurrentUserContext currentUserContext,
            MovieRecommendationsApiService service,
            CancellationToken cancellationToken) =>
        {
            _ = await currentUserContext.ResolveAsync(principal, cancellationToken)
                ?? throw new MediaDock.Api.Middleware.ApiForbiddenException("An active linked user is required.");
            return TypedResults.Ok(await service.GetGenresAsync(cancellationToken));
        })
            .WithName("GetRecommendationGenres")
            .WithSummary("List IMDb genres for recommendation filters.")
            .RequireAuthorization(AccessPolicies.OwnerScopedRecommendations)
            .Produces<IReadOnlyList<string>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapGet("/api/recommendations", async Task<Ok<PageResponse<RecommendationResponse>>> (
            [AsParameters] RecommendationsQuery query,
            ClaimsPrincipal principal,
            CurrentUserContext currentUserContext,
            MovieRecommendationsApiService service,
            CancellationToken cancellationToken) =>
        {
            var currentUser = await currentUserContext.ResolveAsync(principal, cancellationToken)
                ?? throw new MediaDock.Api.Middleware.ApiForbiddenException("An active linked user is required.");
            return TypedResults.Ok(await service.GetAsync(currentUser.Id, query, cancellationToken));
        })
            .WithName("GetRecommendations")
            .WithSummary("List personal movie recommendations for the current user.")
            .RequireAuthorization(AccessPolicies.OwnerScopedRecommendations)
            .Produces<PageResponse<RecommendationResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapPost("/api/recommendations/{imdbId}/watched", async Task<IResult> (
            string imdbId,
            [FromBody] MarkRecommendationWatchedRequest request,
            HttpContext context,
            IAntiforgery antiforgery,
            ClaimsPrincipal principal,
            CurrentUserContext currentUserContext,
            MovieRecommendationsApiService service,
            CancellationToken cancellationToken) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "A valid anti-forgery token is required.");
            }

            var currentUser = await currentUserContext.ResolveAsync(principal, cancellationToken)
                ?? throw new MediaDock.Api.Middleware.ApiForbiddenException("An active linked user is required.");
            var markedWatched = await service.MarkWatchedAsync(
                currentUser.Id,
                imdbId,
                request.Rating,
                cancellationToken);
            if (!markedWatched)
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "This recommendation is no longer available.");
            }

            return TypedResults.NoContent();
        })
            .WithName("MarkRecommendationWatched")
            .WithSummary("Mark an eligible recommendation as watched with a personal rating.")
            .RequireAuthorization(AccessPolicies.OwnerScopedRecommendations)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}