using System.Net;
using System.Net.Http.Json;
using MediaDock.Api.Catalog;
using MediaDock.Api.Common;
using MediaDock.Api.Health;
using MediaDock.Api.Operations;
using MediaDock.Api.Sources;
using MediaDock.Api.Versioning;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace MediaDock.IntegrationTests;

[Trait("Category", "Api")]
public sealed class CatalogApiTests
{
    [Fact]
    public async Task CatalogApiValidatesPaginatesAndQueriesPostgresBackedResources()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_api_test").Build();
        await postgres.StartAsync();

        var connectionString = postgres.GetConnectionString();
        var dbOptions = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using var db = new MediaDockDbContext(dbOptions);
        await db.Database.MigrateAsync();

        using var factory = new ApiFactory(connectionString, new Dictionary<string, string?>
        {
            ["MediaDock:Version"] = "2026.10.01+abc1234",
            ["MediaDock:CommitSha"] = "0123456789abcdef0123456789abcdef01234567",
            ["MediaDock:CommitDateUtc"] = "2026-10-01T12:30:00Z"
        });
        using var client = factory.CreateClient();

        using var versionResponse = await client.GetAsync("/api/version");
        Assert.Equal(HttpStatusCode.OK, versionResponse.StatusCode);
        Assert.Equal(new VersionResponse(
            "2026.10.01+abc1234",
            "0123456789abcdef0123456789abcdef01234567",
            new DateTimeOffset(2026, 10, 1, 12, 30, 0, TimeSpan.Zero)),
            await versionResponse.Content.ReadFromJsonAsync<VersionResponse>());

