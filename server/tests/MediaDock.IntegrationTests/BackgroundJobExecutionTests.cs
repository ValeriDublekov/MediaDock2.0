using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using MediaDock.Api.BackgroundJobs;
using MediaDock.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

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

    private sealed class BackgroundJobsApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MediaDock"] = connectionString
                }));
        }
    }
}