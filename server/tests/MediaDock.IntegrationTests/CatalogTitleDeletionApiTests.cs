using System.Net;
using System.Net.Http.Json;
using MediaDock.Api.Catalog;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using static MediaDock.IntegrationTests.CatalogApiTestData;

namespace MediaDock.IntegrationTests;

[Trait("Category", "Api")]
public sealed class CatalogTitleDeletionApiTests
{
    [Fact]
    public async Task DeleteTitleRemovesReleasesAndOnlyDeletesRequestedUnsharedImdbInfo()
    {
        await using var host = await CatalogApiTestHost.CreateAsync("mediadock_catalog_delete_api_test");
        var db = host.Db;
        var source = new Source
        {
            StableKey = "delete-test",
            Name = "Delete test",
            FeedType = "movie",
            Url = "https://feed.rutracker.cc/delete-test.atom",
            IsEnabled = true
        };
        var standaloneTitle = CreateTitle("Standalone Film", "standalone film", 1998, "movie");
        var sharedTitle = CreateTitle("Shared Film", "shared film", 1999, "movie");
        var retainedTitle = CreateTitle("Retained Film", "retained film", 2000, "movie");
        db.Sources.Add(source);
        db.ImdbTitles.AddRange(
            new ImdbTitle { ImdbId = "tt1998", TitleText = "Standalone Film" },
            new ImdbTitle { ImdbId = "tt1999", TitleText = "Shared Film" },
            new ImdbTitle { ImdbId = "tt2000", TitleText = "Retained Film" });
        db.TorrentMovies.AddRange(standaloneTitle, sharedTitle, retainedTitle);
        db.OscarMovies.Add(new OscarMovie
        {
            StableKey = "shared-film",
            FilmTitle = "Shared Film",
            NormalizedTitle = "shared film",
            FilmYear = 1999,
            ImdbId = "tt1999",
            ImportedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var observedAt = DateTimeOffset.UtcNow;
        var standaloneRelease = CreateRelease(standaloneTitle.Id, source.Id, "standalone", "Standalone Film", observedAt);
        var sharedRelease = CreateRelease(sharedTitle.Id, source.Id, "shared", "Shared Film", observedAt);
        var retainedRelease = CreateRelease(retainedTitle.Id, source.Id, "retained", "Retained Film", observedAt);
        db.TorrentReleases.AddRange(standaloneRelease, sharedRelease, retainedRelease);
        db.RssItemStates.AddRange(
            CreateItemState(source.Id, "standalone", observedAt),
            CreateItemState(source.Id, "shared", observedAt),
            CreateItemState(source.Id, "retained", observedAt));
        await db.SaveChangesAsync();

        using var standaloneResponse = await host.Client.DeleteAsync($"/api/titles/{standaloneTitle.Id}?deleteImdbInfo=true");
        Assert.Equal(HttpStatusCode.OK, standaloneResponse.StatusCode);
        var standaloneResult = await standaloneResponse.Content.ReadFromJsonAsync<DeleteTitleResponse>();
        Assert.NotNull(standaloneResult);
        Assert.True(standaloneResult.ImdbInfoDeleted);
        Assert.False(await db.TorrentMovies.AnyAsync(movie => movie.Id == standaloneTitle.Id));
        Assert.False(await db.TorrentReleases.AnyAsync(release => release.Id == standaloneRelease.Id));
        Assert.False(await db.RssItemStates.AnyAsync(state => state.SourceId == source.Id && state.SourceItemKey == "standalone"));
        Assert.False(await db.ImdbTitles.AnyAsync(title => title.ImdbId == "tt1998"));

        using var sharedResponse = await host.Client.DeleteAsync($"/api/titles/{sharedTitle.Id}?deleteImdbInfo=true");
        var sharedResult = await sharedResponse.Content.ReadFromJsonAsync<DeleteTitleResponse>();
        Assert.Equal(HttpStatusCode.OK, sharedResponse.StatusCode);
        Assert.NotNull(sharedResult);
        Assert.False(sharedResult.ImdbInfoDeleted);
        Assert.False(await db.TorrentMovies.AnyAsync(movie => movie.Id == sharedTitle.Id));
        Assert.True(await db.ImdbTitles.AnyAsync(title => title.ImdbId == "tt1999"));
        Assert.True(await db.OscarMovies.AnyAsync(movie => movie.ImdbId == "tt1999"));

        using var retainedResponse = await host.Client.DeleteAsync($"/api/titles/{retainedTitle.Id}?deleteImdbInfo=false");
        var retainedResult = await retainedResponse.Content.ReadFromJsonAsync<DeleteTitleResponse>();
        Assert.Equal(HttpStatusCode.OK, retainedResponse.StatusCode);
        Assert.NotNull(retainedResult);
        Assert.False(retainedResult.ImdbInfoDeleted);
        Assert.True(await db.ImdbTitles.AnyAsync(title => title.ImdbId == "tt2000"));
    }
}
