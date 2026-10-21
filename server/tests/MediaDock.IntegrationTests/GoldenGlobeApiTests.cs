using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MediaDock.Api.Common;
using MediaDock.Api.GoldenGlobes;
using MediaDock.Application.Metadata;
using MediaDock.Infrastructure.GoldenGlobes;
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
            var aFilmMovie = CreateGroup("A Film", 2025, imdbId: "tt12345678", enrichmentStatus: "enriched");
            var aFilmSeries = CreateGroup("A Film", 2025, "series", "tt87654321", "enriched");
            var manualTypeMismatch = CreateGroup("Manual Type Mismatch", 2025, "series", "tt76543210", "enriched");
            manualTypeMismatch.IsImdbIdManual = true;
            db.ImdbTitles.AddRange(
                CreateImdbTitle("tt12345678", "movie", 8.0m, "https://example.test/a-film.jpg"),
                CreateImdbTitle("tt87654321", "series", 7.4m, "https://example.test/a-film-series.jpg"),
                CreateImdbTitle("tt8749198", "series", 8.1m, "https://example.test/daisy.jpg"),
                CreateImdbTitle("tt32345678", "movie", null, null),
                CreateImdbTitle("tt76543210", "movie", 6.7m, "https://example.test/manual-type-mismatch.jpg"));
            db.GoldenGlobeNominations.AddRange(
                CreateNomination("a-picture", true, aFilmMovie, bestPicture),
                CreateNomination("a-director", false, aFilmMovie, bestDirector),
                CreateNomination("a-series", false, aFilmSeries, bestPicture),
                CreateNomination("a-older-year", false, CreateGroup("A Film", 2024), bestPicture),
                CreateNomination("b-picture", false, CreateGroup("B Film", 2025, enrichmentStatus: "not_found"), bestPicture),
                CreateNomination("older-picture", true, CreateGroup("Older Film", 2024, enrichmentStatus: "problem"), bestPicture),
                CreateNomination("daisy-series", false, CreateGroup("Daisy Jones and the Six", 2024, "series", "tt8749198", "enriched"), bestPicture),
                CreateNomination("no-art", false, CreateGroup("No Art Film", 2025, imdbId: "tt32345678", enrichmentStatus: "enriched"), bestPicture),
                CreateNomination("manual-type-mismatch", false, manualTypeMismatch, bestPicture),
                CreateNomination("broken-enriched", false, CreateGroup("Broken Enriched Film", 2025, enrichmentStatus: "enriched"), bestPicture));
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
        Assert.Equal(new YearBounds(2024, 2025), firstPage.YearBounds);
        Assert.Equal(new ImdbRatingBounds(6.7m, 8.1m), firstPage.ImdbRatingBounds);
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
        Assert.Equal(new YearBounds(2024, 2025), filteredPage.YearBounds);
        Assert.Equal(new ImdbRatingBounds(6.7m, 8.1m), filteredPage.ImdbRatingBounds);
        var filteredFilm = Assert.Single(filteredPage.Items);
        Assert.Equal("A Film", filteredFilm.Title);
        Assert.Single(filteredFilm.Nominations);
        Assert.Contains(filteredFilm.Nominations, nomination => nomination.Award == "Best Motion Picture" && nomination.IsWinner);

        using var ratingFilteredResponse = await client.GetAsync("/api/golden-globes?imdbRatingFrom=8&imdbRatingTo=8.1");
        var ratingFilteredPage = await ratingFilteredResponse.Content.ReadFromJsonAsync<PageResponse<GoldenGlobeFilmResponse>>();
        Assert.NotNull(ratingFilteredPage);
        Assert.Equal(new ImdbRatingBounds(6.7m, 8.1m), ratingFilteredPage.ImdbRatingBounds);
        Assert.Equal(new[] { "A Film", "Daisy Jones and the Six" }, ratingFilteredPage.Items.Select(film => film.Title).OrderBy(title => title));

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
    public async Task CatalogReadsCanonicalMetadataWhenCacheIsExpiredOrMissing()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_golden_globe_canonical_metadata_test").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>().UseNpgsql(connectionString).Options;
        await using (var db = new MediaDockDbContext(options))
        {
            await db.Database.MigrateAsync();
            var award = new GoldenGlobeAward { Name = "Best Motion Picture" };
            db.ImdbTitles.AddRange(
                CreateImdbTitle("tt44556677", "movie", 8.2m, "https://example.test/canonical-film.jpg"),
                CreateImdbTitle("tt44556678", "movie", 7.6m, "https://example.test/expired-film.jpg"));
            db.GoldenGlobeNominations.AddRange(
                CreateNomination("canonical-film", true, CreateGroup("Canonical Film", 2025, imdbId: "tt44556677", enrichmentStatus: "enriched"), award),
                CreateNomination("expired-cache-film", true, CreateGroup("Expired Cache Film", 2024, imdbId: "tt44556678", enrichmentStatus: "enriched"), award));
            var fetchedAt = DateTimeOffset.UtcNow.AddDays(-30);
            db.MetadataCache.Add(new MetadataCacheEntry
            {
                CacheKey = "expired-golden-globe-film-cache",
                LookupTitle = "expired cache film",
                LookupYearSemantics = "title",
                SourceType = "movie",
                Status = "found",
                PayloadJson = JsonSerializer.Serialize(new MetadataDetails(
                    "Expired Cache Film", 2020, "tt44556678", "movie", "movie", "standard", null,
                    1.1m, 10, null, [], [], null, null, "https://example.test/stale-film.jpg", null, null, null)),
                FetchedAt = fetchedAt,
                ExpiresAt = fetchedAt.AddDays(1)
            });
            await db.SaveChangesAsync();
        }

        using var factory = new GoldenGlobesApiFactory(connectionString);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/golden-globes?pageSize=100");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<PageResponse<GoldenGlobeFilmResponse>>();
        Assert.NotNull(page);
        Assert.Equal(new ImdbRatingBounds(7.6m, 8.2m), page.ImdbRatingBounds);
        Assert.Equal(2, page.Items.Count);
        var canonicalFilm = page.Items.Single(film => film.Title == "Canonical Film");
        Assert.Equal(8.2m, canonicalFilm.ImdbRating);
        Assert.Equal("https://example.test/canonical-film.jpg", canonicalFilm.PosterUrl);
        var expiredCacheFilm = page.Items.Single(film => film.Title == "Expired Cache Film");
        Assert.Equal(7.6m, expiredCacheFilm.ImdbRating);
        Assert.Equal("https://example.test/expired-film.jpg", expiredCacheFilm.PosterUrl);

        using var filteredResponse = await client.GetAsync("/api/golden-globes?imdbRatingFrom=8.2&imdbRatingTo=8.2");
        Assert.Equal(HttpStatusCode.OK, filteredResponse.StatusCode);
        var filteredPage = await filteredResponse.Content.ReadFromJsonAsync<PageResponse<GoldenGlobeFilmResponse>>();
        Assert.NotNull(filteredPage);
        Assert.Equal("Canonical Film", Assert.Single(filteredPage.Items).Title);
    }

    [Fact]
    public async Task CatalogReturnsSharedEnrichmentAfterImporterAddsAnotherNomination()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_golden_globe_new_nomination_api_test").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>().UseNpgsql(connectionString).Options;
        const string initialCsv = """
            nominee_type,year,winner,award,title
            film,2025,true,Best Motion Picture,Shared API Film
            """;
        const string updatedCsv = """
            nominee_type,year,winner,award,title
            film,2025,true,Best Motion Picture,Shared API Film
            film,2025,false,Best Director,Shared API Film
            """;
        await using (var db = new MediaDockDbContext(options))
        {
            await db.Database.MigrateAsync();
            var importer = new GoldenGlobeDatasetImporter(db);
            await importer.ImportAsync(Encoding.UTF8.GetBytes(initialCsv));
            var group = await db.GoldenGlobeTitles.SingleAsync(row => row.Title == "Shared API Film");
            group.ImdbId = "tt22334455";
            group.EnrichmentStatus = "enriched";
            group.EnrichmentAttemptCount = 2;
            db.ImdbTitles.Add(CreateImdbTitle("tt22334455", "movie", 8.6m, "https://example.test/shared-api-film.jpg"));
            await db.SaveChangesAsync();

            var import = await importer.ImportAsync(Encoding.UTF8.GetBytes(updatedCsv));
            Assert.Equal(1, import.NominationsCreated);
        }

        using var factory = new GoldenGlobesApiFactory(connectionString);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/golden-globes?pageSize=100");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<PageResponse<GoldenGlobeFilmResponse>>();
        Assert.NotNull(page);
        var film = Assert.Single(page.Items, item => item.Title == "Shared API Film");
        Assert.Equal("tt22334455", film.ImdbId);
        Assert.Equal("enriched", film.EnrichmentStatus);
        Assert.Equal(8.6m, film.ImdbRating);
        Assert.Equal("https://example.test/shared-api-film.jpg", film.PosterUrl);
        Assert.Equal(new[] { "Best Director", "Best Motion Picture" }, film.Nominations.Select(row => row.Award));
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
            var group = CreateGroup("A: Film", 2025);
            group.EnrichmentStatus = "temporary_error";
            group.EnrichmentAttemptCount = 2;
            db.GoldenGlobeNominations.AddRange(
                CreateNomination("manual-pending", false, group, new GoldenGlobeAward { Name = "Manual Award 1" }),
                CreateNomination("manual-enriched", false, group, new GoldenGlobeAward { Name = "Manual Award 2" }),
                CreateNomination("manual-problem", false, group, new GoldenGlobeAward { Name = "Manual Award 3" }),
                CreateNomination("manual-not-found", false, group, new GoldenGlobeAward { Name = "Manual Award 4" }),
                CreateNomination("manual-temporary", false, group, new GoldenGlobeAward { Name = "Manual Award 5" }),
                CreateNomination("manual-other-year", false, CreateGroup("A: Film", 2024), new GoldenGlobeAward { Name = "Manual Other Year" }),
                CreateNomination("manual-series", false, CreateGroup("A: Film", 2025, "series"), new GoldenGlobeAward { Name = "Manual Series" }));
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
            var targetRows = await db.GoldenGlobeNominations
                .Where(row => row.GoldenGlobeTitle!.Title == "A: Film"
                    && row.GoldenGlobeTitle.Year == 2025
                    && row.GoldenGlobeTitle.NomineeType == "movie")
                .ToListAsync();
            Assert.Equal(5, targetRows.Count);
            var linkedGroup = await db.GoldenGlobeTitles.SingleAsync(row => row.Title == "A: Film" && row.Year == 2025 && row.NomineeType == "movie");
            Assert.Equal("tt12345678", linkedGroup.ImdbId);
            Assert.True(linkedGroup.IsImdbIdManual);
            Assert.Equal(1, linkedGroup.ImdbIdVersion);
            Assert.Equal("pending", linkedGroup.EnrichmentStatus);
            Assert.Equal(1, await db.BackgroundJobs.CountAsync(job => job.JobType == "golden_globe_manual_refresh"));
            Assert.Contains("tt12345678", (await db.BackgroundJobs.SingleAsync(job => job.Id == first.RefreshJob.Id)).ResultSummary!);
            Assert.Equal("pending", (await db.ImdbTitles.SingleAsync(title => title.ImdbId == "tt12345678")).MetadataStatus);

            using var clearResponse = await client.PutAsJsonAsync("/api/golden-globes/imdb-link", new { filmId = request.filmId, imdbId = (string?)null });
            Assert.Equal(HttpStatusCode.OK, clearResponse.StatusCode);
            var cleared = await clearResponse.Content.ReadFromJsonAsync<GoldenGlobeImdbLinkResponse>();
            Assert.NotNull(cleared);
            Assert.Null(cleared.ImdbId);
            Assert.Null(cleared.RefreshJob);
        }

        await using var verifyDb = new MediaDockDbContext(options);
        var clearedGroup = await verifyDb.GoldenGlobeTitles.SingleAsync(row => row.Title == "A: Film" && row.Year == 2025 && row.NomineeType == "movie");
        Assert.Null(clearedGroup.ImdbId);
        Assert.False(clearedGroup.IsImdbIdManual);
        Assert.Equal(2, clearedGroup.ImdbIdVersion);
        Assert.Null((await verifyDb.GoldenGlobeTitles.SingleAsync(row => row.Title == "A: Film" && row.Year == 2024)).ImdbId);
        Assert.Null((await verifyDb.GoldenGlobeTitles.SingleAsync(row => row.Title == "A: Film" && row.NomineeType == "series")).ImdbId);
    }

    private static ImdbTitle CreateImdbTitle(string imdbId, string mediaType, decimal? imdbRating, string? posterUrl) => new()
    {
        ImdbId = imdbId,
        MediaType = mediaType,
        ImdbRating = imdbRating,
        PosterUrl = posterUrl,
        MetadataStatus = "enriched",
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static GoldenGlobeTitle CreateGroup(
        string title,
        int year,
        string nomineeType = "movie",
        string? imdbId = null,
        string enrichmentStatus = "pending") => new()
    {
        Title = title,
        Year = year,
        NomineeType = nomineeType,
        ImdbId = imdbId,
        EnrichmentStatus = enrichmentStatus
    };

    private static GoldenGlobeNomination CreateNomination(
        string importKey,
        bool winner,
        GoldenGlobeTitle group,
        GoldenGlobeAward award) => new()
    {
        ImportKey = importKey,
        Winner = winner,
        GoldenGlobeTitle = group,
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