using System.Net;
using System.Net.Http.Json;
using MediaDock.Api.Awards;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace MediaDock.IntegrationTests;

[Trait("Category", "Api")]
public sealed class MovieAwardsApiTests
{
    [Fact]
    public async Task BatchLookupReturnsMatchingOscarAndGoldenGlobesNominations()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_movie_awards_api_test").Build();
        await postgres.StartAsync();

        var connectionString = postgres.GetConnectionString();
        await using (var db = new MediaDockDbContext(new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString).Options))
        {
            await db.Database.MigrateAsync();
            var now = DateTimeOffset.UtcNow;
            db.OscarFilms.Add(new OscarFilm
            {
                StableKey = "shared-oscar-film",
                FilmTitle = "Shared Film",
                NormalizedTitle = "shared film",
                FilmYear = 2024,
                ImportedAt = now,
                UpdatedAt = now,
                Title = new Title
                {
                    TitleText = "Shared Film",
                    NormalizedTitle = "shared film",
                    Year = 2024,
                    MediaType = "movie",
                    ImdbId = "tt1234567",
                    UpdatedAt = now
                },
                Nominations =
                [
                    new OscarNomination
                    {
                        ImportKey = "shared-oscar-nomination",
                        Ceremony = 97,
                        Class = "feature film",
                        CanonicalCategory = "BEST PICTURE",
                        Category = "Best Picture",
                        Name = "Producer",
                        Nominees = "Producer",
                        NomineeIds = string.Empty,
                        Detail = string.Empty,
                        IsWinner = true,
                        ImportedAt = now,
                        UpdatedAt = now
                    }
                ]
            });

            var bestPicture = new GoldenGlobeAward { Name = "Best Motion Picture" };
            db.GoldenGlobeNominations.AddRange(
                new GoldenGlobeNomination
                {
                    ImportKey = "shared-globe-nomination",
                    Year = 2025,
                    Winner = false,
                    Title = "Shared Film",
                    NomineeType = "movie",
                    ImdbId = "tt1234567",
                    Award = bestPicture
                },
                new GoldenGlobeNomination
                {
                    ImportKey = "unrequested-globe-nomination",
                    Year = 2025,
                    Winner = true,
                    Title = "Other Film",
                    NomineeType = "movie",
                    ImdbId = "tt7654321",
                    Award = bestPicture
                },
                new GoldenGlobeNomination
                {
                    ImportKey = "unlinked-globe-nomination",
                    Year = 2025,
                    Winner = true,
                    Title = "Shared Film",
                    NomineeType = "movie",
                    Award = bestPicture
                });
            await db.SaveChangesAsync();
        }

        using var factory = new MovieAwardsApiFactory(connectionString);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/movie-awards?imdbIds=tt1234567");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var awards = await response.Content.ReadFromJsonAsync<MovieAwardRecognitionResponse[]>();
        Assert.NotNull(awards);
        Assert.Equal(2, awards.Length);
        Assert.Contains(awards, award => award.Source == "oscars"
            && award.ImdbId == "tt1234567"
            && award.FilmYear == 2024
            && award.Ceremony == 97
            && award.IsWinner);
        Assert.Contains(awards, award => award.Source == "golden_globes"
            && award.ImdbId == "tt1234567"
            && award.CeremonyYear == 2025
            && !award.IsWinner);

        using var invalidResponse = await client.GetAsync("/api/movie-awards?imdbIds=not-an-imdb-id");
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);

        var tooManyIds = string.Join(',', Enumerable.Repeat("tt1234567", 101));
        using var tooManyResponse = await client.GetAsync($"/api/movie-awards?imdbIds={Uri.EscapeDataString(tooManyIds)}");
        Assert.Equal(HttpStatusCode.BadRequest, tooManyResponse.StatusCode);
    }

    private sealed class MovieAwardsApiFactory(string connectionString) : WebApplicationFactory<Program>
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