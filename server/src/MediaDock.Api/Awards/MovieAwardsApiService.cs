using MediaDock.Api.Middleware;
using MediaDock.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.Api.Awards;

internal interface IMovieAwardsApiService
{
    Task<IReadOnlyList<MovieAwardRecognitionResponse>> GetAwardsAsync(
        MovieAwardsQuery query,
        CancellationToken cancellationToken);
}

internal sealed class MovieAwardsApiService(MediaDockDbContext dbContext) : IMovieAwardsApiService
{
    private const int MaximumImdbIds = 100;

    public async Task<IReadOnlyList<MovieAwardRecognitionResponse>> GetAwardsAsync(
        MovieAwardsQuery query,
        CancellationToken cancellationToken)
    {
        var requestedIds = query.ImdbIds?.Split(',').Select(id => id.Trim()).ToArray() ?? [];
        if (requestedIds.Length is 0 or > MaximumImdbIds || requestedIds.Any(id => !IsValidImdbId(id)))
        {
            throw new ApiValidationException(new Dictionary<string, string[]>
            {
                [nameof(query.ImdbIds)] = [$"Provide between 1 and {MaximumImdbIds} valid IMDb IDs."]
            });
        }

        var imdbIds = requestedIds.Select(id => id.ToLowerInvariant()).Distinct().ToArray();
        var oscarMovies = await dbContext.OscarMovies
            .AsNoTracking()
            .Where(film => film.ImdbId != null && imdbIds.Contains(film.ImdbId))
            .Include(film => film.Nominations)
            .ToListAsync(cancellationToken);

        var awards = oscarMovies
            .SelectMany(film => film.Nominations.Select(nomination => new MovieAwardRecognitionResponse(
                nomination.Id,
                film.ImdbId!,
                "oscars",
                film.FilmYear,
                null,
                nomination.Ceremony,
                nomination.Category,
                nomination.Name,
                nomination.Nominees,
                nomination.Detail,
                nomination.IsWinner)))
            .ToList();

        var goldenGlobes = await dbContext.GoldenGlobeNominations
            .AsNoTracking()
            .Include(nomination => nomination.Award)
            .Include(nomination => nomination.GoldenGlobeTitle)
            .Where(nomination => nomination.GoldenGlobeTitle != null
                && nomination.GoldenGlobeTitle.ImdbId != null
                && imdbIds.Contains(nomination.GoldenGlobeTitle.ImdbId))
            .ToListAsync(cancellationToken);

        awards.AddRange(goldenGlobes.Select(nomination => new MovieAwardRecognitionResponse(
            nomination.Id,
            nomination.GoldenGlobeTitle!.ImdbId!,
            "golden_globes",
            null,
            nomination.GoldenGlobeTitle.Year,
            null,
            nomination.Award.Name,
            null,
            nomination.GoldenGlobeTitle.Title,
            null,
            nomination.Winner)));

        return awards
            .OrderBy(award => award.ImdbId)
            .ThenBy(award => award.Source, StringComparer.Ordinal)
            .ThenBy(award => award.FilmYear ?? award.CeremonyYear)
            .ThenBy(award => award.Ceremony)
            .ThenBy(award => award.Award, StringComparer.Ordinal)
            .ThenBy(award => award.Id)
            .ToArray();
    }

    private static bool IsValidImdbId(string imdbId) =>
        imdbId.Length is >= 9 and <= 12
        && imdbId.StartsWith("tt", StringComparison.OrdinalIgnoreCase)
        && imdbId.AsSpan(2).IndexOfAnyExceptInRange('0', '9') < 0;
}