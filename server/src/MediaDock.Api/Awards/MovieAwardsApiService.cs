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
        var oscarFilms = await dbContext.OscarFilms
            .AsNoTracking()
            .Where(film => (film.ImdbId != null && imdbIds.Contains(film.ImdbId))
                || (film.ImdbId == null && film.Title.ImdbId != null && imdbIds.Contains(film.Title.ImdbId)))
            .Include(film => film.Title)
            .Include(film => film.Nominations)
            .ToListAsync(cancellationToken);

        var awards = oscarFilms
            .SelectMany(film => film.Nominations.Select(nomination => new MovieAwardRecognitionResponse(
                nomination.Id,
                film.ImdbId ?? film.Title.ImdbId!,
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
            .Where(nomination => nomination.ImdbId != null && imdbIds.Contains(nomination.ImdbId))
            .ToListAsync(cancellationToken);

        awards.AddRange(goldenGlobes.Select(nomination => new MovieAwardRecognitionResponse(
            nomination.Id,
            nomination.ImdbId!,
            "golden_globes",
            null,
            nomination.Year,
            null,
            nomination.Award.Name,
            null,
            nomination.Title,
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