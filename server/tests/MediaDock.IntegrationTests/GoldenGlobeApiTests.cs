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
            db.GoldenGlobeNominations.AddRange(
                CreateNomination("a-picture", "A Film", 2025, true, "tt12345678", bestPicture, "enriched"),
                CreateNomination("a-director", "A Film", 2025, false, "tt12345678", bestDirector, "enriched"),
                CreateNomination("a-series", "A Film", 2025, false, null, bestPicture, nomineeType: "series"),
                CreateNomination("a-older-year", "A Film", 2024, false, null, bestPicture),
                CreateNomination("b-picture", "B Film", 2025, false, null, bestPicture, "not_found"),
                CreateNomination("older-picture", "Older Film", 2024, true, null, bestPicture, "problem"));
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
            await db.SaveChangesAsync();
        }

        using var factory = new GoldenGlobesApiFactory(connectionString);
        using var client = factory.CreateClient();

        using var firstPageResponse = await client.GetAsync("/api/golden-globes?page=1&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, firstPageResponse.StatusCode);
        var firstPage = await firstPageResponse.Content.ReadFromJsonAsync<PageResponse<GoldenGlobeFilmResponse>>();
        Assert.NotNull(firstPage);
        Assert.Equal(5, firstPage.TotalCount);
        Assert.Equal(5, firstPage.TotalPages);
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
        Assert.Equal("2024:movie:A Film", groupedPage.Items.Single(film => film.Title == "A Film" && film.Year == 2024).FilmId);

        using var filteredResponse = await client.GetAsync(
            "/api/golden-globes?yearFrom=2025&yearTo=2025&award=Best%20Motion%20Picture&result=winner");
        var filteredPage = await filteredResponse.Content.ReadFromJsonAsync<PageResponse<GoldenGlobeFilmResponse>>();
        Assert.NotNull(filteredPage);
        var filteredFilm = Assert.Single(filteredPage.Items);
        Assert.Equal("A Film", filteredFilm.Title);
        Assert.True(Assert.Single(filteredFilm.Nominations).IsWinner);

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