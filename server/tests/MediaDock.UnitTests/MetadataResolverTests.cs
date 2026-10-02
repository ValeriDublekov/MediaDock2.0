using MediaDock.Application.Metadata;

namespace MediaDock.UnitTests;

public sealed class MetadataResolverTests
{
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