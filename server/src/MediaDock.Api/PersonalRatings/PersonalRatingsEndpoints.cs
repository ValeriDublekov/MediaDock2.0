using System.Security.Claims;
using MediaDock.Api.Authentication;
using MediaDock.Api.BackgroundJobs;
using MediaDock.Api.Middleware;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediaDock.Api.PersonalRatings;

internal static class PersonalRatingsEndpoints
{
    public static WebApplication MapPersonalRatingsEndpoints(this WebApplication app)
    {
        app.MapPost("/api/personal-ratings/enrichment", async Task<IResult> (
            HttpContext context,
            IAntiforgery antiforgery,
            ClaimsPrincipal principal,
            CurrentUserContext currentUserContext,
            BackgroundJobApiService backgroundJobService,
            CancellationToken cancellationToken) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "A valid anti-forgery token is required.");
            }

            var currentUser = await currentUserContext.ResolveAsync(principal, cancellationToken)
                ?? throw new ApiForbiddenException("An active linked user is required.");
            var result = await backgroundJobService.EnqueueImdbTitleEnrichmentAsync(currentUser.Id, cancellationToken);
            return result is null
                ? TypedResults.NoContent()
                : Results.Accepted(value: new PersonalRatingsEnrichmentAcceptedResponse(
                    result.Job.Id,
                    result.Job.Status));
        })
            .WithName("EnqueuePersonalRatingsMetadataEnrichment")
            .WithSummary("Queue metadata enrichment for the current user's IMDb ratings.")
            .RequireAuthorization(AccessPolicies.OwnerScopedPersonalRatings)
            .Produces<PersonalRatingsEnrichmentAcceptedResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapPost("/api/personal-ratings/import", async Task<IResult> (
            [FromForm] PersonalRatingsImportForm form,
            HttpContext context,
            IAntiforgery antiforgery,
            ClaimsPrincipal principal,
            CurrentUserContext currentUserContext,
            PersonalRatingsApiService service,
            CancellationToken cancellationToken) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "A valid anti-forgery token is required.");
            }

            var currentUser = await currentUserContext.ResolveAsync(principal, cancellationToken)
                ?? throw new ApiForbiddenException("An active linked user is required.");
            var result = await service.ImportAsync(currentUser.Id, form, cancellationToken);
            return TypedResults.Ok(result);
        })
            .WithName("ImportPersonalRatings")
            .WithSummary("Merge personal IMDb ratings from a JSON export.")
            .WithDescription("Repeated imports add new IMDb IDs and update changed ratings; ratings absent from the file are retained.")
            .RequireAuthorization(AccessPolicies.OwnerScopedPersonalRatings)
            .WithMetadata(new RequestSizeLimitAttribute(PersonalRatingsApiService.MaximumUploadBytes + 1024 * 1024))
            .DisableAntiforgery()
            .Produces<PersonalRatingsImportResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}