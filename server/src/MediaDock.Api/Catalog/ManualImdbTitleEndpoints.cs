using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using MediaDock.Api.Authentication;
using MediaDock.Api.Middleware;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MediaDock.Api.Catalog;

internal static class ManualImdbTitleEndpoints
{
    public static WebApplication MapManualImdbTitleEndpoints(this WebApplication app)
    {
        app.MapPost("/api/imdb-titles/manual-candidates", async Task<Ok<ImdbCandidateSearchResponse>> (
            [Range(1, 100)] int? page,
            ImdbCandidateSearchRequest request,
            ClaimsPrincipal principal,
            CurrentUserContext currentUserContext,
            ITorrentTitleMatchApiService service,
            CancellationToken cancellationToken) =>
        {
            await RequireActiveUserAsync(principal, currentUserContext, cancellationToken);
            return TypedResults.Ok(await service.SearchManualImdbTitleCandidatesAsync(
                request.Title, page ?? 1, cancellationToken));
        })
            .WithName("SearchManualImdbTitleCandidates")
            .WithSummary("Search OMDb for an IMDb title to add to a personal list.")
            .RequireAuthorization(AccessPolicies.OwnerScopedPersonalRatings)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapPost("/api/imdb-titles/manual-candidate", async Task<Ok<ImdbCandidateSearchResponse>> (
            LookupImdbCandidateRequest request,
            ClaimsPrincipal principal,
            CurrentUserContext currentUserContext,
            ITorrentTitleMatchApiService service,
            CancellationToken cancellationToken) =>
        {
            await RequireActiveUserAsync(principal, currentUserContext, cancellationToken);
            return TypedResults.Ok(await service.LookupManualImdbTitleCandidateAsync(
                request.ImdbId, cancellationToken));
        })
            .WithName("LookupManualImdbTitleCandidate")
            .WithSummary("Load an IMDb title candidate by ID for a personal list.")
            .RequireAuthorization(AccessPolicies.OwnerScopedPersonalRatings)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapPost("/api/imdb-titles/manual-add", async Task<Ok<ManualImdbTitleResponse>> (
            ApplyImdbCandidateRequest request,
            ClaimsPrincipal principal,
            CurrentUserContext currentUserContext,
            ITorrentTitleMatchApiService service,
            CancellationToken cancellationToken) =>
        {
            await RequireActiveUserAsync(principal, currentUserContext, cancellationToken);
            return TypedResults.Ok(await service.AddManualImdbTitleAsync(request.ImdbId, cancellationToken));
        })
            .WithName("AddManualImdbTitle")
            .WithSummary("Resolve and store an IMDb movie or series for a personal list.")
            .RequireAuthorization(AccessPolicies.OwnerScopedPersonalRatings)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task RequireActiveUserAsync(
        ClaimsPrincipal principal,
        CurrentUserContext currentUserContext,
        CancellationToken cancellationToken) =>
        _ = await currentUserContext.ResolveAsync(principal, cancellationToken)
            ?? throw new ApiForbiddenException("An active linked user is required.");
}