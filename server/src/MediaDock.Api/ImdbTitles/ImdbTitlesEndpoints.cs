using System.Security.Claims;
using MediaDock.Api.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MediaDock.Api.ImdbTitles;

internal static class ImdbTitlesEndpoints
{
    public static WebApplication MapImdbTitlesEndpoints(this WebApplication app)
    {
        app.MapGet("/api/imdb-titles", async Task<Ok<ImdbTitlesPageResponse>> (
            [AsParameters] ImdbTitlesQuery query,
            ClaimsPrincipal principal,
            CurrentUserContext currentUserContext,
            ImdbTitlesApiService service,
            CancellationToken cancellationToken) =>
        {
            var currentUser = await currentUserContext.ResolveAsync(principal, cancellationToken);
            return TypedResults.Ok(await service.GetAsync(query, currentUser?.Id, cancellationToken));
        })
            .WithName("GetImdbTitles")
            .WithSummary("Search and filter the canonical IMDb titles catalog.")
            .WithDescription("Returns every shared IMDb title row, including unknown types and all metadata states, with only the active user's personal state.")
            .Produces<ImdbTitlesPageResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapDelete("/api/imdb-titles/{imdbId}", async Task<Ok<DeleteImdbTitleResponse>> (
            string imdbId,
            ImdbTitlesApiService service,
            CancellationToken cancellationToken) =>
        {
            return TypedResults.Ok(await service.DeleteAsync(imdbId, cancellationToken));
        })
            .WithName("DeleteImdbTitle")
            .WithSummary("Delete canonical IMDb metadata.")
            .WithDescription("Unlinks torrent catalog titles before deleting IMDb metadata. Records still referenced by awards or personal data cannot be deleted.")
            .RequireAuthorization(AccessPolicies.Administrator)
            .Produces<DeleteImdbTitleResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}