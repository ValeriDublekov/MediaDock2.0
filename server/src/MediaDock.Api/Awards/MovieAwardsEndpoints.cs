using Microsoft.AspNetCore.Http.HttpResults;

namespace MediaDock.Api.Awards;

internal static class MovieAwardsEndpoints
{
    public static WebApplication MapMovieAwardsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/movie-awards", async Task<Ok<IReadOnlyList<MovieAwardRecognitionResponse>>>(
            [AsParameters] MovieAwardsQuery query,
            IMovieAwardsApiService service,
            CancellationToken cancellationToken) =>
                TypedResults.Ok(await service.GetAwardsAsync(query, cancellationToken)))
            .WithName("GetMovieAwards")
            .WithSummary("Get Oscar and Golden Globes nominations for IMDb IDs.")
            .Produces<IReadOnlyList<MovieAwardRecognitionResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }
}