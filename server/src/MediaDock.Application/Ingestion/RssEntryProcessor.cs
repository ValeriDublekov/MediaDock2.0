using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediaDock.Application.Matching;
using MediaDock.Application.Metadata;
using MediaDock.Application.Parsing;

namespace MediaDock.Application.Ingestion;

internal sealed class RssEntryProcessor
{
    private const int ItemFingerprintVersion = 2;

    private readonly IRssIngestionRepository _repository;
    private readonly MetadataResolver _metadataResolver;
    private readonly TimeProvider _timeProvider;

    public RssEntryProcessor(
        IRssIngestionRepository repository,
        MetadataResolver metadataResolver,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _metadataResolver = metadataResolver;
        _timeProvider = timeProvider;
    }

    public async Task ProcessAsync(
        IngestionSource source,
        IngestionFeedItem entry,
        IngestionMatchSettings settings,
        IngestionRunContext context,
        CancellationToken cancellationToken,
        bool skipProcessedItem = true,
        string? sourceItemKeyOverride = null)
    {
        var prepared = await PrepareEntryAsync(
            source, entry, settings, context, cancellationToken, skipProcessedItem, sourceItemKeyOverride);
        if (prepared is null)
        {
            return;
        }

        var lookup = await ResolveMetadataAsync(prepared, context, cancellationToken);
        if (lookup is not null)
        {
            await ApplyResolutionAsync(prepared, lookup, settings, context, cancellationToken);
        }
    }

    private async Task<PreparedEntry?> PrepareEntryAsync(
        IngestionSource source,
        IngestionFeedItem entry,
        IngestionMatchSettings settings,
        IngestionRunContext context,
        CancellationToken cancellationToken,
        bool skipProcessedItem,
        string? sourceItemKeyOverride)
    {
        var observedAt = _timeProvider.GetUtcNow();
        var rawTitle = entry.Title ?? string.Empty;
        var sourceItemKey = sourceItemKeyOverride ?? TryCreateSourceItemKey(entry);
        if (string.IsNullOrWhiteSpace(rawTitle) ||
            rawTitle.Length > IngestionRunContext.MaximumTitleLength ||
            !IsUsableUrl(entry.TorrentUrl))
        {
            RecordEntryFailure(source, entry, sourceItemKey, rawTitle, null, "malformed_entry", context, observedAt);
            return null;
        }

        if (sourceItemKey is null)
        {
            RecordEntryFailure(source, entry, null, rawTitle, null, "malformed_entry", context, observedAt);
            return null;
        }

        var itemFingerprint = CreateItemFingerprint(source, entry, settings);
        if (skipProcessedItem && await _repository.TrySkipProcessedItemAsync(
            source, entry, sourceItemKey, itemFingerprint, observedAt, cancellationToken))
        {
            context.KnownEntriesSkipped++;
            return null;
        }

        ParsedRutrackerTitle parsed;
        try
        {
            parsed = RutrackerTitleParser.Parse(rawTitle, source.FeedType);
        }
        catch (Exception exception)
        {
            RecordEntryFailure(
                source, entry, sourceItemKey, rawTitle, null,
                $"parse_error:{exception.GetType().Name}", context, observedAt);
            return null;
        }

        if (string.IsNullOrWhiteSpace(parsed.Title) || parsed.Title.Length > 160)
        {
            RecordEntryFailure(source, entry, sourceItemKey, rawTitle, parsed, "empty_title", context, observedAt);
            return null;
        }

        var lookupTitles = parsed.Candidates
            .Select(candidate => candidate.Title)
            .Prepend(parsed.Title)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();

        return new PreparedEntry(
            source, entry, sourceItemKey, itemFingerprint, rawTitle, parsed, observedAt, lookupTitles);
    }

