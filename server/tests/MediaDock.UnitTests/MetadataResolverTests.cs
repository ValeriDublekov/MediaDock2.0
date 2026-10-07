using MediaDock.Application.Metadata;

namespace MediaDock.UnitTests;

public sealed class MetadataResolverTests
{
    [Fact]
    public async Task ResolveByTitleUsesConfirmedNotFoundCacheWithoutCallingProvider()
    {
        var now = DateTimeOffset.UtcNow;
        var cache = new InMemoryMetadataCacheStore();
        await cache.StoreAsync("negative-title", new MetadataCacheValue(
            "unlisted film", null, "movie_release_year", "movie", MetadataLookupStatus.ConfirmedNotFound,
            null, now, now.AddDays(2)));
        var client = new RecordingOmdbClient();
        var resolver = new MetadataResolver(client, cache);

        var result = await resolver.ResolveByTitleAsync("Unlisted Film", "movie", now);

        Assert.Equal(MetadataLookupStatus.ConfirmedNotFound, result.Status);
        Assert.True(result.CacheHit);
        Assert.Equal(0, result.HttpAttempts);
        Assert.Empty(client.RequestedImdbIds);
    }

    [Fact]
    public async Task ResolveAsyncSeparatesCacheEntriesByImdbId()
    {
        var now = new DateTimeOffset(2026, 10, 2, 8, 0, 0, TimeSpan.Zero);
        var client = new RecordingOmdbClient();
        var resolver = new MetadataResolver(client, new InMemoryMetadataCacheStore());

        var first = await resolver.ResolveAsync(
            "Shared title", 2022, "movie", now,
            requestPurpose: OmdbRequestPurpose.OscarEnrichment,
            imdbId: "tt11564570");
        var second = await resolver.ResolveAsync(
            "Shared title", 2022, "movie", now,
            requestPurpose: OmdbRequestPurpose.OscarEnrichment,
            imdbId: "tt8579674");
        var cachedFirst = await resolver.ResolveAsync(
            "Alternate title", 2020, "movie", now,
            requestPurpose: OmdbRequestPurpose.OscarEnrichment,
            imdbId: "tt11564570");

        Assert.False(first.CacheHit);
        Assert.False(second.CacheHit);
        Assert.True(cachedFirst.CacheHit);
        Assert.Equal("tt11564570", first.Metadata?.ImdbId);
        Assert.Equal("tt8579674", second.Metadata?.ImdbId);
        Assert.Equal("tt11564570", cachedFirst.Metadata?.ImdbId);
        Assert.Equal(["tt11564570", "tt8579674"], client.RequestedImdbIds);
    }

    [Fact]
    public async Task ResolveGoldenGlobeSearchDoesNotFilterByNomineeType()
    {
        var now = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        var client = new TooBigToFailSearchClient();
        var resolver = new MetadataResolver(client, new InMemoryMetadataCacheStore());

        var result = await resolver.ResolveGoldenGlobeAsync("Too Big To Fail", 2012, "series", now);

        Assert.Equal(MetadataLookupStatus.Found, result.Status);
        Assert.Equal("tt1742683", result.Metadata?.ImdbId);
        Assert.Equal("movie", result.Metadata?.SourceType);
        Assert.Null(client.SearchSourceType);
    }

    private sealed class TooBigToFailSearchClient : IOmdbClient
    {
        public string? SearchSourceType { get; private set; }

        public Task<MetadataLookupResult> LookupAsync(
            string title,
            int? year,
            string sourceType,
            CancellationToken cancellationToken = default,
            OmdbRequestPurpose requestPurpose = OmdbRequestPurpose.RssIngestion,
            string? imdbId = null)
        {
            if (imdbId is null)
            {
                return Task.FromResult(new MetadataLookupResult(
                    MetadataLookupStatus.ConfirmedNotFound,
                    HttpAttempts: 1,
                    ErrorCode: "not_found"));
            }

            return Task.FromResult(new MetadataLookupResult(
                MetadataLookupStatus.Found,
                new MetadataDetails(
                    "Too Big to Fail",
                    2011,
                    imdbId,
                    "movie",
                    "movie",
                    "standard",
                    null,
                    null,
                    null,
                    null,
                    [],
                    [],
                    null,
                    null,
                    null,
                    null,
                    "Nominated for 1 Golden Globe",
                    null),
                1));
        }

        public Task<MetadataSearchResult> SearchAsync(
            string title,
            string? sourceType,
            int page,
            CancellationToken cancellationToken = default,
            OmdbRequestPurpose requestPurpose = OmdbRequestPurpose.RssIngestion)
        {
            SearchSourceType = sourceType;
            return Task.FromResult(new MetadataSearchResult(
                MetadataLookupStatus.Found,
                [new MetadataSearchCandidate("Too Big to Fail", 2011, "tt1742683", "movie")],
                1,
                1));
        }
    }

    private sealed class RecordingOmdbClient : IOmdbClient
    {
        public List<string?> RequestedImdbIds { get; } = [];

        public Task<MetadataLookupResult> LookupAsync(
            string title,
            int? year,
            string sourceType,
            CancellationToken cancellationToken = default,
            OmdbRequestPurpose requestPurpose = OmdbRequestPurpose.RssIngestion,
            string? imdbId = null)
        {
            RequestedImdbIds.Add(imdbId);
            var metadata = new MetadataDetails(
                title,
                year,
                imdbId,
                "movie",
                "movie",
                "standard",
                null,
                7.5m,
                1000,
                80m,
                [],
                [],
                null,
                null,
                null,
                null,
                null,
                null);
            return Task.FromResult(new MetadataLookupResult(MetadataLookupStatus.Found, metadata, 1));
        }
    }

    private sealed class InMemoryMetadataCacheStore : IMetadataCacheStore
    {
        private readonly Dictionary<string, MetadataCacheValue> _values = new(StringComparer.Ordinal);

        public Task<MetadataCacheValue?> GetAsync(
            string cacheKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.GetValueOrDefault(cacheKey));

        public Task<MetadataCacheValue?> GetByTitleAsync(
            string normalizedTitle,
            string sourceType,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.Values
                .Where(value => value.LookupTitle == normalizedTitle
                    && value.SourceType == sourceType
                    && value.ExpiresAt > DateTimeOffset.UtcNow)
                .OrderByDescending(value => value.FetchedAt)
                .FirstOrDefault());

        public Task StoreAsync(
            string cacheKey,
            MetadataCacheValue value,
            CancellationToken cancellationToken = default)
        {
            _values[cacheKey] = value;
            return Task.CompletedTask;
        }
    }
}