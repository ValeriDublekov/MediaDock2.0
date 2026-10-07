using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using MediaDock.Application.Matching;
using MediaDock.Application.Metadata;
using MediaDock.Application.Parsing;

namespace MediaDock.Application.Ingestion;

public sealed class RssIngestionService
{
    private const int MaximumTitleLength = 2048;
    private const int MaximumFeedUrlLength = 2048;
    private const int MaximumParseLogsPerRun = 1000;
    private const int MaximumErrorSummaries = 50;
    private const int MaximumFeedSizeBytes = 4 * 1024 * 1024;
    private const int ItemFingerprintVersion = 2;

    private static readonly Regex SensitiveValue = new(
        @"\b(api_?key|key)(\s*[=:]\s*)[^\s&]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly IRssIngestionRepository _repository;
    private readonly IRssFeedTransport _feedTransport;
    private readonly MetadataResolver _metadataResolver;
    private readonly TimeProvider _timeProvider;

    public RssIngestionService(
        IRssIngestionRepository repository,
        IRssFeedTransport feedTransport,
        MetadataResolver metadataResolver,
        TimeProvider? timeProvider = null)
    {
        _repository = repository;
        _feedTransport = feedTransport;
        _metadataResolver = metadataResolver;
        _timeProvider = timeProvider ?? TimeProvider.System;
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
        var progress = new RunProgress { RunId = runId };
        var pendingLogs = new List<IngestionParseLog>();
        await ReportProgressAsync("started", progress, reportProgress, cancellationToken);

        try
        {
            var sources = await _repository.GetEnabledSourcesAsync(cancellationToken);
            var settings = await _repository.GetMatchSettingsAsync(cancellationToken);
            foreach (var source in sources)
            {
                progress.CurrentSource = BoundText(source.Name, 200);
                await ReportProgressAsync("source_started", progress, reportProgress, cancellationToken);
                try
                {
                    var body = await _feedTransport.FetchAsync(source.Url, cancellationToken);
                    var entries = ParseFeed(body);
                    progress.FeedsProcessed++;
                    foreach (var entry in entries)
                    {
                        progress.EntriesSeen++;
                        await ProcessEntryAsync(source, entry, settings, progress, pendingLogs, cancellationToken);
                        if (progress.EntriesSeen % 25 == 0)
                        {
                            await ReportProgressAsync("processing_entries", progress, reportProgress, cancellationToken);
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
                    AddLog(
                        pendingLogs,
                        progress,
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

                await FlushLogsAsync(pendingLogs, progress, cancellationToken);
                await ReportProgressAsync("source_completed", progress, reportProgress, cancellationToken);
                if (progress.OmdbBudgetExhausted)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            progress.RecordError("Scan cancelled.");
            await FlushLogsAsync(pendingLogs, progress, CancellationToken.None);
            var cancelled = BuildSummary(progress, _timeProvider.GetUtcNow(), "failed");
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
        var summary = BuildSummary(progress, _timeProvider.GetUtcNow(), status);
        await FlushLogsAsync(pendingLogs, progress, cancellationToken);
        await _repository.CompleteRunAsync(runId, summary, cancellationToken);
        await ReportProgressAsync("completed", progress, reportProgress, cancellationToken);
        return new IngestionRunResult(runId, summary);
    }

    public async Task<IngestionRecheckResult> RecheckFailedAsync(
        CancellationToken cancellationToken = default,
        Func<IngestionProgressUpdate, CancellationToken, Task>? reportProgress = null)
    {
        var startedAt = _timeProvider.GetUtcNow();
        var runId = await _repository.StartRunAsync("manual", startedAt, cancellationToken);
        var progress = new RunProgress { RunId = runId };
        var pendingLogs = new List<IngestionParseLog>();
        IReadOnlyList<IngestionRetryItem> items = [];
        var fallbackFeeds = new Dictionary<long, IReadOnlyDictionary<string, IngestionFeedItem>>();
        var processedSources = new HashSet<long>();
        var unavailableItems = 0;

        await ReportProgressAsync("recheck_started", progress, reportProgress, cancellationToken);
        try
        {
            items = await _repository.GetLatestRetryableItemsAsync(cancellationToken);
            progress.MaximumParseLogs = items.Count;
            var settings = await _repository.GetMatchSettingsAsync(cancellationToken);
            foreach (var item in items)
            {
                progress.EntriesSeen++;
                progress.CurrentSource = BoundText(item.Source.Name, 200);
                if (processedSources.Add(item.Source.Id))
                {
                    progress.FeedsProcessed++;
                }

                var feedItem = item.FeedItem;
                if (!IsUsableUrl(feedItem.TorrentUrl))
                {
                    if (!fallbackFeeds.TryGetValue(item.Source.Id, out var entries))
                    {
                        try
                        {
                            var body = await _feedTransport.FetchAsync(item.Source.Url, cancellationToken);
                            entries = ParseFeed(body)
                                .Select(entry => (Entry: entry, Key: TryCreateSourceItemKey(entry)))
                                .Where(value => value.Key is not null)
                                .GroupBy(value => value.Key!, StringComparer.Ordinal)
                                .ToDictionary(group => group.Key, group => group.First().Entry, StringComparer.Ordinal);
                            fallbackFeeds[item.Source.Id] = entries;
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            entries = new Dictionary<string, IngestionFeedItem>(StringComparer.Ordinal);
                            fallbackFeeds[item.Source.Id] = entries;
                            progress.RecordError($"Feed '{item.Source.Name}' could not restore an old retry item ({exception.GetType().Name}).");
                        }
                    }

                    if (!entries.TryGetValue(item.SourceItemKey, out feedItem))
                    {
                        unavailableItems++;
                        progress.KnownEntriesSkipped++;
                        continue;
                    }
                }

                await ProcessEntryAsync(
                    item.Source,
                    feedItem,
                    settings,
                    progress,
                    pendingLogs,
                    cancellationToken,
                    skipProcessedItem: false,
                    sourceItemKeyOverride: item.SourceItemKey);

                if (progress.EntriesSeen % 25 == 0)
                {
                    await FlushLogsAsync(pendingLogs, progress, cancellationToken);
                    await ReportProgressAsync("rechecking_entries", progress, reportProgress, cancellationToken);
                }

                if (progress.OmdbBudgetExhausted)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            progress.RecordError("Failed-entry recheck cancelled.");
            await FlushLogsAsync(pendingLogs, progress, CancellationToken.None);
            var cancelled = BuildSummary(progress, _timeProvider.GetUtcNow(), "failed");
            await _repository.CompleteRunAsync(runId, cancelled, CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            progress.RecordError($"Failed-entry recheck failed ({exception.GetType().Name}).");
        }

        var status = progress.ErrorCount == 0
            ? "succeeded"
            : progress.EntriesSeen > unavailableItems ? "partial" : "failed";
        var summary = BuildSummary(progress, _timeProvider.GetUtcNow(), status);
        await FlushLogsAsync(pendingLogs, progress, cancellationToken);
        await _repository.CompleteRunAsync(runId, summary, cancellationToken);
        await ReportProgressAsync("recheck_completed", progress, reportProgress, cancellationToken);
        return new IngestionRecheckResult(new IngestionRunResult(runId, summary), items.Count, unavailableItems);
    }

    private async Task ProcessEntryAsync(
        IngestionSource source,
        IngestionFeedItem entry,
        IngestionMatchSettings settings,
        RunProgress progress,
        List<IngestionParseLog> pendingLogs,
        CancellationToken cancellationToken,
        bool skipProcessedItem = true,
        string? sourceItemKeyOverride = null)
    {
        var observedAt = _timeProvider.GetUtcNow();
        var rawTitle = entry.Title ?? string.Empty;
        var sourceItemKey = sourceItemKeyOverride ?? TryCreateSourceItemKey(entry);
        if (string.IsNullOrWhiteSpace(rawTitle) || rawTitle.Length > MaximumTitleLength || !IsUsableUrl(entry.TorrentUrl))
        {
            RecordEntryFailure(
                source,
                entry,
                sourceItemKey,
                rawTitle,
                null,
                "malformed_entry",
                progress,
                pendingLogs,
                observedAt);
            return;
        }

        if (sourceItemKey is null)
        {
            RecordEntryFailure(
                source,
                entry,
                null,
                rawTitle,
                null,
                "malformed_entry",
                progress,
                pendingLogs,
                observedAt);
            return;
        }

        var itemFingerprint = CreateItemFingerprint(source, entry, settings);
        if (skipProcessedItem && await _repository.TrySkipProcessedItemAsync(
            source,
            entry,
                sourceItemKey,
                itemFingerprint,
                observedAt,
                cancellationToken))
        {
            progress.KnownEntriesSkipped++;
            return;
        }

        ParsedRutrackerTitle parsed;
        try
        {
            parsed = RutrackerTitleParser.Parse(rawTitle, source.FeedType);
        }
        catch (Exception exception)
        {
            RecordEntryFailure(
                source,
                entry,
                sourceItemKey,
                rawTitle,
                null,
                $"parse_error:{exception.GetType().Name}",
                progress,
                pendingLogs,
                observedAt);
            return;
        }

        if (string.IsNullOrWhiteSpace(parsed.Title) || parsed.Title.Length > 160)
        {
            RecordEntryFailure(
                source,
                entry,
                sourceItemKey,
                rawTitle,
                parsed,
                "empty_title",
                progress,
                pendingLogs,
                observedAt);
            return;
        }

        var lookupTitles = parsed.Candidates
            .Select(candidate => candidate.Title)
            .Prepend(parsed.Title)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();
        var attemptedLookupTitles = new List<string>(lookupTitles.Length);
        MetadataResolution? resolution = null;
        var fallbackUsed = false;
        var ambiguousHit = false;
        var totalAttempts = 0;
        foreach (var lookupTitle in lookupTitles)
        {
            fallbackUsed = resolution is not null;
            attemptedLookupTitles.Add(lookupTitle);
            resolution = await _metadataResolver.ResolveAsync(
                lookupTitle,
                parsed.Year,
                RssFeedTypes.MetadataSourceType(source.FeedType),
                observedAt,
                cancellationToken);
            progress.CacheHits += resolution.CacheHit ? 1 : 0;
            progress.OmdbRequests += resolution.HttpAttempts;
            totalAttempts += resolution.HttpAttempts;
            if (resolution.Status is MetadataLookupStatus.QuotaExceeded or MetadataLookupStatus.RequestBudgetExhausted)
            {
                progress.OmdbBudgetExhausted = true;
            }

            if (resolution.Status == MetadataLookupStatus.Found && resolution.Metadata is not null &&
                MatchPolicy.VerifyTitle(parsed, resolution.Metadata.Title).Status == MatchDecisionStatus.Accepted)
            {
                break;
            }

            ambiguousHit |= resolution.Status == MetadataLookupStatus.Found;

            if (resolution.Status != MetadataLookupStatus.ConfirmedNotFound && resolution.Status != MetadataLookupStatus.Found)
            {
                break;
            }
        }

        if (resolution is null)
        {
            return;
        }

        if (resolution.Status == MetadataLookupStatus.ConfirmedNotFound && !ambiguousHit)
        {
            await _repository.MarkTerminalItemAsync(
                source.Id, sourceItemKey, itemFingerprint, observedAt, cancellationToken);
            progress.IgnoredEntries++;
            AddLog(
                pendingLogs,
                progress,
                CreateLog(
                    source,
                    entry,
                    sourceItemKey,
                    rawTitle,
                    parsed,
                    "confirmed_not_found",
                    true,
                    "metadata_not_found",
                    null,
                    fallbackUsed ? "fallback_not_found" : "not_found",
                    observedAt,
                    "terminal",
                    totalAttempts,
                    attemptedLookupTitles));
            return;
        }

        if (ambiguousHit && resolution.Status == MetadataLookupStatus.ConfirmedNotFound)
        {
            await _repository.MarkTerminalItemAsync(
                source.Id, sourceItemKey, itemFingerprint, observedAt, cancellationToken);
            progress.IgnoredEntries++;
            AddLog(pendingLogs, progress, CreateLog(
                source, entry, sourceItemKey, rawTitle, parsed, "found", true,
                "ambiguous_title_match", null, "fallback_ambiguous", observedAt, "terminal", totalAttempts,
                attemptedLookupTitles));
            return;
        }

        if (resolution.Status != MetadataLookupStatus.Found || resolution.Metadata is null)
        {
            var errorCode = StatusCode(resolution.Status);
            RecordEntryFailure(
                source,
                entry,
                sourceItemKey,
                rawTitle,
                parsed,
                errorCode,
                progress,
                pendingLogs,
                observedAt,
                OmdbStatus(resolution.Status),
                totalAttempts,
                attemptedLookupTitles);
            return;
        }

        var metadata = resolution.Metadata;
        if (MatchPolicy.VerifyTitle(parsed, metadata.Title).Status != MatchDecisionStatus.Accepted)
        {
            await _repository.MarkTerminalItemAsync(
                source.Id, sourceItemKey, itemFingerprint, observedAt, cancellationToken);
            progress.IgnoredEntries++;
            AddLog(pendingLogs, progress, CreateLog(
                source, entry, sourceItemKey, rawTitle, parsed, "found", true,
                "ambiguous_title_match", null, fallbackUsed ? "fallback_ambiguous" : "ambiguous_title_match",
                observedAt, "terminal", totalAttempts, attemptedLookupTitles));
            return;
        }

        var decision = MatchPolicy.Evaluate(
            source.FeedType,
            metadata.SourceType,
            metadata.MediaType,
            RssFeedTypes.IsSeries(source.FeedType) ? null : parsed.Year,
            metadata.Year,
            metadata.BroadcastRange,
            metadata.Countries,
            metadata.Genres,
            settings.ExcludedCountries,
            settings.ExcludedGenres);
        if (decision.Status != MatchDecisionStatus.Accepted)
        {
            await _repository.MarkTerminalItemAsync(
                source.Id, sourceItemKey, itemFingerprint, observedAt, cancellationToken);
            progress.IgnoredEntries++;
            AddLog(
                pendingLogs,
                progress,
                CreateLog(
                    source,
                    entry,
                    sourceItemKey,
                    rawTitle,
                    parsed,
                    "found",
                    true,
                    decision.ReasonCode,
                    null,
                    decision.ReasonCode,
                    observedAt,
                    "terminal",
                    totalAttempts,
                    attemptedLookupTitles));
            return;
        }

        var upsert = await _repository.UpsertCatalogItemAsync(
            source,
            entry,
            sourceItemKey,
            itemFingerprint,
            parsed,
            metadata,
            observedAt,
            cancellationToken);
        progress.TitlesCreated += upsert.TitleCreated ? 1 : 0;
        progress.OccurrencesCreated += upsert.OccurrenceCreated ? 1 : 0;
        AddLog(
            pendingLogs,
            progress,
            CreateLog(
                source,
                entry,
                sourceItemKey,
                rawTitle,
                parsed,
                "found",
                false,
                null,
                null,
                fallbackUsed ? $"fallback_{decision.ReasonCode}" : decision.ReasonCode,
                observedAt,
                "resolved",
                totalAttempts,
                attemptedLookupTitles));
    }

    private void RecordEntryFailure(
        IngestionSource source,
        IngestionFeedItem entry,
        string? sourceItemKey,
        string rawTitle,
        ParsedRutrackerTitle? parsed,
        string errorCode,
        RunProgress progress,
        List<IngestionParseLog> pendingLogs,
        DateTimeOffset observedAt,
        string omdbStatus = "not_requested",
        int attempts = 0,
        IReadOnlyList<string>? lookupTitles = null)
    {
        progress.IgnoredEntries++;
        progress.RecordError($"Entry in '{source.Name}' failed ({errorCode}).");
        AddLog(
            pendingLogs,
            progress,
            CreateLog(
                source,
                entry,
                sourceItemKey,
                rawTitle,
                parsed,
                omdbStatus,
                true,
                errorCode,
                errorCode,
                null,
                observedAt,
                "retryable",
                attempts,
                lookupTitles));
    }

    private static IngestionParseLog CreateLog(
        IngestionSource source,
        IngestionFeedItem entry,
        string? sourceItemKey,
        string rawTitle,
        ParsedRutrackerTitle? parsed,
        string omdbStatus,
        bool ignored,
        string? ignoreReason,
        string? errorMessage,
        string? decision,
        DateTimeOffset observedAt,
        string retryState,
        int attempts,
        IReadOnlyList<string>? lookupTitles = null) =>
        new(
            source.Id,
            sourceItemKey,
            rawTitle,
            source.Name,
            parsed is not null && !string.IsNullOrWhiteSpace(parsed.Title),
            parsed?.Title,
            parsed?.Year,
            omdbStatus,
            ignored,
            ignoreReason,
            errorMessage,
            parsed is null ? decision : string.Join('|', new[] { decision,
                RssFeedTypes.IsSeries(source.FeedType) && parsed.Year is not null ? "y_season" : parsed.YearSource switch
                {
                    "title_parenthesis" => "y_paren",
                    "bracket" => "y_bracket",
                    "standalone" => "y_standalone",
                    "invalid_year_range" => "y_invalid",
                    _ => "y_missing"
                }, parsed.YearAmbiguous ? "multi" : null }
                .Where(code => !string.IsNullOrEmpty(code))),
            observedAt,
            retryState,
            attempts,
            attempts > 0 ? observedAt : null,
            source.FeedType,
            entry.PublishedAt,
            observedAt)
        {
            LookupTitles = lookupTitles ?? [],
            FeedEntryId = entry.FeedEntryId,
            TorrentUrl = entry.TorrentUrl
        };

    private static IReadOnlyList<IngestionFeedItem> ParseFeed(byte[] body)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumFeedSizeBytes,
            MaxCharactersFromEntities = 0,
            ConformanceLevel = ConformanceLevel.Document
        };

        using var stream = new MemoryStream(body, writable: false);
        using var reader = XmlReader.Create(stream, settings);
        var document = XDocument.Load(reader);
        var root = document.Root ?? throw new FormatException("Feed has no root element.");
        IEnumerable<XElement> elements = root.Name.LocalName switch
        {
            "rss" => root.Elements().FirstOrDefault(element => element.Name.LocalName == "channel")?
                .Elements().Where(element => element.Name.LocalName == "item")
                ?? throw new FormatException("RSS feed has no channel."),
            "feed" => root.Elements().Where(element => element.Name.LocalName == "entry"),
            _ => throw new FormatException("Feed root is not RSS or Atom.")
        };

        return elements.Select(ParseEntry).ToArray();
    }

    private static IngestionFeedItem ParseEntry(XElement element)
    {
        var title = ChildValue(element, "title");
        var id = ChildValue(element, "guid") ?? ChildValue(element, "id");
        var linkElement = element.Elements().FirstOrDefault(child => child.Name.LocalName == "link");
        var link = linkElement?.Attribute("href")?.Value ?? linkElement?.Value;
        var published = ChildValue(element, "pubDate")
            ?? ChildValue(element, "published")
            ?? ChildValue(element, "updated");
        DateTimeOffset? publishedAt = DateTimeOffset.TryParse(
            published,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsedDate)
            ? parsedDate
            : null;

        return new IngestionFeedItem(title, id, link?.Trim(), publishedAt);
    }

    private static string? ChildValue(XElement parent, string name) =>
        parent.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value.Trim();

    private static bool IsUsableUrl(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= MaximumFeedUrlLength
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https";

    private static string? TryCreateSourceItemKey(IngestionFeedItem entry)
    {
        try
        {
            return SourceItemIdentity.From(entry.FeedEntryId, entry.TorrentUrl);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string StatusCode(MetadataLookupStatus status) => status switch
    {
        MetadataLookupStatus.QuotaExceeded => "quota_exhausted",
        MetadataLookupStatus.RequestBudgetExhausted => "daily_budget_exhausted",
        MetadataLookupStatus.TransportFailure => "transport_error",
        MetadataLookupStatus.AuthenticationFailure => "authentication_error",
        MetadataLookupStatus.InvalidRequest => "invalid_request",
        _ => "provider_error"
    };

    private static string OmdbStatus(MetadataLookupStatus status) => status switch
    {
        MetadataLookupStatus.QuotaExceeded => "quota_exhausted",
        MetadataLookupStatus.RequestBudgetExhausted => "budget_exhausted",
        MetadataLookupStatus.TransportFailure => "transport_error",
        MetadataLookupStatus.AuthenticationFailure => "authentication_error",
        MetadataLookupStatus.InvalidRequest => "invalid_request",
        MetadataLookupStatus.ProviderFailure => "provider_error",
        _ => "not_requested"
    };

    private static void AddLog(
        List<IngestionParseLog> pendingLogs,
        RunProgress progress,
        IngestionParseLog log)
    {
        if (progress.ParseLogsWritten + pendingLogs.Count >= progress.MaximumParseLogs)
        {
            return;
        }

        pendingLogs.Add(log with
        {
            RawTitle = BoundText(log.RawTitle, MaximumTitleLength + 1),
            FeedName = BoundText(log.FeedName, 200),
            ParsedTitle = BoundText(log.ParsedTitle, 500),
            OmdbStatus = BoundText(log.OmdbStatus, 32),
            IgnoreReason = BoundText(log.IgnoreReason, 64),
            ErrorMessage = BoundText(log.ErrorMessage, 500),
            Decision = BoundText(log.Decision, 64),
            LookupTitles = log.LookupTitles.Take(2).Select(title => BoundText(title, 160)).ToArray(),
            FeedEntryId = BoundText(log.FeedEntryId, MaximumFeedUrlLength + 1),
            TorrentUrl = BoundText(log.TorrentUrl, MaximumFeedUrlLength + 1)
        });
    }

    private async Task FlushLogsAsync(
        List<IngestionParseLog> pendingLogs,
        RunProgress progress,
        CancellationToken cancellationToken)
    {
        if (pendingLogs.Count == 0)
        {
            return;
        }

        await _repository.AddParseLogsAsync(progress.RunId, pendingLogs, cancellationToken);
        progress.ParseLogsWritten += pendingLogs.Count;
        pendingLogs.Clear();
    }

    private async Task ReportProgressAsync(
        string stage,
        RunProgress progress,
        Func<IngestionProgressUpdate, CancellationToken, Task>? reportProgress,
        CancellationToken cancellationToken)
    {
        if (reportProgress is null)
        {
            return;
        }

        await reportProgress(
            new IngestionProgressUpdate(
                progress.RunId,
                stage,
                progress.CurrentSource,
                progress.FeedsProcessed,
                progress.EntriesSeen,
                progress.KnownEntriesSkipped,
                progress.TitlesCreated,
                progress.OccurrencesCreated,
                progress.CacheHits,
                progress.OmdbRequests,
                progress.IgnoredEntries,
                progress.ErrorCount),
            cancellationToken);
    }

    private static IngestionRunSummary BuildSummary(
        RunProgress progress,
        DateTimeOffset finishedAt,
        string status) =>
        new(
            status,
            progress.FeedsProcessed,
            progress.EntriesSeen,
            progress.KnownEntriesSkipped,
            progress.TitlesCreated,
            progress.OccurrencesCreated,
            progress.CacheHits,
            progress.OmdbRequests,
            progress.IgnoredEntries,
            progress.ErrorCount,
            progress.ErrorSummary,
            finishedAt);

    private static string BoundText(string? value, int maximumLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var sanitized = new string(value.Select(character => char.IsControl(character) ? ' ' : character).ToArray());
        sanitized = SensitiveValue.Replace(sanitized, "$1$2[REDACTED]");
        sanitized = Regex.Replace(sanitized, @"\s+", " ", RegexOptions.CultureInvariant);
        return sanitized.Length <= maximumLength ? sanitized : sanitized[..maximumLength];
    }

    private static string CreateItemFingerprint(
        IngestionSource source,
        IngestionFeedItem entry,
        IngestionMatchSettings settings)
    {
        var input = JsonSerializer.Serialize(new
        {
            Version = ItemFingerprintVersion,
            source.FeedType,
            entry.Title,
            entry.TorrentUrl,
            entry.PublishedAt,
            ExcludedCountries = settings.ExcludedCountries.OrderBy(value => value, StringComparer.OrdinalIgnoreCase),
            ExcludedGenres = settings.ExcludedGenres.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }

    private sealed class RunProgress
    {
        public long RunId { get; init; }
        public int MaximumParseLogs { get; set; } = MaximumParseLogsPerRun;
        public string? CurrentSource { get; set; }
        public int FeedsProcessed { get; set; }
        public int EntriesSeen { get; set; }
        public int KnownEntriesSkipped { get; set; }
        public int TitlesCreated { get; set; }
        public int OccurrencesCreated { get; set; }
        public int CacheHits { get; set; }
        public int OmdbRequests { get; set; }
        public bool OmdbBudgetExhausted { get; set; }
        public int IgnoredEntries { get; set; }
        public int ErrorCount { get; private set; }
        public int ParseLogsWritten { get; set; }
        public List<string> ErrorSummary { get; } = [];

        public void RecordError(string message)
        {
            ErrorCount++;
            if (ErrorSummary.Count < MaximumErrorSummaries)
            {
                ErrorSummary.Add(BoundText(message, 300));
            }
        }
    }
}