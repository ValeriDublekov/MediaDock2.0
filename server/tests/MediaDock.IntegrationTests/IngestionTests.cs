using System.Net;
using System.Text;
using System.Text.Json;
using MediaDock.Application.Ingestion;
using MediaDock.Application.Metadata;
using MediaDock.Application.Parsing;
using MediaDock.Infrastructure.Ingestion;
using MediaDock.Infrastructure.Metadata;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using MediaDock.Infrastructure.Rss;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MediaDock.IntegrationTests;

[Trait("Category", "Ingestion")]
public sealed class IngestionTests
{
    private const string FakeApiKey = "integration-test-only-key";
    private const string SuccessfulFeedUrl = "https://feed.rutracker.cc/success.atom";
    private const string PartialFeedUrl = "https://feed.rutracker.cc/partial.atom";

    [Fact]
    public async Task TitleCacheLookupReturnsConfirmedNotFoundEntries()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_metadata_negative_cache_test").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options;
        await using var db = new MediaDockDbContext(options);
        await db.Database.MigrateAsync();

        var fetchedAt = DateTimeOffset.UtcNow;
        db.MetadataCache.Add(new MetadataCacheEntry
        {
            CacheKey = "confirmed-negative-title-cache",
            LookupTitle = "unlisted film",
            LookupYear = 2024,
            LookupYearSemantics = "movie_release_year",
            SourceType = "movie",
            Status = "confirmed_not_found",
            FetchedAt = fetchedAt,
            ExpiresAt = fetchedAt.AddDays(2)
        });
        await db.SaveChangesAsync();

        var result = await new PostgresMetadataCacheStore(db).GetByTitleAsync("unlisted film", "movie");

        Assert.NotNull(result);
        Assert.Equal(MetadataLookupStatus.ConfirmedNotFound, result.Status);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public async Task FailedEntryRecheckUsesStoredFeedPayloadAndOverridesProcessedState()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_ingestion_recheck_test").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options;
        await using var db = new MediaDockDbContext(options);
        await db.Database.MigrateAsync();
        var publishedAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var source = new Source
        {
            StableKey = "recheck-source",
            Name = "Movies",
            FeedType = "movie",
            Url = SuccessfulFeedUrl
        };
        db.Sources.Add(source);
        await db.SaveChangesAsync();

        db.ParseLogs.Add(new ParseLog
        {
            SourceId = source.Id,
            SourceItemKey = "entry:recheck-matrix",
            RawTitle = "The Matrix (1999) [1080p]",
            FeedName = source.Name,
            ParsedSuccessfully = false,
            OmdbStatus = "transport_error",
            Ignored = true,
            ErrorMessage = "transport_error",
            ProcessedAt = publishedAt,
            RetryState = "retryable",
            AttemptCount = 1,
            FeedType = source.FeedType,
            SourcePublishedAt = publishedAt,
            FeedEntryId = "recheck-matrix",
            TorrentUrl = "https://rutracker.org/forum/viewtopic.php?t=91"
        });
        db.ParseLogs.AddRange(
            new ParseLog
            {
                SourceId = source.Id,
                SourceItemKey = "entry:matrix-1",
                RawTitle = "The Matrix (1999) [1080p]",
                FeedName = source.Name,
                ParsedSuccessfully = false,
                OmdbStatus = "transport_error",
                Ignored = true,
                ErrorMessage = "transport_error",
                ProcessedAt = publishedAt.AddMinutes(1),
                RetryState = "retryable",
                AttemptCount = 1,
                FeedType = source.FeedType,
                SourcePublishedAt = publishedAt,
                FeedEntryId = "matrix-1"
            },
            new ParseLog
            {
                SourceId = source.Id,
                SourceItemKey = "entry:already-resolved",
                RawTitle = "The Matrix (1999) [1080p]",
                FeedName = source.Name,
                ParsedSuccessfully = false,
                OmdbStatus = "transport_error",
                Ignored = true,
                ErrorMessage = "transport_error",
                ProcessedAt = publishedAt.AddMinutes(2),
                RetryState = "retryable",
                AttemptCount = 1,
                FeedType = source.FeedType,
                FeedEntryId = "already-resolved",
                TorrentUrl = "https://rutracker.org/forum/viewtopic.php?t=92"
            },
            new ParseLog
            {
                SourceId = source.Id,
                SourceItemKey = "entry:already-resolved",
                RawTitle = "The Matrix (1999) [1080p]",
                FeedName = source.Name,
                ParsedSuccessfully = true,
                OmdbStatus = "found",
                Ignored = false,
                ProcessedAt = publishedAt.AddMinutes(3),
                RetryState = "resolved",
                AttemptCount = 1,
                FeedType = source.FeedType,
                FeedEntryId = "already-resolved",
                TorrentUrl = "https://rutracker.org/forum/viewtopic.php?t=92"
            });
        db.RssItemStates.Add(new RssItemProcessingState
        {
            SourceId = source.Id,
            SourceItemKey = "entry:recheck-matrix",
            Fingerprint = "old-fingerprint",
            Disposition = "terminal",
            UpdatedAt = publishedAt,
            ExpiresAt = publishedAt.AddDays(2)
        });
        await db.SaveChangesAsync();

