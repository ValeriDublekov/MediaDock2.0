using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using MediaDock.Api.Authentication;
using MediaDock.Api.Common;
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
public sealed class MovieRecommendationsApiTests
{
    [Fact]
    public async Task GetRecommendationsIsOwnerScopedFilteredScoredAndPaged()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_recommendations_api_test").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        long firstUserId;
        long secondUserId;
        await using (var db = new MediaDockDbContext(new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString).Options))
        {
            await db.Database.MigrateAsync();
            var now = DateTimeOffset.UtcNow;

            User CreateUser(string email, string subject)
            {
                var user = new User
                {
                    NormalizedEmail = email,
                    GivenName = "Recommendation",
                    FamilyName = "Test",
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

            var firstUser = CreateUser("recommendations-first@example.com", "recommendations-first");
            var secondUser = CreateUser("recommendations-second@example.com", "recommendations-second");
            db.Users.AddRange(firstUser, secondUser);
            await db.SaveChangesAsync();
            firstUserId = firstUser.Id;
            secondUserId = secondUser.Id;

            var titles = new Dictionary<string, ImdbTitle>(StringComparer.Ordinal);
            ImdbTitle AddTitle(
                string imdbId,
                string? titleText,
                decimal? imdbRating,
                string mediaType = "movie",
                string metadataStatus = "enriched")
            {
                var title = new ImdbTitle
                {
                    ImdbId = imdbId,
                    TitleText = titleText,
                    Year = 2025,
                    MediaType = mediaType,
                    ImdbRating = imdbRating,
                    ImdbVotes = 1000,
                    NormalizedVoteWeight = 0.5d,
                    PosterUrl = $"https://example.com/{imdbId}.jpg",
                    Genres = ["Drama"],
                    MetadataStatus = metadataStatus,
                    UpdatedAt = now
                };
                titles.Add(imdbId, title);
                db.ImdbTitles.Add(title);
                return title;
            }

            AddTitle("tt3000001", "Winner One", 8m);
            AddTitle("tt3000002", "Winner Two", 8m);
            AddTitle("tt3000003", "Winner Three", 8m);
            AddTitle("tt3000004", "Candidate A", 7m);
            AddTitle("tt3000005", "Candidate B", 7m);
            AddTitle("tt3000006", "Download Only", 7m);
            AddTitle("tt3000007", "Watch List", 7m);
            AddTitle("tt3000008", "Rated Candidate", 7m);
            AddTitle("tt3000009", "Series Nominee", 7m);
            AddTitle("tt3000010", "Linked Series", 8m, "series");
            AddTitle("tt3000011", "Pending Candidate", 7m, metadataStatus: "pending");
            AddTitle("tt3000012", "Missing Rating", null);
            AddTitle("tt3000013", " ", null);
            AddTitle("tt3000014", "Unawarded Movie", 7m);
            AddTitle("tt3000015", "Unknown Award", 7m);
            AddTitle("tt3000016", "Other Rated Title", null, metadataStatus: "pending");
            await db.SaveChangesAsync();

            void AddOscarMovie(string imdbId, string stableKey, bool isWinner, int duplicateRows = 1)
            {
                var titleText = titles[imdbId].TitleText?.Trim();
                var displayTitle = string.IsNullOrEmpty(titleText) ? stableKey : titleText;
                var oscarMovie = new OscarMovie
                {
                    StableKey = stableKey,
                    FilmTitle = displayTitle,
                    NormalizedTitle = displayTitle.ToLowerInvariant(),
                    FilmYear = 2025,
                    ImdbId = imdbId,
                    EnrichmentStatus = "enriched",
                    ImportedAt = now,
                    UpdatedAt = now
                };
                for (var row = 0; row < duplicateRows; row++)
                {
                    oscarMovie.Nominations.Add(new OscarNomination
                    {
                        ImportKey = $"{stableKey}-{row}",
                        Ceremony = 97,
                        Class = "feature film",
                        CanonicalCategory = "BEST PICTURE",
                        Category = "Best Picture",
                        Name = "Producer",
                        Nominees = "Producer",
                        NomineeIds = string.Empty,
                        Detail = string.Empty,
                        IsWinner = isWinner,
                        ImportedAt = now,
                        UpdatedAt = now
                    });
                }

                db.OscarMovies.Add(oscarMovie);
            }

            AddOscarMovie("tt3000001", "recommendation-winner-one", true, duplicateRows: 2);
            AddOscarMovie("tt3000002", "recommendation-winner-two", true);
            AddOscarMovie("tt3000003", "recommendation-winner-three", true);
            AddOscarMovie("tt3000004", "recommendation-candidate-a", false, duplicateRows: 2);
            AddOscarMovie("tt3000005", "recommendation-candidate-b", false);
            AddOscarMovie("tt3000007", "recommendation-watch-list", false);
            AddOscarMovie("tt3000008", "recommendation-rated-candidate", false);
            AddOscarMovie("tt3000011", "recommendation-pending-candidate", false);
            AddOscarMovie("tt3000012", "recommendation-missing-rating", false);
            AddOscarMovie("tt3000013", "recommendation-empty-title", false);

            var dramaAward = new GoldenGlobeAward
            {
                Name = "Best Motion Picture - Drama",
                CategoryKey = "best_motion_picture_drama"
            };
            var unknownAward = new GoldenGlobeAward
            {
                Name = "Unmapped Movie Award",
                CategoryKey = "award_other"
            };
            db.GoldenGlobeAwards.AddRange(dramaAward, unknownAward);
            db.GoldenGlobeNominations.AddRange(
                CreateGoldenGlobeNomination("recommendation-download-only", "Download Only", "movie", "tt3000006", dramaAward),
                CreateGoldenGlobeNomination("recommendation-unknown-award", "Unknown Award", "movie", "tt3000015", unknownAward),
                CreateGoldenGlobeNomination("recommendation-series-nominee", "Series Nominee", "series", "tt3000009", dramaAward),
                CreateGoldenGlobeNomination("recommendation-linked-series", "Linked Series", "movie", "tt3000010", dramaAward),
                CreateGoldenGlobeNomination("recommendation-unmatched", "Unmatched Nominee", "movie", null, dramaAward));
            await db.SaveChangesAsync();

            void AddRating(long userId, string imdbId, int rating) => db.PersonalRatings.Add(new PersonalRating
            {
                UserId = userId,
                ImdbId = imdbId,
                Rating = rating,
                UpdatedAt = now
            });

            AddRating(firstUserId, "tt3000001", 10);
            AddRating(firstUserId, "tt3000002", 10);
            AddRating(firstUserId, "tt3000003", 10);
            AddRating(firstUserId, "tt3000008", 1);
            AddRating(firstUserId, "tt3000016", 1);
            AddRating(secondUserId, "tt3000001", 1);
            AddRating(secondUserId, "tt3000002", 1);
            AddRating(secondUserId, "tt3000003", 1);
            AddRating(secondUserId, "tt3000004", 1);
            db.ImdbFavoriteMovies.AddRange(
                new ImdbFavoriteMovie
                {
                    UserId = firstUserId,
                    ImdbId = "tt3000006",
                    ToDownload = true,
                    AddedFromCatalog = true,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new ImdbFavoriteMovie
                {
                    UserId = firstUserId,
                    ImdbId = "tt3000007",
                    ToWatch = true,
                    AddedFromCatalog = true,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            await db.SaveChangesAsync();
        }

        static GoldenGlobeNomination CreateGoldenGlobeNomination(
            string importKey,
            string title,
            string nomineeType,
            string? imdbId,
            GoldenGlobeAward award) => new()
        {
            ImportKey = importKey,
            Winner = false,
            Award = award,
            GoldenGlobeTitle = new GoldenGlobeTitle
            {
                Title = title,
                Year = 2025,
                NomineeType = nomineeType,
                ImdbId = imdbId,
                EnrichmentStatus = "enriched"
            }
        };

        var firstPrincipal = CreatePrincipal("recommendations-first", "recommendations-first@example.com");
        const string weights = "ratingWeight=0.5&votesWeight=0.25&awardsWeight=0.25";
        using var anonymousFactory = new RecommendationsApiFactory(connectionString, null);
        using var anonymousClient = anonymousFactory.CreateClient();
        using var anonymousResponse = await anonymousClient.GetAsync($"/api/recommendations?{weights}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var unknownFactory = new RecommendationsApiFactory(
            connectionString,
            CreatePrincipal("recommendations-unknown", "unknown@example.com"));
        using var unknownClient = unknownFactory.CreateClient();
        using var unknownResponse = await unknownClient.GetAsync($"/api/recommendations?{weights}");
        Assert.Equal(HttpStatusCode.Forbidden, unknownResponse.StatusCode);

        using var firstFactory = new RecommendationsApiFactory(connectionString, firstPrincipal);
        using var firstClient = firstFactory.CreateClient();
        using var firstResponse = await firstClient.GetAsync($"/api/recommendations?{weights}");
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var firstPage = await firstResponse.Content.ReadFromJsonAsync<PageResponse<RecommendationResponse>>();
        Assert.NotNull(firstPage);
        Assert.Equal(1, firstPage.Page);
        Assert.Equal(25, firstPage.PageSize);
        Assert.Equal(4, firstPage.TotalCount);
        Assert.Equal(1, firstPage.TotalPages);
        Assert.Equal(
            ["tt3000004", "tt3000005", "tt3000006", "tt3000015"],
            firstPage.Items.Select(item => item.ImdbId));

        var candidate = firstPage.Items[0];
        Assert.Equal("Candidate A", candidate.Title);
        Assert.Equal("movie", candidate.MediaType);
        Assert.Equal(0.5m, candidate.PopularityComponent);
        Assert.InRange(Math.Abs(candidate.RatingComponent - ((7m + 80m / 11m) / 2m)), 0m, 0.000001m);
        Assert.Equal(1.5m, candidate.AwardComponent);
        Assert.Equal(1.5m, candidate.AwardEvents.Single().AdjustedPoints);
        Assert.Equal("nomination", candidate.AwardEvents.Single().Outcome);
        Assert.Equal<int?>(97, candidate.AwardEvents.Single().Ceremony);
        Assert.Null(candidate.AwardEvents.Single().CeremonyYear);
        Assert.Equal(0.5m, candidate.RatingWeight);
        Assert.Equal(0.25m, candidate.VotesWeight);
        Assert.Equal(0.25m, candidate.AwardsWeight);
        Assert.InRange(candidate.FinalScore, 0m, 10m);

        var downloadOnly = firstPage.Items.Single(item => item.ImdbId == "tt3000006");
        Assert.Equal("golden_globes", downloadOnly.AwardEvents.Single().AwardSource);
        Assert.Equal("best_motion_picture_drama", downloadOnly.AwardEvents.Single().CategoryKey);
        Assert.Null(downloadOnly.AwardEvents.Single().Ceremony);
        Assert.Equal<int?>(2025, downloadOnly.AwardEvents.Single().CeremonyYear);
        Assert.Equal(0.5m, downloadOnly.AwardComponent);
        var unknownCategory = firstPage.Items.Single(item => item.ImdbId == "tt3000015");
        Assert.Equal("award_other", unknownCategory.AwardEvents.Single().CategoryKey);
        Assert.Equal(0m, unknownCategory.AwardComponent);

        using var firstSortedPageResponse = await firstClient.GetAsync(
            $"/api/recommendations?{weights}&page=1&pageSize=1");
        var firstSortedPage = await firstSortedPageResponse.Content.ReadFromJsonAsync<PageResponse<RecommendationResponse>>();
        Assert.Equal("tt3000004", firstSortedPage?.Items.Single().ImdbId);
        using var secondSortedPageResponse = await firstClient.GetAsync(
            $"/api/recommendations?{weights}&page=2&pageSize=1");
        var secondSortedPage = await secondSortedPageResponse.Content.ReadFromJsonAsync<PageResponse<RecommendationResponse>>();
        Assert.Equal("tt3000005", secondSortedPage?.Items.Single().ImdbId);
        using var pastEndResponse = await firstClient.GetAsync(
            $"/api/recommendations?{weights}&page=5&pageSize=1");
        var pastEndPage = await pastEndResponse.Content.ReadFromJsonAsync<PageResponse<RecommendationResponse>>();
        Assert.Empty(pastEndPage!.Items);
        Assert.Equal(4, pastEndPage.TotalCount);
        Assert.Equal(4, pastEndPage.TotalPages);

        using var invalidSumResponse = await firstClient.GetAsync(
            "/api/recommendations?ratingWeight=0.5&votesWeight=0.25&awardsWeight=0.3");
        Assert.Equal(HttpStatusCode.BadRequest, invalidSumResponse.StatusCode);
        using var missingWeightsResponse = await firstClient.GetAsync("/api/recommendations");
        Assert.Equal(HttpStatusCode.BadRequest, missingWeightsResponse.StatusCode);
        using var invalidPageSizeResponse = await firstClient.GetAsync(
            $"/api/recommendations?{weights}&pageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, invalidPageSizeResponse.StatusCode);

        using var secondFactory = new RecommendationsApiFactory(
            connectionString,
            CreatePrincipal("recommendations-second", "recommendations-second@example.com"));
        using var secondClient = secondFactory.CreateClient();
        using var secondResponse = await secondClient.GetAsync($"/api/recommendations?{weights}");
        var secondPage = await secondResponse.Content.ReadFromJsonAsync<PageResponse<RecommendationResponse>>();
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.NotNull(secondPage);
        Assert.Equal(5, secondPage.TotalCount);
        Assert.DoesNotContain(secondPage.Items, item => item.ImdbId == "tt3000004");
        Assert.Contains(secondPage.Items, item => item.ImdbId == "tt3000007");
        Assert.Contains(secondPage.Items, item => item.ImdbId == "tt3000008");
        Assert.Equal(1m, secondPage.Items.Single(item => item.ImdbId == "tt3000005").AwardComponent);

        using var enforcedFactory = new RecommendationsApiFactory(connectionString, firstPrincipal, accessControlEnforced: true);
        using var enforcedClient = enforcedFactory.CreateClient();
        using var enforcedResponse = await enforcedClient.GetAsync($"/api/recommendations?{weights}");
        Assert.Equal(HttpStatusCode.OK, enforcedResponse.StatusCode);
        var enforcedPage = await enforcedResponse.Content.ReadFromJsonAsync<PageResponse<RecommendationResponse>>();
        Assert.Equal(firstPage.TotalCount, enforcedPage?.TotalCount);
        Assert.Equal(firstPage.Items.Select(item => item.ImdbId), enforcedPage?.Items.Select(item => item.ImdbId));
    }

    private static ClaimsPrincipal CreatePrincipal(string subject, string email) => new(new ClaimsIdentity(
    [
        new Claim(GoogleSignInSettings.IssuerClaimType, GoogleSignInSettings.GoogleIssuer),
        new Claim("sub", subject),
        new Claim("email", email),
        new Claim("email_verified", "true")
    ], "test-google"));

    private sealed class RecommendationsApiFactory(
        string connectionString,
        ClaimsPrincipal? principal,
        bool accessControlEnforced = false) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MediaDock"] = connectionString,
                    ["Authentication:Google:Enabled"] = "false",
                    ["AccessControl:Enforced"] = accessControlEnforced.ToString()
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddAuthentication()
                    .AddScheme<TestAuthenticationOptions, TestAuthenticationHandler>(
                        TestAuthenticationOptions.Scheme,
                        options => options.Principal = principal);
                services.PostConfigure<AuthenticationOptions>(options =>
                    options.DefaultAuthenticateScheme = TestAuthenticationOptions.Scheme);
            });
        }
    }

    private sealed class TestAuthenticationOptions : AuthenticationSchemeOptions
    {
        public const string Scheme = "RecommendationTest";
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