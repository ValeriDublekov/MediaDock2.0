using MediaDock.Api.Middleware;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MediaDock.Api.Sources;

internal static class SourceSettingsEndpoints
{
    private const string LanTrustedWriteDescription =
        "LAN-trusted MVP write endpoint. Requests are unauthenticated; do not expose this API beyond a trusted LAN.";

    public static WebApplication MapSourceSettingsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/sources", async Task<Ok<IReadOnlyList<SourceProfileResponse>>> (
            ISourceSettingsApiService service,
            CancellationToken cancellationToken) =>
        {
            var profiles = await service.GetSourceProfilesAsync(cancellationToken);
            return TypedResults.Ok(profiles);
        })
            .WithName("GetSourceProfiles")
            .WithSummary("List the fixed RSS profiles and their URLs.")
            .Produces<IReadOnlyList<SourceProfileResponse>>(StatusCodes.Status200OK);

        app.MapPost("/api/sources/{profileId}/urls", async Task<Created<SourceUrlResponse>> (
            string profileId,
            SourceUrlRequest request,
            ISourceSettingsApiService service,
            CancellationToken cancellationToken) =>
        {
            var source = await service.AddSourceUrlAsync(profileId, request, cancellationToken);
            return TypedResults.Created("/api/sources", source);
        })
            .WithName("AddSourceUrl")
            .WithSummary("Add an RSS URL to a fixed profile.")
            .WithDescription(LanTrustedWriteDescription)
            .Produces<SourceUrlResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapPut("/api/sources/{profileId}/urls/{id:long}", async Task<Ok<SourceUrlResponse>> (
            string profileId,
            long id,
            SourceUrlRequest request,
            ISourceSettingsApiService service,
            CancellationToken cancellationToken) =>
        {
            var source = await service.ReplaceSourceUrlAsync(profileId, id, request, cancellationToken);
            return TypedResults.Ok(source);
        })
            .WithName("ReplaceSourceUrl")
            .WithSummary("Replace an RSS URL within its fixed profile.")
            .WithDescription(LanTrustedWriteDescription)
            .Produces<SourceUrlResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapDelete("/api/sources/{profileId}/urls/{id:long}", async Task<NoContent> (
            string profileId,
            long id,
            ISourceSettingsApiService service,
            CancellationToken cancellationToken) =>
        {
            await service.RemoveSourceUrlAsync(profileId, id, cancellationToken);
            return TypedResults.NoContent();
        })
            .WithName("RemoveSourceUrl")
            .WithSummary("Remove an RSS URL from its fixed profile.")
            .WithDescription(LanTrustedWriteDescription)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/api/settings", async Task<Ok<SettingsResponse>> (
            ISourceSettingsApiService service,
            CancellationToken cancellationToken) =>
        {
            var settings = await service.GetSettingsAsync(cancellationToken);
            return TypedResults.Ok(settings);
        })
            .WithName("GetSettings")
            .WithSummary("Get application matching settings.")
            .WithDescription("Returns saved settings or their defaults when no settings row exists.")
            .Produces<SettingsResponse>(StatusCodes.Status200OK);

        app.MapPut("/api/settings", async Task<Ok<SettingsResponse>> (
            UpdateSettingsRequest request,
            ISourceSettingsApiService service,
            CancellationToken cancellationToken) =>
        {
            var settings = await service.UpdateSettingsAsync(request, cancellationToken);
            return TypedResults.Ok(settings);
        })
            .WithName("UpdateSettings")
            .WithSummary("Replace application matching settings.")
            .WithDescription(LanTrustedWriteDescription)
            .Produces<SettingsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapGet("/api/settings/providers/omdb", async Task<Ok<ProviderSettingsResponse>> (
            ISourceSettingsApiService service,
            CancellationToken cancellationToken) =>
        {
            var settings = await service.GetProviderSettingsAsync(cancellationToken);
            return TypedResults.Ok(settings);
        })
            .WithName("GetOmdbSettings")
            .WithSummary("Get OMDb settings without exposing the API key.")
            .Produces<ProviderSettingsResponse>(StatusCodes.Status200OK);

        app.MapPut("/api/settings/providers/omdb", async Task<Ok<ProviderSettingsResponse>> (
            UpdateProviderSettingsRequest request,
            ISourceSettingsApiService service,
            CancellationToken cancellationToken) =>
        {
            var settings = await service.UpdateProviderSettingsAsync(request, cancellationToken);
            return TypedResults.Ok(settings);
        })
            .WithName("UpdateOmdbSettings")
            .WithSummary("Update OMDb settings and the write-only API key.")
            .WithDescription(LanTrustedWriteDescription)
            .Produces<ProviderSettingsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }
}