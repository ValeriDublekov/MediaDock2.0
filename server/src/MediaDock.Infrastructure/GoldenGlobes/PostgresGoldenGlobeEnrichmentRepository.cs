using MediaDock.Application.GoldenGlobes;
using MediaDock.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.Infrastructure.GoldenGlobes;

public sealed class PostgresGoldenGlobeEnrichmentRepository(MediaDockDbContext db) : IGoldenGlobeEnrichmentRepository
{
    public async Task<IReadOnlyList<GoldenGlobeEnrichmentCandidate>> GetEligibleCandidatesAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var candidates = await db.GoldenGlobeNominations.AsNoTracking()
            .Where(x => x.EnrichmentStatus == GoldenGlobeEnrichmentStatuses.Pending
                || (x.EnrichmentStatus == GoldenGlobeEnrichmentStatuses.TemporaryError
                    && (x.NextEnrichmentAttemptAt == null || x.NextEnrichmentAttemptAt <= now)))
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
        var rows = db.GoldenGlobeNominations
            .Where(x => x.Title == title && x.Year == ceremonyYear && x.NomineeType == sourceType && !x.IsImdbIdManual);
        if (update.ImdbId is null)
        {
            await rows.ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.EnrichmentStatus, update.Status)
                .SetProperty(x => x.EnrichmentAttemptCount, update.AttemptCount)
                .SetProperty(x => x.LastEnrichmentAttemptAt, update.AttemptedAt)
                .SetProperty(x => x.NextEnrichmentAttemptAt, update.NextAttemptAt)
                .SetProperty(x => x.LastEnrichmentError, update.ErrorCode), cancellationToken);
        }
        else
        {
            await rows.ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.EnrichmentStatus, update.Status)
                .SetProperty(x => x.EnrichmentAttemptCount, update.AttemptCount)
                .SetProperty(x => x.LastEnrichmentAttemptAt, update.AttemptedAt)
                .SetProperty(x => x.NextEnrichmentAttemptAt, update.NextAttemptAt)
                .SetProperty(x => x.LastEnrichmentError, update.ErrorCode)
                .SetProperty(x => x.ImdbId, update.ImdbId), cancellationToken);
        }
    }

    public async Task<bool> SaveManualOutcomeAsync(
        GoldenGlobeManualRefreshCandidate candidate,
        GoldenGlobeEnrichmentUpdate update,
        CancellationToken cancellationToken = default)
    {
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
        return affected > 0;
    }
}
