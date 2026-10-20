namespace MediaDock.Application.Ingestion;

internal sealed class RssFailedEntryRecheckOrchestrator
{
    private readonly IRssIngestionRepository _repository;
    private readonly IRssFeedTransport _feedTransport;
    private readonly RssEntryProcessor _entryProcessor;
    private readonly TimeProvider _timeProvider;

    public RssFailedEntryRecheckOrchestrator(
        IRssIngestionRepository repository,
        IRssFeedTransport feedTransport,
        RssEntryProcessor entryProcessor,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _feedTransport = feedTransport;
        _entryProcessor = entryProcessor;
        _timeProvider = timeProvider;
    }

    public async Task<IngestionRecheckResult> RunAsync(
        CancellationToken cancellationToken,
        Func<IngestionProgressUpdate, CancellationToken, Task>? reportProgress)
    {
        var startedAt = _timeProvider.GetUtcNow();
        var runId = await _repository.StartRunAsync("manual", startedAt, cancellationToken);
        var progress = new IngestionRunContext(runId);
        IReadOnlyList<IngestionRetryItem> items = [];
        IReadOnlyList<IngestionLegacyRetryItem> legacyItems = [];
        var fallbackFeeds = new FallbackFeedCache(_feedTransport, progress);
        var processedSources = new HashSet<long>();
        var unavailableItems = 0;

        await progress.ReportProgressAsync("recheck_started", reportProgress, cancellationToken);
        try
        {
            items = await _repository.GetLatestRetryableItemsAsync(cancellationToken);
            legacyItems = await _repository.GetLegacyRetryItemsAsync(cancellationToken);
            progress.MaximumParseLogs = items.Count + legacyItems.Count;
            var settings = await _repository.GetMatchSettingsAsync(cancellationToken);
            unavailableItems += await ProcessRetryableItemsAsync(
                items, settings, progress, fallbackFeeds, processedSources, reportProgress, cancellationToken);

            var enabledSources = await _repository.GetEnabledSourcesAsync(cancellationToken);
            unavailableItems += await ProcessLegacyItemsAsync(
                legacyItems, enabledSources, settings, progress, fallbackFeeds, processedSources,
                reportProgress, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            progress.RecordError("Failed-entry recheck cancelled.");
            await progress.FlushLogsAsync(_repository, CancellationToken.None);
            var cancelled = progress.ToSummary(_timeProvider.GetUtcNow(), "failed");
            await _repository.CompleteRunAsync(runId, cancelled, CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            progress.RecordError($"Failed-entry recheck failed ({exception.GetType().Name}).");
        }

        var status = progress.ErrorCount == 0 && unavailableItems == 0
            ? "succeeded"
            : progress.ErrorCount == 0 || progress.EntriesSeen > unavailableItems ? "partial" : "failed";
        var summary = progress.ToSummary(_timeProvider.GetUtcNow(), status);
        await progress.FlushLogsAsync(_repository, cancellationToken);
        await _repository.CompleteRunAsync(runId, summary, cancellationToken);
        await progress.ReportProgressAsync("recheck_completed", reportProgress, cancellationToken);
        return new IngestionRecheckResult(
            new IngestionRunResult(runId, summary), items.Count + legacyItems.Count, unavailableItems);
    }

    private async Task<int> ProcessRetryableItemsAsync(
        IReadOnlyList<IngestionRetryItem> items,
        IngestionMatchSettings settings,
        IngestionRunContext progress,
        FallbackFeedCache fallbackFeeds,
        HashSet<long> processedSources,
        Func<IngestionProgressUpdate, CancellationToken, Task>? reportProgress,
        CancellationToken cancellationToken)
    {
        var unavailableItems = 0;
        foreach (var item in items)
        {
            progress.EntriesSeen++;
            progress.CurrentSource = IngestionRunContext.BoundText(item.Source.Name, 200);
            if (processedSources.Add(item.Source.Id))
            {
                progress.FeedsProcessed++;
            }

            var feedItem = item.FeedItem;
            if (!RssEntryProcessor.IsUsableUrl(feedItem.TorrentUrl))
            {
                var entries = await fallbackFeeds.GetAsync(item.Source, "an old", cancellationToken);
                if (!entries.TryGetValue(item.SourceItemKey, out feedItem))
                {
                    unavailableItems++;
                    progress.KnownEntriesSkipped++;
                    continue;
                }
            }

            await _entryProcessor.ProcessAsync(
                item.Source,
                feedItem,
                settings,
                progress,
                cancellationToken,
                skipProcessedItem: false,
                sourceItemKeyOverride: item.SourceItemKey);
            await ReportEntryProgressAsync(progress, reportProgress, cancellationToken);

            if (progress.OmdbBudgetExhausted)
            {
                break;
            }
        }

        return unavailableItems;
    }

    private async Task<int> ProcessLegacyItemsAsync(
        IReadOnlyList<IngestionLegacyRetryItem> items,
        IReadOnlyList<IngestionSource> enabledSources,
        IngestionMatchSettings settings,
        IngestionRunContext progress,
        FallbackFeedCache fallbackFeeds,
        HashSet<long> processedSources,
        Func<IngestionProgressUpdate, CancellationToken, Task>? reportProgress,
        CancellationToken cancellationToken)
    {
        var unavailableItems = 0;
        foreach (var item in items)
        {
            progress.EntriesSeen++;
            var matches = await FindLegacyMatchesAsync(item, enabledSources, fallbackFeeds, cancellationToken);
            if (matches.Count != 1)
            {
                unavailableItems++;
                progress.KnownEntriesSkipped++;
                continue;
            }

            var match = matches[0];
            if (processedSources.Add(match.Source.Id))
            {
                progress.FeedsProcessed++;
            }

            progress.CurrentSource = IngestionRunContext.BoundText(match.Source.Name, 200);
            await _repository.AssociateLegacyRetryItemAsync(
                item.ParseLogId,
                match.Source.Id,
                match.SourceItemKey,
                match.FeedItem,
                match.Source.FeedType,
                cancellationToken);
            await _entryProcessor.ProcessAsync(
                match.Source,
                match.FeedItem,
                settings,
                progress,
                cancellationToken,
                skipProcessedItem: false,
                sourceItemKeyOverride: match.SourceItemKey);
            await ReportEntryProgressAsync(progress, reportProgress, cancellationToken);

            if (progress.OmdbBudgetExhausted)
            {
                break;
            }
        }

        return unavailableItems;
    }

    private static bool IsEligibleSource(IngestionLegacyRetryItem item, IngestionSource source) =>
        (item.SourceId is null || item.SourceId == source.Id) &&
        (string.IsNullOrWhiteSpace(item.FeedName) ||
            string.Equals(item.FeedName, source.Name, StringComparison.OrdinalIgnoreCase)) &&
        (string.IsNullOrWhiteSpace(item.FeedType) ||
            string.Equals(item.FeedType, source.FeedType, StringComparison.OrdinalIgnoreCase));

    private async Task<List<LegacyMatch>> FindLegacyMatchesAsync(
        IngestionLegacyRetryItem item,
        IReadOnlyList<IngestionSource> enabledSources,
        FallbackFeedCache fallbackFeeds,
        CancellationToken cancellationToken)
    {
        var matches = new List<LegacyMatch>();
        foreach (var source in enabledSources)
        {
            if (!IsEligibleSource(item, source))
            {
                continue;
            }

            var entries = await fallbackFeeds.GetAsync(source, "a legacy", cancellationToken);
            foreach (var entry in entries.Values)
            {
                var sourceItemKey = TryMatchLegacyEntry(item, entry);
                if (sourceItemKey is not null)
                {
                    matches.Add(new LegacyMatch(source, entry, sourceItemKey));
                }
            }
        }

        return matches;
    }

    private static string? TryMatchLegacyEntry(IngestionLegacyRetryItem item, IngestionFeedItem entry)
    {
        if (!string.Equals(item.FeedItem.Title?.Trim(), entry.Title?.Trim(), StringComparison.Ordinal) ||
            (!string.IsNullOrWhiteSpace(item.FeedItem.FeedEntryId) &&
                !string.Equals(item.FeedItem.FeedEntryId, entry.FeedEntryId, StringComparison.Ordinal)) ||
            (!string.IsNullOrWhiteSpace(item.FeedItem.TorrentUrl) &&
                !string.Equals(item.FeedItem.TorrentUrl, entry.TorrentUrl, StringComparison.Ordinal)))
        {
            return null;
        }

        var sourceItemKey = RssEntryProcessor.TryCreateSourceItemKey(entry);
        return sourceItemKey is not null &&
            (item.SourceItemKey is null || item.SourceItemKey == sourceItemKey)
            ? sourceItemKey
            : null;
    }

    private async Task ReportEntryProgressAsync(
        IngestionRunContext progress,
        Func<IngestionProgressUpdate, CancellationToken, Task>? reportProgress,
        CancellationToken cancellationToken)
    {
        if (progress.EntriesSeen % 25 != 0)
        {
            return;
        }

        await progress.FlushLogsAsync(_repository, cancellationToken);
        await progress.ReportProgressAsync("rechecking_entries", reportProgress, cancellationToken);
    }

    private sealed record LegacyMatch(
        IngestionSource Source,
        IngestionFeedItem FeedItem,
        string SourceItemKey);

    private sealed class FallbackFeedCache
    {
        private readonly IRssFeedTransport _feedTransport;
        private readonly IngestionRunContext _progress;
        private readonly Dictionary<long, IReadOnlyDictionary<string, IngestionFeedItem>> _feeds = [];

        public FallbackFeedCache(IRssFeedTransport feedTransport, IngestionRunContext progress)
        {
            _feedTransport = feedTransport;
            _progress = progress;
        }

        public async Task<IReadOnlyDictionary<string, IngestionFeedItem>> GetAsync(
            IngestionSource source,
            string retryDescription,
            CancellationToken cancellationToken)
        {
            if (_feeds.TryGetValue(source.Id, out var cachedEntries))
            {
                return cachedEntries;
            }

            IReadOnlyDictionary<string, IngestionFeedItem> entries;
            try
            {
                var body = await _feedTransport.FetchAsync(source.Url, cancellationToken);
                entries = RssFeedParser.Parse(body)
                    .Select(entry => (Entry: entry, Key: RssEntryProcessor.TryCreateSourceItemKey(entry)))
                    .Where(value => value.Key is not null)
                    .GroupBy(value => value.Key!, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.First().Entry, StringComparer.Ordinal);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                entries = new Dictionary<string, IngestionFeedItem>(StringComparer.Ordinal);
                _progress.RecordError(
                    $"Feed '{source.Name}' could not restore {retryDescription} retry item ({exception.GetType().Name}).");
            }

            _feeds[source.Id] = entries;
            return entries;
        }
    }
}