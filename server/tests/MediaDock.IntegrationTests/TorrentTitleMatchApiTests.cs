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
        var scanRun = new ScanRun
        {
            StartedAt = observedAt.AddMinutes(-10),
            FinishedAt = observedAt.AddMinutes(-9),
            Status = "succeeded",
            Trigger = "manual"
        };
        var settings = await db.Settings.SingleAsync();
        settings.OmdbApiKey = "test-omdb-key";
        settings.OmdbDailyRequestLimit = 40;
        settings.UpdatedAt = observedAt;
        db.Sources.Add(source);
        db.TorrentMovies.Add(title);
        db.ScanRuns.Add(scanRun);
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
            ScanRunId = scanRun.Id,
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

        using var catalogSearch = await host.Client.PostAsJsonAsync(
            $"/api/titles/{title.Id}/imdb-candidates",
            new ImdbCandidateSearchRequest("Quiet River (Director's Cut)"));
        Assert.Equal(HttpStatusCode.OK, catalogSearch.StatusCode);
        var catalogCandidates = await catalogSearch.Content.ReadFromJsonAsync<ImdbCandidateSearchResponse>();
        Assert.NotNull(catalogCandidates);
        Assert.Equal("Quiet River", Assert.Single(catalogCandidates.Candidates).Title);
        Assert.Equal(2004, catalogCandidates.Candidates[0].Year);
        Assert.Equal(imdbId, catalogCandidates.Candidates[0].ImdbId);
        Assert.Equal(1, omdbClient.SearchCalls);
        Assert.Equal("Quiet River (Director's Cut)", omdbClient.LastSearchTitle);
        Assert.Equal(1, omdbClient.LastSearchPage);
        Assert.Equal("test-omdb-key", omdbClientFactory.LastApiKey);
        Assert.Equal(40, omdbClientFactory.LastDailyRequestLimit);

        using var nextCatalogPage = await host.Client.PostAsync(
            $"/api/titles/{title.Id}/imdb-candidates?page=2",
            JsonContent.Create(new ImdbCandidateSearchRequest("Quiet River (Director's Cut)")));
        Assert.Equal(HttpStatusCode.OK, nextCatalogPage.StatusCode);
        Assert.Equal(2, omdbClient.LastSearchPage);

        using var catalogIdLookup = await host.Client.PostAsJsonAsync(
            $"/api/titles/{title.Id}/imdb-candidate",
            new LookupImdbCandidateRequest(imdbId));
        Assert.Equal(HttpStatusCode.OK, catalogIdLookup.StatusCode);
        var directCandidate = await catalogIdLookup.Content.ReadFromJsonAsync<ImdbCandidateSearchResponse>();
        Assert.NotNull(directCandidate);
        Assert.Equal(imdbId, Assert.Single(directCandidate.Candidates).ImdbId);

        using var catalogApply = await host.Client.PostAsJsonAsync(
            $"/api/titles/{title.Id}/imdb-match",
            new ApplyImdbCandidateRequest(imdbId));
        Assert.Equal(HttpStatusCode.OK, catalogApply.StatusCode);
        var storedTitle = await db.TorrentMovies.AsNoTracking().SingleAsync(movie => movie.Id == title.Id);
        Assert.Equal(imdbId, storedTitle.ImdbId);
        Assert.Equal("Quiet River", await db.ImdbTitles.Where(item => item.ImdbId == imdbId)
            .Select(item => item.TitleText).SingleAsync());

        using var logSearch = await host.Client.PostAsJsonAsync(
            $"/api/parse-logs/{parseLogId}/imdb-candidates",
            new ImdbCandidateSearchRequest("Quiet River alternate spelling"));
        Assert.Equal(HttpStatusCode.OK, logSearch.StatusCode);
        var logCandidates = await logSearch.Content.ReadFromJsonAsync<ImdbCandidateSearchResponse>();
        Assert.NotNull(logCandidates);
        Assert.Equal(imdbId, Assert.Single(logCandidates.Candidates).ImdbId);
        Assert.Equal(3, omdbClient.SearchCalls);

        using var logIdLookup = await host.Client.PostAsJsonAsync(
            $"/api/parse-logs/{parseLogId}/imdb-candidate",
            new LookupImdbCandidateRequest(imdbId));
        Assert.Equal(HttpStatusCode.OK, logIdLookup.StatusCode);

        using var logApply = await host.Client.PostAsJsonAsync(
            $"/api/parse-logs/{parseLogId}/imdb-match",
            new ApplyImdbCandidateRequest(imdbId));
        Assert.Equal(HttpStatusCode.OK, logApply.StatusCode);
        Assert.True(await db.TorrentReleases.AnyAsync(release => release.SourceId == source.Id
            && release.SourceItemKey == "entry:retry-1"
            && release.TorrentMovieId == title.Id));
        var manualRetry = await db.ParseLogs.AsNoTracking()
            .SingleAsync(log => log.EventKind == "manual_retry");
        Assert.Equal("resolved", manualRetry.RetryState);
        Assert.Equal("manual_imdb_match", manualRetry.Decision);
        Assert.Equal(2, manualRetry.AttemptCount);
        Assert.Equal(scanRun.Id, manualRetry.ScanRunId);

        var resolvedOriginal = await db.ParseLogs.AsNoTracking().SingleAsync(log => log.Id == parseLogId);
        Assert.Equal("resolved", resolvedOriginal.RetryState);
        Assert.Equal("found", resolvedOriginal.OmdbStatus);
        Assert.False(resolvedOriginal.Ignored);
        Assert.Equal("manual_imdb_match", resolvedOriginal.Decision);

        using var scopedLogsResponse = await host.Client.GetAsync(
            $"/api/parse-logs?scanRunId={scanRun.Id}&page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, scopedLogsResponse.StatusCode);
        var scopedLogs = await scopedLogsResponse.Content.ReadFromJsonAsync<MediaDock.Api.Common.PageResponse<MediaDock.Api.Operations.ParseLogResponse>>();
        Assert.NotNull(scopedLogs);
        Assert.Contains(scopedLogs.Items, log => log.Id == parseLogId
            && log.RetryState == "resolved"
            && log.OmdbStatus == "found"
            && !log.Ignored
            && log.Decision == "manual_imdb_match"
            && !log.CanRetryMatch);
        Assert.Contains(scopedLogs.Items, log => log.Id == manualRetry.Id && !log.CanRetryMatch);
        Assert.All(scopedLogs.Items.Where(log => log.SourceItemKey == "entry:retry-1"), log => Assert.False(log.CanRetryMatch));

        using var repeatedApply = await host.Client.PostAsJsonAsync(
            $"/api/parse-logs/{parseLogId}/imdb-match",
            new ApplyImdbCandidateRequest(imdbId));
        Assert.Equal(HttpStatusCode.Conflict, repeatedApply.StatusCode);
        Assert.Equal(1, await db.ParseLogs.CountAsync(log => log.EventKind == "manual_retry"));
    }

    [Fact]
    public async Task ApplyingParseLogMatchResolvesSameUrlLogsAndRejectsChangedNewerUrl()
    {
        const string imdbId = "tt12345678";
        await using var host = await CatalogApiTestHost.CreateAsync(
            "mediadock_torrent_title_match_duplicate_url_test",
            omdbClientFactory: new StubOmdbClientFactory(new StubOmdbClient(imdbId)));
        var db = host.Db;
        var observedAt = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        var source = new Source
        {
            StableKey = "movies-duplicate-url-test",
            Name = "Movies",
            FeedType = "movie",
            Url = "https://feed.rutracker.cc/movies.atom",
            IsEnabled = true
        };
        var scanRun = new ScanRun
        {
            StartedAt = observedAt.AddMinutes(-10),
            FinishedAt = observedAt.AddMinutes(-9),
            Status = "succeeded",
            Trigger = "manual"
        };
        var settings = await db.Settings.SingleAsync();
        settings.OmdbApiKey = "test-omdb-key";
        settings.OmdbDailyRequestLimit = 40;
        settings.UpdatedAt = observedAt;
        db.Sources.Add(source);
        db.ScanRuns.Add(scanRun);
        await db.SaveChangesAsync();

        var urls = new[]
        {
            "https://rutracker.org/forum/viewtopic.php?t=200",
            "http://www.rutracker.org/forum/viewtopic.php?start=0&t=0200",
            "https://rutracker.cc/forum/viewtopic.php?t=200",
            "https://www.rutracker.cc/forum/viewtopic.php?t=200"
        };
        var logs = urls.Select((url, index) => new ParseLog
        {
            ScanRunId = scanRun.Id,
            SourceId = source.Id,
            SourceItemKey = $"entry:duplicate-url-{index}",
            RawTitle = "Quiet River (2004)",
            FeedName = source.Name,
            ParsedSuccessfully = false,
            OmdbStatus = "not_requested",
            ProcessedAt = observedAt.AddMinutes(index - urls.Length),
            RetryState = "retryable",
            AttemptCount = index,
            FeedType = source.FeedType,
            ObservedAt = observedAt.AddMinutes(index - urls.Length),
            EventKind = "ingestion",
            FeedEntryId = $"duplicate-{index}",
            TorrentUrl = url
        }).ToArray();
        db.ParseLogs.AddRange(logs);
        await db.SaveChangesAsync();

        using var response = await host.Client.PostAsJsonAsync(
            $"/api/parse-logs/{logs[0].Id}/imdb-match",
            new ApplyImdbCandidateRequest(imdbId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updatedLogs = await db.ParseLogs.AsNoTracking()
            .Where(log => logs.Select(item => item.Id).Contains(log.Id))
            .ToListAsync();
        Assert.Equal(urls.Length, updatedLogs.Count);
        Assert.All(updatedLogs, log =>
        {
            Assert.Equal("resolved", log.RetryState);
            Assert.Equal("manual_imdb_match", log.Decision);
            Assert.False(log.Ignored);
        });
        Assert.Equal(1, await db.ParseLogs.CountAsync(log => log.EventKind == "manual_retry"));
        var release = await db.TorrentReleases.Include(item => item.TorrentMovie).SingleAsync();
        Assert.Equal(imdbId, release.TorrentMovie.ImdbId);
        Assert.Equal("https://rutracker.org/forum/viewtopic.php?t=200", release.TorrentUrl);

        using var historyResponse = await host.Client.GetAsync("/api/parse-logs?page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        var history = await historyResponse.Content.ReadFromJsonAsync<MediaDock.Api.Common.PageResponse<MediaDock.Api.Operations.ParseLogResponse>>();
        Assert.NotNull(history);
        Assert.All(history.Items.Where(log => logs.Select(item => item.Id).Contains(log.Id)),
            log => Assert.False(log.CanRetryMatch));

        var changedUrlLogs = new[]
        {
            new ParseLog
            {
                ScanRunId = scanRun.Id,
                SourceId = source.Id,
                SourceItemKey = "entry:changed-url",
                RawTitle = "Quiet River (2004)",
                FeedName = source.Name,
                OmdbStatus = "not_requested",
                ProcessedAt = observedAt.AddMinutes(-2),
                RetryState = "retryable",
                FeedType = source.FeedType,
                EventKind = "ingestion",
                TorrentUrl = "https://rutracker.org/forum/viewtopic.php?t=300"
            },
            new ParseLog
            {
                ScanRunId = scanRun.Id,
                SourceId = source.Id,
                SourceItemKey = "entry:changed-url",
                RawTitle = "Quiet River (2004)",
                FeedName = source.Name,
                OmdbStatus = "not_requested",
                ProcessedAt = observedAt.AddMinutes(-1),
                RetryState = "retryable",
                FeedType = source.FeedType,
                EventKind = "ingestion",
                TorrentUrl = "https://rutracker.org/forum/viewtopic.php?t=301"
            }
        };
        db.ParseLogs.AddRange(changedUrlLogs);
        await db.SaveChangesAsync();

        using var staleMatch = await host.Client.PostAsJsonAsync(
            $"/api/parse-logs/{changedUrlLogs[0].Id}/imdb-match",
            new ApplyImdbCandidateRequest(imdbId));
        Assert.Equal(HttpStatusCode.Conflict, staleMatch.StatusCode);
        Assert.All(await db.ParseLogs.AsNoTracking()
                .Where(log => changedUrlLogs.Select(item => item.Id).Contains(log.Id))
                .ToListAsync(),
            log => Assert.Equal("retryable", log.RetryState));
        Assert.Equal(1, await db.ParseLogs.CountAsync(log => log.EventKind == "manual_retry"));
    }

    [Fact]
    public async Task ApplyingParseLogMatchUsesImdbMetadataWhenFeedTitleCannotBeParsed()
    {
        const string imdbId = "tt12345678";
        await using var host = await CatalogApiTestHost.CreateAsync(
            "mediadock_torrent_title_match_unparsed_title_test",
            omdbClientFactory: new StubOmdbClientFactory(new StubOmdbClient(imdbId)));
        var db = host.Db;
        var observedAt = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        var source = new Source
        {
            StableKey = "movies-unparsed-title-test",
            Name = "Movies",
            FeedType = "movie",
            Url = "https://feed.rutracker.cc/movies.atom",
            IsEnabled = true
        };
        var scanRun = new ScanRun
        {
            StartedAt = observedAt.AddMinutes(-10),
            FinishedAt = observedAt.AddMinutes(-9),
            Status = "succeeded",
            Trigger = "manual"
        };
        var settings = await db.Settings.SingleAsync();
        settings.OmdbApiKey = "test-omdb-key";
        settings.OmdbDailyRequestLimit = 40;
        settings.UpdatedAt = observedAt;
        db.Sources.Add(source);
        db.ScanRuns.Add(scanRun);
        await db.SaveChangesAsync();

        var log = new ParseLog
        {
            ScanRunId = scanRun.Id,
            SourceId = source.Id,
            SourceItemKey = "entry:unparsed-title",
            RawTitle = string.Empty,
            FeedName = source.Name,
            ParsedSuccessfully = false,
            OmdbStatus = "not_requested",
            ErrorMessage = "empty_title",
            ProcessedAt = observedAt,
            RetryState = "terminal",
            FeedType = source.FeedType,
            EventKind = "ingestion",
            FeedEntryId = "unparsed-title",
            TorrentUrl = "https://rutracker.org/forum/viewtopic.php?t=400"
        };
        db.ParseLogs.Add(log);
        await db.SaveChangesAsync();

        using var response = await host.Client.PostAsJsonAsync(
            $"/api/parse-logs/{log.Id}/imdb-match",
            new ApplyImdbCandidateRequest(imdbId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var resolvedLog = await db.ParseLogs.AsNoTracking().SingleAsync(value => value.Id == log.Id);
        Assert.Equal("Quiet River", resolvedLog.ParsedTitle);
        Assert.Equal(2004, resolvedLog.ParsedYear);
        Assert.Equal("resolved", resolvedLog.RetryState);
        var release = await db.TorrentReleases.Include(item => item.TorrentMovie).SingleAsync();
        Assert.Equal("Quiet River", release.TorrentMovie.TitleText);
        Assert.Equal(2004, release.TorrentMovie.Year);
        Assert.Equal(imdbId, release.TorrentMovie.ImdbId);
    }

    private sealed class StubOmdbClient(string imdbId) : IOmdbClient
    {
        public int SearchCalls { get; private set; }
        public int LastSearchPage { get; private set; }
        public string? LastSearchTitle { get; private set; }

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
            LastSearchTitle = title;
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