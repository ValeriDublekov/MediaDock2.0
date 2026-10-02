using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MediaDock.Api.PersonalRatings;

internal static class PersonalRatingsEndpoints
{
    public static WebApplication MapPersonalRatingsEndpoints(this WebApplication app)
    {
        app.MapPost("/api/personal-ratings/import", async Task<Ok<PersonalRatingsImportResponse>> (
            [FromForm] PersonalRatingsImportForm form,
            PersonalRatingsApiService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ImportAsync(form, cancellationToken);
            return TypedResults.Ok(result);
        })
            .WithName("ImportPersonalRatings")
            .WithSummary("Merge personal IMDb ratings from a JSON export.")
            .WithDescription("Repeated imports add new IMDb IDs and update changed ratings; ratings absent from the file are retained.")
            .WithMetadata(new RequestSizeLimitAttribute(PersonalRatingsApiService.MaximumUploadBytes + 1024 * 1024))
            .DisableAntiforgery()
            .Produces<PersonalRatingsImportResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }
}