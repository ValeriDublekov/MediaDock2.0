using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediaDock.Api.Common;
using MediaDock.Api.GoldenGlobes;
using MediaDock.Application.Metadata;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
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
public sealed class GoldenGlobeApiTests
{
    [Fact]
    public async Task CatalogGroupsNominationsFiltersAndPaginatesFilms()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_golden_globe_api_test").Build();
        await postgres.StartAsync();

        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using (var db = new MediaDockDbContext(options))
        {
            await db.Database.MigrateAsync();
            var bestPicture = new GoldenGlobeAward { Name = "Best Motion Picture" };
            var bestDirector = new GoldenGlobeAward { Name = "Best Director" };
            var manualTypeMismatch = CreateNomination(
                "manual-type-mismatch", "Manual Type Mismatch", 2025, false, "tt76543210", bestPicture, "enriched", "series");
            manualTypeMismatch.IsImdbIdManual = true;
            db.GoldenGlobeNominations.AddRange(
                CreateNomination("a-picture", "A Film", 2025, true, "tt12345678", bestPicture, "enriched"),
                CreateNomination("a-director", "A Film", 2025, false, "tt12345678", bestDirector, "enriched"),
                CreateNomination("a-series", "A Film", 2025, false, "tt87654321", bestPicture, "enriched", "series"),
                CreateNomination("a-older-year", "A Film", 2024, false, null, bestPicture),
                CreateNomination("b-picture", "B Film", 2025, false, null, bestPicture, "not_found"),
                CreateNomination("older-picture", "Older Film", 2024, true, null, bestPicture, "problem"),
                CreateNomination("daisy-series", "Daisy Jones and the Six", 2024, false, "tt8749198", bestPicture, "enriched", "series"),
                CreateNomination("no-art", "No Art Film", 2025, false, "tt32345678", bestPicture, "enriched"),
                manualTypeMismatch,
                CreateNomination("broken-enriched", "Broken Enriched Film", 2025, false, null, bestPicture, "enriched"));
            var fetchedAt = DateTimeOffset.UtcNow;
            db.MetadataCache.Add(new MetadataCacheEntry
            {
                CacheKey = "golden-globe-film-cache",
                LookupTitle = "a film",
                LookupYearSemantics = "title",
                SourceType = "movie",
                Status = "found",
                PayloadJson = JsonSerializer.Serialize(new MetadataDetails(
                    "A Film", 2024, "tt12345678", "movie", "movie", "standard", null,
                    8.0m, 1000, 80m, [], [], null, null, "https://example.test/a-film.jpg", null, null, null)),
                FetchedAt = fetchedAt,
                ExpiresAt = fetchedAt.AddDays(30)
            });
            db.MetadataCache.AddRange(
                new MetadataCacheEntry
                {
                    CacheKey = "golden-globe-series-cache",
                    LookupTitle = "a film",
                    LookupYearSemantics = "title",
                    SourceType = "series",
                    Status = "found",
                    PayloadJson = JsonSerializer.Serialize(new MetadataDetails(
                        "A Film", 2010, "tt87654321", "series", "series", "standard", null,
                        7.4m, 5000, null, [], [], null, null, "https://example.test/a-film-series.jpg", null, null, null)),
                    FetchedAt = fetchedAt,
                    ExpiresAt = fetchedAt.AddDays(30)
                },
                new MetadataCacheEntry
                {
                    CacheKey = "golden-globe-no-art-cache",
                    LookupTitle = "no art film",
                    LookupYearSemantics = "title",
                    SourceType = "movie",
                    Status = "found",
                    PayloadJson = JsonSerializer.Serialize(new MetadataDetails(
                        "No Art Film", 2024, "tt32345678", "movie", "movie", "standard", null,
                        null, null, null, [], [], null, null, null, null, null, null)),
                    FetchedAt = fetchedAt,
                    ExpiresAt = fetchedAt.AddDays(30)
                },
                new MetadataCacheEntry
                {
                    CacheKey = "golden-globe-manual-type-mismatch-cache",
                    LookupTitle = "manual type mismatch",
                    LookupYearSemantics = "series_title",
                    SourceType = "series",
                    LookupIdentity = "tt76543210",
                    Status = "found",
                    PayloadJson = JsonSerializer.Serialize(new MetadataDetails(
                        "Manual Type Mismatch", 2024, "tt76543210", "movie", "movie", "standard", null,
                        6.7m, 1200, null, [], [], null, null, "https://example.test/manual-type-mismatch.jpg", null, null, null)),
                    FetchedAt = fetchedAt,
                    ExpiresAt = fetchedAt.AddDays(30)
                },
                new MetadataCacheEntry
                {
                    CacheKey = "golden-globe-daisy-negative-cache",
                    LookupTitle = "daisy jones and the six",
                    LookupYearSemantics = "title",
                    SourceType = "series",
                    Status = "confirmed_not_found",
                    FetchedAt = fetchedAt.AddSeconds(1),
                    ExpiresAt = fetchedAt.AddDays(2)
                },
                new MetadataCacheEntry
                {
                    CacheKey = "golden-globe-daisy-id-cache",
                    LookupTitle = "daisy jones and the six",
                    LookupYearSemantics = "series_title",
                    SourceType = "series",
                    LookupIdentity = "tt8749198",
                    Status = "found",
                    PayloadJson = JsonSerializer.Serialize(new MetadataDetails(
                        "Daisy Jones & The Six", 2023, "tt8749198", "series", "series", "standard", null,
                        8.1m, 47051, null, ["Drama", "Music"], ["United States"], null, "A valid plot.",
                        "https://example.test/daisy.jpg", null, null, null)),
                    FetchedAt = fetchedAt,
                    ExpiresAt = fetchedAt.AddDays(30)
                });
            await db.SaveChangesAsync();
        }