    private async Task<MetadataLookup?> ResolveMetadataAsync(
        PreparedEntry entry,
        IngestionRunContext context,
        CancellationToken cancellationToken)
    {
        var attemptedTitles = new List<string>(entry.LookupTitles.Count);
        MetadataResolution? resolution = null;
        var fallbackUsed = false;
        var ambiguousHit = false;
        var totalAttempts = 0;

        foreach (var lookupTitle in entry.LookupTitles)
        {
            fallbackUsed = resolution is not null;
            attemptedTitles.Add(lookupTitle);
            resolution = await _metadataResolver.ResolveAsync(
                lookupTitle,
                entry.Parsed.Year,
                RssFeedTypes.MetadataSourceType(entry.Source.FeedType),
                entry.ObservedAt,
                cancellationToken);
            context.CacheHits += resolution.CacheHit ? 1 : 0;
            context.OmdbRequests += resolution.HttpAttempts;
            totalAttempts += resolution.HttpAttempts;
            context.OmdbBudgetExhausted |= resolution.Status is
                MetadataLookupStatus.QuotaExceeded or MetadataLookupStatus.RequestBudgetExhausted;

            if (IsAcceptedTitleMatch(entry.Parsed, resolution))
            {
                break;
            }

            ambiguousHit |= resolution.Status == MetadataLookupStatus.Found;
            if (resolution.Status is not (MetadataLookupStatus.ConfirmedNotFound or MetadataLookupStatus.Found))
            {
                break;
            }
        }

        return resolution is null
            ? null
            : new MetadataLookup(resolution, fallbackUsed, ambiguousHit, totalAttempts, attemptedTitles);
    }

    private async Task ApplyResolutionAsync(
        PreparedEntry entry,
        MetadataLookup lookup,
        IngestionMatchSettings settings,
        IngestionRunContext context,
        CancellationToken cancellationToken)
    {
        if (lookup.Resolution.Status == MetadataLookupStatus.ConfirmedNotFound)
        {
            await ApplyConfirmedNotFoundAsync(entry, lookup, context, cancellationToken);
            return;
        }

        if (lookup.Resolution.Status != MetadataLookupStatus.Found || lookup.Resolution.Metadata is null)
        {
            RecordEntryFailure(
                entry.Source,
                entry.FeedItem,
                entry.SourceItemKey,
                entry.RawTitle,
                entry.Parsed,
                StatusCode(lookup.Resolution.Status),
                context,
                entry.ObservedAt,
                OmdbStatus(lookup.Resolution.Status),
                lookup.TotalAttempts,
                lookup.AttemptedTitles);
            return;
        }

        await ApplyFoundMetadataAsync(entry, lookup, settings, context, cancellationToken);
    }

    private async Task ApplyConfirmedNotFoundAsync(
        PreparedEntry entry,
        MetadataLookup lookup,
        IngestionRunContext context,
        CancellationToken cancellationToken)
    {
        if (lookup.AmbiguousHit)
        {
            await RecordTerminalAsync(
                entry, lookup, context, "found", "ambiguous_title_match", "fallback_ambiguous", cancellationToken);
            return;
        }

        await _repository.MarkTerminalItemAsync(
            entry.Source.Id, entry.SourceItemKey, entry.ItemFingerprint, entry.ObservedAt, cancellationToken);
        context.IgnoredEntries++;
        context.AddLog(CreateLog(
            entry, "confirmed_not_found", true, "metadata_not_found", null,
            lookup.FallbackUsed ? "fallback_not_found" : "not_found", "terminal", lookup.TotalAttempts,
            lookup.AttemptedTitles));
    }

