using System.Net;
using System.Net.Http.Json;
using MediaDock.Api.Health;
using MediaDock.Api.Versioning;

namespace MediaDock.IntegrationTests;

[Trait("Category", "Api")]
public sealed class ApiMetadataApiTests
{
    [Fact]
    public async Task VersionHealthAndOpenApiEndpointsExposeHostMetadata()
    {
        var buildMetadata = new Dictionary<string, string?>
        {
            ["MediaDock:Version"] = "2026.10.01+abc1234",
            ["MediaDock:CommitSha"] = "0123456789abcdef0123456789abcdef01234567",
            ["MediaDock:CommitDateUtc"] = "2026-10-01T12:30:00Z"
        };
        await using var host = await CatalogApiTestHost.CreateAsync("mediadock_api_metadata_test", buildMetadata);
        var client = host.Client;

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
    }
}