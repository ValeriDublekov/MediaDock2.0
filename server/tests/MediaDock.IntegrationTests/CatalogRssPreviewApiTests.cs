using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MediaDock.Api.Catalog;
using MediaDock.Application.Ingestion;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.IntegrationTests;

[Trait("Category", "Api")]
public sealed class CatalogRssPreviewApiTests
{
    [Fact]
    public async Task RssPreviewReadsCachedItemsAndRefreshesOnlyEnabledSourcesInTheSelectedProfile()
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

        using var emptyResponse = await host.Client.GetAsync("/api/catalog/rss/movie");
        Assert.Equal(HttpStatusCode.OK, emptyResponse.StatusCode);
        var emptyPreview = await emptyResponse.Content.ReadFromJsonAsync<RssFeedPreviewResponse>();
        Assert.NotNull(emptyPreview);
        Assert.Null(emptyPreview.CachedAt);
        Assert.Empty(emptyPreview.Feeds);
        Assert.Empty(transport.RequestedUrls);

        using var refreshResponse = await host.Client.PostAsync("/api/catalog/rss/movie/refresh", content: null);
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var preview = await refreshResponse.Content.ReadFromJsonAsync<RssFeedPreviewResponse>();
        Assert.NotNull(preview);
        Assert.Equal("movie", preview.ProfileId);
        Assert.Equal("Movies", preview.ProfileName);
        Assert.NotNull(preview.CachedAt);
        Assert.Equal(2, preview.Feeds.Count);
        Assert.All(preview.Feeds, feed =>
        {
            var item = Assert.Single(feed.Items);
            Assert.Equal("Example Film", item.Title);
            Assert.Equal("entry-1", item.FeedEntryId);
            Assert.Equal("https://feed.rutracker.cc/download/1", item.TorrentUrl);
            Assert.Equal(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero), item.PublishedAt);
            Assert.Equal(Encoding.UTF8.GetString(transport.Body), feed.OriginalContent);
            Assert.Contains(feed.SourceUrl, transport.RequestedUrls);
            Assert.True(feed.FetchDurationMs >= 0);
            Assert.True(feed.ParseDurationMs >= 0);
        });
        Assert.Equal(1, transport.MaxConcurrentRequests);
        var initialRequestedUrls = transport.RequestedUrls;
        Assert.Equal(
            new[] { "https://feed.rutracker.cc/movies-secondary.atom", "https://feed.rutracker.cc/movies.atom" },
            initialRequestedUrls.OrderBy(url => url));

        using var cachedResponse = await host.Client.GetAsync("/api/catalog/rss/movie");
        var cachedPreview = await cachedResponse.Content.ReadFromJsonAsync<RssFeedPreviewResponse>();
        Assert.NotNull(cachedPreview);
        Assert.Equal(preview.CachedAt, cachedPreview.CachedAt);
        Assert.Equal(JsonSerializer.Serialize(preview.Feeds), JsonSerializer.Serialize(cachedPreview.Feeds));
        Assert.Equal(initialRequestedUrls.Count, transport.RequestedUrls.Count);

        var latestCachedAt = preview.CachedAt;
        for (var refreshIndex = 0; refreshIndex < 5; refreshIndex++)
        {
            using var additionalRefresh = await host.Client.PostAsync("/api/catalog/rss/movie/refresh", content: null);
            Assert.Equal(HttpStatusCode.OK, additionalRefresh.StatusCode);
            var additionalPreview = await additionalRefresh.Content.ReadFromJsonAsync<RssFeedPreviewResponse>();
            Assert.NotNull(additionalPreview);
            latestCachedAt = additionalPreview.CachedAt;
        }

        var retainedSnapshots = await host.Db.RssFeedPreviewSnapshots
            .Where(snapshot => snapshot.ProfileId == "movie")
            .OrderByDescending(snapshot => snapshot.CachedAt)
            .ThenByDescending(snapshot => snapshot.Id)
            .ToListAsync();
        Assert.Equal(5, retainedSnapshots.Count);
        Assert.Equal(latestCachedAt, retainedSnapshots[0].CachedAt);

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
}