    private async Task ApplyFoundMetadataAsync(
        PreparedEntry entry,
        MetadataLookup lookup,
        IngestionMatchSettings settings,
        IngestionRunContext context,
        CancellationToken cancellationToken)
    {
        var metadata = lookup.Resolution.Metadata!;
        if (MatchPolicy.VerifyTitle(entry.Parsed, metadata.Title).Status != MatchDecisionStatus.Accepted)
        {
            var decision = lookup.FallbackUsed ? "fallback_ambiguous" : "ambiguous_title_match";
            await RecordTerminalAsync(
                entry, lookup, context, "found", "ambiguous_title_match", decision, cancellationToken);
            return;
        }

        var decisionResult = MatchPolicy.Evaluate(
            entry.Source.FeedType,
            metadata.SourceType,
            metadata.MediaType,
            RssFeedTypes.IsSeries(entry.Source.FeedType) ? null : entry.Parsed.Year,
            metadata.Year,
            metadata.BroadcastRange,
            metadata.Countries,
            metadata.Genres,
            settings.ExcludedCountries,
            settings.ExcludedGenres);
        if (decisionResult.Status != MatchDecisionStatus.Accepted)
        {
            await RecordTerminalAsync(
                entry, lookup, context, "found", decisionResult.ReasonCode, decisionResult.ReasonCode, cancellationToken);
            return;
        }

        var upsert = await _repository.UpsertCatalogItemAsync(
            entry.Source,
            entry.FeedItem,
            entry.SourceItemKey,
            entry.ItemFingerprint,
            entry.Parsed,
            metadata,
            entry.ObservedAt,
            cancellationToken);
        context.TitlesCreated += upsert.TitleCreated ? 1 : 0;
        context.OccurrencesCreated += upsert.OccurrenceCreated ? 1 : 0;
        context.AddLog(CreateLog(
            entry, "found", false, null, null,
            lookup.FallbackUsed ? $"fallback_{decisionResult.ReasonCode}" : decisionResult.ReasonCode,
            "resolved", lookup.TotalAttempts, lookup.AttemptedTitles));
    }

    private async Task RecordTerminalAsync(
        PreparedEntry entry,
        MetadataLookup lookup,
        IngestionRunContext context,
        string omdbStatus,
        string ignoreReason,
        string decision,
        CancellationToken cancellationToken)
    {
        await _repository.MarkTerminalItemAsync(
            entry.Source.Id, entry.SourceItemKey, entry.ItemFingerprint, entry.ObservedAt, cancellationToken);
        context.IgnoredEntries++;
        context.AddLog(CreateLog(
            entry, omdbStatus, true, ignoreReason, null, decision, "terminal",
            lookup.TotalAttempts, lookup.AttemptedTitles));
    }

    private void RecordEntryFailure(
        IngestionSource source,
        IngestionFeedItem entry,
        string? sourceItemKey,
        string rawTitle,
        ParsedRutrackerTitle? parsed,
        string errorCode,
        IngestionRunContext context,
        DateTimeOffset observedAt,
        string omdbStatus = "not_requested",
        int attempts = 0,
        IReadOnlyList<string>? lookupTitles = null)
    {
        context.IgnoredEntries++;
        context.RecordError($"Entry in '{source.Name}' failed ({errorCode}).");
        context.AddLog(CreateLog(
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

    private static bool IsAcceptedTitleMatch(ParsedRutrackerTitle parsed, MetadataResolution resolution) =>
        resolution.Status == MetadataLookupStatus.Found && resolution.Metadata is not null &&
        MatchPolicy.VerifyTitle(parsed, resolution.Metadata.Title).Status == MatchDecisionStatus.Accepted;

    private static IngestionParseLog CreateLog(
        PreparedEntry entry,
        string omdbStatus,
        bool ignored,
        string? ignoreReason,
        string? errorMessage,
        string? decision,
        string retryState,
        int attempts,
        IReadOnlyList<string>? lookupTitles = null) =>
        CreateLog(
            entry.Source,
            entry.FeedItem,
            entry.SourceItemKey,
            entry.RawTitle,
            entry.Parsed,
            omdbStatus,
            ignored,
            ignoreReason,
            errorMessage,
            decision,
            entry.ObservedAt,
            retryState,
            attempts,
            lookupTitles);

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

    internal static bool IsUsableUrl(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= IngestionRunContext.MaximumFeedUrlLength
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https";

    internal static string? TryCreateSourceItemKey(IngestionFeedItem entry)
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

    private sealed record PreparedEntry(
        IngestionSource Source,
        IngestionFeedItem FeedItem,
        string SourceItemKey,
        string ItemFingerprint,
        string RawTitle,
        ParsedRutrackerTitle Parsed,
        DateTimeOffset ObservedAt,
        IReadOnlyList<string> LookupTitles);

    private sealed record MetadataLookup(
        MetadataResolution Resolution,
        bool FallbackUsed,
        bool AmbiguousHit,
        int TotalAttempts,
        IReadOnlyList<string> AttemptedTitles);
}