using MediaDock.Application.GoldenGlobes;
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
    public async Task ResolveAsyncUsesSeasonYearForSeriesCacheAndProviderLookup()
    {
        var now = new DateTimeOffset(2026, 10, 2, 8, 0, 0, TimeSpan.Zero);
        var client = new RecordingOmdbClient();
        var resolver = new MetadataResolver(client, new InMemoryMetadataCacheStore());

        await resolver.ResolveAsync("East of Eden", 2026, "series", now);
        var cached = await resolver.ResolveAsync("East of Eden", 2026, "series", now.AddMinutes(1));
        await resolver.ResolveAsync("East of Eden", 2025, "series", now.AddMinutes(2));

        Assert.True(cached.CacheHit);
        Assert.Equal([2026, 2025], client.RequestedYears);
        Assert.Equal(["series", "series"], client.RequestedSourceTypes);
    }

    [Fact]
    public async Task ResolveByImdbIdCachesVerifiedMetadataWithoutATitle()
    {
        var now = new DateTimeOffset(2026, 10, 2, 8, 0, 0, TimeSpan.Zero);
        var client = new RecordingOmdbClient();
        var resolver = new MetadataResolver(client, new InMemoryMetadataCacheStore());

        var result = await resolver.ResolveByImdbIdAsync(
            " TT11564570 ",
            now,
            requestPurpose: OmdbRequestPurpose.ImdbTitleEnrichment);
        var cached = await resolver.ResolveByImdbIdAsync("tt11564570", now.AddMinutes(1));

        Assert.Equal(MetadataLookupStatus.Found, result.Status);
        Assert.False(result.CacheHit);
        Assert.Equal("tt11564570", result.Metadata?.ImdbId);
        Assert.Equal(MetadataLookupStatus.Found, cached.Status);
        Assert.True(cached.CacheHit);
        Assert.Equal(0, cached.HttpAttempts);
        Assert.Equal([""], client.RequestedTitles);
        Assert.Equal(["tt11564570"], client.RequestedImdbIds);
        Assert.Equal([OmdbRequestPurpose.ImdbTitleEnrichment], client.RequestPurposes);
    }

    [Fact]
    public async Task ResolveByImdbIdDoesNotCacheMismatchedMetadata()
    {
        var now = new DateTimeOffset(2026, 10, 2, 8, 0, 0, TimeSpan.Zero);
        var client = new RecordingOmdbClient { ReturnedImdbIdOverride = "tt8579674" };
        var resolver = new MetadataResolver(client, new InMemoryMetadataCacheStore());

        var first = await resolver.ResolveByImdbIdAsync("tt11564570", now);
        var second = await resolver.ResolveByImdbIdAsync("tt11564570", now.AddMinutes(1));

        Assert.Equal(MetadataLookupStatus.ProviderFailure, first.Status);
        Assert.Equal("imdb_id_mismatch", first.ErrorCode);
        Assert.False(first.CacheHit);
        Assert.Equal(MetadataLookupStatus.ProviderFailure, second.Status);
        Assert.False(second.CacheHit);
        Assert.Equal(["tt11564570", "tt11564570"], client.RequestedImdbIds);
    }

    [Fact]
    public async Task ResolveByImdbIdCachesConfirmedNotFoundSeparatelyFromTransientErrors()
    {
        var now = new DateTimeOffset(2026, 10, 2, 8, 0, 0, TimeSpan.Zero);
        var client = new RecordingOmdbClient { LookupStatus = MetadataLookupStatus.ConfirmedNotFound };
        var resolver = new MetadataResolver(client, new InMemoryMetadataCacheStore());

        var first = await resolver.ResolveByImdbIdAsync("tt11564570", now);
        var cached = await resolver.ResolveByImdbIdAsync("tt11564570", now.AddMinutes(1));

        Assert.Equal(MetadataLookupStatus.ConfirmedNotFound, first.Status);
        Assert.False(first.CacheHit);
        Assert.Equal(MetadataLookupStatus.ConfirmedNotFound, cached.Status);
        Assert.True(cached.CacheHit);
        Assert.Equal(0, cached.HttpAttempts);
        Assert.Single(client.RequestedImdbIds);
    }

    [Fact]
    public async Task ResolveByImdbIdDoesNotCacheTransientFailures()
    {
        var now = new DateTimeOffset(2026, 10, 2, 8, 0, 0, TimeSpan.Zero);
        var client = new RecordingOmdbClient { LookupStatus = MetadataLookupStatus.TransportFailure };
        var resolver = new MetadataResolver(client, new InMemoryMetadataCacheStore());

        var first = await resolver.ResolveByImdbIdAsync("tt11564570", now);
        var second = await resolver.ResolveByImdbIdAsync("tt11564570", now.AddMinutes(1));

        Assert.Equal(MetadataLookupStatus.TransportFailure, first.Status);
        Assert.Equal(MetadataLookupStatus.TransportFailure, second.Status);
        Assert.False(first.CacheHit);
        Assert.False(second.CacheHit);
        Assert.Equal(["tt11564570", "tt11564570"], client.RequestedImdbIds);
    }

    [Fact]
    public async Task ResolveGoldenGlobeSearchDoesNotFilterByNomineeType()
    {
        var now = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        var client = new TooBigToFailSearchClient();
        var resolver = CreateGoldenGlobeResolver(client);

        var result = await resolver.ResolveGoldenGlobeAsync("Too Big To Fail", 2012, "series", now);

        Assert.Equal(MetadataLookupStatus.Found, result.Status);
        Assert.Equal("tt1742683", result.Metadata?.ImdbId);
        Assert.Equal("movie", result.Metadata?.SourceType);
        Assert.Null(client.SearchSourceType);
    }

    [Fact]
    public async Task ResolveGoldenGlobeSearchResolvesTheHighestRankedCandidate()
    {
        var now = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        var client = new ScriptedGoldenGlobeClient
        {
            SearchResultFactory = (_, _) => new MetadataSearchResult(
                MetadataLookupStatus.Found,
                [
                    new("A Quiet Stage", 2024, "tt1111111", "movie"),
                    new("A Quiet Stag", 2024, "tt2222222", "movie")
                ],
                2,
                1)
        };
        client.IdLookups["tt1111111"] = CreateGoldenGlobeLookup("A Quiet Stage", 2024, "tt1111111");
        var resolver = CreateGoldenGlobeResolver(client);

        var result = await resolver.ResolveGoldenGlobeAsync("A Quiet Stage", 2024, "movie", now);

        Assert.Equal(MetadataLookupStatus.Found, result.Status);
        Assert.Equal("tt1111111", result.Metadata?.ImdbId);
        Assert.Equal([null, "tt1111111"], client.RequestedImdbIds);
    }

    [Fact]
    public async Task ResolveGoldenGlobeSearchReportsAmbiguousTopCandidates()
    {
        var now = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        var client = new ScriptedGoldenGlobeClient
        {
            SearchResultFactory = (_, _) => new MetadataSearchResult(
                MetadataLookupStatus.Found,
                [
                    new("A Quiet Stage", 2024, "tt1111111", "movie"),
                    new("A Quiet Stage", 2024, "tt2222222", "movie")
                ],
                2,
                1)
        };
        client.IdLookups["tt1111111"] = CreateGoldenGlobeLookup("A Quiet Stage", 2024, "tt1111111");
        client.IdLookups["tt2222222"] = CreateGoldenGlobeLookup("A Quiet Stage", 2024, "tt2222222");
        var resolver = CreateGoldenGlobeResolver(client);

        var result = await resolver.ResolveGoldenGlobeAsync("A Quiet Stage", 2024, "movie", now);

        Assert.Equal(MetadataLookupStatus.ProviderFailure, result.Status);
        Assert.Equal("ambiguous_match", result.ErrorCode);
        Assert.Equal([null, "tt1111111", "tt2222222"], client.RequestedImdbIds);
    }

    [Fact]
    public async Task ResolveGoldenGlobeSearchRejectsIncompleteSearchResults()
    {
        var now = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        var client = new ScriptedGoldenGlobeClient
        {
            SearchResultFactory = (_, page) => new MetadataSearchResult(
                MetadataLookupStatus.Found,
                page == 1 ? [new("A Quiet Stage", 2024, "tt1111111", "movie")] : [],
                51,
                1)
        };
        var resolver = CreateGoldenGlobeResolver(client);

        var result = await resolver.ResolveGoldenGlobeAsync("A Quiet Stage", 2024, "movie", now);

        Assert.Equal(MetadataLookupStatus.ProviderFailure, result.Status);
        Assert.Equal("no_confident_match", result.ErrorCode);
        Assert.Equal([1, 2, 3, 4, 5], client.SearchPages);
        Assert.Equal([null], client.RequestedImdbIds);
    }

    [Theory]
    [InlineData(MetadataLookupStatus.RequestBudgetExhausted)]
    [InlineData(MetadataLookupStatus.QuotaExceeded)]
    public async Task ResolveGoldenGlobeSearchPreservesSearchBudgetOutcomes(MetadataLookupStatus status)
    {
        var now = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        var client = new ScriptedGoldenGlobeClient
        {
            SearchResultFactory = (_, _) => new MetadataSearchResult(
                status,
                [],
                0,
                3,
                "budget_stopped")
        };
        var resolver = CreateGoldenGlobeResolver(client);

        var result = await resolver.ResolveGoldenGlobeAsync("A Quiet Stage", 2024, "movie", now);

        Assert.Equal(status, result.Status);
        Assert.Equal("budget_stopped", result.ErrorCode);
        Assert.Equal(4, result.HttpAttempts);
        Assert.Single(client.SearchPages);
    }

    [Theory]
    [InlineData(MetadataLookupStatus.RequestBudgetExhausted)]
    [InlineData(MetadataLookupStatus.QuotaExceeded)]
    public async Task ResolveGoldenGlobeCandidatePreservesBudgetOutcomes(MetadataLookupStatus status)
    {
        var now = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        var client = new ScriptedGoldenGlobeClient
        {
            SearchResultFactory = (_, _) => new MetadataSearchResult(
                MetadataLookupStatus.Found,
                [new("A Quiet Stage", 2024, "tt1111111", "movie")],
                1,
                2)
        };
        client.IdLookups["tt1111111"] = new MetadataLookupResult(status, HttpAttempts: 3, ErrorCode: "budget_stopped");
        var resolver = CreateGoldenGlobeResolver(client);

        var result = await resolver.ResolveGoldenGlobeAsync("A Quiet Stage", 2024, "movie", now);

        Assert.Equal(status, result.Status);
        Assert.Equal("budget_stopped", result.ErrorCode);
        Assert.Equal(6, result.HttpAttempts);
    }

    private static MetadataLookupResult CreateGoldenGlobeLookup(string title, int year, string imdbId) => new(
        MetadataLookupStatus.Found,
        new MetadataDetails(
            title,
            year,
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
        1);

    private static GoldenGlobeMetadataResolver CreateGoldenGlobeResolver(IOmdbClient client) =>
        new(client, new MetadataResolver(client, new InMemoryMetadataCacheStore()));

    private sealed class ScriptedGoldenGlobeClient : IOmdbClient
    {
        public MetadataLookupResult ExactLookup { get; init; } = new(
            MetadataLookupStatus.ConfirmedNotFound,
            HttpAttempts: 1,
            ErrorCode: "not_found");

        public Dictionary<string, MetadataLookupResult> IdLookups { get; } = new(StringComparer.Ordinal);
        public List<string?> RequestedImdbIds { get; } = [];
        public List<int> SearchPages { get; } = [];
        public Func<string, int, MetadataSearchResult> SearchResultFactory { get; init; } =
            static (_, _) => new(MetadataLookupStatus.ConfirmedNotFound, [], 0, 1, "not_found");

        public Task<MetadataLookupResult> LookupAsync(
            string title,
            int? year,
            string sourceType,
            CancellationToken cancellationToken = default,
            OmdbRequestPurpose requestPurpose = OmdbRequestPurpose.RssIngestion,
            string? imdbId = null)
        {
            RequestedImdbIds.Add(imdbId);
            return Task.FromResult(imdbId is null
                ? ExactLookup
                : IdLookups.GetValueOrDefault(imdbId)
                    ?? new MetadataLookupResult(MetadataLookupStatus.ProviderFailure, ErrorCode: "unexpected_lookup"));
        }

        public Task<MetadataSearchResult> SearchAsync(
            string title,
            string? sourceType,
            int page,
            CancellationToken cancellationToken = default,
            OmdbRequestPurpose requestPurpose = OmdbRequestPurpose.RssIngestion)
        {
            SearchPages.Add(page);
            return Task.FromResult(SearchResultFactory(title, page));
        }
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
        public List<string> RequestedTitles { get; } = [];
        public List<int?> RequestedYears { get; } = [];
        public List<string> RequestedSourceTypes { get; } = [];
        public List<OmdbRequestPurpose> RequestPurposes { get; } = [];
        public MetadataLookupStatus LookupStatus { get; init; } = MetadataLookupStatus.Found;
        public string? ReturnedImdbIdOverride { get; init; }

        public Task<MetadataLookupResult> LookupAsync(
            string title,
            int? year,
            string sourceType,
            CancellationToken cancellationToken = default,
            OmdbRequestPurpose requestPurpose = OmdbRequestPurpose.RssIngestion,
            string? imdbId = null)
        {
            RequestedImdbIds.Add(imdbId);
            RequestedTitles.Add(title);
            RequestedYears.Add(year);
            RequestedSourceTypes.Add(sourceType);
            RequestPurposes.Add(requestPurpose);
            if (LookupStatus != MetadataLookupStatus.Found)
            {
                return Task.FromResult(new MetadataLookupResult(LookupStatus, HttpAttempts: 1, ErrorCode: "not_found"));
            }

            var metadata = new MetadataDetails(
                string.IsNullOrWhiteSpace(title) ? "Resolved title" : title,
                year,
                ReturnedImdbIdOverride ?? imdbId,
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