using System.Net;
using System.Net.Http.Json;
using MediaDock.Api.Common;
using MediaDock.Api.Operations;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.IntegrationTests;

[Trait("Category", "Api")]
public sealed class CatalogOperationsApiTests
{
    [Fact]
    public async Task ParseLogsAndScanRunsCanBeQueried()
    {
        await using var host = await CatalogApiTestHost.CreateAsync("mediadock_catalog_operations_api_test");
        var db = host.Db;
        var client = host.Client;
        var source = new Source
        {
            StableKey = "movies-main",
            Name = "Movies",
            FeedType = "movie",
            Url = "https://feed.rutracker.cc/movies.atom",
            IsEnabled = true
        };
        db.Sources.Add(source);
        await db.SaveChangesAsync();

        var firstSeen = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
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
                LookupTitles = ["The Matrix"],
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
            },
            new ParseLog
            {
                SourceId = source.Id,
                SourceItemKey = "matchable-entry",
                RawTitle = "Example Film (2024)",
                FeedName = "Movies",
                ParsedSuccessfully = true,
                ParsedTitle = "Example Film",
                ParsedYear = 2024,
                LookupTitles = ["Example Film"],
                OmdbStatus = "confirmed_not_found",
                Ignored = false,
                ProcessedAt = firstSeen.AddMinutes(2),
                RetryState = "terminal",
                FeedType = source.FeedType,
                EventKind = "ingestion",
                TorrentUrl = "https://rutracker.org/forum/viewtopic.php?t=100"
            },
            new ParseLog
            {
                SourceId = source.Id,
                RawTitle = string.Empty,
                FeedName = "Movies",
                ParsedSuccessfully = false,
                OmdbStatus = "feed_error",
                Ignored = false,
                IgnoreReason = "feed_error",
                ProcessedAt = firstSeen.AddMinutes(3),
                RetryState = "retryable",
                FeedType = source.FeedType,
                EventKind = "feed"
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

        using var logsResponse = await client.GetAsync("/api/parse-logs?page=1&pageSize=1&ignored=true");
        var logs = await logsResponse.Content.ReadFromJsonAsync<PageResponse<ParseLogResponse>>();
        Assert.NotNull(logs);
        Assert.Equal(1, logs.TotalCount);
        Assert.True(Assert.Single(logs.Items).Ignored);

        using var resolvedLogsResponse = await client.GetAsync(
            "/api/parse-logs?page=1&pageSize=1&ignored=false&search=The%20Matrix");
        var resolvedLogs = await resolvedLogsResponse.Content.ReadFromJsonAsync<PageResponse<ParseLogResponse>>();
        Assert.NotNull(resolvedLogs);
        Assert.Equal(new[] { "The Matrix" }, Assert.Single(resolvedLogs.Items).LookupTitles);

        using var allLogsResponse = await client.GetAsync("/api/parse-logs?page=1&pageSize=10");
        var allLogs = await allLogsResponse.Content.ReadFromJsonAsync<PageResponse<ParseLogResponse>>();
        Assert.NotNull(allLogs);
        Assert.True(allLogs.Items.Single(log => log.SourceItemKey == "matchable-entry").CanRetryMatch);
        Assert.False(allLogs.Items.Single(log => log.EventKind == "feed").CanRetryMatch);

        using var runsResponse = await client.GetAsync("/api/scan-runs?page=1&pageSize=10&status=succeeded");
        var runs = await runsResponse.Content.ReadFromJsonAsync<PageResponse<ScanRunResponse>>();
        Assert.NotNull(runs);
        Assert.Equal("succeeded", Assert.Single(runs.Items).Status);
    }

    [Fact]
    public async Task OldOperationalHistoryCanBeDeletedWithoutRemovingRecentOrActiveRecords()
    {
        await using var host = await CatalogApiTestHost.CreateAsync("mediadock_history_cleanup_api_test");
        var now = DateTimeOffset.UtcNow;
        var oldRun = new ScanRun
        {
            StartedAt = now.AddDays(-40),
            FinishedAt = now.AddDays(-40).AddMinutes(1),
            Status = "succeeded",
            Trigger = "manual"
        };
        var recentRun = new ScanRun
        {
            StartedAt = now.AddDays(-20),
            FinishedAt = now.AddDays(-20).AddMinutes(1),
            Status = "succeeded",
            Trigger = "manual"
        };
        var activeRun = new ScanRun
        {
            StartedAt = now.AddDays(-40),
            Status = "running",
            Trigger = "manual"
        };
        host.Db.ScanRuns.AddRange(oldRun, recentRun, activeRun);
        await host.Db.SaveChangesAsync();

        host.Db.ParseLogs.AddRange(
            new ParseLog
            {
                ScanRunId = oldRun.Id,
                RawTitle = "Old entry",
                FeedName = "Movies",
                OmdbStatus = "not_requested",
                ProcessedAt = now.AddDays(-40),
                RetryState = "terminal"
            },
            new ParseLog
            {
                ScanRunId = recentRun.Id,
                RawTitle = "Recent entry",
                FeedName = "Movies",
                OmdbStatus = "not_requested",
                ProcessedAt = now.AddDays(-20),
                RetryState = "terminal"
            },
            new ParseLog
            {
                ScanRunId = activeRun.Id,
                RawTitle = "Active scan entry",
                FeedName = "Movies",
                OmdbStatus = "not_requested",
                ProcessedAt = now.AddDays(-40),
                RetryState = "terminal"
            });
        var oldJob = new BackgroundJob
        {
            JobType = "rss_scan",
            Trigger = "manual",
            Status = "succeeded",
            EnqueuedAt = now.AddDays(-40),
            StartedAt = now.AddDays(-40),
            FinishedAt = now.AddDays(-40).AddMinutes(1),
            ScanRunId = oldRun.Id
        };
        host.Db.BackgroundJobs.Add(oldJob);
        await host.Db.SaveChangesAsync();

        using var response = await host.Client.DeleteAsync("/api/operation-history?olderThanDays=30");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<OperationalHistoryCleanupResponse>();
        Assert.NotNull(result);
        Assert.Equal(30, result.OlderThanDays);
        Assert.Equal(1, result.DeletedScanRuns);
        Assert.Equal(1, result.DeletedParseLogs);
        Assert.False(await host.Db.ScanRuns.AnyAsync(run => run.Id == oldRun.Id));
        Assert.True(await host.Db.ScanRuns.AnyAsync(run => run.Id == recentRun.Id));
        Assert.True(await host.Db.ScanRuns.AnyAsync(run => run.Id == activeRun.Id));
        Assert.Equal(
            new[] { "Active scan entry", "Recent entry" },
            await host.Db.ParseLogs.OrderBy(log => log.RawTitle).Select(log => log.RawTitle).ToArrayAsync());
        Assert.Null(await host.Db.BackgroundJobs.AsNoTracking()
            .Where(job => job.Id == oldJob.Id)
            .Select(job => job.ScanRunId)
            .SingleAsync());
    }

    [Fact]
    public async Task OperationalHistoryCleanupRejectsInvalidRetentionPeriods()
    {
        await using var host = await CatalogApiTestHost.CreateAsync("mediadock_history_cleanup_validation_api_test");

        using var response = await host.Client.DeleteAsync("/api/operation-history?olderThanDays=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}