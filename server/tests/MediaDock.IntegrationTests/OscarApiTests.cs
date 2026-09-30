using System.Net;
using System.Net.Http.Json;
using MediaDock.Api.Catalog;
using MediaDock.Api.Common;
using MediaDock.Api.Favorites;
using MediaDock.Api.OscarAwards;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace MediaDock.IntegrationTests;

[Trait("Category", "Api")]
public sealed class OscarApiTests
{
    [Fact]
    public async Task FavoritesMergeOriginsAndKeepIndependentMarkersWhenTorrentAppears()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_favorite_api_test").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        await using var db = new MediaDockDbContext(new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString).Options);
        await db.Database.MigrateAsync();
        var now = DateTimeOffset.UtcNow;
        var film = CreateFilm("favorite-film", "Favorite Film", 2024, "pending", now, null,
            CreateNomination("favorite-nomination", 97, "BEST PICTURE", "Best Picture", "Producer", true));
        db.OscarFilms.Add(film);
        await db.SaveChangesAsync();
        using var factory = new ApiFactory(connectionString);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/favorites",
            new { titleId = film.TitleId, from = "catalog" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/favorites",
            new { titleId = film.TitleId, from = "invalid" })).StatusCode);
        var added = await (await client.PostAsJsonAsync("/api/favorites",
            new { titleId = film.TitleId, from = "oscar" })).Content.ReadFromJsonAsync<FavoriteMovieResponse>();
        Assert.NotNull(added);
        Assert.True(added.ToWatch);
        Assert.False(added.ToDownload);
        Assert.Equal(1, added.WinCount);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync($"/api/favorites/{film.TitleId}",
            new { toDownload = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync($"/api/favorites/{film.TitleId}",
            new { })).StatusCode);
        await client.PatchAsJsonAsync($"/api/favorites/{film.TitleId}", new { toWatch = false });
        await client.PostAsJsonAsync("/api/favorites", new { titleId = film.TitleId, from = "oscar" });

        db.Sources.Add(new Source { StableKey = "favorite-feed", Name = "Feed", FeedType = "movie", Url = "https://feed.rutracker.cc" });
        await db.SaveChangesAsync();
        db.Occurrences.Add(new Occurrence
        {
            TitleId = film.TitleId, SourceId = db.Sources.Local.Single().Id, SourceItemKey = "entry:favorite",
            TorrentUrl = "https://example.org/torrent", RawTitle = "Favorite Film", SourceFeedName = "Feed",
            FirstSeenAt = now, LastSeenAt = now
        });
        await db.SaveChangesAsync();
        var page = await client.GetFromJsonAsync<PageResponse<FavoriteMovieResponse>>("/api/favorites?status=all&pageSize=1");
        Assert.NotNull(page);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(1, page.Items[0].OccurrenceCount);
        Assert.False(page.Items[0].ToWatch);
        Assert.False(page.Items[0].ToDownload);
        Assert.Equal(0, (await client.GetFromJsonAsync<PageResponse<FavoriteMovieResponse>>(
            "/api/favorites?status=to_download"))!.TotalCount);
        var catalogAdds = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync(
            "/api/favorites", new { titleId = film.TitleId, from = "catalog" })));
        Assert.All(catalogAdds, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var merged = await catalogAdds[0].Content.ReadFromJsonAsync<FavoriteMovieResponse>();
        Assert.NotNull(merged);
        Assert.False(merged.ToWatch);
        Assert.True(merged.ToDownload);
        Assert.True(merged.AddedFromOscar);
        Assert.True(merged.AddedFromCatalog);
        Assert.Equal(1, await db.FavoriteMovies.CountAsync());
        Assert.Equal(1, (await client.GetFromJsonAsync<PageResponse<FavoriteMovieResponse>>(
            "/api/favorites?status=to_download"))!.TotalCount);
        Assert.Equal(0, (await client.GetFromJsonAsync<PageResponse<FavoriteMovieResponse>>(
            "/api/favorites?status=to_watch"))!.TotalCount);
        await client.PatchAsJsonAsync($"/api/favorites/{film.TitleId}", new { toDownload = false });
        var repeated = await (await client.PostAsJsonAsync("/api/favorites",
            new { titleId = film.TitleId, from = "catalog" })).Content.ReadFromJsonAsync<FavoriteMovieResponse>();
        Assert.NotNull(repeated);
        Assert.False(repeated.ToDownload);
        var torrentOnly = new Title
        {
            TitleText = "Torrent Only", NormalizedTitle = "torrent only", Year = 2025,
            MediaType = "movie", UpdatedAt = now
        };
        db.Titles.Add(torrentOnly);
        await db.SaveChangesAsync();
        db.Occurrences.Add(new Occurrence
        {
            TitleId = torrentOnly.Id, SourceId = db.Sources.Local.Single().Id,
            SourceItemKey = "entry:torrent-only", TorrentUrl = "https://example.org/another",
            RawTitle = "Torrent Only", SourceFeedName = "Feed", FirstSeenAt = now, LastSeenAt = now
        });
        await db.SaveChangesAsync();
        var torrentAdded = await (await client.PostAsJsonAsync("/api/favorites",
            new { titleId = torrentOnly.Id, from = "catalog" })).Content.ReadFromJsonAsync<FavoriteMovieResponse>();
        Assert.NotNull(torrentAdded);
        Assert.True(torrentAdded.ToDownload);
        Assert.False(torrentAdded.ToWatch);
        Assert.Equal(0, torrentAdded.OscarFilmCount);
        var all = await client.GetFromJsonAsync<PageResponse<FavoriteMovieResponse>>(
            "/api/favorites?status=all&pageSize=1");
        Assert.NotNull(all);
        Assert.Equal(2, all.TotalCount);
        Assert.Equal(2, all.TotalPages);
        Assert.Single(all.Items);
        Assert.Equal(torrentOnly.Id, (await client.GetFromJsonAsync<PageResponse<FavoriteMovieResponse>>(
            "/api/favorites?status=to_download&pageSize=1"))!.Items.Single().TitleId);
        Assert.Single((await client.GetFromJsonAsync<OscarFilmResponse[]>($"/api/titles/{film.TitleId}/oscars"))!);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/favorites/{film.TitleId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/favorites/{film.TitleId}")).StatusCode);
    }

    [Fact]
    public async Task OscarApiFiltersPagesAndReturnsFilmsWithoutOccurrences()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_oscar_api_test").Build();
        await postgres.StartAsync();

        var connectionString = postgres.GetConnectionString();
        var dbOptions = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using var db = new MediaDockDbContext(dbOptions);
        await db.Database.MigrateAsync();
        var now = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);
        var films = new[]
        {
            CreateFilm("oppenheimer-2023", "Oppenheimer", 2023, "pending", now, 8.3m,
                CreateNomination("oppenheimer-best-picture", 96, "BEST PICTURE", "Best Picture", "Emma Thomas", true),
                CreateNomination("oppenheimer-directing", 96, "DIRECTING", "Directing", "Christopher Nolan", true)),
            CreateFilm("poor-things-2023", "Poor Things", 2023, "not_found", now, null,
                CreateNomination("poor-things-best-picture", 96, "BEST PICTURE", "Best Picture", "Ed Guiney", false),
                CreateNomination("poor-things-directing", 96, "DIRECTING", "Directing", "Yorgos Lanthimos", false)),
            CreateFilm("category-mix-2023", "Category Mix Example", 2023, "pending", now, 7.0m,
                CreateNomination("category-mix-best-picture", 96, "BEST PICTURE", "Best Picture", "Producer", false),
                CreateNomination("category-mix-directing", 96, "DIRECTING", "Directing", "Director", true))
        };
        db.OscarFilms.AddRange(films);
        await db.SaveChangesAsync();
        Assert.Empty(await db.Occurrences.AsNoTracking().ToListAsync());

        using var factory = new ApiFactory(connectionString);
        using var client = factory.CreateClient();

        using var catalogResponse = await client.GetAsync("/api/catalog");
        Assert.Equal(HttpStatusCode.OK, catalogResponse.StatusCode);
        var catalog = await catalogResponse.Content.ReadFromJsonAsync<PageResponse<CatalogTitleResponse>>();
        Assert.NotNull(catalog);
        Assert.Equal(0, catalog.TotalCount);

        using var pageResponse = await client.GetAsync("/api/oscars?page=1&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);
        var page = await pageResponse.Content.ReadFromJsonAsync<PageResponse<OscarFilmResponse>>();
        Assert.NotNull(page);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(3, page.TotalPages);
        Assert.Single(page.Items);

        using var titleDetailsResponse = await client.GetAsync($"/api/titles/{films[0].TitleId}");
        Assert.Equal(HttpStatusCode.OK, titleDetailsResponse.StatusCode);
        var titleDetails = await titleDetailsResponse.Content.ReadFromJsonAsync<TitleDetailsResponse>();
        Assert.NotNull(titleDetails);
        Assert.Null(titleDetails.FirstSeenAt);
        Assert.Null(titleDetails.LastSeenAt);
        Assert.Equal(0, titleDetails.OccurrenceCount);

        using var winnerResponse = await client.GetAsync(
            "/api/oscars?category=BEST%20PICTURE&result=winner&enrichmentStatus=pending");
        var winners = await winnerResponse.Content.ReadFromJsonAsync<PageResponse<OscarFilmResponse>>();
        Assert.NotNull(winners);
        var winner = Assert.Single(winners.Items);
        Assert.Equal("Oppenheimer", winner.Title);
        Assert.Equal(8.3m, winner.ImdbRating);
        Assert.Contains(winner.Nominations, nomination => nomination.IsWinner);

        using var nomineeResponse = await client.GetAsync(
            "/api/oscars?search=poor&yearFrom=2022&yearTo=2024&category=DIRECTING&result=nominee&enrichmentStatus=not_found");
        var nominees = await nomineeResponse.Content.ReadFromJsonAsync<PageResponse<OscarFilmResponse>>();
        Assert.NotNull(nominees);
        var nominee = Assert.Single(nominees.Items);
        Assert.Equal("Poor Things", nominee.Title);
        Assert.Equal("not_found", nominee.EnrichmentStatus);
        Assert.Null(nominee.ImdbRating);

        using var detailsResponse = await client.GetAsync($"/api/oscars/{nominee.Id}");
        Assert.Equal(HttpStatusCode.OK, detailsResponse.StatusCode);
        var details = await detailsResponse.Content.ReadFromJsonAsync<OscarFilmResponse>();
        Assert.NotNull(details);
        Assert.Equal("Poor Things", details.Title);
        Assert.Equal(2, details.Nominations.Count);
        Assert.Contains(details.Nominations, nomination => nomination.Category == "Best Picture");
        Assert.Contains(details.Nominations, nomination => nomination.Category == "Directing");

        using var reversedYearsResponse = await client.GetAsync("/api/oscars?yearFrom=2024&yearTo=2022");
        Assert.Equal(HttpStatusCode.BadRequest, reversedYearsResponse.StatusCode);
        Assert.NotNull(await reversedYearsResponse.Content.ReadFromJsonAsync<ValidationProblemDetails>());

        using var missingFilmResponse = await client.GetAsync("/api/oscars/999999");
        Assert.Equal(HttpStatusCode.NotFound, missingFilmResponse.StatusCode);
        Assert.NotNull(await missingFilmResponse.Content.ReadFromJsonAsync<ProblemDetails>());
    }

    private static OscarFilm CreateFilm(
        string stableKey,
        string title,
        int year,
        string enrichmentStatus,
        DateTimeOffset now,
        decimal? imdbRating,
        params OscarNomination[] nominations)
    {
        var normalizedTitle = title.ToLowerInvariant();
        return new OscarFilm
        {
            StableKey = stableKey,
            FilmTitle = title,
            NormalizedTitle = normalizedTitle,
            FilmYear = year,
            ImdbId = stableKey == "oppenheimer-2023" ? "tt15398776" : null,
            EnrichmentStatus = enrichmentStatus,
            ImportedAt = now,
            UpdatedAt = now,
            Title = new Title
            {
                TitleText = title,
                NormalizedTitle = normalizedTitle,
                Year = year,
                MediaType = "movie",
                SourceType = "movie",
                ContentKind = "standard",
                ImdbId = stableKey == "oppenheimer-2023" ? "tt15398776" : null,
                ImdbRating = imdbRating,
                UpdatedAt = now
            },
            Nominations = nominations
        };
    }

    private static OscarNomination CreateNomination(
        string importKey,
        int ceremony,
        string canonicalCategory,
        string category,
        string nominees,
        bool isWinner) => new()
    {
        ImportKey = importKey,
        Ceremony = ceremony,
        Class = "feature film",
        CanonicalCategory = canonicalCategory,
        Category = category,
        Name = nominees,
        Nominees = nominees,
        NomineeIds = string.Empty,
        Detail = string.Empty,
        IsWinner = isWinner,
        ImportedAt = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero)
    };

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