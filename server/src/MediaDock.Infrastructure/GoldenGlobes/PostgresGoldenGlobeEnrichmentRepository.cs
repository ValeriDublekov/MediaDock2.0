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
        var candidates = await db.GoldenGlobeTitles.AsNoTracking()
            .Where(x => x.Nominations.Any())
            .Where(x => x.EnrichmentStatus == GoldenGlobeEnrichmentStatuses.Pending
                || x.EnrichmentStatus == GoldenGlobeEnrichmentStatuses.Problem
                || x.EnrichmentStatus == GoldenGlobeEnrichmentStatuses.NotFound
                || (x.EnrichmentStatus == GoldenGlobeEnrichmentStatuses.TemporaryError
                    && (x.NextEnrichmentAttemptAt == null || x.NextEnrichmentAttemptAt <= now))
                || (x.EnrichmentStatus == GoldenGlobeEnrichmentStatuses.Enriched
                    && (x.ImdbId == null || !Regex.IsMatch(x.ImdbId, "^tt[0-9]{7,10}$"))))
            .Where(x => !x.IsImdbIdManual)
            .Select(x => new
            {
                x.Title,
                CeremonyYear = x.Year,
                x.NomineeType,
                x.EnrichmentAttemptCount
            })
            .OrderByDescending(x => x.CeremonyYear).ThenBy(x => x.Title).ThenBy(x => x.NomineeType)
            .ToListAsync(cancellationToken);

        return candidates
            .Select(x => new GoldenGlobeEnrichmentCandidate(x.Title, x.CeremonyYear, x.EnrichmentAttemptCount, x.NomineeType))
            .ToArray();
    }

    public async Task SaveOutcomeAsync(string title, int ceremonyYear, string sourceType, GoldenGlobeEnrichmentUpdate update, CancellationToken cancellationToken = default)
    {
        EnsureEnrichedOutcomeHasValidImdbId(update);
        var imdbId = ImdbIdNormalizer.Normalize(update.ImdbId);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var filmId = $"{ceremonyYear}:{sourceType}:{title}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({filmId}, 0))",
            cancellationToken);

        var sourceGroup = await db.GoldenGlobeTitles.SingleOrDefaultAsync(
            x => x.Title == title && x.Year == ceremonyYear && x.NomineeType == sourceType,
            cancellationToken);
        if (sourceGroup is null || sourceGroup.IsImdbIdManual) return;

        IReadOnlyDictionary<string, ImdbTitle>? imdbTitles = null;
        if (imdbId is not null)
            imdbTitles = await ImdbTitleMetadataMapper.EnsureAsync(db, [imdbId], update.AttemptedAt, cancellationToken);

        var resolvedType = imdbId is null ? sourceType : update.ResolvedNomineeType ?? sourceType;
        var targetGroup = resolvedType == sourceType
            ? null
            : await db.GoldenGlobeTitles.SingleOrDefaultAsync(
                x => x.Title == title && x.Year == ceremonyYear && x.NomineeType == resolvedType,
                cancellationToken);
        int affected;
        if (targetGroup is null)
        {
            affected = await ApplyOutcomeAsync(
                db.GoldenGlobeTitles.Where(x => x.Id == sourceGroup.Id && !x.IsImdbIdManual),
                imdbId,
                update,
                resolvedType,
                cancellationToken);
        }
        else
        {
            var moved = await db.GoldenGlobeNominations
                .Where(x => x.GoldenGlobeTitleId == sourceGroup.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.GoldenGlobeTitleId, targetGroup.Id), cancellationToken);
            affected = moved == 0
                ? 0
                : await ApplyOutcomeAsync(
                    db.GoldenGlobeTitles.Where(x => x.Id == targetGroup.Id && !x.IsImdbIdManual),
                    imdbId,
                    update,
                    resolvedType,
                    cancellationToken);
            if (moved > 0)
                await db.GoldenGlobeTitles.Where(x => x.Id == sourceGroup.Id).ExecuteDeleteAsync(cancellationToken);
        }

        if (affected > 0 && update.Metadata is { } metadata && imdbTitles is not null && imdbId is not null)
        {
            ImdbTitleMetadataMapper.Apply(imdbTitles[imdbId], metadata, update.AttemptedAt);
            await db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> SaveManualOutcomeAsync(
        GoldenGlobeManualRefreshCandidate candidate,
        GoldenGlobeEnrichmentUpdate update,
        CancellationToken cancellationToken = default)
    {
        EnsureEnrichedOutcomeHasValidImdbId(update);
        var imdbTitles = await ImdbTitleMetadataMapper.EnsureAsync(db, [candidate.ImdbId], update.AttemptedAt, cancellationToken);
        var affected = await db.GoldenGlobeTitles
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

    private static Task<int> ApplyOutcomeAsync(
        IQueryable<GoldenGlobeTitle> groups,
        string? imdbId,
        GoldenGlobeEnrichmentUpdate update,
        string resolvedType,
        CancellationToken cancellationToken)
    {
        if (imdbId is null)
        {
            return groups.ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.EnrichmentStatus, update.Status)
                .SetProperty(x => x.EnrichmentAttemptCount, update.AttemptCount)
                .SetProperty(x => x.LastEnrichmentAttemptAt, update.AttemptedAt)
                .SetProperty(x => x.NextEnrichmentAttemptAt, update.NextAttemptAt)
                .SetProperty(x => x.LastEnrichmentError, update.ErrorCode), cancellationToken);
        }

        return groups.ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.EnrichmentStatus, update.Status)
            .SetProperty(x => x.EnrichmentAttemptCount, update.AttemptCount)
            .SetProperty(x => x.LastEnrichmentAttemptAt, update.AttemptedAt)
            .SetProperty(x => x.NextEnrichmentAttemptAt, update.NextAttemptAt)
            .SetProperty(x => x.LastEnrichmentError, update.ErrorCode)
            .SetProperty(x => x.ImdbId, imdbId)
            .SetProperty(x => x.NomineeType, resolvedType), cancellationToken);
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
