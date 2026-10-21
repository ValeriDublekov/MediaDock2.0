using MediaDock.Application.OscarAwards;
using MediaDock.Application.Metadata;
using MediaDock.Infrastructure.Metadata;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.Infrastructure.OscarAwards;

public sealed class PostgresOscarEnrichmentRepository(MediaDockDbContext dbContext) : IOscarEnrichmentRepository
{
    public async Task<IReadOnlyList<OscarEnrichmentCandidate>> GetEligibleCandidatesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        await dbContext.OscarMovies
            .AsNoTracking()
            .Where(film => (film.EnrichmentStatus == OscarEnrichmentStatuses.Pending
                    || film.EnrichmentStatus == OscarEnrichmentStatuses.TemporaryError)
                && (film.NextEnrichmentAttemptAt == null || film.NextEnrichmentAttemptAt <= now))
            .OrderByDescending(film => film.FilmYear)
            .ThenBy(film => film.StableKey)
            .ThenBy(film => film.Id)
            .Select(film => new OscarEnrichmentCandidate(
                film.Id,
                film.FilmTitle,
                film.FilmYear,
                film.ImdbId,
                film.EnrichmentAttemptCount))
            .ToListAsync(cancellationToken);

    public async Task SaveOutcomeAsync(
        long filmId,
        OscarEnrichmentUpdate update,
        CancellationToken cancellationToken = default)
    {
        var movie = await dbContext.OscarMovies
            .SingleAsync(candidate => candidate.Id == filmId, cancellationToken);

        var metadata = update.Metadata;
        var movieImdbId = ImdbIdNormalizer.Normalize(movie.ImdbId);
        var metadataImdbId = ImdbIdNormalizer.Normalize(metadata?.ImdbId);
        if (metadata is not null && !ImdbIdNormalizer.IsCompatible(movieImdbId, metadataImdbId))
        {
            throw new InvalidOperationException("Oscar enrichment IMDb identity conflicts with the imported source ID.");
        }

        movie.EnrichmentStatus = update.Status;
        movie.EnrichmentAttemptCount = update.AttemptCount;
        movie.LastEnrichmentAttemptAt = update.AttemptedAt;
        movie.NextEnrichmentAttemptAt = update.NextAttemptAt;
        movie.LastEnrichmentError = update.ErrorCode;
        movie.UpdatedAt = update.AttemptedAt;

        if (metadata is not null)
        {
            var imdbId = metadataImdbId ?? movieImdbId;
            if (imdbId is not null)
            {
                var imdbTitle = (await ImdbTitleMetadataMapper.EnsureAsync(
                    dbContext, [imdbId], update.AttemptedAt, cancellationToken))[imdbId];
                ImdbTitleMetadataMapper.Apply(imdbTitle, metadata, update.AttemptedAt);
            }

            movie.ImdbId = imdbId;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}