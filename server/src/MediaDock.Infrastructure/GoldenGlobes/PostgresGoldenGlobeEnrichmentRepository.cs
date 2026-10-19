using MediaDock.Application.GoldenGlobes;
using MediaDock.Application.Metadata;
using MediaDock.Infrastructure.Metadata;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace MediaDock.Infrastructure.GoldenGlobes;

public sealed class PostgresGoldenGlobeEnrichmentRepository(MediaDockDbContext db) : IGoldenGlobeEnrichmentRepository
{
    public async Task<IReadOnlyList<GoldenGlobeEnrichmentCandidate>> GetEligibleCandidatesAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var candidates = await db.GoldenGlobeNominations.AsNoTracking()
            .Where(x => x.EnrichmentStatus == GoldenGlobeEnrichmentStatuses.Pending
                || x.EnrichmentStatus == GoldenGlobeEnrichmentStatuses.Problem
                || x.EnrichmentStatus == GoldenGlobeEnrichmentStatuses.NotFound
                || (x.EnrichmentStatus == GoldenGlobeEnrichmentStatuses.TemporaryError
                    && (x.NextEnrichmentAttemptAt == null || x.NextEnrichmentAttemptAt <= now))
                || (x.EnrichmentStatus == GoldenGlobeEnrichmentStatuses.Enriched
                    && (x.ImdbId == null || !Regex.IsMatch(x.ImdbId, "^tt[0-9]{7,10}$"))))
            .Where(x => !x.IsImdbIdManual)
            .GroupBy(x => new { x.Title, x.Year, x.NomineeType })
            .Select(x => new
            {
                x.Key.Title,
                CeremonyYear = x.Key.Year,
                x.Key.NomineeType,
                AttemptCount = x.Max(y => y.EnrichmentAttemptCount)
            })
            .OrderByDescending(x => x.CeremonyYear).ThenBy(x => x.Title).ThenBy(x => x.NomineeType)
            .ToListAsync(cancellationToken);

        return candidates
            .Select(x => new GoldenGlobeEnrichmentCandidate(x.Title, x.CeremonyYear, x.AttemptCount, x.NomineeType))
            .ToArray();
    }

    public async Task SaveOutcomeAsync(string title, int ceremonyYear, string sourceType, GoldenGlobeEnrichmentUpdate update, CancellationToken cancellationToken = default)
    {
        EnsureEnrichedOutcomeHasValidImdbId(update);
        var imdbId = ImdbIdNormalizer.Normalize(update.ImdbId);
        IReadOnlyDictionary<string, ImdbTitle>? imdbTitles = null;
        if (imdbId is not null)
        {
            imdbTitles = await ImdbTitleMetadataMapper.EnsureAsync(db, [imdbId], update.AttemptedAt, cancellationToken);
        }
        var rows = db.GoldenGlobeNominations
            .Where(x => x.Title == title && x.Year == ceremonyYear && x.NomineeType == sourceType && !x.IsImdbIdManual);
        int affected;
        if (imdbId is null)
        {
            affected = await rows.ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.EnrichmentStatus, update.Status)
                .SetProperty(x => x.EnrichmentAttemptCount, update.AttemptCount)
                .SetProperty(x => x.LastEnrichmentAttemptAt, update.AttemptedAt)
                .SetProperty(x => x.NextEnrichmentAttemptAt, update.NextAttemptAt)
                .SetProperty(x => x.LastEnrichmentError, update.ErrorCode), cancellationToken);
        }
        else
        {
            affected = await rows.ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.EnrichmentStatus, update.Status)
                .SetProperty(x => x.EnrichmentAttemptCount, update.AttemptCount)
                .SetProperty(x => x.LastEnrichmentAttemptAt, update.AttemptedAt)
                .SetProperty(x => x.NextEnrichmentAttemptAt, update.NextAttemptAt)
                .SetProperty(x => x.LastEnrichmentError, update.ErrorCode)
                .SetProperty(x => x.ImdbId, imdbId)
                .SetProperty(x => x.NomineeType, update.ResolvedNomineeType ?? sourceType), cancellationToken);
        }
        if (affected > 0 && update.Metadata is { } metadata && imdbTitles is not null && imdbId is not null)
        {
            ImdbTitleMetadataMapper.Apply(imdbTitles[imdbId], metadata, update.AttemptedAt);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<bool> SaveManualOutcomeAsync(
        GoldenGlobeManualRefreshCandidate candidate,
        GoldenGlobeEnrichmentUpdate update,
        CancellationToken cancellationToken = default)
    {
        EnsureEnrichedOutcomeHasValidImdbId(update);
        var imdbTitles = await ImdbTitleMetadataMapper.EnsureAsync(db, [candidate.ImdbId], update.AttemptedAt, cancellationToken);
        var affected = await db.GoldenGlobeNominations
            .Where(x => x.Title == candidate.Title
                && x.Year == candidate.CeremonyYear
                && x.NomineeType == candidate.SourceType
                && x.IsImdbIdManual
                && x.ImdbId == candidate.ImdbId
                && x.ImdbIdVersion == candidate.LinkVersion)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.EnrichmentStatus, update.Status)
                .SetProperty(x => x.EnrichmentAttemptCount, update.AttemptCount)
                .SetProperty(x => x.LastEnrichmentAttemptAt, update.AttemptedAt)
                .SetProperty(x => x.NextEnrichmentAttemptAt, update.NextAttemptAt)
                .SetProperty(x => x.LastEnrichmentError, update.ErrorCode)
                .SetProperty(x => x.ImdbId, candidate.ImdbId), cancellationToken);
        if (affected > 0 && update.Metadata is { } metadata)
        {
            ImdbTitleMetadataMapper.Apply(imdbTitles[candidate.ImdbId], metadata, update.AttemptedAt);
            await db.SaveChangesAsync(cancellationToken);
        }
        return affected > 0;
    }

    private static void EnsureEnrichedOutcomeHasValidImdbId(GoldenGlobeEnrichmentUpdate update)
    {
        if (update.Status == GoldenGlobeEnrichmentStatuses.Enriched && !ImdbIdNormalizer.IsValid(update.ImdbId))
            throw new InvalidOperationException("An enriched Golden Globes outcome must include a valid IMDb ID.");
        if (update.Metadata is { } metadata
            && (update.Status != GoldenGlobeEnrichmentStatuses.Enriched
                || ImdbIdNormalizer.Normalize(metadata.ImdbId) != ImdbIdNormalizer.Normalize(update.ImdbId)))
        {
            throw new InvalidOperationException("Verified Golden Globes metadata must match an enriched IMDb ID.");
        }
    }
}
