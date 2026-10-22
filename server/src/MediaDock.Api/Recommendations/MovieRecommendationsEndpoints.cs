using System.Security.Claims;
using MediaDock.Api.Authentication;
using MediaDock.Api.Common;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MediaDock.Api.Recommendations;

internal static class MovieRecommendationsEndpoints
{
    public static WebApplication MapMovieRecommendationsEndpoints(this WebApplication app)
    {
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

        return app;
    }
}