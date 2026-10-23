using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using MediaDock.Api.Authentication;
using MediaDock.Api.Catalog;
using MediaDock.Api.Common;
using MediaDock.Api.GoldenGlobes;
using MediaDock.Api.IgnoredMovies;
using MediaDock.Api.OscarAwards;
using MediaDock.Api.Recommendations;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediaDock.IntegrationTests;

[Trait("Category", "Api")]
public sealed class UserIgnoredMoviesApiTests
{
    [Fact]
    public async Task IgnoreListIsOwnerScopedRequiresAnExistingMovieOrSeriesAndFiltersMovieLists()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_user_ignored_movies_api_test").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        long ownerId;
        await using (var db = new MediaDockDbContext(options))
        {
            await db.Database.MigrateAsync();
            var now = DateTimeOffset.UtcNow;
            var owner = CreateUser("ignored-owner@example.com", "ignored-owner", now);
            var otherUser = CreateUser("ignored-other@example.com", "ignored-other", now);
            db.Users.AddRange(owner, otherUser);
            await db.SaveChangesAsync();
            ownerId = owner.Id;

            var ignoredTitle = CreateImdbTitle("tt4100001", "Ignored Candidate", 2023, now);
            var visibleTitle = CreateImdbTitle("tt4100002", "Visible Candidate", 2024, now);
            var seriesTitle = CreateImdbTitle("tt4100003", "A Series", 2024, now, "series");
            db.ImdbTitles.AddRange(ignoredTitle, visibleTitle, seriesTitle);

            var source = new Source
            {
                StableKey = "user-ignored-movies-test",
                Name = "Ignore test",
                FeedType = "movie",
                Url = "https://feed.example/ignored-movies.atom",
                IsEnabled = true
            };
            db.Sources.Add(source);
            var torrentMovies = new[]
            {
                CreateTorrentMovie(ignoredTitle, now),
                CreateTorrentMovie(visibleTitle, now)
            };
            db.TorrentMovies.AddRange(torrentMovies);
            await db.SaveChangesAsync();

            db.TorrentReleases.AddRange(torrentMovies.Select((movie, index) => new TorrentRelease
            {
                TorrentMovieId = movie.Id,
                SourceId = source.Id,
                SourceItemKey = $"ignored-list-{index}",
                TorrentUrl = $"https://feed.example/release/ignored-list-{index}",
                RawTitle = movie.TitleText,
                SourceFeedName = "Movies",
                FeedType = "movie",
                FirstSeenAt = now,
                LastSeenAt = now
            }));

            db.OscarMovies.AddRange(
                CreateOscarMovie(ignoredTitle, now),
                CreateOscarMovie(visibleTitle, now));

            var award = new GoldenGlobeAward { Name = "Best Picture", CategoryKey = "best_picture" };
            db.GoldenGlobeNominations.AddRange(
                CreateGoldenGlobeNomination("ignored-list-ignored", ignoredTitle, award),
                CreateGoldenGlobeNomination("ignored-list-visible", visibleTitle, award));
            await db.SaveChangesAsync();
        }

        using var anonymousFactory = new ApiFactory(connectionString, null);
        using var anonymousClient = anonymousFactory.CreateClient();
        using var anonymousResponse = await anonymousClient.GetAsync("/api/ignored-movies");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var ownerFactory = new ApiFactory(connectionString, CreatePrincipal("ignored-owner", "ignored-owner@example.com"));
        using var ownerClient = ownerFactory.CreateClient();
        using var otherFactory = new ApiFactory(connectionString, CreatePrincipal("ignored-other", "ignored-other@example.com"));
        using var otherClient = otherFactory.CreateClient();

        using var missingMovieResponse = await ownerClient.PostAsJsonAsync(
            "/api/ignored-movies", new { imdbId = "tt49999999" });
        Assert.Equal(HttpStatusCode.NotFound, missingMovieResponse.StatusCode);
        using var seriesResponse = await ownerClient.PostAsJsonAsync(
            "/api/ignored-movies", new { imdbId = "tt4100003" });
        Assert.Equal(HttpStatusCode.OK, seriesResponse.StatusCode);
        var ignoredSeries = await seriesResponse.Content.ReadFromJsonAsync<IgnoredMovieResponse>();
        Assert.NotNull(ignoredSeries);
        Assert.Equal("tt4100003", ignoredSeries.ImdbId);
        Assert.Equal("series", ignoredSeries.MediaType);

