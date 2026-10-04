using MediaDock.Application.GoldenGlobes;
using MediaDock.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.Infrastructure.GoldenGlobes;

public sealed class PostgresGoldenGlobeEnrichmentRepository(MediaDockDbContext db) : IGoldenGlobeEnrichmentRepository
{
    public async Task<IReadOnlyList<GoldenGlobeEnrichmentCandidate>> GetEligibleCandidatesAsync(DateTimeOffset now, CancellationToken cancellationToken = default) =>
        await db.GoldenGlobeNominations.AsNoTracking()
            .Where(x => x.EnrichmentStatus == GoldenGlobeEnrichmentStatuses.Pending || x.EnrichmentStatus == GoldenGlobeEnrichmentStatuses.TemporaryError)
            .GroupBy(x => new { x.Title, x.Year })
            .Select(x => new GoldenGlobeEnrichmentCandidate(x.Key.Title, x.Key.Year, x.Max(y => y.EnrichmentAttemptCount)))
            .OrderByDescending(x => x.CeremonyYear).ThenBy(x => x.Title)
            .ToListAsync(cancellationToken);

    public async Task SaveOutcomeAsync(string title, int ceremonyYear, GoldenGlobeEnrichmentUpdate update, CancellationToken cancellationToken = default)
    {
        var rows = await db.GoldenGlobeNominations.Where(x => x.Title == title && x.Year == ceremonyYear).ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            row.EnrichmentStatus = update.Status; row.EnrichmentAttemptCount = update.AttemptCount;
            row.LastEnrichmentAttemptAt = update.AttemptedAt; row.NextEnrichmentAttemptAt = update.NextAttemptAt; row.LastEnrichmentError = update.ErrorCode;
            if (update.ImdbId is not null) row.ImdbId = update.ImdbId;
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
