using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace MediaDock.IntegrationTests;

[Trait("Category", "Api")]
public sealed class BackgroundJobApiTests
{
    [Fact]
    public async Task ScanQueueRejectsDuplicatesAndOscarUploadPersistsSanitizedBytes()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_background_jobs_api_test").Build();
        await postgres.StartAsync();

        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDock.Infrastructure.Persistence.MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using (var db = new MediaDock.Infrastructure.Persistence.MediaDockDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        using var factory = new BackgroundJobsApiFactory(connectionString);
        using var client = factory.CreateClient();

        using var queuedResponse = await client.PostAsync("/api/background-jobs/scans", content: null);
        Assert.Equal(HttpStatusCode.Accepted, queuedResponse.StatusCode);
        var queuedJob = await queuedResponse.Content.ReadFromJsonAsync<AcceptedJob>();
        Assert.NotNull(queuedJob);

        using var activeResponse = await client.GetAsync("/api/background-jobs/active");
        Assert.Equal(HttpStatusCode.OK, activeResponse.StatusCode);
        using var activeDocument = JsonDocument.Parse(await activeResponse.Content.ReadAsStringAsync());
        Assert.Equal(queuedJob.Id, activeDocument.RootElement.GetProperty("id").GetInt64());

        using var duplicateResponse = await client.PostAsync("/api/background-jobs/scans", content: null);
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        using var conflictDocument = JsonDocument.Parse(await duplicateResponse.Content.ReadAsStringAsync());
        Assert.Equal(queuedJob.Id, conflictDocument.RootElement.GetProperty("activeJobId").GetInt64());

        using var eventsResponse = await client.GetAsync($"/api/background-jobs/{queuedJob.Id}/events?afterId=0&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, eventsResponse.StatusCode);
        using var eventsDocument = JsonDocument.Parse(await eventsResponse.Content.ReadAsStringAsync());
        Assert.Equal("job_queued", eventsDocument.RootElement.GetProperty("items")[0].GetProperty("eventCode").GetString());

        using var upload = new MultipartFormDataContent();
        var csv = new ByteArrayContent(Encoding.UTF8.GetBytes(
            "Ceremony,Year,Class,CanonicalCategory,Category,Film,FilmId,Name,Nominees,NomineeIds,Winner,Detail\n"));
        csv.Headers.ContentType = MediaTypeHeaderValue.Parse("text/csv");
        upload.Add(csv, "File", "../WINNERS.CSV");
        upload.Add(new StringContent("1980"), "YearAfter");

        using var uploadResponse = await client.PostAsync("/api/background-jobs/oscar-import", upload);
        Assert.Equal(HttpStatusCode.Accepted, uploadResponse.StatusCode);
        var importJob = await uploadResponse.Content.ReadFromJsonAsync<AcceptedJob>();
        Assert.NotNull(importJob);

        await using var verificationDb = new MediaDock.Infrastructure.Persistence.MediaDockDbContext(options);
        var savedImport = await verificationDb.BackgroundJobs.AsNoTracking().SingleAsync(job => job.Id == importJob.Id);
        Assert.Equal("WINNERS.CSV", savedImport.InputFileName);
        Assert.Equal(Encoding.UTF8.GetByteCount(
            "Ceremony,Year,Class,CanonicalCategory,Category,Film,FilmId,Name,Nominees,NomineeIds,Winner,Detail\n"),
            savedImport.InputBytes!.Length);
    }

    private sealed record AcceptedJob(long Id, string Status, string StatusUrl);

    private sealed class BackgroundJobsApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MediaDock"] = connectionString
                }));
            builder.ConfigureTestServices(services => services.RemoveAll<IHostedService>());
        }
    }
}
