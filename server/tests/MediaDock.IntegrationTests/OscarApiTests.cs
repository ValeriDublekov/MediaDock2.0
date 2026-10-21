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
    public async Task FavoritesRequireAnActiveLinkedUserEvenWhenAccessControlIsOpen()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_favorite_gate_test").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        await using var db = new MediaDockDbContext(new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString).Options);
        await db.Database.MigrateAsync();
        using var factory = new ApiFactory(connectionString);
        using var client = factory.CreateClient();

        using var listResponse = await client.GetAsync("/api/favorites");
        Assert.Equal(HttpStatusCode.Unauthorized, listResponse.StatusCode);
        using var addResponse = await client.PostAsJsonAsync("/api/favorites", new { titleId = 1, from = "catalog" });
        Assert.Equal(HttpStatusCode.Unauthorized, addResponse.StatusCode);
        using var updateResponse = await client.PatchAsJsonAsync("/api/favorites/1", new { toWatch = true });
        Assert.Equal(HttpStatusCode.Unauthorized, updateResponse.StatusCode);
        using var deleteResponse = await client.DeleteAsync("/api/favorites/1");
        Assert.Equal(HttpStatusCode.Unauthorized, deleteResponse.StatusCode);
        Assert.Empty(await db.ImdbFavoriteMovies.ToListAsync());
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
            CreateFilm("oppenheimer-2023", "Oppenheimer", 2023, "pending", now, "tt15398776",
                CreateNomination("oppenheimer-best-picture", 96, "BEST PICTURE", "Best Picture", "Emma Thomas", true),
                CreateNomination("oppenheimer-directing", 96, "DIRECTING", "Directing", "Christopher Nolan", true)),
            CreateFilm("poor-things-2023", "Poor Things", 2023, "not_found", now, null,
                CreateNomination("poor-things-best-picture", 96, "BEST PICTURE", "Best Picture", "Ed Guiney", false),
                CreateNomination("poor-things-directing", 96, "DIRECTING", "Directing", "Yorgos Lanthimos", false)),
            CreateFilm("category-mix-2023", "Category Mix Example", 2023, "pending", now, "tt76543210",
                CreateNomination("category-mix-best-picture", 96, "BEST PICTURE", "Best Picture", "Producer", false),
                CreateNomination("category-mix-directing", 96, "DIRECTING", "Directing", "Director", true)),
            CreateFilm("year-bounds-2021", "Year Bounds Example", 2021, "pending", now, null,
                CreateNomination("year-bounds-sound", 93, "SOUND", "Sound", "Sound Team", false))
        };
        db.ImdbTitles.AddRange(
            new ImdbTitle
            {
                ImdbId = "tt15398776",
                TitleText = "Canonical Oppenheimer",
                Year = 2024,
                MediaType = "movie",
                ImdbRating = 8.3m,
                ImdbVotes = 400000,
                Metascore = 90m,
                Genres = ["Biography", "Drama"],
                Countries = ["USA", "UK"],
                Director = "Canonical Director",
                Plot = "Canonical plot.",
                PosterUrl = "https://example.test/canonical-oppenheimer.jpg",
                Runtime = "180 min",
                Awards = "Canonical awards.",
                BoxOffice = "$900 million",
                UpdatedAt = now
            },
            new ImdbTitle
            {
                ImdbId = "tt76543210",
                TitleText = "Canonical Category Mix",
                Year = 2023,
                MediaType = "movie",
                ImdbRating = 7.0m,
                UpdatedAt = now
            });
        db.OscarMovies.AddRange(films);
        var torrentMovie = new TorrentMovie
        {
            TitleText = "Canonical Oppenheimer",
            NormalizedTitle = "canonical oppenheimer",
            Year = 2024,
            MediaType = "movie",
            ImdbId = "tt15398776",
            UpdatedAt = now
        };
        db.TorrentMovies.Add(torrentMovie);
        await db.SaveChangesAsync();
        Assert.Empty(await db.TorrentReleases.AsNoTracking().ToListAsync());

        using var factory = new ApiFactory(connectionString);
        using var client = factory.CreateClient();

        using var categoriesResponse = await client.GetAsync("/api/oscars/categories");
        Assert.Equal(HttpStatusCode.OK, categoriesResponse.StatusCode);
        var categories = await categoriesResponse.Content.ReadFromJsonAsync<string[]>();
        Assert.NotNull(categories);
        Assert.Contains("BEST PICTURE", categories);
        Assert.Contains("DIRECTING", categories);
        Assert.Equal(categories.OrderBy(category => category, StringComparer.Ordinal), categories);

        using var catalogResponse = await client.GetAsync("/api/catalog");
        Assert.Equal(HttpStatusCode.OK, catalogResponse.StatusCode);
        var catalog = await catalogResponse.Content.ReadFromJsonAsync<PageResponse<CatalogTitleResponse>>();
        Assert.NotNull(catalog);
        Assert.Equal(0, catalog.TotalCount);

        using var pageResponse = await client.GetAsync("/api/oscars?page=1&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);
        var page = await pageResponse.Content.ReadFromJsonAsync<PageResponse<OscarFilmResponse>>();
        Assert.NotNull(page);
        Assert.Equal(4, page.TotalCount);
        Assert.Equal(4, page.TotalPages);
        Assert.Equal(new YearBounds(2021, 2023), page.YearBounds);
        Assert.Equal(new ImdbRatingBounds(7.0m, 8.3m), page.ImdbRatingBounds);
        Assert.Single(page.Items);

        using var titleOscarsResponse = await client.GetAsync($"/api/titles/{torrentMovie.Id}/oscars");
        Assert.Equal(HttpStatusCode.OK, titleOscarsResponse.StatusCode);
        var titleOscars = await titleOscarsResponse.Content.ReadFromJsonAsync<OscarFilmResponse[]>();
        Assert.NotNull(titleOscars);
        Assert.Equal("tt15398776", Assert.Single(titleOscars).ImdbId);

        using var winnerResponse = await client.GetAsync(
            "/api/oscars?category=BEST%20PICTURE&result=winner&enrichmentStatus=pending");
        var winners = await winnerResponse.Content.ReadFromJsonAsync<PageResponse<OscarFilmResponse>>();
        Assert.NotNull(winners);
        var winner = Assert.Single(winners.Items);
        Assert.Equal("Oppenheimer", winner.Title);
        Assert.Equal("Canonical Oppenheimer", winner.MetadataTitle);
        Assert.Equal(2024, winner.MetadataYear);
        Assert.Equal(8.3m, winner.ImdbRating);
        Assert.Equal(400000, winner.ImdbVotes);
        Assert.Equal(90m, winner.Metascore);
        Assert.Equal(new[] { "Biography", "Drama" }, winner.Genres);
        Assert.Equal(new[] { "USA", "UK" }, winner.Countries);
        Assert.Equal("Canonical Director", winner.Director);
        Assert.Equal("Canonical plot.", winner.Plot);
        Assert.Equal("https://example.test/canonical-oppenheimer.jpg", winner.PosterUrl);
        Assert.Equal("180 min", winner.Runtime);
        Assert.Equal("Canonical awards.", winner.Awards);
        Assert.Equal("$900 million", winner.BoxOffice);
        Assert.Contains(winner.Nominations, nomination => nomination.IsWinner);

        using var canonicalSearchResponse = await client.GetAsync("/api/oscars?search=Canonical%20Oppenheimer");
        var canonicalSearch = await canonicalSearchResponse.Content.ReadFromJsonAsync<PageResponse<OscarFilmResponse>>();
        Assert.NotNull(canonicalSearch);
        Assert.Equal("Oppenheimer", Assert.Single(canonicalSearch.Items).Title);

        using var nomineeResponse = await client.GetAsync(
            "/api/oscars?search=poor&yearFrom=2022&yearTo=2024&category=DIRECTING&result=nominee&enrichmentStatus=not_found");
        var nominees = await nomineeResponse.Content.ReadFromJsonAsync<PageResponse<OscarFilmResponse>>();
        Assert.NotNull(nominees);
        Assert.Equal(new YearBounds(2021, 2023), nominees.YearBounds);
        var nominee = Assert.Single(nominees.Items);
        Assert.Equal("Poor Things", nominee.Title);
        Assert.Equal("not_found", nominee.EnrichmentStatus);
        Assert.Null(nominee.ImdbRating);

        using var allFilmsResponse = await client.GetAsync("/api/oscars?pageSize=100");
        var allFilms = await allFilmsResponse.Content.ReadFromJsonAsync<PageResponse<OscarFilmResponse>>();
        Assert.NotNull(allFilms);
        Assert.Equal(7.0m, Assert.Single(allFilms.Items, film => film.Title == "Category Mix Example").ImdbRating);

        using var ratingFilteredResponse = await client.GetAsync("/api/oscars?imdbRatingFrom=8&imdbRatingTo=9");
        var ratingFilteredPage = await ratingFilteredResponse.Content.ReadFromJsonAsync<PageResponse<OscarFilmResponse>>();
        Assert.NotNull(ratingFilteredPage);
        Assert.Equal(new ImdbRatingBounds(7.0m, 8.3m), ratingFilteredPage.ImdbRatingBounds);
        Assert.Equal("Oppenheimer", Assert.Single(ratingFilteredPage.Items).Title);

        using var multiCategoryResponse = await client.GetAsync(
            "/api/oscars?categoryFilter=true&categories=BEST%20PICTURE&categories=DIRECTING&result=nominee");
        var multiCategoryPage = await multiCategoryResponse.Content.ReadFromJsonAsync<PageResponse<OscarFilmResponse>>();
        Assert.NotNull(multiCategoryPage);
        var multiCategoryNominee = Assert.Single(multiCategoryPage.Items, film => film.Title == "Poor Things");
        Assert.Equal(2, multiCategoryNominee.Nominations.Count);

        using var emptyCategoryResponse = await client.GetAsync("/api/oscars?categoryFilter=true");
        var emptyCategoryPage = await emptyCategoryResponse.Content.ReadFromJsonAsync<PageResponse<OscarFilmResponse>>();
        Assert.NotNull(emptyCategoryPage);
        Assert.Empty(emptyCategoryPage.Items);
        Assert.Equal(0, emptyCategoryPage.TotalCount);

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

    private static OscarMovie CreateFilm(
        string stableKey,
        string title,
        int year,
        string enrichmentStatus,
        DateTimeOffset now,
        string? imdbId,
        params OscarNomination[] nominations)
    {
        var normalizedTitle = title.ToLowerInvariant();
        return new OscarMovie
        {
            StableKey = stableKey,
            FilmTitle = title,
            NormalizedTitle = normalizedTitle,
            FilmYear = year,
            ImdbId = imdbId,
            EnrichmentStatus = enrichmentStatus,
            ImportedAt = now,
            UpdatedAt = now,
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