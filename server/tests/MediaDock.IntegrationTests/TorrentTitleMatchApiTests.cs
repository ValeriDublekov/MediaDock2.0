using System.Net;
using System.Net.Http.Json;
using MediaDock.Api.Catalog;
using MediaDock.Application.Matching;
using MediaDock.Application.Metadata;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.IntegrationTests;

[Trait("Category", "Api")]
public sealed class TorrentTitleMatchApiTests
{
    [Fact]
    public async Task CatalogAndParseLogCanSearchAndApplyAnExplicitImdbCandidate()
    {
        const string imdbId = "tt12345678";
        var omdbClient = new StubOmdbClient(imdbId);
        var omdbClientFactory = new StubOmdbClientFactory(omdbClient);
        await using var host = await CatalogApiTestHost.CreateAsync(
            "mediadock_torrent_title_match_api_test",
            omdbClientFactory: omdbClientFactory);
        var db = host.Db;
        var observedAt = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        var source = new Source
        {
            StableKey = "movies-match-test",
            Name = "Movies",
            FeedType = "movie",
            Url = "https://feed.rutracker.cc/movies.atom",
            IsEnabled = true
        };
        var title = new TorrentMovie
        {
            TitleText = "Quiet River",
            NormalizedTitle = "quiet river",
            Year = 2004,
            MediaType = "movie",
            SourceType = "movie",
            ContentKind = "standard",
            FirstSeenAt = observedAt,
            LastSeenAt = observedAt,
            UpdatedAt = observedAt
        };
        db.Settings.Add(new AppSetting
        {
            Id = 1,
            OmdbApiKey = "test-omdb-key",
            OmdbDailyRequestLimit = 40,
            UpdatedAt = observedAt
        });
        db.Sources.Add(source);
        db.TorrentMovies.Add(title);
        await db.SaveChangesAsync();

        db.TorrentReleases.Add(new TorrentRelease
        {
            TorrentMovieId = title.Id,
            SourceId = source.Id,
            SourceItemKey = "entry:existing-1",
            FeedEntryId = "existing-1",
            TorrentUrl = "https://rutracker.org/forum/viewtopic.php?t=100",
            RawTitle = "Quiet River (2004)",
            SourceFeedName = source.Name,
            FeedType = source.FeedType,
            FirstSeenAt = observedAt,
            LastSeenAt = observedAt
        });

        var retryUrl = "https://rutracker.org/forum/viewtopic.php?t=200";
        db.ParseLogs.Add(new ParseLog
        {
            SourceId = source.Id,
            SourceItemKey = "entry:retry-1",
            RawTitle = "Quiet River (2004)",
            FeedName = source.Name,
            ParsedSuccessfully = true,
            ParsedTitle = "Quiet River",
            ParsedYear = 2004,
            LookupTitles = ["Quiet River"],
            OmdbStatus = "confirmed_not_found",
            Ignored = true,
            IgnoreReason = "metadata_not_found",
            ProcessedAt = observedAt.AddMinutes(-5),
            RetryState = "terminal",
            AttemptCount = 1,
            FeedType = source.FeedType,
            SourcePublishedAt = observedAt.AddDays(-1),
            ObservedAt = observedAt.AddMinutes(-5),
            EventKind = "ingestion",
            FeedEntryId = "retry-1",
            TorrentUrl = retryUrl
        });
        await db.SaveChangesAsync();
        var parseLogId = db.ParseLogs.Single().Id;

        using var catalogSearch = await host.Client.PostAsync($"/api/titles/{title.Id}/imdb-candidates", null);
        Assert.Equal(HttpStatusCode.OK, catalogSearch.StatusCode);
        var catalogCandidates = await catalogSearch.Content.ReadFromJsonAsync<ImdbCandidateSearchResponse>();
        Assert.NotNull(catalogCandidates);
        Assert.Equal("Quiet River", Assert.Single(catalogCandidates.Candidates).Title);
        Assert.Equal(2004, catalogCandidates.Candidates[0].Year);
        Assert.Equal(imdbId, catalogCandidates.Candidates[0].ImdbId);
        Assert.Equal(1, omdbClient.SearchCalls);
        Assert.Equal(1, omdbClient.LastSearchPage);
        Assert.Equal("test-omdb-key", omdbClientFactory.LastApiKey);
        Assert.Equal(40, omdbClientFactory.LastDailyRequestLimit);

        using var nextCatalogPage = await host.Client.PostAsync(
            $"/api/titles/{title.Id}/imdb-candidates?page=2",
            null);
        Assert.Equal(HttpStatusCode.OK, nextCatalogPage.StatusCode);
        Assert.Equal(2, omdbClient.LastSearchPage);

        using var catalogApply = await host.Client.PostAsJsonAsync(
            $"/api/titles/{title.Id}/imdb-match",
            new ApplyImdbCandidateRequest(imdbId));
        Assert.Equal(HttpStatusCode.OK, catalogApply.StatusCode);
        var storedTitle = await db.TorrentMovies.AsNoTracking().SingleAsync(movie => movie.Id == title.Id);
        Assert.Equal(imdbId, storedTitle.ImdbId);
        Assert.Equal("Quiet River", await db.ImdbTitles.Where(item => item.ImdbId == imdbId)
            .Select(item => item.TitleText).SingleAsync());

        using var logSearch = await host.Client.PostAsync($"/api/parse-logs/{parseLogId}/imdb-candidates", null);
        Assert.Equal(HttpStatusCode.OK, logSearch.StatusCode);
        var logCandidates = await logSearch.Content.ReadFromJsonAsync<ImdbCandidateSearchResponse>();
        Assert.NotNull(logCandidates);
        Assert.Equal(imdbId, Assert.Single(logCandidates.Candidates).ImdbId);
        Assert.Equal(3, omdbClient.SearchCalls);

        using var logApply = await host.Client.PostAsJsonAsync(
            $"/api/parse-logs/{parseLogId}/imdb-match",
            new ApplyImdbCandidateRequest(imdbId));
        Assert.Equal(HttpStatusCode.OK, logApply.StatusCode);
        Assert.Equal(2, await db.TorrentReleases.CountAsync(release => release.TorrentMovieId == title.Id));
        var manualRetry = await db.ParseLogs.AsNoTracking()
            .SingleAsync(log => log.EventKind == "manual_retry");
        Assert.Equal("resolved", manualRetry.RetryState);
        Assert.Equal("manual_imdb_match", manualRetry.Decision);
        Assert.Equal(2, manualRetry.AttemptCount);

        using var repeatedApply = await host.Client.PostAsJsonAsync(
            $"/api/parse-logs/{parseLogId}/imdb-match",
            new ApplyImdbCandidateRequest(imdbId));
        Assert.Equal(HttpStatusCode.Conflict, repeatedApply.StatusCode);
        Assert.Equal(1, await db.ParseLogs.CountAsync(log => log.EventKind == "manual_retry"));
    }

