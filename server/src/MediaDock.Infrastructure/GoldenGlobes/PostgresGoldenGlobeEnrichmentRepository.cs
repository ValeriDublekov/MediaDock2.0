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
        var rows = await db.GoldenGlobeNominations.Where(x => x.Title == title && x.Year == ceremonyYear && x.NomineeType == sourceType).ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            row.EnrichmentStatus = update.Status; row.EnrichmentAttemptCount = update.AttemptCount;
            row.LastEnrichmentAttemptAt = update.AttemptedAt; row.NextEnrichmentAttemptAt = update.NextAttemptAt; row.LastEnrichmentError = update.ErrorCode;
            if (update.ImdbId is not null) row.ImdbId = update.ImdbId;
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