        var handler = new MockProviderHandler();
        using var httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var service = CreateService(db, httpClient, new RssFeedTransport(httpClient, new PublicDnsResolver()));

        var result = await service.RecheckFailedAsync();

        Assert.Equal(2, result.RetryableEntriesSelected);
        Assert.Equal(0, result.EntriesUnavailable);
        Assert.Equal(1, result.Run.Summary.TitlesCreated);
        Assert.Equal(2, result.Run.Summary.OccurrencesCreated);
        Assert.Equal(1, handler.OmdbRequestCount);
        var occurrences = await db.Occurrences.OrderBy(value => value.SourceItemKey).ToListAsync();
        var occurrence = occurrences.Single(value => value.SourceItemKey == "entry:recheck-matrix");
        Assert.Equal("entry:recheck-matrix", occurrence.SourceItemKey);
        Assert.Equal("recheck-matrix", occurrence.FeedEntryId);
        Assert.Equal("https://rutracker.org/forum/viewtopic.php?t=91", occurrence.TorrentUrl);
        Assert.Contains(occurrences, value => value.SourceItemKey == "entry:matrix-1"
            && value.TorrentUrl == "https://rutracker.org/forum/viewtopic.php?t=1");
        Assert.Equal("resolved", (await db.RssItemStates.SingleAsync(state => state.SourceItemKey == "entry:recheck-matrix")).Disposition);
        Assert.DoesNotContain(occurrences, value => value.SourceItemKey == "entry:already-resolved");
    }

    [Fact]
    public async Task FailedEntryRecheckFindsLegacyParseLogInCurrentFeed()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_legacy_recheck_test").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options;
        await using var db = new MediaDockDbContext(options);
        await db.Database.MigrateAsync();
        var processedAt = DateTimeOffset.UtcNow.AddDays(-10);
        var source = new Source
        {
            StableKey = "legacy-recheck-source",
            Name = "Movies",
            FeedType = "movie",
            Url = SuccessfulFeedUrl
        };
        db.Sources.Add(source);
        await db.SaveChangesAsync();
        db.ParseLogs.AddRange(
            new ParseLog
            {
                RawTitle = "The Matrix (1999) [1080p]",
                FeedName = "Movies",
                ParsedSuccessfully = false,
                OmdbStatus = "not_requested",
                Ignored = true,
                ErrorMessage = "empty_title",
                ProcessedAt = processedAt,
                RetryState = "retryable",
                FeedType = "movie"
            },
            new ParseLog
            {
                RawTitle = "Removed Film (1987) [1080p]",
                FeedName = "Movies",
                ParsedSuccessfully = false,
                OmdbStatus = "not_requested",
                Ignored = true,
                ErrorMessage = "empty_title",
                ProcessedAt = processedAt.AddMinutes(1),
                RetryState = "retryable",
                FeedType = "movie"
            });
        await db.SaveChangesAsync();

        var handler = new MockProviderHandler();
        using var httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var service = CreateService(db, httpClient, new RssFeedTransport(httpClient, new PublicDnsResolver()));

        var result = await service.RecheckFailedAsync();

        Assert.Equal(2, result.RetryableEntriesSelected);
        Assert.Equal(1, result.EntriesUnavailable);
        Assert.Equal("partial", result.Run.Summary.Status);
        Assert.Equal(1, result.Run.Summary.TitlesCreated);
        Assert.Equal("entry:matrix-1", (await db.Occurrences.SingleAsync()).SourceItemKey);
        var repairedLog = await db.ParseLogs.OrderByDescending(log => log.Id).FirstAsync();
        Assert.Equal(source.Id, repairedLog.SourceId);
        Assert.Equal("entry:matrix-1", repairedLog.SourceItemKey);
        Assert.Equal("resolved", repairedLog.RetryState);
    }

    [Fact]
    public async Task AmbiguousHitIsNotPersistedAndAlternateTitleIsBoundedAndAudited()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_ingestion_title_match_test").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options;
        await using var db = new MediaDockDbContext(options);
        await db.Database.MigrateAsync();
        db.Sources.Add(new Source { StableKey = "title-match", Name = "Movies", FeedType = "movie", Url = SuccessfulFeedUrl });
        await db.SaveChangesAsync();

        var handler = new MockProviderHandler(titleMatchScenario: true);
        using var httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var service = CreateService(db, httpClient, new RssFeedTransport(httpClient, new PublicDnsResolver()));

        var first = await service.RunAsync();
        Assert.Equal(1, first.Summary.TitlesCreated);
        Assert.Equal(2, first.Summary.OccurrencesCreated);
        Assert.Equal(5, first.Summary.OmdbRequests);
        Assert.Equal("tt2222222", (await db.Titles.SingleAsync()).ImdbId);
        var alternateTitleLog = await db.ParseLogs.SingleAsync(log => log.RawTitle.StartsWith("Wrong Film / Right Film"));
        Assert.Equal(new[] { "Wrong Film", "Right Film" }, alternateTitleLog.LookupTitles);
        Assert.Contains(await db.ParseLogs.ToListAsync(), log => log.IgnoreReason == "ambiguous_title_match" && log.RetryState == "terminal");
        Assert.Contains(await db.ParseLogs.ToListAsync(), log => log.Decision != null && log.Decision.Contains("fallback_movie_release_year_within_tolerance"));

        var second = await service.RunAsync();
        Assert.Equal(0, second.Summary.OmdbRequests);
        Assert.Equal(5, handler.OmdbRequestCount);
        Assert.Equal(2, await db.Occurrences.CountAsync());
    }

    [Fact]
    public async Task ScanIsIdempotentCachesConfirmedNegativeAndContinuesAfterEntryAndProviderFailures()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_ingestion_test").Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var db = new MediaDockDbContext(options);
        await db.Database.MigrateAsync();
        var importedAt = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);
        db.Sources.Add(new Source
        {
            StableKey = "movies-main",
            Name = "Movies",
            FeedType = "movie",
            Url = SuccessfulFeedUrl
        });
        db.OscarFilms.Add(new OscarFilm
        {
            StableKey = "imdb:tt0133093",
            FilmTitle = "The Matrix",
            NormalizedTitle = "the matrix",
            FilmYear = 1999,
            ImdbId = "tt0133093",
            EnrichmentStatus = "pending",
            ImportedAt = importedAt,
            UpdatedAt = importedAt,
            Title = new Title
            {
                TitleText = "The Matrix",
                NormalizedTitle = "the matrix",
                Year = 1999,
                MediaType = "movie",
                SourceType = "movie",
                ContentKind = "standard",
                ImdbId = "tt0133093",
                UpdatedAt = importedAt
            }
        });
        await db.SaveChangesAsync();
        var oscarTitle = await db.OscarFilms.Include(film => film.Title).Select(film => film.Title).SingleAsync();
        Assert.Null(oscarTitle.FirstSeenAt);
        Assert.Null(oscarTitle.LastSeenAt);

        var handler = new MockProviderHandler();
        using var httpClient = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        var rssTransport = new RssFeedTransport(httpClient, new PublicDnsResolver());
        var service = CreateService(db, httpClient, rssTransport);

        var first = await service.RunAsync();
        Assert.Equal("succeeded", first.Summary.Status);
        Assert.Equal(0, first.Summary.TitlesCreated);
        Assert.Equal(1, first.Summary.OccurrencesCreated);
        Assert.Equal(3, first.Summary.OmdbRequests);
        Assert.Equal(1, await db.Titles.CountAsync());
        Assert.Equal(1, await db.Occurrences.CountAsync());
        Assert.Contains(await db.MetadataCache.ToListAsync(), entry => entry.Status == "confirmed_not_found");
        var matrixFirstSeenAt = (await db.Titles.AsNoTracking()
            .SingleAsync(title => title.ImdbId == "tt0133093")).FirstSeenAt;
        Assert.NotNull(matrixFirstSeenAt);
        Assert.NotNull((await db.Titles.SingleAsync(title => title.ImdbId == "tt0133093")).LastSeenAt);

        await db.RssItemStates.ExecuteDeleteAsync();
        db.ChangeTracker.Clear();
        var repeated = await service.RunAsync();
        Assert.Equal("succeeded", repeated.Summary.Status);
        Assert.Equal(0, repeated.Summary.TitlesCreated);
        Assert.Equal(0, repeated.Summary.OccurrencesCreated);
        Assert.Equal(2, repeated.Summary.KnownEntriesSkipped);
        Assert.Equal(0, repeated.Summary.CacheHits);
        Assert.Equal(0, repeated.Summary.OmdbRequests);
        Assert.Equal(3, handler.OmdbRequestCount);
        Assert.Equal(1, await db.Titles.CountAsync());
        Assert.Equal(1, await db.Occurrences.CountAsync());
        Assert.Equal(
            matrixFirstSeenAt,
            (await db.Titles.AsNoTracking().SingleAsync(title => title.ImdbId == "tt0133093")).FirstSeenAt);

        var source = await db.Sources.SingleAsync();
        source.Url = PartialFeedUrl;
        await db.SaveChangesAsync();

        var partial = await service.RunAsync();
        Assert.Equal("partial", partial.Summary.Status);
        Assert.Equal(3, partial.Summary.EntriesSeen);
        Assert.Equal(2, partial.Summary.ErrorCount);
        Assert.Equal(1, partial.Summary.OccurrencesCreated);
        Assert.Equal(1, partial.Summary.OmdbRequests);
        Assert.Equal(4, handler.OmdbRequestCount);
        Assert.Equal(2, await db.Occurrences.CountAsync());
        Assert.DoesNotContain(await db.MetadataCache.ToListAsync(), entry => entry.LookupTitle == "temporary film");

        var retried = await service.RunAsync();
        Assert.Equal("partial", retried.Summary.Status);
        Assert.Equal(1, retried.Summary.TitlesCreated);
        Assert.Equal(1, retried.Summary.OccurrencesCreated);
        Assert.Equal(5, handler.OmdbRequestCount);
        Assert.Equal(2, await db.Titles.CountAsync());
        Assert.Equal(3, await db.Occurrences.CountAsync());
        Assert.Contains(await db.MetadataCache.ToListAsync(), entry => entry.LookupTitle == "temporary film" && entry.Status == "found");

        var logs = await db.ParseLogs.OrderBy(log => log.Id).ToListAsync();
        Assert.Contains(logs, log => log.IgnoreReason == "malformed_entry");
        Assert.Contains(logs, log => log.OmdbStatus == "provider_error");
        Assert.Contains(logs, log => log.OmdbStatus == "confirmed_not_found" &&
            log.LookupTitles.SequenceEqual(new[] { "Unknown Film" }));
        Assert.Contains(logs, log => log.OmdbStatus == "provider_error" &&
            log.LookupTitles.SequenceEqual(new[] { "Temporary Film" }));
        var temporaryFilmLog = logs.Single(log => log.RawTitle.StartsWith("Temporary Film")
            && log.OmdbStatus == "provider_error");
        Assert.Equal("temporary-1", temporaryFilmLog.FeedEntryId);
        Assert.Equal("https://rutracker.org/forum/viewtopic.php?t=3", temporaryFilmLog.TorrentUrl);
        Assert.All(logs, log => Assert.True(log.RawTitle.Length <= 2049));
        Assert.DoesNotContain(logs, log => log.RawTitle.Contains(FakeApiKey, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("series_ongoing", "Silo S07E07 [2026]")]
    [InlineData("series_complete", "Silo / Season 7 / Episodes 1-10 of 10 [2026]")]
    public async Task SeriesProfilesUseSeriesLookupAndDoNotMatchSeasonYearToPremiereYear(
        string feedType,
        string releaseTitle)
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_ingestion_series_test").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options;
        await using var db = new MediaDockDbContext(options);
        await db.Database.MigrateAsync();
        db.Sources.Add(new Source
        {
            StableKey = "silo-profile",
            Name = RssFeedTypes.ProfileName(feedType),
            FeedType = feedType,
            Url = "https://feed.rutracker.cc/silo.atom"
        });
        await db.SaveChangesAsync();

        var handler = new MockProviderHandler(seriesScenario: true, seriesFeedTitle: releaseTitle);
        using var httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var service = CreateService(db, httpClient, new RssFeedTransport(httpClient, new PublicDnsResolver()));

        var result = await service.RunAsync();

        Assert.Equal("succeeded", result.Summary.Status);
        Assert.Equal(1, result.Summary.OmdbRequests);
        Assert.Equal(1, result.Summary.OccurrencesCreated);
        var title = await db.Titles.SingleAsync();
        Assert.Equal("Silo", title.TitleText);
        Assert.Equal("series", title.SourceType);
        Assert.Equal("series", title.MediaType);
        Assert.Equal(2023, title.Year);
        var log = await db.ParseLogs.SingleAsync();
        Assert.Contains("series_season_year_unknown", log.Decision);
        Assert.Contains("y_season", log.Decision);
    }

    [Fact]
    public async Task UpsertDoesNotMergeSameTitleAndYearWithDifferentImdbIds()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_ingestion_identity_conflict_test").Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var db = new MediaDockDbContext(options);
        await db.Database.MigrateAsync();

        var now = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);
        var source = new Source
        {
            StableKey = "movies-identity",
            Name = "Movies",
            FeedType = "movie",
            Url = SuccessfulFeedUrl
        };
        var existingTitle = new Title
        {
            TitleText = "Shared Film",
            NormalizedTitle = "shared film",
            Year = 2020,
            MediaType = "movie",
            ImdbId = "tt11111111",
            FirstSeenAt = now,
            LastSeenAt = now,
            UpdatedAt = now
        };
        db.Sources.Add(source);
        db.Titles.Add(existingTitle);
        await db.SaveChangesAsync();

        var sourceInfo = new IngestionSource(
            source.Id,
            RssFeedTypes.ProfileName(source.FeedType),
            source.FeedType,
            source.Url);
        var feedItem = new IngestionFeedItem(
            "Shared Film (2020)",
            "identity-conflict",
            "https://feed.example/topic/identity-conflict",
            now);
        var result = await new PostgresRssIngestionRepository(db).UpsertCatalogItemAsync(
            sourceInfo,
            feedItem,
            SourceItemIdentity.From(feedItem.FeedEntryId, feedItem.TorrentUrl),
            "integration-test-fingerprint",
            RutrackerTitleParser.Parse(feedItem.Title, "movie"),
            CreateMetadata("Shared Film", 2020, "TT22222222"),
            now);

        Assert.True(result.TitleCreated);
        Assert.Equal(2, await db.Titles.CountAsync());
        Assert.Equal("tt11111111", (await db.Titles.SingleAsync(title => title.Id == existingTitle.Id)).ImdbId);
        Assert.Equal(1, await db.Titles.CountAsync(title => title.ImdbId == "tt22222222"));
    }

    [Fact]
    public async Task ConcurrentUpsertsForOneImdbIdReuseTheCanonicalTitle()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_ingestion_concurrency_test").Build();
        await postgres.StartAsync();

        var connectionString = postgres.GetConnectionString();
        var seedOptions = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        long sourceId;
        await using (var seedDb = new MediaDockDbContext(seedOptions))
        {
            await seedDb.Database.MigrateAsync();
            var source = new Source
            {
                StableKey = "movies-concurrent",
                Name = "Movies",
                FeedType = "movie",
                Url = SuccessfulFeedUrl
            };
            seedDb.Sources.Add(source);
            await seedDb.SaveChangesAsync();
            sourceId = source.Id;
        }

        var barrier = new ConcurrentSaveChangesInterceptor();
        var concurrentOptions = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(barrier)
            .Options;
        var now = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);
        var sourceInfo = new IngestionSource(sourceId, "Movies", "movie", SuccessfulFeedUrl);
        var parsed = RutrackerTitleParser.Parse("Shared Film (2020)", "movie");
        var results = await Task.WhenAll(Enumerable.Range(1, 2).Select(async index =>
        {
            await using var context = new MediaDockDbContext(concurrentOptions);
            var feedItem = new IngestionFeedItem(
                "Shared Film (2020)",
                $"concurrent-{index}",
                $"https://feed.example/topic/concurrent-{index}",
                now);
            return await new PostgresRssIngestionRepository(context).UpsertCatalogItemAsync(
                sourceInfo,
                feedItem,
                SourceItemIdentity.From(feedItem.FeedEntryId, feedItem.TorrentUrl),
                $"fingerprint-{index}",
                parsed,
                CreateMetadata("Shared Film", 2020, index == 1 ? "TT33333333" : "tt33333333"),
                now);
        }));

        await using var verifyDb = new MediaDockDbContext(seedOptions);
        Assert.Equal(1, results.Count(result => result.TitleCreated));
        Assert.Equal(1, await verifyDb.Titles.CountAsync());
        Assert.Equal("tt33333333", (await verifyDb.Titles.SingleAsync()).ImdbId);
        Assert.Equal(2, await verifyDb.Occurrences.CountAsync());
    }

    private static RssIngestionService CreateService(
        MediaDockDbContext db,
        HttpClient httpClient,
        RssFeedTransport rssTransport)
    {
        var client = new OmdbClient(httpClient, FakeApiKey, TimeSpan.FromSeconds(2));
        var cache = new PostgresMetadataCacheStore(db);
        var resolver = new MetadataResolver(client, cache);
        return new RssIngestionService(
            new PostgresRssIngestionRepository(db),
            new RssFeedTransportAdapter(rssTransport),
            resolver);
    }

    private static MetadataDetails CreateMetadata(string title, int year, string imdbId) =>
        new(title, year, imdbId, "movie", "movie", "standard", null, null, null, null, [], [],
            null, null, null, null, null, null);

    private sealed class MockProviderHandler : HttpMessageHandler
    {
        private readonly bool _titleMatchScenario;
        private readonly bool _seriesScenario;
        private readonly string _seriesFeedTitle;
        private int _temporaryRequests;

        public MockProviderHandler(
            bool titleMatchScenario = false,
            bool seriesScenario = false,
            string seriesFeedTitle = "Silo S07E07 [2026]")
        {
            _titleMatchScenario = titleMatchScenario;
            _seriesScenario = seriesScenario;
            _seriesFeedTitle = seriesFeedTitle;
        }

        public int OmdbRequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uri = request.RequestUri!;
            if (uri.Host == RssFeedTransport.AllowedFeedHost)
            {
                return Task.FromResult(FeedResponse(_seriesScenario ? SeriesFeed(_seriesFeedTitle) : _titleMatchScenario ? TitleMatchFeed :
                    uri.AbsolutePath == "/success.atom" ? SuccessfulFeed : PartialFeed));
            }

            if (uri.Host != "www.omdbapi.com")
            {
                throw new InvalidOperationException("Unexpected test HTTP host.");
            }

            OmdbRequestCount++;
            var query = ParseQuery(uri.Query);
            if (_seriesScenario)
            {
                if (query["t"] != "Silo" || query["type"] != "series" || query.ContainsKey("y"))
                {
                    throw new InvalidOperationException("Series lookup must omit season year and use series type.");
                }

                return Task.FromResult(JsonResponse(SeriesPayload("Silo", "2023-2025", "tt8111088")));
            }

            if (_titleMatchScenario)
            {
                var payload = query["t"] switch
                {
                    "Wrong Film" => MoviePayload("Other Film", "2020", "tt1111111"),
                    "Right Film" => MoviePayload("Right Film", "2020", "tt2222222"),
                    "Lone Film" => MoviePayload("Other Film", "2020", "tt1111111"),
                    "Missing Film" => """{"Response":"False","Error":"Movie not found!"}""",
                    _ => throw new InvalidOperationException("Unexpected title-match lookup.")
                };
                return Task.FromResult(JsonResponse(payload));
            }

            if (query["t"] == "The Matrix")
            {
                if (query["type"] != "movie" || query.GetValueOrDefault("y") != "1999")
                {
                    throw new InvalidOperationException("Movie lookup must use its release year and movie type.");
                }

                return Task.FromResult(JsonResponse(MoviePayload("The Matrix", "1999", "tt0133093")));
            }

            if (query["t"] == "Unknown Film")
            {
                return Task.FromResult(JsonResponse("""{"Response":"False","Error":"Movie not found!"}"""));
            }

            if (query["t"] == "Temporary Film")
            {
                _temporaryRequests++;
                if (_temporaryRequests == 1)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                }

                return Task.FromResult(JsonResponse(MoviePayload("Temporary Film", "2020", "tt9876543")));
            }

            throw new InvalidOperationException("Unexpected OMDb lookup in the test.");
        }

        private static string SuccessfulFeed => """
            <?xml version="1.0" encoding="utf-8"?>
            <rss version="2.0"><channel>
              <item><title>The Matrix (1999) [1080p]</title><link>https://rutracker.org/forum/viewtopic.php?t=1</link><guid>matrix-1</guid></item>
              <item><title>Unknown Film (2024) [1080p]</title><link>https://rutracker.org/forum/viewtopic.php?t=2</link><guid>unknown-1</guid></item>
            </channel></rss>
            """;

                private static string SeriesFeed(string title) => $"""
                        <?xml version="1.0" encoding="utf-8"?>
                        <rss version="2.0"><channel>
                            <item><title>{title}</title><link>https://rutracker.org/forum/viewtopic.php?t=7</link><guid>silo-s07e07</guid></item>
                        </channel></rss>
                        """;

                private static string TitleMatchFeed => """
                        <?xml version="1.0" encoding="utf-8"?>
                        <rss version="2.0"><channel>
                            <item><title>Wrong Film / Right Film (2020) [2024, BDRip]</title><link>https://rutracker.org/forum/viewtopic.php?t=11</link><guid>right-1</guid></item>
                            <item><title>Lone Film (2020) [1080p]</title><link>https://rutracker.org/forum/viewtopic.php?t=12</link><guid>lone-1</guid></item>
                              <item><title>Missing Film / Right Film (2020) [1080p]</title><link>https://rutracker.org/forum/viewtopic.php?t=13</link><guid>right-2</guid></item>
                        </channel></rss>
                        """;

        private static string PartialFeed => """
            <?xml version="1.0" encoding="utf-8"?>
            <rss version="2.0"><channel>
              <item><title>malformed apikey=integration-test-only-key</title><guid>bad-1</guid></item>
              <item><title>Temporary Film (2020) [1080p]</title><link>https://rutracker.org/forum/viewtopic.php?t=3</link><guid>temporary-1</guid></item>
              <item><title>The Matrix (1999) [2160p]</title><link>https://rutracker.org/forum/viewtopic.php?t=4</link><guid>matrix-2</guid></item>
            </channel></rss>
            """;

        private static string MoviePayload(string title, string year, string imdbId) =>
            JsonSerializer.Serialize(new
            {
                Response = "True",
                Title = title,
                Year = year,
                imdbID = imdbId,
                Type = "movie",
                imdbRating = "8.7",
                imdbVotes = "1,234",
                Metascore = "73",
                Genre = "Action, Sci-Fi",
                Country = "USA",
                Director = "Example Director",
                Plot = "Example plot",
                Poster = "https://example.test/poster.jpg",
                Runtime = "120 min",
                Awards = "None",
                BoxOffice = "$100"
            });

        private static string SeriesPayload(string title, string year, string imdbId) =>
            JsonSerializer.Serialize(new
            {
                Response = "True",
                Title = title,
                Year = year,
                imdbID = imdbId,
                Type = "series",
                imdbRating = "8.7",
                imdbVotes = "1,234",
                Metascore = "73",
                Genre = "Drama",
                Country = "USA",
                Director = "Example Director",
                Plot = "Example plot",
                Poster = "https://example.test/poster.jpg",
                Runtime = "60 min",
                Awards = "None",
                BoxOffice = "$100"
            });

        private static HttpResponseMessage FeedResponse(string feed) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(feed, Encoding.UTF8, "application/rss+xml")
        };

        private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        private static Dictionary<string, string> ParseQuery(string query) => query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0]),
                pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : string.Empty,
                StringComparer.Ordinal);
    }

    private sealed class PublicDnsResolver : IRssDnsResolver
    {
        public Task<IPAddress[]> ResolveAsync(string hostname, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new[] { IPAddress.Parse("8.8.8.8") });
        }
    }

    private sealed class ConcurrentSaveChangesInterceptor : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _bothSaving = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _savingCount;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var count = Interlocked.Increment(ref _savingCount);
            if (count <= 2)
            {
                if (count == 2)
                {
                    _bothSaving.TrySetResult();
                }

                await _bothSaving.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }

            return result;
        }
    }
}