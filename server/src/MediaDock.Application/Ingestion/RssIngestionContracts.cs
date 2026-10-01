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
    string? EventKind = "ingestion");

public sealed record IngestionUpsertResult(bool TitleCreated, bool OccurrenceCreated);

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

    Task<long> StartRunAsync(
        string trigger,
        DateTimeOffset startedAt,
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
            return $"url:{torrentUrl.Trim()}";
        }

        throw new ArgumentException("A feed entry ID or torrent URL is required.", nameof(feedEntryId));
    }
}

public sealed record RssFeedProfile(string Id, string Name);