using System.Diagnostics;
using System.Text;
using MediaDock.Application.Metadata;

namespace MediaDock.Application.Ingestion;

public sealed class RssIngestionService
{
    private const int MaximumFeedSizeBytes = 4 * 1024 * 1024;

    private readonly IRssIngestionRepository _repository;
    private readonly IRssFeedTransport _feedTransport;
    private readonly TimeProvider _timeProvider;
    private readonly RssEntryProcessor _entryProcessor;
    private readonly RssFailedEntryRecheckOrchestrator _recheckOrchestrator;

    public RssIngestionService(
        IRssIngestionRepository repository,
        IRssFeedTransport feedTransport,
        MetadataResolver metadataResolver,
        TimeProvider? timeProvider = null)
    {
        _repository = repository;
        _feedTransport = feedTransport;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _entryProcessor = new RssEntryProcessor(_repository, metadataResolver, _timeProvider);
        _recheckOrchestrator = new RssFailedEntryRecheckOrchestrator(
            _repository, _feedTransport, _entryProcessor, _timeProvider);
    }

    public async Task<IngestionRunResult> RunAsync(
        string trigger = "manual",
        CancellationToken cancellationToken = default,
        Func<IngestionProgressUpdate, CancellationToken, Task>? reportProgress = null)
    {
        if (trigger is not ("schedule" or "manual" or "local"))
        {
            throw new ArgumentException("Unsupported scan trigger.", nameof(trigger));
        }

        var startedAt = _timeProvider.GetUtcNow();
        var runId = await _repository.StartRunAsync(trigger, startedAt, cancellationToken);
        var progress = new IngestionRunContext(runId);
        var previewFeeds = new Dictionary<string, List<IngestionFeedSnapshot>>(StringComparer.Ordinal);
        await progress.ReportProgressAsync("started", reportProgress, cancellationToken);

        try
        {
            var sources = await _repository.GetEnabledSourcesAsync(cancellationToken);
            var settings = await _repository.GetMatchSettingsAsync(cancellationToken);
            foreach (var source in sources)
            {
                await ProcessSourceAsync(source, settings, progress, previewFeeds, reportProgress, cancellationToken);
                if (progress.OmdbBudgetExhausted)
                {
                    break;
                }
            }

            foreach (var (profileId, feeds) in previewFeeds)
            {
                await _repository.SavePreviewSnapshotAsync(
                    profileId,
                    _timeProvider.GetUtcNow(),
                    feeds,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            progress.RecordError("Scan cancelled.");
            await progress.FlushLogsAsync(_repository, CancellationToken.None);
            var cancelled = progress.ToSummary(_timeProvider.GetUtcNow(), "failed");
            await _repository.CompleteRunAsync(runId, cancelled, CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            progress.RecordError($"Scan failed ({exception.GetType().Name}).");
        }

        var status = progress.ErrorCount == 0
            ? "succeeded"
            : progress.FeedsProcessed == 0
                ? "failed"
                : "partial";
        var summary = progress.ToSummary(_timeProvider.GetUtcNow(), status);
        await progress.FlushLogsAsync(_repository, cancellationToken);
        await _repository.CompleteRunAsync(runId, summary, cancellationToken);
        await progress.ReportProgressAsync("completed", reportProgress, cancellationToken);
        return new IngestionRunResult(runId, summary);
    }

    private async Task ProcessSourceAsync(
        IngestionSource source,
        IngestionMatchSettings settings,
        IngestionRunContext progress,
        Dictionary<string, List<IngestionFeedSnapshot>> previewFeeds,
        Func<IngestionProgressUpdate, CancellationToken, Task>? reportProgress,
        CancellationToken cancellationToken)
    {
        progress.CurrentSource = IngestionRunContext.BoundText(source.Name, 200);
        await progress.ReportProgressAsync("source_started", reportProgress, cancellationToken);
        try
        {
            var fetchStartedAt = Stopwatch.GetTimestamp();
            var body = await _feedTransport.FetchAsync(source.Url, cancellationToken);
            var fetchDurationMs = Stopwatch.GetElapsedTime(fetchStartedAt).TotalMilliseconds;
            var parseStartedAt = Stopwatch.GetTimestamp();
            var entries = RssFeedParser.Parse(body);
            var parseDurationMs = Stopwatch.GetElapsedTime(parseStartedAt).TotalMilliseconds;
            if (!previewFeeds.TryGetValue(source.FeedType, out var feeds))
            {
                feeds = [];
                previewFeeds.Add(source.FeedType, feeds);
            }

            feeds.Add(new IngestionFeedSnapshot(
                source.Id,
                source.Url,
                Encoding.UTF8.GetString(body),
                fetchDurationMs,
                parseDurationMs,
                entries));
            progress.FeedsProcessed++;
            foreach (var entry in entries)
            {
                progress.EntriesSeen++;
                await _entryProcessor.ProcessAsync(source, entry, settings, progress, cancellationToken);
                if (progress.EntriesSeen % 25 == 0)
                {
                    await progress.ReportProgressAsync("processing_entries", reportProgress, cancellationToken);
                }

                if (progress.OmdbBudgetExhausted)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            progress.RecordError($"Feed '{source.Name}' failed ({exception.GetType().Name}).");
            progress.AddLog(
                new IngestionParseLog(
                    source.Id,
                    null,
                    string.Empty,
                    source.Name,
                    false,
                    null,
                    null,
                    "feed_error",
                    true,
                    "feed_error",
                    exception.GetType().Name,
                    null,
                    _timeProvider.GetUtcNow(),
                    "retryable",
                    1,
                    _timeProvider.GetUtcNow(),
                    source.FeedType,
                    null,
                    _timeProvider.GetUtcNow(),
                    "feed"));
        }

        await progress.FlushLogsAsync(_repository, cancellationToken);
        await progress.ReportProgressAsync("source_completed", reportProgress, cancellationToken);
    }

    public Task<IngestionRecheckResult> RecheckFailedAsync(
        CancellationToken cancellationToken = default,
        Func<IngestionProgressUpdate, CancellationToken, Task>? reportProgress = null)
        => _recheckOrchestrator.RunAsync(cancellationToken, reportProgress);

}