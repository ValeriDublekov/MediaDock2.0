using System.Net;
using System.Net.Http.Json;
using System.Text;
using MediaDock.Api.Catalog;
using MediaDock.Api.Common;
using MediaDock.Application.Ingestion;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;

namespace MediaDock.IntegrationTests;

[Trait("Category", "Api")]
public sealed class CatalogEndpointsApiTests
{
    [Fact]
    public async Task CatalogAndTitleEndpointsValidateFilterAndReturnPostgresBackedResources()
    {
        await using var host = await CatalogApiTestHost.CreateAsync("mediadock_catalog_api_test");
        var db = host.Db;
        var client = host.Client;

        using var emptyResponse = await client.GetAsync("/api/catalog");
        Assert.Equal(HttpStatusCode.OK, emptyResponse.StatusCode);
        var emptyCatalog = await emptyResponse.Content.ReadFromJsonAsync<PageResponse<CatalogTitleResponse>>();
        Assert.NotNull(emptyCatalog);
        Assert.Empty(emptyCatalog.Items);
        Assert.Equal(0, emptyCatalog.TotalCount);
        Assert.Null(emptyCatalog.YearBounds);
        Assert.Null(emptyCatalog.ImdbRatingBounds);

        using var invalidPageResponse = await client.GetAsync("/api/catalog?page=0&pageSize=25");
        Assert.Equal(HttpStatusCode.BadRequest, invalidPageResponse.StatusCode);
        Assert.Equal("application/problem+json", invalidPageResponse.Content.Headers.ContentType?.MediaType);
        var invalidPageProblem = await invalidPageResponse.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(invalidPageProblem);
        Assert.NotEmpty(invalidPageProblem.Errors);

        var source = new Source
        {
            StableKey = "movies-main",
            Name = "Movies",
            FeedType = "movie",
            Url = "https://feed.rutracker.cc/movies.atom",
            IsEnabled = true
        };
        var olderTitle = CreateTitle("The Matrix", "the matrix", 1999, "movie");
        var newerTitle = CreateTitle("The Matrix Reloaded", "the matrix reloaded", 2003, "movie");
        var seriesTitle = CreateTitle("Example Series", "example series", 2010, "series");
        var yearlessTitle = CreateTitle("Year Unknown", "year unknown", null, "movie");
        db.ImdbTitles.AddRange(
            new ImdbTitle
            {
                ImdbId = "tt1999", TitleText = "Canonical Matrix", ImdbRating = 7.2m, ImdbVotes = 1000,
                Metascore = 73m, Genres = ["Action", "Sci-Fi"], Countries = ["USA"],
                Director = "The Wachowskis", Plot = "Canonical plot.", PosterUrl = "https://example.test/matrix.jpg",
                Runtime = "136 min", Awards = "Canonical awards.", BoxOffice = "$100",
                UpdatedAt = olderTitle.UpdatedAt
            },
            new ImdbTitle
            {
                ImdbId = "tt2003", ImdbRating = 8.5m, ImdbVotes = 1000,
                Genres = ["Action", "Sci-Fi"], Countries = ["USA"], UpdatedAt = newerTitle.UpdatedAt
            },
            new ImdbTitle
            {
                ImdbId = "tt2010", ImdbRating = 6.8m, ImdbVotes = 1000,
                Genres = ["Drama"], Countries = ["USA"], UpdatedAt = seriesTitle.UpdatedAt
            });
        db.Sources.Add(source);
        db.TorrentMovies.AddRange(olderTitle, newerTitle, seriesTitle, yearlessTitle);
        await db.SaveChangesAsync();

        var firstSeen = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        db.TorrentReleases.AddRange(
            CreateRelease(olderTitle.Id, source.Id, "matrix-1999", "The Matrix (1999)", firstSeen, quality: "1080p", ripType: "BDRip"),
            CreateRelease(newerTitle.Id, source.Id, "matrix-2003", "The Matrix Reloaded (2003)", firstSeen.AddDays(1)),
            CreateRelease(yearlessTitle.Id, source.Id, "year-unknown", "Year Unknown", firstSeen.AddDays(2)));
        var completeSeriesSource = new Source
        {
            StableKey = "series-complete",
            Name = "Series",
            FeedType = "series_complete",
            Url = "https://feed.rutracker.cc/series-complete.atom",
            IsEnabled = true
        };
        var ongoingSeriesSource = new Source
        {
            StableKey = "series-ongoing",
            Name = "Series in progress",
            FeedType = "series_ongoing",
            Url = "https://feed.rutracker.cc/series-ongoing.atom",
            IsEnabled = true
        };
        db.Sources.AddRange(completeSeriesSource, ongoingSeriesSource);
        await db.SaveChangesAsync();
        db.TorrentReleases.AddRange(
            CreateRelease(seriesTitle.Id, completeSeriesSource.Id, "series-complete", "Example Series", firstSeen.AddDays(2), "series_complete", "Series"),
            CreateRelease(seriesTitle.Id, ongoingSeriesSource.Id, "series-ongoing", "Example Series", firstSeen.AddDays(3), "series_ongoing", "Series in progress"));
        await db.SaveChangesAsync();

        using var catalogResponse = await client.GetAsync(
            "/api/catalog?page=1&pageSize=1&search=matrix&mediaType=movie&yearFrom=1990&yearTo=2005&genre=Sci-Fi&country=USA");
        Assert.Equal(HttpStatusCode.OK, catalogResponse.StatusCode);
        var firstPage = await catalogResponse.Content.ReadFromJsonAsync<PageResponse<CatalogTitleResponse>>();
        Assert.NotNull(firstPage);
        Assert.Equal(2, firstPage.TotalCount);
        Assert.Equal(2, firstPage.TotalPages);
        Assert.Equal(new YearBounds(1999, 2010), firstPage.YearBounds);
        Assert.Equal(new ImdbRatingBounds(6.8m, 8.5m), firstPage.ImdbRatingBounds);
        Assert.Equal("The Matrix Reloaded", Assert.Single(firstPage.Items).Title);
        Assert.Equal("tt2003", Assert.Single(firstPage.Items).ImdbId);
        Assert.Equal(8.5m, Assert.Single(firstPage.Items).ImdbRating);

        using var canonicalSearchResponse = await client.GetAsync("/api/catalog?search=Canonical%20Matrix");
        var canonicalSearch = await canonicalSearchResponse.Content.ReadFromJsonAsync<PageResponse<CatalogTitleResponse>>();
        Assert.NotNull(canonicalSearch);
        Assert.Equal("The Matrix", Assert.Single(canonicalSearch.Items).Title);

        using var ratingFilteredResponse = await client.GetAsync("/api/catalog?search=matrix&imdbRatingFrom=8&imdbRatingTo=9");
        var ratingFilteredPage = await ratingFilteredResponse.Content.ReadFromJsonAsync<PageResponse<CatalogTitleResponse>>();
        Assert.NotNull(ratingFilteredPage);
        Assert.Equal(new ImdbRatingBounds(6.8m, 8.5m), ratingFilteredPage.ImdbRatingBounds);
        Assert.Equal("The Matrix Reloaded", Assert.Single(ratingFilteredPage.Items).Title);

        using var unfilteredCatalogResponse = await client.GetAsync("/api/catalog?pageSize=100");
        var unfilteredCatalog = await unfilteredCatalogResponse.Content.ReadFromJsonAsync<PageResponse<CatalogTitleResponse>>();
        Assert.NotNull(unfilteredCatalog);
        Assert.Equal(new YearBounds(1999, 2010), unfilteredCatalog.YearBounds);
        var yearlessCatalogTitle = Assert.Single(unfilteredCatalog.Items, title => title.Title == "Year Unknown");
        Assert.Null(yearlessCatalogTitle.Year);
        Assert.Null(yearlessCatalogTitle.ImdbRating);
        Assert.Empty(yearlessCatalogTitle.Genres);
        Assert.Null(yearlessCatalogTitle.PosterUrl);

        var catalogMovie = Assert.Single(unfilteredCatalog.Items, title => title.Title == "The Matrix");
        Assert.Equal("The Wachowskis", catalogMovie.Director);
        Assert.Equal("Canonical plot.", catalogMovie.Plot);

        using var inProgressSeriesResponse = await client.GetAsync("/api/catalog?feedTypes=series_ongoing");
        var inProgressSeries = await inProgressSeriesResponse.Content.ReadFromJsonAsync<PageResponse<CatalogTitleResponse>>();
        Assert.NotNull(inProgressSeries);
        Assert.Equal("Example Series", Assert.Single(inProgressSeries.Items).Title);

        using var mainFeedsResponse = await client.GetAsync("/api/catalog?feedTypes=movie,series_complete,series_ongoing");
        var mainFeeds = await mainFeedsResponse.Content.ReadFromJsonAsync<PageResponse<CatalogTitleResponse>>();
        Assert.NotNull(mainFeeds);
        Assert.Equal(4, mainFeeds.TotalCount);

        using var secondPageResponse = await client.GetAsync(
            "/api/catalog?page=2&pageSize=1&search=matrix&mediaType=movie");
        var secondPage = await secondPageResponse.Content.ReadFromJsonAsync<PageResponse<CatalogTitleResponse>>();
        Assert.NotNull(secondPage);
        Assert.Equal("The Matrix", Assert.Single(secondPage.Items).Title);

        using var reversedYearsResponse = await client.GetAsync("/api/catalog?yearFrom=2005&yearTo=1999");
        Assert.Equal(HttpStatusCode.BadRequest, reversedYearsResponse.StatusCode);
        Assert.NotNull(await reversedYearsResponse.Content.ReadFromJsonAsync<ValidationProblemDetails>());

        using var titleResponse = await client.GetAsync($"/api/titles/{olderTitle.Id}");
        var titleDetails = await titleResponse.Content.ReadFromJsonAsync<TitleDetailsResponse>();
        Assert.NotNull(titleDetails);
        Assert.Equal("The Matrix", titleDetails.Title);
        Assert.Equal(1, titleDetails.OccurrenceCount);
        Assert.Equal(7.2m, titleDetails.ImdbRating);
        Assert.Equal(1000, titleDetails.ImdbVotes);
        Assert.Equal(73m, titleDetails.Metascore);
        Assert.Equal(new[] { "Action", "Sci-Fi" }, titleDetails.Genres);
        Assert.Equal("The Wachowskis", titleDetails.Director);
        Assert.Equal("Canonical plot.", titleDetails.Plot);
        Assert.Equal("https://example.test/matrix.jpg", titleDetails.PosterUrl);
        Assert.Equal("136 min", titleDetails.Runtime);
        Assert.Equal("Canonical awards.", titleDetails.Awards);
        Assert.Equal("$100", titleDetails.BoxOffice);

        using var missingTitleResponse = await client.GetAsync("/api/titles/999999");
        Assert.Equal(HttpStatusCode.NotFound, missingTitleResponse.StatusCode);
        Assert.NotNull(await missingTitleResponse.Content.ReadFromJsonAsync<ProblemDetails>());

        using var occurrencesResponse = await client.GetAsync($"/api/titles/{olderTitle.Id}/occurrences?page=1&pageSize=10");
        var occurrences = await occurrencesResponse.Content.ReadFromJsonAsync<PageResponse<OccurrenceResponse>>();
        Assert.NotNull(occurrences);
        Assert.Equal(1, occurrences.TotalCount);
        Assert.Equal("Movies", Assert.Single(occurrences.Items).SourceName);
        Assert.Equal("1080p", Assert.Single(occurrences.Items).Quality);
        Assert.Equal("BDRip", Assert.Single(occurrences.Items).RipType);
        Assert.DoesNotContain("\"yearBounds\"", await occurrencesResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RssPreviewReturnsItemsOnlyForEnabledSourcesInTheSelectedProfile()
    {
        var transport = new StaticRssFeedTransport(Encoding.UTF8.GetBytes("""
            <rss version="2.0"><channel><title>Movies</title><item>
              <title>Example Film</title><guid>entry-1</guid>
              <link>https://feed.rutracker.cc/download/1</link>
              <pubDate>Tue, 27 Oct 2026 12:00:00 GMT</pubDate>
            </item></channel></rss>
            """));
        await using var host = await CatalogApiTestHost.CreateAsync(
            "mediadock_catalog_rss_preview_test",
            rssFeedTransport: transport);
        host.Db.Sources.AddRange(
            new Source
            {
                StableKey = "preview-movie-enabled",
                Name = "Movies",
                FeedType = "movie",
                Url = "https://feed.rutracker.cc/movies.atom",
                IsEnabled = true
            },
            new Source
            {
                StableKey = "preview-movie-enabled-secondary",
                Name = "Movies",
                FeedType = "movie",
                Url = "https://feed.rutracker.cc/movies-secondary.atom",
                IsEnabled = true
            },
            new Source
            {
                StableKey = "preview-movie-disabled",
                Name = "Movies",
                FeedType = "movie",
                Url = "https://feed.rutracker.cc/disabled.atom",
                IsEnabled = false
            },
            new Source
            {
                StableKey = "preview-series-enabled",
                Name = "Series",
                FeedType = "series_complete",
                Url = "https://feed.rutracker.cc/series.atom",
                IsEnabled = true
            });
        await host.Db.SaveChangesAsync();

        using var response = await host.Client.GetAsync("/api/catalog/rss/movie");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.Content.ReadFromJsonAsync<RssFeedPreviewResponse>();
        Assert.NotNull(preview);
        Assert.Equal("movie", preview.ProfileId);
        Assert.Equal("Movies", preview.ProfileName);
        Assert.Equal(2, preview.Feeds.Count);
        Assert.All(preview.Feeds, feed =>
        {
            var item = Assert.Single(feed.Items);
            Assert.Equal("Example Film", item.Title);
            Assert.Equal("entry-1", item.FeedEntryId);
            Assert.Equal("https://feed.rutracker.cc/download/1", item.TorrentUrl);
            Assert.Equal(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero), item.PublishedAt);
            Assert.Equal(Encoding.UTF8.GetString(transport.Body), feed.OriginalContent);
        });
        Assert.Equal(2, transport.MaxConcurrentRequests);
        Assert.Equal(
            new[] { "https://feed.rutracker.cc/movies-secondary.atom", "https://feed.rutracker.cc/movies.atom" },
            transport.RequestedUrls.OrderBy(url => url));

        using var invalidProfileResponse = await host.Client.GetAsync("/api/catalog/rss/unknown");
        Assert.Equal(HttpStatusCode.BadRequest, invalidProfileResponse.StatusCode);
    }

    private sealed class StaticRssFeedTransport(byte[] body) : IRssFeedTransport
    {
        private readonly object _sync = new();
        private readonly List<string> _requestedUrls = [];
        private int _activeRequests;
        private int _maxConcurrentRequests;

        public byte[] Body => body;
        public IReadOnlyList<string> RequestedUrls
        {
            get
            {
                lock (_sync)
                {
                    return [.. _requestedUrls];
                }
            }
        }
        public int MaxConcurrentRequests
        {
            get
            {
                lock (_sync)
                {
                    return _maxConcurrentRequests;
                }
            }
        }

        public async Task<byte[]> FetchAsync(string url, CancellationToken cancellationToken = default)
        {
            var activeRequests = Interlocked.Increment(ref _activeRequests);
            lock (_sync)
            {
                _requestedUrls.Add(url);
                _maxConcurrentRequests = Math.Max(_maxConcurrentRequests, activeRequests);
            }

            try
            {
                await Task.Delay(100, cancellationToken);
                return body;
            }
            finally
            {
                Interlocked.Decrement(ref _activeRequests);
            }
        }
    }

    [Fact]
    public async Task DeleteTitleRemovesReleasesAndOnlyDeletesRequestedUnsharedImdbInfo()
    {
        await using var host = await CatalogApiTestHost.CreateAsync("mediadock_catalog_delete_api_test");
        var db = host.Db;
        var source = new Source
        {
            StableKey = "delete-test",
            Name = "Delete test",
            FeedType = "movie",
            Url = "https://feed.rutracker.cc/delete-test.atom",
            IsEnabled = true
        };
        var standaloneTitle = CreateTitle("Standalone Film", "standalone film", 1998, "movie");
        var sharedTitle = CreateTitle("Shared Film", "shared film", 1999, "movie");
        var retainedTitle = CreateTitle("Retained Film", "retained film", 2000, "movie");
        db.Sources.Add(source);
        db.ImdbTitles.AddRange(
            new ImdbTitle { ImdbId = "tt1998", TitleText = "Standalone Film" },
            new ImdbTitle { ImdbId = "tt1999", TitleText = "Shared Film" },
            new ImdbTitle { ImdbId = "tt2000", TitleText = "Retained Film" });
        db.TorrentMovies.AddRange(standaloneTitle, sharedTitle, retainedTitle);
        db.OscarMovies.Add(new OscarMovie
        {
            StableKey = "shared-film",
            FilmTitle = "Shared Film",
            NormalizedTitle = "shared film",
            FilmYear = 1999,
            ImdbId = "tt1999",
            ImportedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var observedAt = DateTimeOffset.UtcNow;
        var standaloneRelease = CreateRelease(standaloneTitle.Id, source.Id, "standalone", "Standalone Film", observedAt);
        var sharedRelease = CreateRelease(sharedTitle.Id, source.Id, "shared", "Shared Film", observedAt);
        var retainedRelease = CreateRelease(retainedTitle.Id, source.Id, "retained", "Retained Film", observedAt);
        db.TorrentReleases.AddRange(standaloneRelease, sharedRelease, retainedRelease);
        db.RssItemStates.AddRange(
            CreateItemState(source.Id, "standalone", observedAt),
            CreateItemState(source.Id, "shared", observedAt),
            CreateItemState(source.Id, "retained", observedAt));
        await db.SaveChangesAsync();

        using var standaloneResponse = await host.Client.DeleteAsync($"/api/titles/{standaloneTitle.Id}?deleteImdbInfo=true");
        Assert.Equal(HttpStatusCode.OK, standaloneResponse.StatusCode);
        var standaloneResult = await standaloneResponse.Content.ReadFromJsonAsync<DeleteTitleResponse>();
        Assert.NotNull(standaloneResult);
        Assert.True(standaloneResult.ImdbInfoDeleted);
        Assert.False(await db.TorrentMovies.AnyAsync(movie => movie.Id == standaloneTitle.Id));
        Assert.False(await db.TorrentReleases.AnyAsync(release => release.Id == standaloneRelease.Id));
        Assert.False(await db.RssItemStates.AnyAsync(state => state.SourceId == source.Id && state.SourceItemKey == "standalone"));
        Assert.False(await db.ImdbTitles.AnyAsync(title => title.ImdbId == "tt1998"));

        using var sharedResponse = await host.Client.DeleteAsync($"/api/titles/{sharedTitle.Id}?deleteImdbInfo=true");
        var sharedResult = await sharedResponse.Content.ReadFromJsonAsync<DeleteTitleResponse>();
        Assert.Equal(HttpStatusCode.OK, sharedResponse.StatusCode);
        Assert.NotNull(sharedResult);
        Assert.False(sharedResult.ImdbInfoDeleted);
        Assert.False(await db.TorrentMovies.AnyAsync(movie => movie.Id == sharedTitle.Id));
        Assert.True(await db.ImdbTitles.AnyAsync(title => title.ImdbId == "tt1999"));
        Assert.True(await db.OscarMovies.AnyAsync(movie => movie.ImdbId == "tt1999"));

        using var retainedResponse = await host.Client.DeleteAsync($"/api/titles/{retainedTitle.Id}?deleteImdbInfo=false");
        var retainedResult = await retainedResponse.Content.ReadFromJsonAsync<DeleteTitleResponse>();
        Assert.Equal(HttpStatusCode.OK, retainedResponse.StatusCode);
        Assert.NotNull(retainedResult);
        Assert.False(retainedResult.ImdbInfoDeleted);
        Assert.True(await db.ImdbTitles.AnyAsync(title => title.ImdbId == "tt2000"));
    }

    private static TorrentMovie CreateTitle(string title, string normalizedTitle, int? year, string mediaType)
    {
        var seenAt = year is { } knownYear
            ? new DateTimeOffset(knownYear, 1, 1, 0, 0, 0, TimeSpan.Zero)
            : new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var imdbId = year is { } imdbYear ? $"tt{imdbYear}" : null;
        return new TorrentMovie
        {
            TitleText = title,
            NormalizedTitle = normalizedTitle,
            Year = year,
            MediaType = mediaType,
            SourceType = mediaType,
            ContentKind = "standard",
            ImdbId = imdbId,
            FirstSeenAt = seenAt,
            LastSeenAt = seenAt,
            UpdatedAt = seenAt
        };
    }

    private static TorrentRelease CreateRelease(
        long torrentMovieId,
        long sourceId,
        string sourceItemKey,
        string rawTitle,
        DateTimeOffset lastSeenAt,
        string feedType = "movie",
        string sourceFeedName = "Movies",
        string? quality = null,
        string? ripType = null) => new()
    {
        TorrentMovieId = torrentMovieId,
        SourceId = sourceId,
        SourceItemKey = sourceItemKey,
        TorrentUrl = $"https://feed.example/release/{Uri.EscapeDataString(sourceItemKey)}",
        RawTitle = rawTitle,
        SourceFeedName = sourceFeedName,
        FeedType = feedType,
        Quality = quality,
        RipType = ripType,
        FirstSeenAt = lastSeenAt,
        LastSeenAt = lastSeenAt
    };

    private static RssItemProcessingState CreateItemState(long sourceId, string sourceItemKey, DateTimeOffset updatedAt) => new()
    {
        SourceId = sourceId,
        SourceItemKey = sourceItemKey,
        Fingerprint = $"fingerprint-{sourceItemKey}",
        Disposition = "resolved",
        UpdatedAt = updatedAt
    };
}