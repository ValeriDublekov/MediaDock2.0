using System.Security.Cryptography;
using System.Text;

namespace MediaDock.Application.Metadata;

public sealed class MetadataResolver
{
    private static readonly TimeSpan FoundTtl = TimeSpan.FromDays(30);
    private static readonly TimeSpan NotFoundTtl = TimeSpan.FromDays(2);

    private readonly IOmdbClient _client;
    private readonly IMetadataCacheStore _cache;

    public MetadataResolver(IOmdbClient client, IMetadataCacheStore cache)
    {
        _client = client;
        _cache = cache;
    }

    public async Task<MetadataResolution> ResolveAsync(
        string title,
        int? year,
        string sourceType,
        DateTimeOffset now,
        CancellationToken cancellationToken = default,
        OmdbRequestPurpose requestPurpose = OmdbRequestPurpose.RssIngestion,
        string? imdbId = null)
    {
        var normalizedTitle = NormalizeTitle(title);
        var normalizedSourceType = sourceType.Trim().ToLowerInvariant();
        var normalizedImdbId = ImdbIdNormalizer.Normalize(imdbId);
        if (normalizedTitle.Length == 0 || normalizedSourceType is not ("movie" or "series"))
        {
            return new MetadataResolution(
                MetadataLookupStatus.InvalidRequest,
                null,
                false,
                0,
                "invalid_lookup");
        }

        var yearSemantics = normalizedSourceType == "series" ? "series_season_year" : "movie_release_year";
        var lookupYear = year;
        var cacheKey = CreateCacheKey(normalizedTitle, lookupYear, normalizedSourceType, yearSemantics, normalizedImdbId);
        var cached = await _cache.GetAsync(cacheKey, cancellationToken);
        if (cached is not null && cached.ExpiresAt > now)
        {
            return new MetadataResolution(cached.Status, cached.Metadata, true, 0);
        }

        var result = await _client.LookupAsync(
            title.Trim(),
            lookupYear,
            normalizedSourceType,
            cancellationToken,
            requestPurpose,
            normalizedImdbId);
        if (result.Status == MetadataLookupStatus.Found
            && result.Metadata is not null
            && normalizedImdbId is not null
            && !ImdbIdNormalizer.IsCompatible(normalizedImdbId, result.Metadata.ImdbId))
        {
            result = result with
            {
                Status = MetadataLookupStatus.ProviderFailure,
                Metadata = null,
                ErrorCode = "imdb_id_mismatch"
            };
        }

        if (result.Status == MetadataLookupStatus.Found && result.Metadata is not null)
        {
            await StoreAsync(
                cacheKey,
                normalizedTitle,
                lookupYear,
                yearSemantics,
                normalizedSourceType,
                MetadataLookupStatus.Found,
                result.Metadata,
                now,
                FoundTtl,
                cancellationToken,
                normalizedImdbId);
        }
        else if (result.Status == MetadataLookupStatus.ConfirmedNotFound)
        {
            await StoreAsync(
                cacheKey,
                normalizedTitle,
                lookupYear,
                yearSemantics,
                normalizedSourceType,
                MetadataLookupStatus.ConfirmedNotFound,
                null,
                now,
                NotFoundTtl,
                cancellationToken,
                normalizedImdbId);
        }
        else if (result.Status == MetadataLookupStatus.Found)
        {
            result = result with
            {
                Status = MetadataLookupStatus.ProviderFailure,
                ErrorCode = "invalid_metadata"
            };
        }

        return new MetadataResolution(
            result.Status,
            result.Metadata,
            false,
            result.HttpAttempts,
            result.ErrorCode);
    }

    public async Task<MetadataResolution> ResolveByTitleAsync(
        string title,
        string sourceType,
        DateTimeOffset now,
        CancellationToken cancellationToken = default,
        OmdbRequestPurpose requestPurpose = OmdbRequestPurpose.RssIngestion)
    {
        var normalizedTitle = NormalizeTitle(title);
        var normalizedSourceType = sourceType.Trim().ToLowerInvariant();
        if (normalizedTitle.Length == 0 || normalizedSourceType is not ("movie" or "series"))
            return new(MetadataLookupStatus.InvalidRequest, null, false, 0, "invalid_lookup");

        var cached = await _cache.GetByTitleAsync(normalizedTitle, normalizedSourceType, cancellationToken);
        if (cached is not null && cached.ExpiresAt > now)
            return new(cached.Status, cached.Metadata, true, 0);

        var result = await _client.LookupAsync(title.Trim(), null, normalizedSourceType, cancellationToken, requestPurpose);
        if (result.Status is MetadataLookupStatus.Found or MetadataLookupStatus.ConfirmedNotFound)
        {
            await StoreAsync(
                CreateCacheKey(normalizedTitle, null, normalizedSourceType, "title", null),
                normalizedTitle, null, "title", normalizedSourceType, result.Status, result.Metadata,
                now, result.Status == MetadataLookupStatus.Found ? FoundTtl : NotFoundTtl, cancellationToken);
        }
        return new(result.Status, result.Metadata, false, result.HttpAttempts, result.ErrorCode);
    }