        using var factory = new GoldenGlobesApiFactory(connectionString);
        using var client = factory.CreateClient();

        using var categoriesResponse = await client.GetAsync("/api/golden-globes/categories");
        Assert.Equal(HttpStatusCode.OK, categoriesResponse.StatusCode);
        var categories = await categoriesResponse.Content.ReadFromJsonAsync<string[]>();
        Assert.NotNull(categories);
        Assert.Contains("Best Motion Picture", categories);
        Assert.Equal(categories.OrderBy(category => category, StringComparer.Ordinal), categories);

        using var firstPageResponse = await client.GetAsync("/api/golden-globes?page=1&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, firstPageResponse.StatusCode);
        var firstPage = await firstPageResponse.Content.ReadFromJsonAsync<PageResponse<GoldenGlobeFilmResponse>>();
        Assert.NotNull(firstPage);
        Assert.Equal(9, firstPage.TotalCount);
        Assert.Equal(9, firstPage.TotalPages);
        var firstFilm = Assert.Single(firstPage.Items);
        Assert.Equal("A Film", firstFilm.Title);
        Assert.Equal("2025:movie:A Film", firstFilm.FilmId);
        Assert.Equal("movie", firstFilm.NomineeType);
        Assert.Equal("tt12345678", firstFilm.ImdbId);
        Assert.Equal("https://example.test/a-film.jpg", firstFilm.PosterUrl);
        Assert.Equal(8.0m, firstFilm.ImdbRating);
        Assert.Equal(new[] { "Best Director", "Best Motion Picture" }, firstFilm.Nominations.Select(row => row.Award));

        using var groupedResponse = await client.GetAsync("/api/golden-globes?pageSize=100");
        var groupedPage = await groupedResponse.Content.ReadFromJsonAsync<PageResponse<GoldenGlobeFilmResponse>>();
        Assert.NotNull(groupedPage);
        var sameYearGroups = groupedPage.Items.Where(film => film.Title == "A Film" && film.Year == 2025).ToArray();
        Assert.Equal(new[] { "movie", "series" }, sameYearGroups.Select(film => film.NomineeType).OrderBy(type => type, StringComparer.Ordinal).ToArray());
        Assert.Equal(2, sameYearGroups.Select(film => film.FilmId).Distinct().Count());
        var seriesFilm = sameYearGroups.Single(film => film.NomineeType == "series");
        Assert.Equal("tt87654321", seriesFilm.ImdbId);
        Assert.Equal(7.4m, seriesFilm.ImdbRating);
        Assert.Equal("https://example.test/a-film-series.jpg", seriesFilm.PosterUrl);
        var daisyFilm = groupedPage.Items.Single(film => film.Title == "Daisy Jones and the Six");
        Assert.Equal("tt8749198", daisyFilm.ImdbId);
        Assert.Equal(8.1m, daisyFilm.ImdbRating);
        Assert.Equal("https://example.test/daisy.jpg", daisyFilm.PosterUrl);
        var manualTypeMismatchFilm = groupedPage.Items.Single(film => film.Title == "Manual Type Mismatch");
        Assert.Equal("series", manualTypeMismatchFilm.NomineeType);
        Assert.True(manualTypeMismatchFilm.IsImdbIdManual);
        Assert.Equal("enriched", manualTypeMismatchFilm.EnrichmentStatus);
        Assert.Equal("tt76543210", manualTypeMismatchFilm.ImdbId);
        Assert.Equal(6.7m, manualTypeMismatchFilm.ImdbRating);
        Assert.Equal("https://example.test/manual-type-mismatch.jpg", manualTypeMismatchFilm.PosterUrl);
        var noArtFilm = groupedPage.Items.Single(film => film.Title == "No Art Film");
        Assert.Equal("tt32345678", noArtFilm.ImdbId);
        Assert.Null(noArtFilm.ImdbRating);
        Assert.Null(noArtFilm.PosterUrl);
        var brokenEnrichedFilm = groupedPage.Items.Single(film => film.Title == "Broken Enriched Film");
        Assert.Equal("pending", brokenEnrichedFilm.EnrichmentStatus);
        Assert.Equal("2024:movie:A Film", groupedPage.Items.Single(film => film.Title == "A Film" && film.Year == 2024).FilmId);

        using var filteredResponse = await client.GetAsync(
            "/api/golden-globes?yearFrom=2025&yearTo=2025&award=Best%20Motion%20Picture&result=winner");
        var filteredPage = await filteredResponse.Content.ReadFromJsonAsync<PageResponse<GoldenGlobeFilmResponse>>();
        Assert.NotNull(filteredPage);
        var filteredFilm = Assert.Single(filteredPage.Items);
        Assert.Equal("A Film", filteredFilm.Title);
        Assert.Single(filteredFilm.Nominations);
        Assert.Contains(filteredFilm.Nominations, nomination => nomination.Award == "Best Motion Picture" && nomination.IsWinner);

        using var multiCategoryResponse = await client.GetAsync(
            "/api/golden-globes?yearFrom=2025&yearTo=2025&categoryFilter=true&categories=Best%20Director&categories=Best%20Motion%20Picture&result=winner");
        var multiCategoryPage = await multiCategoryResponse.Content.ReadFromJsonAsync<PageResponse<GoldenGlobeFilmResponse>>();
        Assert.NotNull(multiCategoryPage);
        var multiCategoryFilm = Assert.Single(multiCategoryPage.Items, film => film.Title == "A Film");
        Assert.Equal(2, multiCategoryFilm.Nominations.Count);

        using var emptyCategoryResponse = await client.GetAsync("/api/golden-globes?categoryFilter=true");
        var emptyCategoryPage = await emptyCategoryResponse.Content.ReadFromJsonAsync<PageResponse<GoldenGlobeFilmResponse>>();
        Assert.NotNull(emptyCategoryPage);
        Assert.Empty(emptyCategoryPage.Items);
        Assert.Equal(0, emptyCategoryPage.TotalCount);

        using var notFoundResponse = await client.GetAsync("/api/golden-globes?enrichmentStatus=not_found");
        var notFoundPage = await notFoundResponse.Content.ReadFromJsonAsync<PageResponse<GoldenGlobeFilmResponse>>();
        Assert.NotNull(notFoundPage);
        Assert.Equal("B Film", Assert.Single(notFoundPage.Items).Title);

        using var problemResponse = await client.GetAsync("/api/golden-globes?enrichmentStatus=problem");
        var problemPage = await problemResponse.Content.ReadFromJsonAsync<PageResponse<GoldenGlobeFilmResponse>>();
        Assert.NotNull(problemPage);
        Assert.Equal("Older Film", Assert.Single(problemPage.Items).Title);

        using var invalidRangeResponse = await client.GetAsync("/api/golden-globes?yearFrom=2026&yearTo=2025");
        Assert.Equal(HttpStatusCode.BadRequest, invalidRangeResponse.StatusCode);
    }

