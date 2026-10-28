using MediaDock.Application.Metadata;
using MediaDock.Application.Parsing;

namespace MediaDock.Application.Ingestion;

public sealed record IngestionSource(long Id, string Name, string FeedType, string Url);

public static class RssFeedTypes
{
    public const string Movie = "movie";
    public const string CompleteSeason = "series_complete";
    public const string OngoingSeries = "series_ongoing";

    public static IReadOnlyList<RssFeedProfile> Profiles { get; } = Array.AsReadOnly(new[]
    {
        new RssFeedProfile(Movie, "Movies"),
        new RssFeedProfile(CompleteSeason, "Complete seasons"),
        new RssFeedProfile(OngoingSeries, "Ongoing episodes")
    });

    public static string ProfileName(string feedType) =>
        Profiles.FirstOrDefault(profile => profile.Id == feedType)?.Name
        ?? throw new ArgumentOutOfRangeException(nameof(feedType), "Unsupported RSS feed type.");

    public static bool IsSeries(string? feedType) => feedType?.Trim().ToLowerInvariant() is
        CompleteSeason or OngoingSeries;

    public static string MetadataSourceType(string? feedType) => feedType?.Trim().ToLowerInvariant() switch
    {
        Movie => Movie,
        CompleteSeason or OngoingSeries => "series",
        _ => throw new ArgumentOutOfRangeException(nameof(feedType), "Unsupported RSS feed type.")
    };
}

public sealed record IngestionMatchSettings(string[] ExcludedCountries, string[] ExcludedGenres);

public sealed record IngestionFeedItem(
    string? Title,
    string? FeedEntryId,
    string? TorrentUrl,
    DateTimeOffset? PublishedAt);

public sealed record IngestionFeedSnapshot(
    long SourceId,
    string SourceUrl,
    string OriginalContent,
    double FetchDurationMs,
    double ParseDurationMs,
    IReadOnlyList<IngestionFeedItem> Items);

public sealed record IngestionRetryItem(
    IngestionSource Source,
    string SourceItemKey,
    IngestionFeedItem FeedItem);

public sealed record IngestionLegacyRetryItem(
    long ParseLogId,
    long? SourceId,
    string? SourceItemKey,
    string FeedName,
    string? FeedType,
    IngestionFeedItem FeedItem);

public sealed record IngestionParseLog(
    long? SourceId,
    string? SourceItemKey,
    string RawTitle,
    string FeedName,
    bool ParsedSuccessfully,
    string? ParsedTitle,
    int? ParsedYear,
    string OmdbStatus,
    bool Ignored,
    string? IgnoreReason,
    string? ErrorMessage,
    string? Decision,
    DateTimeOffset ProcessedAt,
    string RetryState,
    int AttemptCount,
    DateTimeOffset? LastAttemptAt,
    string? FeedType,
    DateTimeOffset? SourcePublishedAt,
    DateTimeOffset? ObservedAt,
    string? EventKind = "ingestion")
{
    public IReadOnlyList<string> LookupTitles { get; init; } = [];
    public string? FeedEntryId { get; init; }
    public string? TorrentUrl { get; init; }
}

public sealed record IngestionUpsertResult(bool TitleCreated, bool OccurrenceCreated);

public sealed record IngestionRecheckResult(
    IngestionRunResult Run,
    int RetryableEntriesSelected,
    int EntriesUnavailable);

public sealed record IngestionRunSummary(
    string Status,
    int FeedsProcessed,
    int EntriesSeen,
    int KnownEntriesSkipped,
    int TitlesCreated,
    int OccurrencesCreated,
    int CacheHits,
    int OmdbRequests,
    int IgnoredEntries,
    int ErrorCount,
    IReadOnlyList<string> ErrorSummary,
    DateTimeOffset FinishedAt);

public sealed record IngestionRunResult(long RunId, IngestionRunSummary Summary);

