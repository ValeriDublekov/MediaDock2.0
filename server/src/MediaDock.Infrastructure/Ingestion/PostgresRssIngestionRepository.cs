using MediaDock.Application.Ingestion;
using MediaDock.Application.Metadata;
using MediaDock.Application.Parsing;
using MediaDock.Infrastructure.Metadata;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text.Json;

namespace MediaDock.Infrastructure.Ingestion;

public sealed class PostgresRssIngestionRepository(MediaDockDbContext dbContext) : IRssIngestionRepository
{
    private const int MaxCachedSnapshotsPerProfile = 5;

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

    public async Task<IReadOnlyList<IngestionRetryItem>> GetLatestRetryableItemsAsync(
        CancellationToken cancellationToken = default)
    {
        var latestLogIds = dbContext.ParseLogs
            .Where(log => log.SourceId != null && log.SourceItemKey != null)
            .GroupBy(log => new { log.SourceId, log.SourceItemKey })
            .Select(group => group
                .OrderByDescending(log => log.ProcessedAt)
                .ThenByDescending(log => log.Id)
                .Select(log => log.Id)
                .First());

        return await dbContext.ParseLogs.AsNoTracking()
            .Where(log => latestLogIds.Contains(log.Id)
                && log.RetryState == "retryable"
                && log.Source != null)
            .OrderBy(log => log.SourceId)
            .ThenBy(log => log.ProcessedAt)
            .ThenBy(log => log.Id)
            .Select(log => new IngestionRetryItem(
                new IngestionSource(
                    log.Source!.Id,
                    log.Source.Name,
                    log.FeedType ?? log.Source.FeedType,
                    log.Source.Url),
                log.SourceItemKey!,
                new IngestionFeedItem(
                    log.RawTitle,
                    log.FeedEntryId,
                    log.TorrentUrl,
                    log.SourcePublishedAt)))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<IngestionLegacyRetryItem>> GetLegacyRetryItemsAsync(
        CancellationToken cancellationToken = default)
    {
        var latestLegacyLogIds = dbContext.ParseLogs
            .Where(log => (log.SourceId == null || log.SourceItemKey == null)
                && log.RetryState == "retryable")
            .GroupBy(log => new { log.FeedName, log.FeedType, log.RawTitle })
            .Select(group => group
                .OrderByDescending(log => log.ProcessedAt)
                .ThenByDescending(log => log.Id)
                .Select(log => log.Id)
                .First());

        return await dbContext.ParseLogs.AsNoTracking()
            .Where(log => latestLegacyLogIds.Contains(log.Id))
            .OrderBy(log => log.ProcessedAt)
            .ThenBy(log => log.Id)
            .Select(log => new IngestionLegacyRetryItem(
                log.Id,
                log.SourceId,
                log.SourceItemKey,
                log.FeedName,
                log.FeedType,
                new IngestionFeedItem(log.RawTitle, log.FeedEntryId, log.TorrentUrl, log.SourcePublishedAt)))
            .ToListAsync(cancellationToken);
    }

    public async Task AssociateLegacyRetryItemAsync(
        long parseLogId,
        long sourceId,
        string sourceItemKey,
        IngestionFeedItem feedItem,
        string feedType,
        CancellationToken cancellationToken = default)
    {
        var log = await dbContext.ParseLogs.SingleAsync(value => value.Id == parseLogId, cancellationToken);
        log.SourceId = sourceId;
        log.SourceItemKey = sourceItemKey;
        log.FeedEntryId = feedItem.FeedEntryId;
        log.TorrentUrl = feedItem.TorrentUrl;
        log.SourcePublishedAt = feedItem.PublishedAt;
        log.FeedType = feedType;
        await dbContext.SaveChangesAsync(cancellationToken);
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

    public async Task SavePreviewSnapshotAsync(
        string profileId,
        DateTimeOffset cachedAt,
        IReadOnlyList<IngestionFeedSnapshot> feeds,
        CancellationToken cancellationToken = default)
    {
        var utcTicks = cachedAt.UtcDateTime.Ticks;
        var snapshot = new RssFeedPreviewSnapshot
        {
            ProfileId = profileId,
            CachedAt = new DateTimeOffset(utcTicks - utcTicks % 10, TimeSpan.Zero),
            FeedsJson = JsonSerializer.Serialize(feeds)
        };
        dbContext.RssFeedPreviewSnapshots.Add(snapshot);
        await dbContext.SaveChangesAsync(cancellationToken);

        var expiredSnapshots = await dbContext.RssFeedPreviewSnapshots
            .Where(candidate => candidate.ProfileId == profileId && candidate.Id != snapshot.Id)
            .OrderByDescending(candidate => candidate.CachedAt)
            .ThenByDescending(candidate => candidate.Id)
            .Skip(MaxCachedSnapshotsPerProfile - 1)
            .ToListAsync(cancellationToken);
        dbContext.RssFeedPreviewSnapshots.RemoveRange(expiredSnapshots);
        await dbContext.SaveChangesAsync(cancellationToken);
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
        feedItem = feedItem with { TorrentUrl = TorrentUrlIdentity.Normalize(feedItem.TorrentUrl!) };
        try
        {
            return await UpsertCatalogItemOnceAsync(
                source, feedItem, sourceItemKey, itemFingerprint, parsed, metadata, observedAt, cancellationToken);
        }
        catch (DbUpdateException exception) when (
            IsImdbIdentityUniqueViolation(exception) || IsTorrentUrlUniqueViolation(exception))
        {
            dbContext.ChangeTracker.Clear();
            if (IsTorrentUrlUniqueViolation(exception) && await TryRefreshKnownTorrentByUrlAsync(
                source, feedItem, sourceItemKey, itemFingerprint, observedAt, cancellationToken))
            {
                return new IngestionUpsertResult(false, false);
            }

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
            var imdbId = ImdbIdNormalizer.Normalize(metadata.ImdbId);
            var release = await dbContext.TorrentReleases
                .Include(entity => entity.TorrentMovie)
                .SingleOrDefaultAsync(
                    entity => entity.SourceId == source.Id && entity.SourceItemKey == sourceItemKey,
                    cancellationToken);
            var movie = imdbId is null
                ? release?.TorrentMovie
                : await dbContext.TorrentMovies.SingleOrDefaultAsync(
                    entity => entity.ImdbId == imdbId,
                    cancellationToken);
            if (movie is null && release?.TorrentMovie is { } existingMovie
                && ImdbIdNormalizer.IsCompatible(existingMovie.ImdbId, imdbId))
            {
                movie = existingMovie;
            }

            var movieCreated = movie is null;
            if (movie is null)
            {
                movie = new TorrentMovie();
                dbContext.TorrentMovies.Add(movie);
            }

            var canonicalImdbId = imdbId ?? ImdbIdNormalizer.Normalize(movie.ImdbId);
            var imdbTitle = canonicalImdbId is null
                ? null
                : (await ImdbTitleMetadataMapper.EnsureAsync(
                    dbContext, [canonicalImdbId], observedAt, cancellationToken))[canonicalImdbId];
            if (imdbTitle is not null)
            {
                ImdbTitleMetadataMapper.Apply(imdbTitle, metadata with { ImdbId = canonicalImdbId }, observedAt);
            }

            ApplyMovieSourceData(movie, metadata, canonicalImdbId, observedAt);
            var releaseCreated = release is null;
            if (release is null)
            {
                release = new TorrentRelease
                {
                    SourceId = source.Id,
                    SourceItemKey = sourceItemKey,
                    FirstSeenAt = observedAt
                };
                dbContext.TorrentReleases.Add(release);
            }

            release.TorrentMovie = movie;
            release.FeedEntryId = feedItem.FeedEntryId;
            release.TorrentUrl = feedItem.TorrentUrl!;
            release.RawTitle = feedItem.Title!;
            release.SourceFeedName = source.Name;
            release.FeedType = source.FeedType;
            release.SourcePublishedAt = feedItem.PublishedAt;
            release.ObservedAt = observedAt;
            release.LastSeenAt = observedAt;

            var itemState = await GetOrCreateItemStateAsync(source.Id, sourceItemKey, cancellationToken);
            itemState.Fingerprint = itemFingerprint;
            itemState.Disposition = "resolved";
            itemState.UpdatedAt = observedAt;
            itemState.ExpiresAt = null;

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new IngestionUpsertResult(movieCreated, releaseCreated);
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
            var release = await dbContext.TorrentReleases
                .Include(value => value.TorrentMovie)
                .SingleOrDefaultAsync(
                    value => value.SourceId == sourceId && value.SourceItemKey == sourceItemKey,
                    cancellationToken);
            if (release is not null && IsSameRelease(release, source, feedItem))
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
                UpdateSeenAt(release, observedAt);
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
            var release = await dbContext.TorrentReleases
                .Include(value => value.TorrentMovie)
                .SingleOrDefaultAsync(
                    value => value.SourceId == sourceId && value.SourceItemKey == sourceItemKey,
                    cancellationToken);
            if (release is null)
            {
                dbContext.RssItemStates.Remove(state);
                await dbContext.SaveChangesAsync(cancellationToken);
                return false;
            }

            UpdateSeenAt(release, observedAt);
        }

        state.UpdatedAt = observedAt;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> TryRefreshKnownTorrentByUrlAsync(
        IngestionSource source,
        IngestionFeedItem feedItem,
        string sourceItemKey,
        string itemFingerprint,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(feedItem.TorrentUrl))
        {
            return false;
        }

        var torrentUrl = TorrentUrlIdentity.Normalize(feedItem.TorrentUrl);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var release = await dbContext.TorrentReleases
                .Include(entity => entity.TorrentMovie)
                .SingleOrDefaultAsync(entity => entity.TorrentUrl == torrentUrl, cancellationToken);
            if (release is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return false;
            }

            if (release.SourceId != source.Id || release.SourceItemKey != sourceItemKey)
            {
                var previousItemState = await dbContext.RssItemStates.SingleOrDefaultAsync(
                    entity => entity.SourceId == release.SourceId && entity.SourceItemKey == release.SourceItemKey,
                    cancellationToken);
                if (previousItemState is not null)
                {
                    dbContext.RssItemStates.Remove(previousItemState);
                }
            }

            long? supersededMovieId = null;
            var sourceItemRelease = await dbContext.TorrentReleases.SingleOrDefaultAsync(
                entity => entity.SourceId == source.Id && entity.SourceItemKey == sourceItemKey,
                cancellationToken);
            if (sourceItemRelease is not null && sourceItemRelease.Id != release.Id)
            {
                supersededMovieId = sourceItemRelease.TorrentMovieId;
                dbContext.TorrentReleases.Remove(sourceItemRelease);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            release.SourceId = source.Id;
            release.SourceItemKey = sourceItemKey;
            release.FeedEntryId = feedItem.FeedEntryId;
            release.TorrentUrl = torrentUrl;
            release.RawTitle = feedItem.Title ?? string.Empty;
            release.SourceFeedName = source.Name;
            release.FeedType = source.FeedType;
            release.SourcePublishedAt = feedItem.PublishedAt;
            release.ObservedAt = observedAt;
            release.LastSeenAt = observedAt;
            release.TorrentMovie.LastSeenAt = observedAt;

            var itemState = await GetOrCreateItemStateAsync(source.Id, sourceItemKey, cancellationToken);
            itemState.Fingerprint = itemFingerprint;
            itemState.Disposition = "resolved";
            itemState.UpdatedAt = observedAt;
            itemState.ExpiresAt = null;

            await dbContext.SaveChangesAsync(cancellationToken);
            if (supersededMovieId is long movieId &&
                movieId != release.TorrentMovieId &&
                !await dbContext.TorrentReleases.AnyAsync(entity => entity.TorrentMovieId == movieId, cancellationToken))
            {
                var orphanedMovie = await dbContext.TorrentMovies.SingleOrDefaultAsync(
                    entity => entity.Id == movieId,
                    cancellationToken);
                if (orphanedMovie is not null)
                {
                    dbContext.TorrentMovies.Remove(orphanedMovie);
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
            }

            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();
            throw;
        }
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
            EventKind = log.EventKind,
            FeedEntryId = log.FeedEntryId,
            TorrentUrl = log.TorrentUrl
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

    private static bool IsSameRelease(
        TorrentRelease release,
        IngestionSource source,
        IngestionFeedItem feedItem) =>
        release.RawTitle == feedItem.Title &&
        release.TorrentUrl == feedItem.TorrentUrl &&
        release.SourcePublishedAt == feedItem.PublishedAt &&
        release.FeedType == source.FeedType;

    private static void UpdateSeenAt(TorrentRelease release, DateTimeOffset observedAt)
    {
        release.LastSeenAt = observedAt;
        release.ObservedAt = observedAt;
        release.TorrentMovie.LastSeenAt = observedAt;
    }

    private static void ApplyMovieSourceData(
        TorrentMovie movie,
        MetadataDetails metadata,
        string? imdbId,
        DateTimeOffset observedAt)
    {
        if (!ImdbIdNormalizer.IsCompatible(movie.ImdbId, imdbId))
        {
            throw new InvalidOperationException("Torrent movie IMDb identity conflicts with its existing identity.");
        }

        movie.TitleText = metadata.Title;
        movie.NormalizedTitle = TitleNormalizer.NormalizeTitle(metadata.Title);
        movie.Year = metadata.Year;
        movie.MediaType = metadata.MediaType;
        movie.SourceType = metadata.SourceType;
        movie.ContentKind = metadata.ContentKind;
        movie.BroadcastRangeStartYear = metadata.BroadcastRange?.StartYear;
        movie.BroadcastRangeEndYear = metadata.BroadcastRange?.EndYear;
        movie.BroadcastRangeRaw = metadata.BroadcastRange?.Raw;
        movie.ImdbId = imdbId ?? ImdbIdNormalizer.Normalize(movie.ImdbId);
        movie.FirstSeenAt ??= observedAt;
        movie.LastSeenAt = observedAt;
        movie.UpdatedAt = observedAt;
    }

    private static bool IsImdbIdentityUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_torrent_movies_imdb_id"
        };

    private static bool IsTorrentUrlUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_torrent_releases_torrent_url"
        };

}