    public async Task<MetadataResolution> ResolveByImdbIdAsync(
        string? imdbId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default,
        OmdbRequestPurpose requestPurpose = OmdbRequestPurpose.RssIngestion)
    {
        var normalizedImdbId = ImdbIdNormalizer.Normalize(imdbId);
        if (!ImdbIdNormalizer.IsValid(normalizedImdbId))
        {
            return new(MetadataLookupStatus.InvalidRequest, null, false, 0, "invalid_lookup");
        }

        var cacheKey = CreateExactImdbCacheKey(normalizedImdbId!);
        var cached = await _cache.GetAsync(cacheKey, cancellationToken);
        if (cached is not null && cached.ExpiresAt > now)
        {
            if (cached.Status == MetadataLookupStatus.Found
                && (cached.Metadata is null
                    || !string.Equals(
                        ImdbIdNormalizer.Normalize(cached.Metadata.ImdbId),
                        normalizedImdbId,
                        StringComparison.Ordinal)))
            {
                return new(MetadataLookupStatus.ProviderFailure, null, true, 0, "imdb_id_mismatch");
            }

            return new(cached.Status, cached.Metadata, true, 0,
                cached.Status == MetadataLookupStatus.ConfirmedNotFound ? "not_found" : null);
        }

        var result = await _client.LookupAsync(
            string.Empty,
            null,
            "movie",
            cancellationToken,
            requestPurpose,
            normalizedImdbId);
        if (result.Status == MetadataLookupStatus.Found)
        {
            var returnedImdbId = ImdbIdNormalizer.Normalize(result.Metadata?.ImdbId);
            if (result.Metadata is null)
            {
                result = result with { Status = MetadataLookupStatus.ProviderFailure, ErrorCode = "invalid_metadata" };
            }
            else if (!ImdbIdNormalizer.IsValid(returnedImdbId)
                || !string.Equals(returnedImdbId, normalizedImdbId, StringComparison.Ordinal))
            {
                result = result with
                {
                    Status = MetadataLookupStatus.ProviderFailure,
                    Metadata = null,
                    ErrorCode = "imdb_id_mismatch"
                };
            }
        }

        if (result.Status == MetadataLookupStatus.Found && result.Metadata is not null)
        {
            var sourceType = result.Metadata.SourceType is "movie" or "series"
                ? result.Metadata.SourceType
                : "unknown";
            await StoreAsync(
                cacheKey,
                normalizedImdbId!,
                null,
                "imdb_id",
                sourceType,
                MetadataLookupStatus.Found,
                result.Metadata,
                now,
                FoundTtl,
                cancellationToken,
                normalizedImdbId);
        }
        else if (result.Status == MetadataLookupStatus.ConfirmedNotFound)
        {
            await StoreAsync(
                cacheKey,
                normalizedImdbId!,
                null,
                "imdb_id",
                "unknown",
                MetadataLookupStatus.ConfirmedNotFound,
                null,
                now,
                NotFoundTtl,
                cancellationToken,
                normalizedImdbId);
        }

        return new(result.Status, result.Metadata, false, result.HttpAttempts, result.ErrorCode);
    }

    private async Task StoreAsync(
        string cacheKey,
        string normalizedTitle,
        int? year,
        string yearSemantics,
        string sourceType,
        MetadataLookupStatus status,
        MetadataDetails? metadata,
        DateTimeOffset now,
        TimeSpan ttl,
        CancellationToken cancellationToken,
        string? lookupIdentity = null)
    {
        await _cache.StoreAsync(
            cacheKey,
            new MetadataCacheValue(
                normalizedTitle,
                year,
                yearSemantics,
                sourceType,
                status,
                metadata,
                now,
                now.Add(ttl),
                lookupIdentity),
            cancellationToken);
    }

    private static string CreateCacheKey(
        string title,
        int? year,
        string sourceType,
        string yearSemantics,
        string? imdbId)
    {
        var identity = imdbId is null
            ? $"v2:{title}:{year?.ToString() ?? ""}:{sourceType}:{yearSemantics}"
            : $"v3:{sourceType}:imdb:{imdbId}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }

    private static string CreateExactImdbCacheKey(string imdbId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"v1:imdb:{imdbId}"))).ToLowerInvariant();

    private static string NormalizeTitle(string? title) =>
        string.Join(' ', (title ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();
}