    private sealed class StubOmdbClient(string imdbId) : IOmdbClient
    {
        public int SearchCalls { get; private set; }
        public int LastSearchPage { get; private set; }

        public Task<MetadataLookupResult> LookupAsync(
            string title,
            int? year,
            string sourceType,
            CancellationToken cancellationToken = default,
            OmdbRequestPurpose requestPurpose = OmdbRequestPurpose.RssIngestion,
            string? requestedImdbId = null) =>
            Task.FromResult(new MetadataLookupResult(
                MetadataLookupStatus.Found,
                new MetadataDetails(
                    "Quiet River", 2004, requestedImdbId ?? imdbId, "movie", "movie", "standard", null,
                    8.1m, 1200, 78m, ["Drama"], ["Canada"], "A. Director", "A quiet story.",
                    "https://example.test/quiet-river.jpg", "112 min", null, null),
                HttpAttempts: 1));

        public Task<MetadataSearchResult> SearchAsync(
            string title,
            string? sourceType,
            int page,
            CancellationToken cancellationToken = default,
            OmdbRequestPurpose requestPurpose = OmdbRequestPurpose.RssIngestion)
        {
            SearchCalls++;
            LastSearchPage = page;
            return Task.FromResult(new MetadataSearchResult(
                MetadataLookupStatus.Found,
                [new MetadataSearchCandidate("Quiet River", 2004, imdbId, "movie")],
                1,
                HttpAttempts: 1));
        }
    }

    private sealed class StubOmdbClientFactory(IOmdbClient client) : IOmdbClientFactory
    {
        public string? LastApiKey { get; private set; }
        public int LastDailyRequestLimit { get; private set; }

        public IOmdbClient Create(string apiKey, int dailyRequestLimit)
        {
            LastApiKey = apiKey;
            LastDailyRequestLimit = dailyRequestLimit;
            return client;
        }
    }
}