        using var liveResponse = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);
        Assert.Equal("ok", (await liveResponse.Content.ReadFromJsonAsync<HealthResponse>())?.Status);

        using var readyResponse = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, readyResponse.StatusCode);

        using var openApiResponse = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, openApiResponse.StatusCode);
        Assert.Contains("/api/catalog", await openApiResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var emptyResponse = await client.GetAsync("/api/catalog");
        Assert.Equal(HttpStatusCode.OK, emptyResponse.StatusCode);
        var emptyCatalog = await emptyResponse.Content.ReadFromJsonAsync<PageResponse<CatalogTitleResponse>>();
        Assert.NotNull(emptyCatalog);
        Assert.Empty(emptyCatalog.Items);
        Assert.Equal(0, emptyCatalog.TotalCount);

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
        var olderTitle = CreateTitle("The Matrix", "the matrix", 1999, "movie", ["Action", "Sci-Fi"]);
        var newerTitle = CreateTitle("The Matrix Reloaded", "the matrix reloaded", 2003, "movie", ["Action", "Sci-Fi"]);
        var seriesTitle = CreateTitle("Example Series", "example series", 2010, "series", ["Drama"]);
        db.Sources.Add(source);
        db.Titles.AddRange(olderTitle, newerTitle, seriesTitle);
        await db.SaveChangesAsync();

        var firstSeen = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        db.Occurrences.AddRange(
            CreateOccurrence(olderTitle.Id, source.Id, "matrix-1999", "The Matrix (1999)", firstSeen),
            CreateOccurrence(newerTitle.Id, source.Id, "matrix-2003", "The Matrix Reloaded (2003)", firstSeen.AddDays(1)));
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
        db.Occurrences.AddRange(
            CreateOccurrence(seriesTitle.Id, completeSeriesSource.Id, "series-complete", "Example Series", firstSeen.AddDays(2), "series_complete", "Series"),
            CreateOccurrence(seriesTitle.Id, ongoingSeriesSource.Id, "series-ongoing", "Example Series", firstSeen.AddDays(3), "series_ongoing", "Series in progress"));
        db.ParseLogs.AddRange(
            new ParseLog
            {
                SourceId = source.Id,
                SourceItemKey = "matrix-1999",
                RawTitle = "The Matrix (1999)",
                FeedName = "Movies",
                ParsedSuccessfully = true,
                ParsedTitle = "The Matrix",
                ParsedYear = 1999,
                OmdbStatus = "found",
                Ignored = false,
                ProcessedAt = firstSeen,
                RetryState = "resolved"
            },
            new ParseLog
            {
                SourceId = source.Id,
                SourceItemKey = "bad-entry",
                RawTitle = "Malformed entry",
                FeedName = "Movies",
                ParsedSuccessfully = false,
                OmdbStatus = "not_requested",
                Ignored = true,
                IgnoreReason = "malformed_entry",
                ProcessedAt = firstSeen.AddMinutes(1),
                RetryState = "terminal"
            });
        db.ScanRuns.Add(new ScanRun
        {
            StartedAt = firstSeen,
            FinishedAt = firstSeen.AddMinutes(1),
            Status = "succeeded",
            Trigger = "local",
            FeedsProcessed = 1,
            EntriesSeen = 2
        });
        await db.SaveChangesAsync();

        using var catalogResponse = await client.GetAsync(
            "/api/catalog?page=1&pageSize=1&search=matrix&mediaType=movie&yearFrom=1990&yearTo=2005&genre=Sci-Fi&country=USA");
        Assert.Equal(HttpStatusCode.OK, catalogResponse.StatusCode);
        var firstPage = await catalogResponse.Content.ReadFromJsonAsync<PageResponse<CatalogTitleResponse>>();
        Assert.NotNull(firstPage);
        Assert.Equal(2, firstPage.TotalCount);
        Assert.Equal(2, firstPage.TotalPages);
        Assert.Equal("The Matrix Reloaded", Assert.Single(firstPage.Items).Title);
        Assert.Equal("tt2003", Assert.Single(firstPage.Items).ImdbId);

        using var inProgressSeriesResponse = await client.GetAsync("/api/catalog?feedTypes=series_ongoing");
        var inProgressSeries = await inProgressSeriesResponse.Content.ReadFromJsonAsync<PageResponse<CatalogTitleResponse>>();
        Assert.NotNull(inProgressSeries);
        Assert.Equal("Example Series", Assert.Single(inProgressSeries.Items).Title);

        using var mainFeedsResponse = await client.GetAsync("/api/catalog?feedTypes=movie,series_complete,series_ongoing");
        var mainFeeds = await mainFeedsResponse.Content.ReadFromJsonAsync<PageResponse<CatalogTitleResponse>>();
        Assert.NotNull(mainFeeds);
        Assert.Equal(3, mainFeeds.TotalCount);

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

        using var missingTitleResponse = await client.GetAsync("/api/titles/999999");
        Assert.Equal(HttpStatusCode.NotFound, missingTitleResponse.StatusCode);
        Assert.NotNull(await missingTitleResponse.Content.ReadFromJsonAsync<ProblemDetails>());

        using var occurrencesResponse = await client.GetAsync($"/api/titles/{olderTitle.Id}/occurrences?page=1&pageSize=10");
        var occurrences = await occurrencesResponse.Content.ReadFromJsonAsync<PageResponse<OccurrenceResponse>>();
        Assert.NotNull(occurrences);
        Assert.Equal(1, occurrences.TotalCount);
        Assert.Equal("Movies", Assert.Single(occurrences.Items).SourceName);

        using var sourcesResponse = await client.GetAsync("/api/sources");
        var profiles = await sourcesResponse.Content.ReadFromJsonAsync<List<SourceProfileResponse>>();
        Assert.NotNull(profiles);
        Assert.Equal(new[] { "movie", "series_complete", "series_ongoing" }, profiles.Select(profile => profile.Id));
        Assert.Equal("Movies", profiles[0].Name);
        Assert.Equal("https://feed.rutracker.cc/movies.atom", Assert.Single(profiles[0].Urls).Url);

        using var firstCompleteResponse = await client.PostAsJsonAsync(
            "/api/sources/series_complete/urls",
            new { Url = "https://feed.rutracker.cc/complete.atom", FeedType = "movie", Name = "Injected name" });
        Assert.Equal(HttpStatusCode.Created, firstCompleteResponse.StatusCode);
        var firstComplete = await firstCompleteResponse.Content.ReadFromJsonAsync<SourceUrlResponse>();
        Assert.NotNull(firstComplete);

        using var secondCompleteResponse = await client.PostAsJsonAsync(
            "/api/sources/series_complete/urls",
            new SourceUrlRequest { Url = "https://feed.rutracker.cc/complete-extra.atom" });
        Assert.Equal(HttpStatusCode.Created, secondCompleteResponse.StatusCode);
        var secondComplete = await secondCompleteResponse.Content.ReadFromJsonAsync<SourceUrlResponse>();
        Assert.NotNull(secondComplete);

        using var ongoingResponse = await client.PostAsJsonAsync(
            "/api/sources/series_ongoing/urls",
            new SourceUrlRequest { Url = "https://feed.rutracker.cc/ongoing.atom" });
        Assert.Equal(HttpStatusCode.Created, ongoingResponse.StatusCode);

        using var replaceCompleteResponse = await client.PutAsJsonAsync(
            $"/api/sources/series_complete/urls/{firstComplete.Id}",
            new { Url = "https://feed.rutracker.cc/complete-replaced.atom", FeedType = "movie" });
        Assert.Equal(HttpStatusCode.OK, replaceCompleteResponse.StatusCode);
        Assert.Equal(
            "https://feed.rutracker.cc/complete-replaced.atom",
            (await replaceCompleteResponse.Content.ReadFromJsonAsync<SourceUrlResponse>())?.Url);

        using var wrongProfileResponse = await client.PutAsJsonAsync(
            $"/api/sources/movie/urls/{firstComplete.Id}",
            new SourceUrlRequest { Url = "https://feed.rutracker.cc/should-not-move.atom" });
        Assert.Equal(HttpStatusCode.NotFound, wrongProfileResponse.StatusCode);

        using var invalidProfileResponse = await client.PostAsJsonAsync(
            "/api/sources/series/urls",
            new SourceUrlRequest { Url = "https://feed.rutracker.cc/invalid-profile.atom" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidProfileResponse.StatusCode);

        using var invalidUrlResponse = await client.PostAsJsonAsync(
            "/api/sources/movie/urls",
            new SourceUrlRequest { Url = "https://example.test/feed.atom" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidUrlResponse.StatusCode);
        Assert.NotNull(await invalidUrlResponse.Content.ReadFromJsonAsync<ValidationProblemDetails>());

        using var duplicateUrlResponse = await client.PostAsJsonAsync(
            "/api/sources/movie/urls",
            new SourceUrlRequest { Url = "https://feed.rutracker.cc/movies.atom" });
        Assert.Equal(HttpStatusCode.Conflict, duplicateUrlResponse.StatusCode);

        using var removeUrlResponse = await client.DeleteAsync(
            $"/api/sources/series_complete/urls/{secondComplete.Id}");
        Assert.Equal(HttpStatusCode.NoContent, removeUrlResponse.StatusCode);

        using var moveRemovedUrlResponse = await client.PostAsJsonAsync(
            "/api/sources/series_ongoing/urls",
            new SourceUrlRequest { Url = "https://feed.rutracker.cc/complete-extra.atom" });
        Assert.Equal(HttpStatusCode.Created, moveRemovedUrlResponse.StatusCode);
        Assert.Equal(secondComplete.Id,
            (await moveRemovedUrlResponse.Content.ReadFromJsonAsync<SourceUrlResponse>())?.Id);

        using var groupedSourcesResponse = await client.GetAsync("/api/sources");
        var groupedProfiles = await groupedSourcesResponse.Content.ReadFromJsonAsync<List<SourceProfileResponse>>();
        Assert.NotNull(groupedProfiles);
        Assert.Equal("https://feed.rutracker.cc/complete-replaced.atom", Assert.Single(groupedProfiles[1].Urls).Url);
        Assert.Equal(
            new[] { "https://feed.rutracker.cc/complete-extra.atom", "https://feed.rutracker.cc/ongoing.atom" },
            groupedProfiles[2].Urls.Select(url => url.Url).OrderBy(url => url));

        using var settingsResponse = await client.GetAsync("/api/settings");
        var defaultSettings = await settingsResponse.Content.ReadFromJsonAsync<SettingsResponse>();
        Assert.NotNull(defaultSettings);
        Assert.Empty(defaultSettings.ExcludedGenres);
        Assert.Null(defaultSettings.UpdatedAt);

        using var updateSettingsResponse = await client.PutAsJsonAsync("/api/settings", new UpdateSettingsRequest
        {
            ExcludedGenres = ["Horror"],
            ExcludedCountries = ["USA"],
            MinMovieRating = 6.5m,
            MinSeriesRating = 7m,
            MinImdbVotes = 1000
        });
        Assert.Equal(HttpStatusCode.OK, updateSettingsResponse.StatusCode);
        Assert.Equal(6.5m, (await updateSettingsResponse.Content.ReadFromJsonAsync<SettingsResponse>())?.MinMovieRating);

        using var invalidSettingsResponse = await client.PutAsJsonAsync("/api/settings", new UpdateSettingsRequest
        {
            MinMovieRating = 11,
            MinSeriesRating = 0,
            MinImdbVotes = 0
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidSettingsResponse.StatusCode);
        Assert.NotNull(await invalidSettingsResponse.Content.ReadFromJsonAsync<ValidationProblemDetails>());

        using var defaultProviderSettingsResponse = await client.GetAsync("/api/settings/providers/omdb");
        var defaultProviderSettings = await defaultProviderSettingsResponse.Content
            .ReadFromJsonAsync<ProviderSettingsResponse>();
        Assert.NotNull(defaultProviderSettings);
        Assert.False(defaultProviderSettings.OmdbApiKeyConfigured);

        var currentUtcDate = DateOnly.FromDateTime(DateTime.UtcNow);
        db.OmdbDailyUsage.AddRange(
            new OmdbDailyUsage
            {
                UtcDate = currentUtcDate,
                TotalRequests = 12,
                OscarRequests = 3,
                DailyRequestLimitReached = true,
                ProviderQuotaExceeded = true,
                LastErrorCode = "quota_exceeded"
            },
            new OmdbDailyUsage
            {
                UtcDate = currentUtcDate.AddDays(-40),
                TotalRequests = 1
            });
        await db.SaveChangesAsync();

        using var omdbUsageResponse = await client.GetAsync("/api/settings/providers/omdb/usage");
        var omdbUsage = await omdbUsageResponse.Content.ReadFromJsonAsync<List<OmdbDailyUsageResponse>>();
        Assert.NotNull(omdbUsage);
        var currentUsage = Assert.Single(omdbUsage);
        Assert.Equal(currentUtcDate, currentUsage.UtcDate);
        Assert.Equal(12, currentUsage.TotalRequests);
        Assert.Equal(9, currentUsage.RssRequests);
        Assert.Equal(3, currentUsage.OscarRequests);
        Assert.True(currentUsage.DailyRequestLimitReached);
        Assert.True(currentUsage.ProviderQuotaExceeded);
        Assert.Equal("quota_exceeded", currentUsage.LastErrorCode);

        using var invalidProviderSettingsResponse = await client.PutAsJsonAsync(
            "/api/settings/providers/omdb",
            new UpdateProviderSettingsRequest
            {
                OmdbApiKey = "test-omdb-key-value"
            });
        Assert.Equal(HttpStatusCode.BadRequest, invalidProviderSettingsResponse.StatusCode);
        Assert.NotNull(await invalidProviderSettingsResponse.Content.ReadFromJsonAsync<ValidationProblemDetails>());

        const string testOmdbApiKey = "test-omdb-key-value";
        using var updateProviderSettingsResponse = await client.PutAsJsonAsync(
            "/api/settings/providers/omdb",
            new UpdateProviderSettingsRequest
            {
                OmdbApiKey = testOmdbApiKey,
                OmdbDailyRequestLimit = 20
            });
        Assert.Equal(HttpStatusCode.OK, updateProviderSettingsResponse.StatusCode);
        var providerSettingsJson = await updateProviderSettingsResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(testOmdbApiKey, providerSettingsJson);
        var savedProviderSettings = await updateProviderSettingsResponse.Content
            .ReadFromJsonAsync<ProviderSettingsResponse>();
        Assert.NotNull(savedProviderSettings);
        Assert.True(savedProviderSettings.OmdbApiKeyConfigured);
        Assert.Equal(20, savedProviderSettings.OmdbDailyRequestLimit);

        db.ChangeTracker.Clear();
        var providerSettingsInDatabase = await db.Settings.AsNoTracking().SingleAsync();
        Assert.Equal(1, providerSettingsInDatabase.Id);
        Assert.Equal(testOmdbApiKey, providerSettingsInDatabase.OmdbApiKey);

        using var clearProviderSettingsResponse = await client.PutAsJsonAsync(
            "/api/settings/providers/omdb",
            new UpdateProviderSettingsRequest
            {
                ClearOmdbApiKey = true,
                OmdbDailyRequestLimit = 20
            });
        Assert.Equal(HttpStatusCode.OK, clearProviderSettingsResponse.StatusCode);
        Assert.False((await clearProviderSettingsResponse.Content
            .ReadFromJsonAsync<ProviderSettingsResponse>())?.OmdbApiKeyConfigured);

        using var logsResponse = await client.GetAsync("/api/parse-logs?page=1&pageSize=1&ignored=true");
        var logs = await logsResponse.Content.ReadFromJsonAsync<PageResponse<ParseLogResponse>>();
        Assert.NotNull(logs);
        Assert.Equal(1, logs.TotalCount);
        Assert.True(Assert.Single(logs.Items).Ignored);

        using var runsResponse = await client.GetAsync("/api/scan-runs?page=1&pageSize=10&status=succeeded");
        var runs = await runsResponse.Content.ReadFromJsonAsync<PageResponse<ScanRunResponse>>();
        Assert.NotNull(runs);
        Assert.Equal("succeeded", Assert.Single(runs.Items).Status);
    }

    private static Title CreateTitle(string title, string normalizedTitle, int year, string mediaType, string[] genres)
    {
        var seenAt = new DateTimeOffset(year, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return new Title
        {
            TitleText = title,
            NormalizedTitle = normalizedTitle,
            Year = year,
            MediaType = mediaType,
            SourceType = mediaType,
            ContentKind = "standard",
            ImdbId = $"tt{year}",
            ImdbRating = 8.0m,
            ImdbVotes = 1000,
            Genres = genres,
            Countries = ["USA"],
            FirstSeenAt = seenAt,
            LastSeenAt = seenAt,
            UpdatedAt = seenAt
        };
    }

    private static Occurrence CreateOccurrence(
        long titleId,
        long sourceId,
        string sourceItemKey,
        string rawTitle,
        DateTimeOffset lastSeenAt,
        string feedType = "movie",
        string sourceFeedName = "Movies") => new()
    {
        TitleId = titleId,
        SourceId = sourceId,
        SourceItemKey = sourceItemKey,
        TorrentUrl = "https://rutracker.org/forum/viewtopic.php?t=1",
        RawTitle = rawTitle,
        SourceFeedName = sourceFeedName,
        FeedType = feedType,
        FirstSeenAt = lastSeenAt,
        LastSeenAt = lastSeenAt
    };

    private sealed class ApiFactory(
        string connectionString,
        IReadOnlyDictionary<string, string?>? buildMetadata = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var values = new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MediaDock"] = connectionString
                };
                if (buildMetadata is not null)
                {
                    foreach (var (key, value) in buildMetadata)
                    {
                        values[key] = value;
                    }
                }

                configuration.AddInMemoryCollection(values);
            });
        }
    }
}
