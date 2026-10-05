using System.Net;
using System.Net.Http.Json;
using MediaDock.Api.Common;
using MediaDock.Api.GoldenGlobes;
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
                CreateNomination("a-picture", "A Film", 2025, true, "tt12345678", bestPicture),
                CreateNomination("a-director", "A Film", 2025, false, "tt12345678", bestDirector),
                CreateNomination("b-picture", "B Film", 2025, false, null, bestPicture),
                CreateNomination("older-picture", "Older Film", 2024, true, null, bestPicture));
            await db.SaveChangesAsync();
        }

        using var factory = new GoldenGlobesApiFactory(connectionString);
        using var client = factory.CreateClient();

        using var firstPageResponse = await client.GetAsync("/api/golden-globes?page=1&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, firstPageResponse.StatusCode);
        var firstPage = await firstPageResponse.Content.ReadFromJsonAsync<PageResponse<GoldenGlobeFilmResponse>>();
        Assert.NotNull(firstPage);
        Assert.Equal(3, firstPage.TotalCount);
        Assert.Equal(3, firstPage.TotalPages);
        var firstFilm = Assert.Single(firstPage.Items);
        Assert.Equal("A Film", firstFilm.Title);
        Assert.Equal("tt12345678", firstFilm.ImdbId);
        Assert.Equal(new[] { "Best Director", "Best Motion Picture" }, firstFilm.Nominations.Select(row => row.Award));

        using var filteredResponse = await client.GetAsync(
            "/api/golden-globes?yearFrom=2025&yearTo=2025&award=Best%20Motion%20Picture&result=winner");
        var filteredPage = await filteredResponse.Content.ReadFromJsonAsync<PageResponse<GoldenGlobeFilmResponse>>();
        Assert.NotNull(filteredPage);
        var filteredFilm = Assert.Single(filteredPage.Items);
        Assert.Equal("A Film", filteredFilm.Title);
        Assert.True(Assert.Single(filteredFilm.Nominations).IsWinner);

        using var invalidRangeResponse = await client.GetAsync("/api/golden-globes?yearFrom=2026&yearTo=2025");
        Assert.Equal(HttpStatusCode.BadRequest, invalidRangeResponse.StatusCode);
    }

    private static GoldenGlobeNomination CreateNomination(
        string importKey,
        string title,
        int year,
        bool winner,
        string? imdbId,
        GoldenGlobeAward award) => new()
    {
        ImportKey = importKey,
        Title = title,
        Year = year,
        Winner = winner,
        ImdbId = imdbId,
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