    [Fact]
    public async Task ManualImdbLinkNormalizesQueuesIdempotentlyAndClearsByExactFilmGroup()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_golden_globe_manual_link_test").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>().UseNpgsql(connectionString).Options;
        await using (var db = new MediaDockDbContext(options))
        {
            await db.Database.MigrateAsync();
            db.GoldenGlobeNominations.AddRange(
                CreateNomination("manual-pending", "A: Film", 2025, false, null, new GoldenGlobeAward { Name = "Manual Award 1" }),
                CreateNomination("manual-enriched", "A: Film", 2025, false, null, new GoldenGlobeAward { Name = "Manual Award 2" }, "enriched"),
                CreateNomination("manual-problem", "A: Film", 2025, false, null, new GoldenGlobeAward { Name = "Manual Award 3" }, "problem"),
                CreateNomination("manual-not-found", "A: Film", 2025, false, null, new GoldenGlobeAward { Name = "Manual Award 4" }, "not_found"),
                CreateNomination("manual-temporary", "A: Film", 2025, false, null, new GoldenGlobeAward { Name = "Manual Award 5" }, "temporary_error"),
                CreateNomination("manual-other-year", "A: Film", 2024, false, null, new GoldenGlobeAward { Name = "Manual Other Year" }),
                CreateNomination("manual-series", "A: Film", 2025, false, null, new GoldenGlobeAward { Name = "Manual Series" }, nomineeType: "series"));
            await db.SaveChangesAsync();
        }

