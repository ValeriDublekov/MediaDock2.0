using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using MediaDock.Api.BackgroundJobs;
using MediaDock.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MediaDock.IntegrationTests;

[Trait("Category", "Api")]
public sealed class BackgroundJobExecutionTests
{
    [Fact]
    public async Task ApiHostedDispatcherCompletesManualScanAndAssociatesScanRun()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_background_scan_execution_test").Build();
        await postgres.StartAsync();

        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using (var db = new MediaDockDbContext(options))
        {
            await db.Database.MigrateAsync();
            db.Settings.Add(new Infrastructure.Persistence.Entities.AppSetting
            {
                Id = 1,
                OmdbApiKey = "synthetic-test-key",
                OmdbDailyRequestLimit = 10,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var factory = new BackgroundJobsApiFactory(connectionString);
        using var client = factory.CreateClient();
        using var acceptedResponse = await client.PostAsync("/api/background-jobs/scans", content: null);
        Assert.Equal(HttpStatusCode.Accepted, acceptedResponse.StatusCode);
        var accepted = await acceptedResponse.Content.ReadFromJsonAsync<BackgroundJobAcceptedResponse>();
        Assert.NotNull(accepted);

        BackgroundJobResponse? completed = null;
        for (var attempt = 0; attempt < 60; attempt++)
        {
            completed = await client.GetFromJsonAsync<BackgroundJobResponse>($"/api/background-jobs/{accepted.Id}");
            if (completed?.Status is "succeeded" or "partial" or "failed")
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        Assert.NotNull(completed);
        Assert.Equal("succeeded", completed.Status);
        Assert.NotNull(completed.ScanRunId);
        await using var verificationDb = new MediaDockDbContext(options);
        var scanRun = await verificationDb.ScanRuns.SingleAsync(run => run.Id == completed.ScanRunId);
        Assert.Equal("succeeded", scanRun.Status);
        Assert.Equal("manual", scanRun.Trigger);
    }

    [Fact]
    public async Task FailedEntryRecheckJobReprocessesStoredParseLogPayload()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_failed_recheck_execution_test").Build();
        await postgres.StartAsync();

        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using (var db = new MediaDockDbContext(options))
        {
            await db.Database.MigrateAsync();
            var processedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
            var source = new Infrastructure.Persistence.Entities.Source
            {
                StableKey = "failed-recheck-source",
                Name = "Movies",
                FeedType = "movie",
                Url = "https://feed.rutracker.cc/success.atom"
            };
            db.Sources.Add(source);
            db.Settings.Add(new Infrastructure.Persistence.Entities.AppSetting
            {
                Id = 1,
                OmdbApiKey = "synthetic-test-key",
                OmdbDailyRequestLimit = 10,
                UpdatedAt = processedAt
            });
            await db.SaveChangesAsync();
            db.ParseLogs.Add(new Infrastructure.Persistence.Entities.ParseLog
            {
                SourceId = source.Id,
                SourceItemKey = "entry:stored-recheck-item",
                RawTitle = "The Matrix (1999) [1080p]",
                FeedName = source.Name,
                ParsedSuccessfully = false,
                OmdbStatus = "transport_error",
                Ignored = true,
                ErrorMessage = "transport_error",
                ProcessedAt = processedAt,
                RetryState = "retryable",
                AttemptCount = 1,
                FeedType = source.FeedType,
                SourcePublishedAt = processedAt,
                FeedEntryId = "stored-recheck-item",
                TorrentUrl = "https://rutracker.org/forum/viewtopic.php?t=191"
            });
            await db.SaveChangesAsync();
        }

        using var factory = new BackgroundJobsApiFactory(connectionString, new OmdbSuccessHandler());
        using var client = factory.CreateClient();
        using var acceptedResponse = await client.PostAsync("/api/background-jobs/recheck-failed", content: null);
        Assert.Equal(HttpStatusCode.Accepted, acceptedResponse.StatusCode);
        var accepted = await acceptedResponse.Content.ReadFromJsonAsync<BackgroundJobAcceptedResponse>();
        Assert.NotNull(accepted);

        BackgroundJobResponse? completed = null;
        for (var attempt = 0; attempt < 60; attempt++)
        {
            completed = await client.GetFromJsonAsync<BackgroundJobResponse>($"/api/background-jobs/{accepted.Id}");
            if (completed?.Status is "succeeded" or "partial" or "failed")
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        Assert.NotNull(completed);
    Assert.True(completed.Status == "succeeded", $"{completed.ErrorCode}: {completed.ResultSummary?.GetRawText()}");
        Assert.NotNull(completed.ScanRunId);
        await using var verificationDb = new MediaDockDbContext(options);
        var occurrence = await verificationDb.Occurrences.SingleAsync();
        Assert.Equal("entry:stored-recheck-item", occurrence.SourceItemKey);
        Assert.Equal("stored-recheck-item", occurrence.FeedEntryId);
        Assert.Equal("https://rutracker.org/forum/viewtopic.php?t=191", occurrence.TorrentUrl);
        Assert.Equal("resolved", (await verificationDb.ParseLogs.OrderByDescending(log => log.Id).FirstAsync()).RetryState);
        Assert.Equal("succeeded", (await verificationDb.ScanRuns.SingleAsync()).Status);
    }

    [Fact]
    public async Task ApiHostedDispatcherExecutesQueuedOscarImportAndCleansUploadBytes()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_background_job_execution_test").Build();
        await postgres.StartAsync();

        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using (var db = new MediaDockDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        using var factory = new BackgroundJobsApiFactory(connectionString);
        using var client = factory.CreateClient();
        using var upload = new MultipartFormDataContent();
        var csv = new ByteArrayContent(Encoding.UTF8.GetBytes("""
            Ceremony,Year,Class,CanonicalCategory,Category,Film,FilmId,Name,Nominees,NomineeIds,Winner,Detail
            98,2025,Title,BEST PICTURE,BEST PICTURE,Queued Film,tt12345678,Producers,Producers,,True,
            """));
        csv.Headers.ContentType = MediaTypeHeaderValue.Parse("text/csv");
        upload.Add(csv, "File", "queued-film.csv");
        upload.Add(new StringContent("1980"), "YearAfter");

        using var acceptedResponse = await client.PostAsync("/api/background-jobs/oscar-import", upload);
        Assert.Equal(HttpStatusCode.Accepted, acceptedResponse.StatusCode);
        var accepted = await acceptedResponse.Content.ReadFromJsonAsync<BackgroundJobAcceptedResponse>();
        Assert.NotNull(accepted);

        BackgroundJobResponse? completed = null;
        for (var attempt = 0; attempt < 60; attempt++)
        {
            completed = await client.GetFromJsonAsync<BackgroundJobResponse>($"/api/background-jobs/{accepted.Id}");
            if (completed?.Status is "succeeded" or "partial" or "failed")
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        Assert.NotNull(completed);
        Assert.Equal("succeeded", completed.Status);
        Assert.Equal("queued-film.csv", completed.InputFileName);

        await using var verificationDb = new MediaDockDbContext(options);
        Assert.Equal(1, await verificationDb.OscarFilms.CountAsync());
        var storedJob = await verificationDb.BackgroundJobs.SingleAsync(job => job.Id == accepted.Id);
        Assert.Null(storedJob.InputBytes);
    }

    [Fact]
    public async Task ApiHostedDispatcherExecutesQueuedGoldenGlobeImportAndCleansUploadBytes()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_golden_globe_job_execution_test").Build();
        await postgres.StartAsync();

        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using (var db = new MediaDockDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        using var factory = new BackgroundJobsApiFactory(connectionString);
        using var client = factory.CreateClient();
        using var upload = new MultipartFormDataContent();
        var csv = new ByteArrayContent(Encoding.UTF8.GetBytes(
            "nominee_type,year,winner,award,title\nfilm,2025,true,Best Picture,Queued Golden Globe Film\n"));
        csv.Headers.ContentType = MediaTypeHeaderValue.Parse("text/csv");
        upload.Add(csv, "File", "golden-globes.csv");
        upload.Add(new StringContent("1980"), "YearAfter");

        using var acceptedResponse = await client.PostAsync("/api/background-jobs/golden-globe-import", upload);
        Assert.Equal(HttpStatusCode.Accepted, acceptedResponse.StatusCode);
        var accepted = await acceptedResponse.Content.ReadFromJsonAsync<BackgroundJobAcceptedResponse>();
        Assert.NotNull(accepted);

        BackgroundJobResponse? completed = null;
        for (var attempt = 0; attempt < 60; attempt++)
        {
            completed = await client.GetFromJsonAsync<BackgroundJobResponse>($"/api/background-jobs/{accepted.Id}");
            if (completed?.Status is "succeeded" or "partial" or "failed")
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        Assert.NotNull(completed);
        Assert.Equal("succeeded", completed.Status);
        Assert.Equal("golden-globes.csv", completed.InputFileName);

        await using var verificationDb = new MediaDockDbContext(options);
        var nomination = await verificationDb.GoldenGlobeNominations.Include(row => row.Award).SingleAsync();
        Assert.Equal("Queued Golden Globe Film", nomination.Title);
        Assert.Equal("Best Picture", nomination.Award.Name);
        var storedJob = await verificationDb.BackgroundJobs.SingleAsync(job => job.Id == accepted.Id);
        Assert.Null(storedJob.InputBytes);
    }

    private sealed class BackgroundJobsApiFactory(
        string connectionString,
        HttpMessageHandler? messageHandler = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MediaDock"] = connectionString
                }));
            builder.ConfigureTestServices(services =>
            {
                if (messageHandler is null)
                {
                    return;
                }

                services.RemoveAll<HttpClient>();
                services.AddSingleton(new HttpClient(messageHandler) { Timeout = Timeout.InfiniteTimeSpan });
            });
        }
    }

    private sealed class OmdbSuccessHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"Response":"True","Title":"The Matrix","Year":"1999","imdbID":"tt0133093","Type":"movie","imdbRating":"8.7","imdbVotes":"1000","Metascore":"73","Genre":"Action, Sci-Fi","Country":"USA","Director":"Example Director","Plot":"Example plot","Poster":"N/A","Runtime":"136 min","Awards":"None","BoxOffice":"$1"}
                    """, Encoding.UTF8, "application/json")
            });
    }
}