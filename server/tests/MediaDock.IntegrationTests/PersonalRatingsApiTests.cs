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
    public async Task ImportMergesRepeatedFilesAndRejectsInvalidJsonWithoutChangingSavedRatings()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_personal_ratings_api_test").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        await using var db = new MediaDockDbContext(new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString).Options);
        await db.Database.MigrateAsync();
        using var factory = new ApiFactory(connectionString);
        using var client = factory.CreateClient();

        using var firstResponse = await client.PostAsync("/api/personal-ratings/import", CreateUpload(
            "[{\"id\":\"tt14452776\",\"rating\":8},{\"id\":\"tt1172049\",\"rating\":6}]"));
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var firstResult = await firstResponse.Content.ReadFromJsonAsync<PersonalRatingsImportResponse>();
        Assert.NotNull(firstResult);
        Assert.Equal(2, firstResult.Added);
        Assert.Equal(2, firstResult.TotalRatings);

        using var repeatedResponse = await client.PostAsync("/api/personal-ratings/import", CreateUpload(
            "[{\"id\":\"tt14452776\",\"rating\":9},{\"id\":\"tt1172049\",\"rating\":6},{\"id\":\"tt0000003\",\"rating\":7}]"));
        Assert.Equal(HttpStatusCode.OK, repeatedResponse.StatusCode);
        var repeatedResult = await repeatedResponse.Content.ReadFromJsonAsync<PersonalRatingsImportResponse>();
        Assert.NotNull(repeatedResult);
        Assert.Equal(1, repeatedResult.Added);
        Assert.Equal(1, repeatedResult.Updated);
        Assert.Equal(1, repeatedResult.Unchanged);
        Assert.Equal(3, repeatedResult.TotalRatings);

        using var invalidResponse = await client.PostAsync("/api/personal-ratings/import", CreateUpload(
            "[{\"id\":\"tt1234567\",\"rating\":0}]"));
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        Assert.Equal(3, await db.PersonalRatings.CountAsync());
        Assert.Equal(9, (await db.PersonalRatings.SingleAsync(rating => rating.ImdbId == "tt14452776")).Rating);

        using var partialResponse = await client.PostAsync("/api/personal-ratings/import", CreateUpload(
            "[{\"id\":\"tt0000003\",\"rating\":7}]"));
        Assert.Equal(HttpStatusCode.OK, partialResponse.StatusCode);
        Assert.Equal(3, (await partialResponse.Content.ReadFromJsonAsync<PersonalRatingsImportResponse>())!.TotalRatings);
        Assert.Equal(6, (await db.PersonalRatings.SingleAsync(rating => rating.ImdbId == "tt1172049")).Rating);

        using var partialWithErrorsResponse = await client.PostAsync("/api/personal-ratings/import", CreateUpload(
            "[{\"id\":\"tt12345678\",\"rating\":8},{\"id\":\"tt5555555\",\"rating\":null},{\"id\":\"tt4444444\"}]"));
        Assert.Equal(HttpStatusCode.OK, partialWithErrorsResponse.StatusCode);
        var partialWithErrorsResult = await partialWithErrorsResponse.Content.ReadFromJsonAsync<PersonalRatingsImportResponse>();
        Assert.NotNull(partialWithErrorsResult);
        Assert.Equal(1, partialWithErrorsResult.Added);
        Assert.Equal(4, partialWithErrorsResult.TotalRatings);
        Assert.Equal(new[] { "tt5555555", "tt4444444" }, partialWithErrorsResult.Errors.Select(error => error.Id));
        Assert.All(partialWithErrorsResult.Errors, error => Assert.Equal("Missing rating.", error.Message));
        Assert.Equal(4, await db.PersonalRatings.CountAsync());
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