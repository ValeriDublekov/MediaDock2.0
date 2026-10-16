using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediaDock.Api.PersonalRatings;
using MediaDock.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace MediaDock.IntegrationTests;

[Trait("Category", "Api")]
public sealed class PersonalRatingsApiTests
{
    [Fact]
    public async Task ImportRemainsClosedUntilOwnerScopedWritesAreImplemented()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_personal_ratings_api_test").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        await using var db = new MediaDockDbContext(new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString).Options);
        await db.Database.MigrateAsync();
        using var factory = new ApiFactory(connectionString);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/personal-ratings/import", CreateUpload("[]"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await db.PersonalRatings.ToListAsync());
    }

    private static MultipartFormDataContent CreateUpload(string json)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(json));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Add(file, "File", "ratings.json");
        return content;
    }

    private sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
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