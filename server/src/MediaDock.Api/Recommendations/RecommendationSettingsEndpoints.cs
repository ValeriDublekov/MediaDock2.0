using MediaDock.Api.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MediaDock.Api.Recommendations;

internal static class RecommendationSettingsEndpoints
{
    private const string AdminBoundaryDescription =
        "Requires an active administrator when access control is enforced. Open mode is unprotected; keep the API within its loopback or configured trusted-network boundary.";

    public static WebApplication MapRecommendationSettingsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/settings/recommendations", async Task<Ok<RecommendationSettingsResponse>> (
            IRecommendationSettingsApiService service,
            CancellationToken cancellationToken) =>
        {
            var settings = await service.GetSettingsAsync(cancellationToken);
            return TypedResults.Ok(settings);
        })
            .WithName("GetRecommendationSettings")
            .WithSummary("Get vote-normalization and award-point settings.")
            .WithDescription(AdminBoundaryDescription)
            .RequireAuthorization(AccessPolicies.Administrator)
            .Produces<RecommendationSettingsResponse>(StatusCodes.Status200OK);

        app.MapPut("/api/settings/recommendations", async Task<Ok<RecommendationSettingsResponse>> (
            UpdateRecommendationSettingsRequest request,
            IRecommendationSettingsApiService service,
            CancellationToken cancellationToken) =>
        {
            var settings = await service.UpdateSettingsAsync(request, cancellationToken);
            return TypedResults.Ok(settings);
        })
            .WithName("UpdateRecommendationSettings")
            .WithSummary("Update vote-normalization and award-point settings.")
            .WithDescription(AdminBoundaryDescription)
            .RequireAuthorization(AccessPolicies.Administrator)
            .Produces<RecommendationSettingsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapPost("/api/settings/recommendations/recalculate-votes", async Task<Ok<VoteWeightRecalculationResponse>> (
            IRecommendationSettingsApiService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.RecalculateVoteWeightsAsync(cancellationToken);
            return TypedResults.Ok(result);
        })
            .WithName("RecalculateRecommendationVoteWeights")
            .WithSummary("Recalculate global IMDb movie vote weights atomically.")
            .WithDescription(AdminBoundaryDescription)
            .RequireAuthorization(AccessPolicies.Administrator)
            .Produces<VoteWeightRecalculationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        return app;
    }
}