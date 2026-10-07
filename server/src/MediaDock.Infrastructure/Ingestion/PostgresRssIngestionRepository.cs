using MediaDock.Application.Ingestion;
using MediaDock.Application.Metadata;
using MediaDock.Application.Parsing;
using MediaDock.Infrastructure.Metadata;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediaDock.Infrastructure.Ingestion;

public sealed class PostgresRssIngestionRepository(MediaDockDbContext dbContext) : IRssIngestionRepository
{
    public async Task<IReadOnlyList<IngestionSource>> GetEnabledSourcesAsync(
        CancellationToken cancellationToken = default)
    {
        var sources = await dbContext.Sources
            .AsNoTracking()
            .Where(source => source.IsEnabled)
            .OrderBy(source => source.FeedType)
            .ThenBy(source => source.Id)
            .Select(source => new { source.Id, source.FeedType, source.Url })
            .ToListAsync(cancellationToken);

        return sources
            .Select(source => new IngestionSource(
                source.Id,
                RssFeedTypes.ProfileName(source.FeedType),
                source.FeedType,
                source.Url))
            .ToArray();
    }

    public async Task<IngestionMatchSettings> GetMatchSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        var setting = await dbContext.Settings.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == 1, cancellationToken);
        return setting is null
            ? new IngestionMatchSettings([], [])
            : new IngestionMatchSettings(setting.ExcludedCountries, setting.ExcludedGenres);
    }

    public async Task<long> StartRunAsync(
        string trigger,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken = default)
    {
        var run = new ScanRun
        {
            StartedAt = startedAt,
            Status = "running",
            Trigger = trigger
        };
        dbContext.ScanRuns.Add(run);
        await dbContext.SaveChangesAsync(cancellationToken);
        return run.Id;
    }

    public async Task<IngestionUpsertResult> UpsertCatalogItemAsync(
        IngestionSource source,
        IngestionFeedItem feedItem,
        string sourceItemKey,
        string itemFingerprint,
        ParsedRutrackerTitle parsed,
        MetadataDetails metadata,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await UpsertCatalogItemOnceAsync(
                source, feedItem, sourceItemKey, itemFingerprint, parsed, metadata, observedAt, cancellationToken);
        }
        catch (DbUpdateException exception) when (IsImdbIdentityUniqueViolation(exception))
        {
            dbContext.ChangeTracker.Clear();
            return await UpsertCatalogItemOnceAsync(
                source, feedItem, sourceItemKey, itemFingerprint, parsed, metadata, observedAt, cancellationToken);
        }
    }

    private async Task<IngestionUpsertResult> UpsertCatalogItemOnceAsync(
        IngestionSource source,
        IngestionFeedItem feedItem,
        string sourceItemKey,
        string itemFingerprint,
        ParsedRutrackerTitle parsed,
        MetadataDetails metadata,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
                var normalizedTitle = TitleMetadataMapper.NormalizeTitle(metadata.Title);
                var imdbId = ImdbIdNormalizer.Normalize(metadata.ImdbId);
            Title? title = null;
                if (imdbId is not null)
            {
                title = await dbContext.Titles.FirstOrDefaultAsync(
                    entity => entity.ImdbId == imdbId,
                    cancellationToken);
            }

            title ??= await dbContext.Titles
                .Where(entity => entity.NormalizedTitle == normalizedTitle
                    && entity.Year == metadata.Year
                    && entity.MediaType == metadata.MediaType
                    && (imdbId == null || entity.ImdbId == null || entity.ImdbId == imdbId))
                .OrderBy(entity => entity.Id)
                .FirstOrDefaultAsync(cancellationToken);

            var titleCreated = title is null;
            if (title is null)
            {
                title = new Title { FirstSeenAt = observedAt };
                dbContext.Titles.Add(title);
            }

            TitleMetadataMapper.Apply(title, metadata, observedAt);

            var occurrence = await dbContext.Occurrences.FirstOrDefaultAsync(
                entity => entity.SourceId == source.Id && entity.SourceItemKey == sourceItemKey,
                cancellationToken);
            var occurrenceCreated = occurrence is null;
            if (occurrence is null)
            {
                occurrence = new Occurrence
                {
                    SourceId = source.Id,
                    SourceItemKey = sourceItemKey,
                    FirstSeenAt = observedAt
                };
                dbContext.Occurrences.Add(occurrence);
            }

            occurrence.Title = title;
            occurrence.FeedEntryId = feedItem.FeedEntryId;
            occurrence.TorrentUrl = feedItem.TorrentUrl!;
            occurrence.RawTitle = feedItem.Title!;
            occurrence.SourceFeedName = source.Name;
            occurrence.FeedType = source.FeedType;
            occurrence.SourcePublishedAt = feedItem.PublishedAt;
            occurrence.ObservedAt = observedAt;
            occurrence.LastSeenAt = observedAt;

            var itemState = await GetOrCreateItemStateAsync(source.Id, sourceItemKey, cancellationToken);
            itemState.Fingerprint = itemFingerprint;
            itemState.Disposition = "resolved";
            itemState.UpdatedAt = observedAt;
            itemState.ExpiresAt = null;

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new IngestionUpsertResult(titleCreated, occurrenceCreated);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<bool> TrySkipProcessedItemAsync(
        IngestionSource source,
        IngestionFeedItem feedItem,
        string sourceItemKey,
        string itemFingerprint,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default)
    {
        var sourceId = source.Id;
        var state = await dbContext.RssItemStates.SingleOrDefaultAsync(
            value => value.SourceId == sourceId && value.SourceItemKey == sourceItemKey,
            cancellationToken);
        if (state is null)
        {
            var occurrence = await dbContext.Occurrences
                .Include(value => value.Title)
                .SingleOrDefaultAsync(
                    value => value.SourceId == sourceId && value.SourceItemKey == sourceItemKey,
                    cancellationToken);
            if (occurrence is not null && IsSameLegacyOccurrence(occurrence, source, feedItem))
            {
                state = new RssItemProcessingState
                {
                    SourceId = sourceId,
                    SourceItemKey = sourceItemKey,
                    Fingerprint = itemFingerprint,
                    Disposition = "resolved",
                    UpdatedAt = observedAt,
                    ExpiresAt = observedAt.AddDays(2)
                };
                dbContext.RssItemStates.Add(state);
                UpdateSeenAt(occurrence, observedAt);
                await dbContext.SaveChangesAsync(cancellationToken);
                return true;
            }

            var previousLog = await dbContext.ParseLogs.AsNoTracking()
                .Where(value => value.SourceId == sourceId && value.SourceItemKey == sourceItemKey)
                .OrderByDescending(value => value.ProcessedAt)
                .ThenByDescending(value => value.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (previousLog is null || previousLog.RetryState != "terminal" ||
                previousLog.RawTitle != feedItem.Title || previousLog.FeedType != source.FeedType ||
                previousLog.SourcePublishedAt != feedItem.PublishedAt ||
                previousLog.ProcessedAt.AddDays(2) <= observedAt)
            {
                return false;
            }

            state = new RssItemProcessingState
            {
                SourceId = sourceId,
                SourceItemKey = sourceItemKey,
                Fingerprint = itemFingerprint,
                Disposition = "terminal",
                UpdatedAt = previousLog.ProcessedAt,
                ExpiresAt = previousLog.ProcessedAt.AddDays(2)
            };
            dbContext.RssItemStates.Add(state);
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        if (state.Fingerprint != itemFingerprint || state.ExpiresAt <= observedAt)
        {
            return false;
        }

        if (state.Disposition == "resolved")
        {
            var occurrence = await dbContext.Occurrences
                .Include(value => value.Title)
                .SingleOrDefaultAsync(
                    value => value.SourceId == sourceId && value.SourceItemKey == sourceItemKey,
                    cancellationToken);
            if (occurrence is null)
            {
                dbContext.RssItemStates.Remove(state);
                await dbContext.SaveChangesAsync(cancellationToken);
                return false;
            }

            UpdateSeenAt(occurrence, observedAt);
        }

        state.UpdatedAt = observedAt;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task MarkTerminalItemAsync(
        long sourceId,
        string sourceItemKey,
        string itemFingerprint,
        DateTimeOffset processedAt,
        CancellationToken cancellationToken = default)
    {
        var state = await GetOrCreateItemStateAsync(sourceId, sourceItemKey, cancellationToken);
        state.Fingerprint = itemFingerprint;
        state.Disposition = "terminal";
        state.UpdatedAt = processedAt;
        state.ExpiresAt = processedAt.AddDays(2);
        await dbContext.SaveChangesAsync(cancellationToken);
    }


    public async Task AddParseLogsAsync(
        long scanRunId,
        IReadOnlyCollection<IngestionParseLog> logs,
        CancellationToken cancellationToken = default)
    {
        if (logs.Count == 0)
        {
            return;
        }

        dbContext.ParseLogs.AddRange(logs.Select(log => new ParseLog
        {
            SourceId = log.SourceId,
            ScanRunId = scanRunId,
            SourceItemKey = log.SourceItemKey,
            RawTitle = log.RawTitle,
            FeedName = log.FeedName,
            ParsedSuccessfully = log.ParsedSuccessfully,
            ParsedTitle = log.ParsedTitle,
            ParsedYear = log.ParsedYear,
            LookupTitles = log.LookupTitles.ToArray(),
            OmdbStatus = log.OmdbStatus,
            Ignored = log.Ignored,
            IgnoreReason = log.IgnoreReason,
            ErrorMessage = log.ErrorMessage,
            Decision = log.Decision,
            ProcessedAt = log.ProcessedAt,
            RetryState = log.RetryState,
            AttemptCount = log.AttemptCount,
            LastAttemptAt = log.LastAttemptAt,
            FeedType = log.FeedType,
            SourcePublishedAt = log.SourcePublishedAt,
            ObservedAt = log.ObservedAt,
            EventKind = log.EventKind
        }));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteRunAsync(
        long runId,
        IngestionRunSummary summary,
        CancellationToken cancellationToken = default)
    {
        var run = await dbContext.ScanRuns.SingleAsync(entity => entity.Id == runId, cancellationToken);
        run.Status = summary.Status;
        run.FinishedAt = summary.FinishedAt;
        run.FeedsProcessed = summary.FeedsProcessed;
        run.EntriesSeen = summary.EntriesSeen;
        run.KnownEntriesSkipped = summary.KnownEntriesSkipped;
        run.TitlesCreated = summary.TitlesCreated;
        run.OccurrencesCreated = summary.OccurrencesCreated;
        run.CacheHits = summary.CacheHits;
        run.OmdbRequests = summary.OmdbRequests;
        run.IgnoredEntries = summary.IgnoredEntries;
        run.ErrorCount = summary.ErrorCount;
        run.ErrorSummary = summary.ErrorSummary.ToArray();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<RssItemProcessingState> GetOrCreateItemStateAsync(
        long sourceId,
        string sourceItemKey,
        CancellationToken cancellationToken)
    {
        var state = await dbContext.RssItemStates.SingleOrDefaultAsync(
            value => value.SourceId == sourceId && value.SourceItemKey == sourceItemKey,
            cancellationToken);
        if (state is not null)
        {
            return state;
        }

        state = new RssItemProcessingState { SourceId = sourceId, SourceItemKey = sourceItemKey };
        dbContext.RssItemStates.Add(state);
        return state;
    }

    private static bool IsSameLegacyOccurrence(
        Occurrence occurrence,
        IngestionSource source,
        IngestionFeedItem feedItem) =>
        occurrence.RawTitle == feedItem.Title &&
        occurrence.TorrentUrl == feedItem.TorrentUrl &&
        occurrence.SourcePublishedAt == feedItem.PublishedAt &&
        occurrence.FeedType == source.FeedType;

    private static void UpdateSeenAt(Occurrence occurrence, DateTimeOffset observedAt)
    {
        occurrence.LastSeenAt = observedAt;
        occurrence.ObservedAt = observedAt;
        occurrence.Title.LastSeenAt = observedAt;
    }

    private static bool IsImdbIdentityUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_titles_imdb_id"
        };

}