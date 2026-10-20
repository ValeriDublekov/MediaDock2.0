using System.Net;
using System.Net.Http.Json;
using MediaDock.Api.Sources;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Mvc;

namespace MediaDock.IntegrationTests;

[Trait("Category", "Api")]
public sealed class SourceApiTests
{
    [Fact]
    public async Task SourceProfilesAndUrlsCanBeReadAndManaged()
    {
        await using var host = await CatalogApiTestHost.CreateAsync("mediadock_sources_api_test");
        var db = host.Db;
        var client = host.Client;
        db.Sources.AddRange(
            new Source
            {
                StableKey = "movies-main", Name = "Movies", FeedType = "movie",
                Url = "https://feed.rutracker.cc/movies.atom", IsEnabled = true
            },
            new Source
            {
                StableKey = "series-complete", Name = "Series", FeedType = "series_complete",
                Url = "https://feed.rutracker.cc/series-complete.atom", IsEnabled = true
            },
            new Source
            {
                StableKey = "series-ongoing", Name = "Series in progress", FeedType = "series_ongoing",
                Url = "https://feed.rutracker.cc/series-ongoing.atom", IsEnabled = true
            });
        await db.SaveChangesAsync();

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
        Assert.Equal(
            new[] { "https://feed.rutracker.cc/complete-replaced.atom", "https://feed.rutracker.cc/series-complete.atom" }.OrderBy(url => url),
            groupedProfiles[1].Urls.Select(url => url.Url).OrderBy(url => url));
        Assert.Equal(
            new[]
            {
                "https://feed.rutracker.cc/complete-extra.atom",
                "https://feed.rutracker.cc/ongoing.atom",
                "https://feed.rutracker.cc/series-ongoing.atom"
            },
            groupedProfiles[2].Urls.Select(url => url.Url).OrderBy(url => url));
    }
}