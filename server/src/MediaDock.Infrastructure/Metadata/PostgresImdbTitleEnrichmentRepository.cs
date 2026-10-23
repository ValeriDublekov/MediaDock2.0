using MediaDock.Application.Metadata;
using MediaDock.Application.PersonalRatings;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace MediaDock.Infrastructure.Metadata;

public sealed class PostgresImdbTitleEnrichmentRepository(MediaDockDbContext dbContext)
    : IImdbTitleEnrichmentRepository
{
    public async Task<IReadOnlyList<ImdbTitleEnrichmentCandidate>> GetEligibleCandidatesAsync(
        long ownerUserId,
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return [];
        }

        var boundedLimit = Math.Min(limit, ImdbTitleEnrichmentService.MaximumBatchSize);
        var eligibleTitles = dbContext.ImdbTitles.AsNoTracking().Where(EligibleAt(now));
        return await dbContext.PersonalRatings
            .AsNoTracking()
            .Where(rating => rating.UserId == ownerUserId)
            .Join(
                eligibleTitles,
                rating => rating.ImdbId,
                title => title.ImdbId,
                (_, title) => title)
            .OrderBy(title => title.NextEnrichmentAttemptAt ?? DateTimeOffset.MinValue)
            .ThenBy(title => title.ImdbId)
            .Take(boundedLimit)
            .Select(title => new ImdbTitleEnrichmentCandidate(title.ImdbId, title.EnrichmentAttemptCount))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ImdbTitleEnrichmentCandidate>> GetEligibleCatalogCandidatesAsync(
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return [];
        }

        var boundedLimit = Math.Min(limit, ImdbTitleEnrichmentService.MaximumBatchSize);
        return await dbContext.ImdbTitles
            .AsNoTracking()
            .Where(EligibleAt(now))
            .OrderBy(title => title.NextEnrichmentAttemptAt ?? DateTimeOffset.MinValue)
            .ThenBy(title => title.ImdbId)
            .Take(boundedLimit)
            .Select(title => new ImdbTitleEnrichmentCandidate(title.ImdbId, title.EnrichmentAttemptCount))
            .ToListAsync(cancellationToken);
    }

    public async Task SaveOutcomeAsync(
        long ownerUserId,
        string imdbId,
        ImdbTitleEnrichmentUpdate update,
        CancellationToken cancellationToken = default)
    {
        await SaveOutcomeCoreAsync(ownerUserId, imdbId, update, cancellationToken);
    }

    public async Task SaveCatalogOutcomeAsync(
        string imdbId,
        ImdbTitleEnrichmentUpdate update,
        CancellationToken cancellationToken = default)
    {
        await SaveOutcomeCoreAsync(null, imdbId, update, cancellationToken);
    }

    private async Task SaveOutcomeCoreAsync(
        long? ownerUserId,
        string imdbId,
        ImdbTitleEnrichmentUpdate update,
        CancellationToken cancellationToken)
    {
        var normalizedImdbId = ImdbIdNormalizer.Normalize(imdbId);
        if (!ImdbIdNormalizer.IsValid(normalizedImdbId))
        {
            throw new ArgumentException("A valid IMDb ID is required.", nameof(imdbId));
        }

        if (update.Status is not (ImdbTitleEnrichmentStatuses.Enriched
            or ImdbTitleEnrichmentStatuses.NotFound
            or ImdbTitleEnrichmentStatuses.TemporaryError))
        {
            throw new ArgumentOutOfRangeException(nameof(update), "The enrichment outcome status is invalid.");
        }

        var titleQuery = dbContext.ImdbTitles.Where(item => item.ImdbId == normalizedImdbId);
        if (ownerUserId is long owner)
        {
            titleQuery = titleQuery.Where(item => dbContext.PersonalRatings.Any(rating =>
                rating.UserId == owner && rating.ImdbId == normalizedImdbId));
        }

        var title = await titleQuery.SingleOrDefaultAsync(cancellationToken);
        if (title is null)
        {
            return;
        }

        if (update.Metadata is not null)
        {
            if (update.Status != ImdbTitleEnrichmentStatuses.Enriched)
            {
                throw new InvalidOperationException("Only successful outcomes may include IMDb metadata.");
            }

            ImdbTitleMetadataMapper.Apply(title, update.Metadata, update.AttemptedAt);
        }

        title.MetadataStatus = update.Status;
        title.EnrichmentAttemptCount = update.AttemptCount;
        title.LastEnrichmentAttemptAt = update.AttemptedAt;
        title.NextEnrichmentAttemptAt = update.NextAttemptAt;
        title.LastEnrichmentError = update.ErrorCode;
        title.UpdatedAt = update.AttemptedAt;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static Expression<Func<ImdbTitle, bool>> EligibleAt(DateTimeOffset now) =>
        title => title.MetadataStatus == ImdbTitleEnrichmentStatuses.Pending
            || ((title.MetadataStatus == ImdbTitleEnrichmentStatuses.TemporaryError
                    || title.MetadataStatus == ImdbTitleEnrichmentStatuses.NotFound)
                && (title.NextEnrichmentAttemptAt == null || title.NextEnrichmentAttemptAt <= now));
}