public sealed record IngestionProgressUpdate(
    long ScanRunId,
    string Stage,
    string? Source,
    int FeedsProcessed,
    int EntriesSeen,
    int KnownEntriesSkipped,
    int TitlesCreated,
    int OccurrencesCreated,
    int CacheHits,
    int OmdbRequests,
    int IgnoredEntries,
    int ErrorCount);

public interface IRssFeedTransport
{
    Task<byte[]> FetchAsync(string url, CancellationToken cancellationToken = default);
}

public interface IRssIngestionRepository
{
    Task<IReadOnlyList<IngestionSource>> GetEnabledSourcesAsync(CancellationToken cancellationToken = default);

    Task<IngestionMatchSettings> GetMatchSettingsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IngestionRetryItem>> GetLatestRetryableItemsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IngestionLegacyRetryItem>> GetLegacyRetryItemsAsync(
        CancellationToken cancellationToken = default);

    Task AssociateLegacyRetryItemAsync(
        long parseLogId,
        long sourceId,
        string sourceItemKey,
        IngestionFeedItem feedItem,
        string feedType,
        CancellationToken cancellationToken = default);

    Task<long> StartRunAsync(
        string trigger,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken = default);

    Task SavePreviewSnapshotAsync(
        string profileId,
        DateTimeOffset cachedAt,
        IReadOnlyList<IngestionFeedSnapshot> feeds,
        CancellationToken cancellationToken = default);

    Task<IngestionUpsertResult> UpsertCatalogItemAsync(
        IngestionSource source,
        IngestionFeedItem feedItem,
        string sourceItemKey,
        string itemFingerprint,
        ParsedRutrackerTitle parsed,
        MetadataDetails metadata,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default);

    Task<bool> TrySkipProcessedItemAsync(
        IngestionSource source,
        IngestionFeedItem feedItem,
        string sourceItemKey,
        string itemFingerprint,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default);

    Task<bool> TryRefreshKnownTorrentByUrlAsync(
        IngestionSource source,
        IngestionFeedItem feedItem,
        string sourceItemKey,
        string itemFingerprint,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default);

    Task MarkTerminalItemAsync(
        long sourceId,
        string sourceItemKey,
        string itemFingerprint,
        DateTimeOffset processedAt,
        CancellationToken cancellationToken = default);

    Task AddParseLogsAsync(
        long scanRunId,
        IReadOnlyCollection<IngestionParseLog> logs,
        CancellationToken cancellationToken = default);

    Task CompleteRunAsync(
        long runId,
        IngestionRunSummary summary,
        CancellationToken cancellationToken = default);
}

public static class SourceItemIdentity
{
    public static string From(string? feedEntryId, string? torrentUrl)
    {
        if (!string.IsNullOrWhiteSpace(feedEntryId))
        {
            return $"entry:{feedEntryId.Trim()}";
        }

        if (!string.IsNullOrWhiteSpace(torrentUrl))
        {
            return $"url:{TorrentUrlIdentity.Normalize(torrentUrl)}";
        }

        throw new ArgumentException("A feed entry ID or torrent URL is required.", nameof(feedEntryId));
    }
}

public static class TorrentUrlIdentity
{
    private static readonly HashSet<string> RutrackerHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "rutracker.org",
        "www.rutracker.org",
        "rutracker.cc",
        "www.rutracker.cc"
    };

    public static string Normalize(string torrentUrl)
    {
        var value = torrentUrl.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !RutrackerHosts.Contains(uri.Host) ||
            !uri.AbsolutePath.Equals("/forum/viewtopic.php", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var topicId = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(parameter => parameter.Split('=', 2))
            .Where(parts => parts.Length == 2 && parts[0].Equals("t", StringComparison.OrdinalIgnoreCase))
            .Select(parts => Uri.UnescapeDataString(parts[1]))
            .FirstOrDefault(value => value.Length > 0 &&
                value.All(character => character is >= '0' and <= '9') &&
                value.Any(character => character != '0'));

        return topicId is null
            ? value
            : $"https://rutracker.org/forum/viewtopic.php?t={topicId.TrimStart('0')}";
    }
}

public sealed record RssFeedProfile(string Id, string Name);