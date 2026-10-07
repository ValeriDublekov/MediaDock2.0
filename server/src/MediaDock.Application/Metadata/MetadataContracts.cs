using MediaDock.Application.Matching;

namespace MediaDock.Application.Metadata;

public enum MetadataLookupStatus
{
    Found,
    ConfirmedNotFound,
    QuotaExceeded,
    RequestBudgetExhausted,
    TransportFailure,
    AuthenticationFailure,
    InvalidRequest,
    ProviderFailure
}

public enum OmdbRequestPurpose
{
    RssIngestion,
    OscarEnrichment,
    GoldenGlobeEnrichment
}

public sealed record MetadataDetails(
    string Title,
    int? Year,
    string? ImdbId,
    string MediaType,
    string SourceType,
    string ContentKind,
    BroadcastRange? BroadcastRange,
    decimal? ImdbRating,
    long? ImdbVotes,
    decimal? Metascore,
    string[] Genres,
    string[] Countries,
    string? Director,
    string? Plot,
    string? PosterUrl,
    string? Runtime,
    string? Awards,
    string? BoxOffice);

public static class ImdbIdNormalizer
{
    public static string? Normalize(string? imdbId) =>
        string.IsNullOrWhiteSpace(imdbId) ? null : imdbId.Trim().ToLowerInvariant();

    public static bool IsValid(string? imdbId)
    {
        var normalizedImdbId = Normalize(imdbId);
        return normalizedImdbId is { Length: >= 9 and <= 12 }
            && normalizedImdbId.StartsWith("tt", StringComparison.Ordinal)
            && normalizedImdbId.AsSpan(2).IndexOfAnyExceptInRange('0', '9') < 0;
    }

    public static bool IsCompatible(string? first, string? second)
    {
        var normalizedFirst = Normalize(first);
        var normalizedSecond = Normalize(second);
        return normalizedFirst is null
            || normalizedSecond is null
            || string.Equals(normalizedFirst, normalizedSecond, StringComparison.Ordinal);
    }
}

public sealed record MetadataLookupResult(
    MetadataLookupStatus Status,
    MetadataDetails? Metadata = null,
    int HttpAttempts = 0,
    string? ErrorCode = null,
    string? ProviderMessage = null);

public interface IOmdbClient
{
    Task<MetadataLookupResult> LookupAsync(
        string title,
        int? year,
        string sourceType,
        CancellationToken cancellationToken = default,
        OmdbRequestPurpose requestPurpose = OmdbRequestPurpose.RssIngestion,
        string? imdbId = null);
}

public interface IOmdbRequestBudget
{
    Task<bool> TryReserveAsync(
        DateOnly utcDate,
        OmdbRequestPurpose requestPurpose,
        int dailyRequestLimit,
        CancellationToken cancellationToken = default);

    Task RecordProviderErrorAsync(
        DateOnly utcDate,
        string errorCode,
        bool providerQuotaExceeded,
        CancellationToken cancellationToken = default);
}

public sealed record MetadataCacheValue(
    string LookupTitle,
    int? LookupYear,
    string LookupYearSemantics,
    string SourceType,
    MetadataLookupStatus Status,
    MetadataDetails? Metadata,
    DateTimeOffset FetchedAt,
    DateTimeOffset ExpiresAt,
    string? LookupIdentity = null);

public interface IMetadataCacheStore
{
    Task<MetadataCacheValue?> GetAsync(
        string cacheKey,
        CancellationToken cancellationToken = default);

    async Task<MetadataCacheValue?> GetByTitleAsync(
        string normalizedTitle,
        string sourceType,
        CancellationToken cancellationToken = default) => null;

    async Task<MetadataCacheValue?> GetByImdbIdAsync(
        string imdbId,
        string sourceType,
        CancellationToken cancellationToken = default) => null;

    Task StoreAsync(
        string cacheKey,
        MetadataCacheValue value,
        CancellationToken cancellationToken = default);
}

public sealed record MetadataResolution(
    MetadataLookupStatus Status,
    MetadataDetails? Metadata,
    bool CacheHit,
    int HttpAttempts,
    string? ErrorCode = null);