        using var addResponse = await ownerClient.PostAsJsonAsync(
            "/api/ignored-movies", new { imdbId = "TT4100001" });
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);
        var ignoredMovie = await addResponse.Content.ReadFromJsonAsync<IgnoredMovieResponse>();
        Assert.NotNull(ignoredMovie);
        Assert.Equal("tt4100001", ignoredMovie.ImdbId);
        Assert.Equal("Ignored Candidate", ignoredMovie.Title);

        using var duplicateResponse = await ownerClient.PostAsJsonAsync(
            "/api/ignored-movies", new { imdbId = "tt4100001" });
        Assert.Equal(HttpStatusCode.OK, duplicateResponse.StatusCode);
        var ownerList = await ownerClient.GetFromJsonAsync<PageResponse<IgnoredMovieResponse>>("/api/ignored-movies");
        Assert.NotNull(ownerList);
        Assert.Equal(2, ownerList.TotalCount);
        Assert.Contains(ownerList.Items, item => item.ImdbId == "tt4100001");
        Assert.Contains(ownerList.Items, item => item.ImdbId == "tt4100003");

        var otherList = await otherClient.GetFromJsonAsync<PageResponse<IgnoredMovieResponse>>("/api/ignored-movies");
        Assert.NotNull(otherList);
        Assert.Empty(otherList.Items);

        var catalog = await ownerClient.GetFromJsonAsync<PageResponse<CatalogTitleResponse>>("/api/catalog?pageSize=100");
        Assert.NotNull(catalog);
        Assert.Equal(1, catalog.TotalCount);
        Assert.Equal("tt4100002", Assert.Single(catalog.Items).ImdbId);

        var oscars = await ownerClient.GetFromJsonAsync<PageResponse<OscarFilmResponse>>("/api/oscars?pageSize=100");
        Assert.NotNull(oscars);
        Assert.Equal(1, oscars.TotalCount);
        Assert.Equal("tt4100002", Assert.Single(oscars.Items).ImdbId);

        var goldenGlobes = await ownerClient.GetFromJsonAsync<PageResponse<GoldenGlobeFilmResponse>>("/api/golden-globes?pageSize=100");
        Assert.NotNull(goldenGlobes);
        Assert.Equal(1, goldenGlobes.TotalCount);
        Assert.Equal("tt4100002", Assert.Single(goldenGlobes.Items).ImdbId);

        var recommendations = await ownerClient.GetFromJsonAsync<PageResponse<RecommendationResponse>>(
            "/api/recommendations?ratingWeight=0.5&votesWeight=0.25&awardsWeight=0.25");
        Assert.NotNull(recommendations);
        Assert.Equal(1, recommendations.TotalCount);
        Assert.Equal("tt4100002", Assert.Single(recommendations.Items).ImdbId);

        var otherCatalog = await otherClient.GetFromJsonAsync<PageResponse<CatalogTitleResponse>>("/api/catalog?pageSize=100");
        Assert.NotNull(otherCatalog);
        Assert.Equal(2, otherCatalog.TotalCount);
        var otherRecommendations = await otherClient.GetFromJsonAsync<PageResponse<RecommendationResponse>>(
            "/api/recommendations?ratingWeight=0.5&votesWeight=0.25&awardsWeight=0.25");
        Assert.NotNull(otherRecommendations);
        Assert.Equal(2, otherRecommendations.TotalCount);

        using var otherDeleteResponse = await otherClient.DeleteAsync("/api/ignored-movies/tt4100001");
        Assert.Equal(HttpStatusCode.NoContent, otherDeleteResponse.StatusCode);
        Assert.Equal(2, (await ownerClient.GetFromJsonAsync<PageResponse<IgnoredMovieResponse>>("/api/ignored-movies"))!.TotalCount);

        using var deleteResponse = await ownerClient.DeleteAsync("/api/ignored-movies/tt4100001");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Equal(1, (await ownerClient.GetFromJsonAsync<PageResponse<IgnoredMovieResponse>>("/api/ignored-movies"))!.TotalCount);

        using var deleteSeriesResponse = await ownerClient.DeleteAsync("/api/ignored-movies/tt4100003");
        Assert.Equal(HttpStatusCode.NoContent, deleteSeriesResponse.StatusCode);
        Assert.Equal(0, (await ownerClient.GetFromJsonAsync<PageResponse<IgnoredMovieResponse>>("/api/ignored-movies"))!.TotalCount);

        await using var foreignKeyDb = new MediaDockDbContext(options);
        foreignKeyDb.UserIgnoredMovies.Add(new UserIgnoredMovie
        {
            UserId = ownerId,
            ImdbId = "tt49999998",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => foreignKeyDb.SaveChangesAsync());
    }

    private static User CreateUser(string email, string subject, DateTimeOffset now)
    {
        var user = new User
        {
            NormalizedEmail = email,
            GivenName = "Test",
            FamilyName = "User",
            Status = "active",
            Role = "user",
            CreatedAt = now,
            UpdatedAt = now
        };
        user.ExternalIdentities.Add(new ExternalIdentity
        {
            User = user,
            Issuer = GoogleSignInSettings.GoogleIssuer,
            Subject = subject,
            CreatedAt = now
        });
        return user;
    }

    private static ImdbTitle CreateImdbTitle(
        string imdbId,
        string title,
        int year,
        DateTimeOffset now,
        string mediaType = "movie") => new()
    {
        ImdbId = imdbId,
        TitleText = title,
        Year = year,
        MediaType = mediaType,
        ImdbRating = 7.5m,
        ImdbVotes = 5000,
        NormalizedVoteWeight = 0.5d,
        Genres = ["Drama"],
        MetadataStatus = "enriched",
        UpdatedAt = now
    };

    private static TorrentMovie CreateTorrentMovie(ImdbTitle title, DateTimeOffset now) => new()
    {
        TitleText = title.TitleText!,
        NormalizedTitle = title.TitleText!.ToLowerInvariant(),
        Year = title.Year,
        MediaType = "movie",
        SourceType = "movie",
        ContentKind = "standard",
        ImdbId = title.ImdbId,
        FirstSeenAt = now,
        LastSeenAt = now,
        UpdatedAt = now
    };

    private static OscarMovie CreateOscarMovie(ImdbTitle title, DateTimeOffset now) => new()
    {
        StableKey = $"ignored-list-{title.ImdbId}",
        FilmTitle = title.TitleText!,
        NormalizedTitle = title.TitleText!.ToLowerInvariant(),
        FilmYear = title.Year!.Value,
        ImdbId = title.ImdbId,
        EnrichmentStatus = "enriched",
        ImportedAt = now,
        UpdatedAt = now,
        Nominations =
        [
            new OscarNomination
            {
                ImportKey = $"ignored-list-{title.ImdbId}",
                Ceremony = 97,
                Class = "feature film",
                CanonicalCategory = "BEST PICTURE",
                Category = "Best Picture",
                Name = "Producer",
                Nominees = "Producer",
                NomineeIds = string.Empty,
                Detail = string.Empty,
                IsWinner = false,
                ImportedAt = now,
                UpdatedAt = now
            }
        ]
    };

    private static GoldenGlobeNomination CreateGoldenGlobeNomination(
        string importKey,
        ImdbTitle title,
        GoldenGlobeAward award) => new()
    {
        ImportKey = importKey,
        Winner = false,
        Award = award,
        GoldenGlobeTitle = new GoldenGlobeTitle
        {
            Title = title.TitleText!,
            Year = title.Year!.Value,
            NomineeType = "movie",
            ImdbId = title.ImdbId,
            EnrichmentStatus = "enriched"
        }
    };

    private static ClaimsPrincipal CreatePrincipal(string subject, string email) => new(new ClaimsIdentity(
    [
        new Claim(GoogleSignInSettings.IssuerClaimType, GoogleSignInSettings.GoogleIssuer),
        new Claim("sub", subject),
        new Claim("email", email),
        new Claim("email_verified", "true")
    ], "test-google"));

    private sealed class ApiFactory(string connectionString, ClaimsPrincipal? principal) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MediaDock"] = connectionString,
                    ["Authentication:Google:Enabled"] = "false",
                    ["AccessControl:Enforced"] = "false"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddAuthentication()
                    .AddScheme<TestAuthenticationOptions, TestAuthenticationHandler>(
                        TestAuthenticationOptions.Scheme,
                        scheme => scheme.Principal = principal);
                services.PostConfigure<AuthenticationOptions>(authentication =>
                    authentication.DefaultAuthenticateScheme = TestAuthenticationOptions.Scheme);
            });
        }
    }

    private sealed class TestAuthenticationOptions : AuthenticationSchemeOptions
    {
        public const string Scheme = "TestGoogle";
        public ClaimsPrincipal? Principal { get; set; }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<TestAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<TestAuthenticationOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Options.Principal is null)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var ticket = new AuthenticationTicket(Options.Principal, TestAuthenticationOptions.Scheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}