        using var factory = new GoldenGlobesApiFactory(connectionString);
        using var client = factory.CreateClient();
        var request = new { filmId = "2025:movie:A: Film", imdbId = " TT12345678 " };
        using var invalidIdResponse = await client.PutAsJsonAsync("/api/golden-globes/imdb-link", new { filmId = request.filmId, imdbId = "tt123" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidIdResponse.StatusCode);
        using var firstResponse = await client.PutAsJsonAsync("/api/golden-globes/imdb-link", request);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var first = await firstResponse.Content.ReadFromJsonAsync<GoldenGlobeImdbLinkResponse>();
        Assert.NotNull(first);
        Assert.Equal("tt12345678", first.ImdbId);
        Assert.NotNull(first.RefreshJob);
        Assert.Equal("queued", first.RefreshJob.Status);

        using var duplicateResponse = await client.PutAsJsonAsync("/api/golden-globes/imdb-link", request);
        var duplicate = await duplicateResponse.Content.ReadFromJsonAsync<GoldenGlobeImdbLinkResponse>();
        Assert.NotNull(duplicate);
        Assert.Equal(first.RefreshJob.Id, duplicate.RefreshJob?.Id);

        await using (var db = new MediaDockDbContext(options))
        {
            var targetRows = await db.GoldenGlobeNominations.Where(row => row.Title == "A: Film" && row.Year == 2025 && row.NomineeType == "movie").ToListAsync();
            Assert.Equal(5, targetRows.Count);
            Assert.All(targetRows, row =>
            {
                Assert.Equal("tt12345678", row.ImdbId);
                Assert.True(row.IsImdbIdManual);
                Assert.Equal(1, row.ImdbIdVersion);
                Assert.Equal("pending", row.EnrichmentStatus);
            });
            Assert.Equal(1, await db.BackgroundJobs.CountAsync(job => job.JobType == "golden_globe_manual_refresh"));
            Assert.Contains("tt12345678", (await db.BackgroundJobs.SingleAsync(job => job.Id == first.RefreshJob.Id)).ResultSummary!);

            using var clearResponse = await client.PutAsJsonAsync("/api/golden-globes/imdb-link", new { filmId = request.filmId, imdbId = (string?)null });
            Assert.Equal(HttpStatusCode.OK, clearResponse.StatusCode);
            var cleared = await clearResponse.Content.ReadFromJsonAsync<GoldenGlobeImdbLinkResponse>();
            Assert.NotNull(cleared);
            Assert.Null(cleared.ImdbId);
            Assert.Null(cleared.RefreshJob);
        }

        await using var verifyDb = new MediaDockDbContext(options);
        var clearedRows = await verifyDb.GoldenGlobeNominations.Where(row => row.Title == "A: Film" && row.Year == 2025 && row.NomineeType == "movie").ToListAsync();
        Assert.All(clearedRows, row =>
        {
            Assert.Null(row.ImdbId);
            Assert.False(row.IsImdbIdManual);
            Assert.Equal(2, row.ImdbIdVersion);
        });
        Assert.Null((await verifyDb.GoldenGlobeNominations.SingleAsync(row => row.ImportKey == "manual-other-year")).ImdbId);
        Assert.Null((await verifyDb.GoldenGlobeNominations.SingleAsync(row => row.ImportKey == "manual-series")).ImdbId);
    }

    private static GoldenGlobeNomination CreateNomination(
        string importKey,
        string title,
        int year,
        bool winner,
        string? imdbId,
        GoldenGlobeAward award,
        string enrichmentStatus = "pending",
        string nomineeType = "movie") => new()
    {
        ImportKey = importKey,
        Title = title,
        Year = year,
        Winner = winner,
        ImdbId = imdbId,
        EnrichmentStatus = enrichmentStatus,
        NomineeType = nomineeType,
        Award = award
    };

    private sealed class GoldenGlobesApiFactory(string connectionString) : WebApplicationFactory<Program>
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