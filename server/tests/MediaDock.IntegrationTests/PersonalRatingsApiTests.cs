using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using MediaDock.Api.Authentication;
using MediaDock.Api.Common;
using MediaDock.Api.PersonalRatings;
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
public sealed class PersonalRatingsApiTests
{
    [Fact]
    public async Task ListIsPagedSortedAndScopedToTheActiveUserInOpenMode()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_personal_ratings_list_api_test").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using (var db = new MediaDockDbContext(options))
        {
            await db.Database.MigrateAsync();
            var now = DateTimeOffset.UtcNow;

            User CreateUser(string email, string subject, string status = "active", string role = "user")
            {
                var user = new User
                {
                    NormalizedEmail = email,
                    GivenName = "Test",
                    FamilyName = "User",
                    Status = status,
                    Role = role,
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

            var firstUser = CreateUser("list-first@example.com", "ratings-list-first");
            var secondUser = CreateUser("list-second@example.com", "ratings-list-second");
            var deactivatedUser = CreateUser("list-disabled@example.com", "ratings-list-disabled", "deactivated");
            var adminUser = CreateUser("list-admin@example.com", "ratings-list-admin", role: "admin");
            db.Users.AddRange(firstUser, secondUser, deactivatedUser, adminUser);
            await db.SaveChangesAsync();

            db.ImdbTitles.AddRange(
                new Infrastructure.Persistence.Entities.ImdbTitle
                {
                    ImdbId = "tt0000001",
                    MetadataStatus = "pending",
                    UpdatedAt = now
                },
                new Infrastructure.Persistence.Entities.ImdbTitle
                {
                    ImdbId = "tt0000002",
                    MetadataStatus = "not_found",
                    UpdatedAt = now
                },
                new Infrastructure.Persistence.Entities.ImdbTitle
                {
                    ImdbId = "tt0000003",
                    TitleText = "Lower Rated",
                    Year = 1984,
                    MediaType = "movie",
                    ImdbRating = 6.5m,
                    MetadataStatus = "enriched",
                    UpdatedAt = now
                },
                new Infrastructure.Persistence.Entities.ImdbTitle
                {
                    ImdbId = "tt0000004",
                    TitleText = "Higher Rated",
                    Year = 2016,
                    MediaType = "movie",
                    ImdbRating = 8.1m,
                    ImdbVotes = 12345,
                    Metascore = 75m,
                    PosterUrl = "https://example.com/poster.jpg",
                    Genres = ["Drama"],
                    Countries = ["United States"],
                    Director = "Example Director",
                    MetadataStatus = "enriched",
                    UpdatedAt = now
                },
                new Infrastructure.Persistence.Entities.ImdbTitle
                {
                    ImdbId = "tt0000005",
                    MetadataStatus = "pending",
                    UpdatedAt = now
                });
            await db.SaveChangesAsync();

            db.PersonalRatings.AddRange(
                new PersonalRating { UserId = firstUser.Id, ImdbId = "tt0000001", Rating = 8, UpdatedAt = now },
                new PersonalRating { UserId = firstUser.Id, ImdbId = "tt0000002", Rating = 8, UpdatedAt = now },
                new PersonalRating { UserId = firstUser.Id, ImdbId = "tt0000003", Rating = 2, UpdatedAt = now },
                new PersonalRating { UserId = firstUser.Id, ImdbId = "tt0000004", Rating = 10, UpdatedAt = now },
                new PersonalRating { UserId = secondUser.Id, ImdbId = "tt0000001", Rating = 1, UpdatedAt = now },
                new PersonalRating { UserId = secondUser.Id, ImdbId = "tt0000005", Rating = 9, UpdatedAt = now });
            await db.SaveChangesAsync();
        }

        using var anonymousFactory = new ApiFactory(connectionString);
        using var anonymousClient = anonymousFactory.CreateClient();
        using var anonymousResponse = await anonymousClient.GetAsync("/api/personal-ratings");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var unknownFactory = new ApiFactory(
            connectionString, CreatePrincipal("ratings-list-unknown", "unknown@example.com"));
        using var unknownClient = unknownFactory.CreateClient();
        using var unknownResponse = await unknownClient.GetAsync("/api/personal-ratings");
        Assert.Equal(HttpStatusCode.Forbidden, unknownResponse.StatusCode);

        using var deactivatedFactory = new ApiFactory(
            connectionString, CreatePrincipal("ratings-list-disabled", "list-disabled@example.com"));
        using var deactivatedClient = deactivatedFactory.CreateClient();
        using var deactivatedResponse = await deactivatedClient.GetAsync("/api/personal-ratings");
        Assert.Equal(HttpStatusCode.Forbidden, deactivatedResponse.StatusCode);

        using var firstFactory = new ApiFactory(
            connectionString, CreatePrincipal("ratings-list-first", "list-first@example.com"));
        using var firstClient = firstFactory.CreateClient();
        using var secondFactory = new ApiFactory(
            connectionString, CreatePrincipal("ratings-list-second", "list-second@example.com"));
        using var secondClient = secondFactory.CreateClient();
        using var adminFactory = new ApiFactory(
            connectionString, CreatePrincipal("ratings-list-admin", "list-admin@example.com"));
        using var adminClient = adminFactory.CreateClient();

        using var defaultResponse = await firstClient.GetAsync("/api/personal-ratings");
        Assert.Equal(HttpStatusCode.OK, defaultResponse.StatusCode);
        var defaultPage = await defaultResponse.Content.ReadFromJsonAsync<PageResponse<PersonalRatingResponse>>();
        Assert.NotNull(defaultPage);
        Assert.Equal(4, defaultPage.TotalCount);
        Assert.Equal(1, defaultPage.Page);
        Assert.Equal(25, defaultPage.PageSize);
        Assert.Equal(1, defaultPage.TotalPages);
        Assert.Equal(
            ["tt0000004", "tt0000001", "tt0000002", "tt0000003"],
            defaultPage.Items.Select(item => item.ImdbId));
        Assert.Equal("Higher Rated", defaultPage.Items[0].Title);
        Assert.Equal(2016, defaultPage.Items[0].Year);
        Assert.Equal(8.1m, defaultPage.Items[0].ImdbRating);
        Assert.Equal(12345, defaultPage.Items[0].ImdbVotes);
        Assert.Equal(75m, defaultPage.Items[0].Metascore);
        Assert.Equal("https://example.com/poster.jpg", defaultPage.Items[0].PosterUrl);
        Assert.Equal("tt0000001", defaultPage.Items[1].Title);
        Assert.Equal("pending", defaultPage.Items[1].MetadataStatus);
        Assert.Equal("tt0000002", defaultPage.Items[2].Title);
        Assert.Equal("not_found", defaultPage.Items[2].MetadataStatus);

        using var firstPageResponse = await firstClient.GetAsync("/api/personal-ratings?page=1&pageSize=2");
        var firstPage = await firstPageResponse.Content.ReadFromJsonAsync<PageResponse<PersonalRatingResponse>>();
        Assert.Equal(["tt0000004", "tt0000001"], firstPage?.Items.Select(item => item.ImdbId));
        using var secondPageResponse = await firstClient.GetAsync("/api/personal-ratings?page=2&pageSize=2");
        var secondPage = await secondPageResponse.Content.ReadFromJsonAsync<PageResponse<PersonalRatingResponse>>();
        Assert.Equal(["tt0000002", "tt0000003"], secondPage?.Items.Select(item => item.ImdbId));
        using var pastEndResponse = await firstClient.GetAsync("/api/personal-ratings?page=3&pageSize=2");
        var pastEndPage = await pastEndResponse.Content.ReadFromJsonAsync<PageResponse<PersonalRatingResponse>>();
        Assert.Empty(pastEndPage!.Items);
        Assert.Equal(4, pastEndPage.TotalCount);
        Assert.Equal(2, pastEndPage.TotalPages);

        using var ascendingResponse = await firstClient.GetAsync("/api/personal-ratings?sort=asc");
        var ascendingPage = await ascendingResponse.Content.ReadFromJsonAsync<PageResponse<PersonalRatingResponse>>();
        Assert.Equal(
            ["tt0000003", "tt0000001", "tt0000002", "tt0000004"],
            ascendingPage?.Items.Select(item => item.ImdbId));

        using var invalidPageResponse = await firstClient.GetAsync("/api/personal-ratings?page=0");
        Assert.Equal(HttpStatusCode.BadRequest, invalidPageResponse.StatusCode);
        using var invalidPageSizeResponse = await firstClient.GetAsync("/api/personal-ratings?pageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, invalidPageSizeResponse.StatusCode);
        using var invalidSortResponse = await firstClient.GetAsync("/api/personal-ratings?sort=invalid");
        Assert.Equal(HttpStatusCode.BadRequest, invalidSortResponse.StatusCode);

        using var secondUserResponse = await secondClient.GetAsync("/api/personal-ratings");
        var secondUserPage = await secondUserResponse.Content.ReadFromJsonAsync<PageResponse<PersonalRatingResponse>>();
        Assert.Equal(2, secondUserPage?.TotalCount);
        Assert.Equal(1, secondUserPage?.Items.Single(item => item.ImdbId == "tt0000001").Rating);
        Assert.DoesNotContain(secondUserPage!.Items, item => item.ImdbId == "tt0000002");

        using var adminResponse = await adminClient.GetAsync("/api/personal-ratings");
        var adminPage = await adminResponse.Content.ReadFromJsonAsync<PageResponse<PersonalRatingResponse>>();
        Assert.Empty(adminPage!.Items);
        Assert.Equal(0, adminPage.TotalCount);
        Assert.Equal(0, adminPage.TotalPages);
    }

    [Fact]
    public async Task EnrichmentQueueIsOwnerScopedDeduplicatedAndOmitsRatingDataFromGlobalJobs()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_personal_ratings_enrichment_api_test").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        long firstUserId;
        long secondUserId;
        long deferredUserId;
        await using (var db = new MediaDockDbContext(options))
        {
            await db.Database.MigrateAsync();
            var now = DateTimeOffset.UtcNow;

            User CreateUser(string email, string subject)
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

            var firstUser = CreateUser("enrichment-first@example.com", "ratings-enrichment-first");
            var secondUser = CreateUser("enrichment-second@example.com", "ratings-enrichment-second");
            var deferredUser = CreateUser("enrichment-deferred@example.com", "ratings-enrichment-deferred");
            db.Users.AddRange(firstUser, secondUser, deferredUser);
            await db.SaveChangesAsync();
            firstUserId = firstUser.Id;
            secondUserId = secondUser.Id;
            deferredUserId = deferredUser.Id;

            db.ImdbTitles.AddRange(
                new Infrastructure.Persistence.Entities.ImdbTitle
                {
                    ImdbId = "tt1111111",
                    MetadataStatus = "pending",
                    UpdatedAt = now
                },
                new Infrastructure.Persistence.Entities.ImdbTitle
                {
                    ImdbId = "tt2222222",
                    MetadataStatus = "pending",
                    UpdatedAt = now
                },
                new Infrastructure.Persistence.Entities.ImdbTitle
                {
                    ImdbId = "tt3333333",
                    MetadataStatus = "temporary_error",
                    NextEnrichmentAttemptAt = now.AddHours(1),
                    UpdatedAt = now
                });
            db.PersonalRatings.AddRange(
                new PersonalRating { UserId = firstUserId, ImdbId = "tt1111111", Rating = 8, UpdatedAt = now },
                new PersonalRating { UserId = secondUserId, ImdbId = "tt2222222", Rating = 7, UpdatedAt = now },
                new PersonalRating { UserId = deferredUserId, ImdbId = "tt3333333", Rating = 6, UpdatedAt = now });
            await db.SaveChangesAsync();
        }

        using var anonymousFactory = new ApiFactory(connectionString);
        using var anonymousClient = anonymousFactory.CreateClient();
        using var anonymousResponse = await anonymousClient.PostAsync("/api/personal-ratings/enrichment", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var firstFactory = new ApiFactory(
            connectionString, CreatePrincipal("ratings-enrichment-first", "enrichment-first@example.com"));
        using var firstClient = firstFactory.CreateClient();
        using var secondFactory = new ApiFactory(
            connectionString, CreatePrincipal("ratings-enrichment-second", "enrichment-second@example.com"));
        using var secondClient = secondFactory.CreateClient();
        using var deferredFactory = new ApiFactory(
            connectionString, CreatePrincipal("ratings-enrichment-deferred", "enrichment-deferred@example.com"));
        using var deferredClient = deferredFactory.CreateClient();
        var firstToken = await GetAntiforgeryTokenAsync(firstClient);
        var secondToken = await GetAntiforgeryTokenAsync(secondClient);
        var deferredToken = await GetAntiforgeryTokenAsync(deferredClient);

        using var missingTokenResponse = await firstClient.PostAsync("/api/personal-ratings/enrichment", content: null);
        Assert.Equal(HttpStatusCode.BadRequest, missingTokenResponse.StatusCode);

        using var firstResponse = await EnqueueEnrichmentAsync(firstClient, firstToken);
        Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);
        var firstJob = await firstResponse.Content.ReadFromJsonAsync<PersonalRatingsEnrichmentAcceptedResponse>();
        Assert.NotNull(firstJob);

        using var duplicateResponse = await EnqueueEnrichmentAsync(firstClient, firstToken);
        Assert.Equal(HttpStatusCode.Accepted, duplicateResponse.StatusCode);
        var duplicateJob = await duplicateResponse.Content.ReadFromJsonAsync<PersonalRatingsEnrichmentAcceptedResponse>();
        Assert.Equal(firstJob.JobId, duplicateJob?.JobId);

        using var secondResponse = await EnqueueEnrichmentAsync(secondClient, secondToken);
        Assert.Equal(HttpStatusCode.Accepted, secondResponse.StatusCode);
        var secondJob = await secondResponse.Content.ReadFromJsonAsync<PersonalRatingsEnrichmentAcceptedResponse>();
        Assert.NotNull(secondJob);
        Assert.NotEqual(firstJob.JobId, secondJob.JobId);

        using var deferredResponse = await EnqueueEnrichmentAsync(deferredClient, deferredToken);
        Assert.Equal(HttpStatusCode.NoContent, deferredResponse.StatusCode);

        using var jobStatusResponse = await firstClient.GetAsync($"/api/background-jobs/{firstJob.JobId}");
        var jobStatusJson = await jobStatusResponse.Content.ReadAsStringAsync();
        Assert.True(jobStatusResponse.StatusCode == HttpStatusCode.OK, jobStatusJson);
        Assert.DoesNotContain("tt1111111", jobStatusJson, StringComparison.Ordinal);
        Assert.DoesNotContain("ownerUserId", jobStatusJson, StringComparison.OrdinalIgnoreCase);
        using var jobStatus = JsonDocument.Parse(jobStatusJson);
        Assert.Equal(JsonValueKind.Null, jobStatus.RootElement.GetProperty("resultSummary").ValueKind);

        using var ownerScopedStatusResponse = await firstClient.GetAsync(
            $"/api/personal-ratings/enrichment/{firstJob.JobId}");
        Assert.Equal(HttpStatusCode.OK, ownerScopedStatusResponse.StatusCode);
        var ownerScopedStatusJson = await ownerScopedStatusResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("tt1111111", ownerScopedStatusJson, StringComparison.Ordinal);
        using var ownerScopedStatus = JsonDocument.Parse(ownerScopedStatusJson);
        Assert.Equal("imdb_title_enrichment", ownerScopedStatus.RootElement.GetProperty("jobType").GetString());
        Assert.Equal("queued", ownerScopedStatus.RootElement.GetProperty("status").GetString());

        using var crossOwnerStatusResponse = await secondClient.GetAsync(
            $"/api/personal-ratings/enrichment/{firstJob.JobId}");
        Assert.Equal(HttpStatusCode.NotFound, crossOwnerStatusResponse.StatusCode);

        using var eventsResponse = await firstClient.GetAsync(
            $"/api/background-jobs/{firstJob.JobId}/events?afterId=0&pageSize=50");
        Assert.Equal(HttpStatusCode.OK, eventsResponse.StatusCode);
        var eventsJson = await eventsResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("tt1111111", eventsJson, StringComparison.Ordinal);
        Assert.DoesNotContain("enrichment-first@example.com", eventsJson, StringComparison.Ordinal);

        await using var verifyDb = new MediaDockDbContext(options);
        var jobs = await verifyDb.BackgroundJobs.AsNoTracking()
            .Where(job => job.JobType == "imdb_title_enrichment")
            .ToListAsync();
        Assert.Equal(2, jobs.Count);
        Assert.Equal(firstUserId, jobs.Single(job => job.Id == firstJob.JobId).OwnerUserId);
        Assert.Equal(secondUserId, jobs.Single(job => job.Id == secondJob.JobId).OwnerUserId);
    }

    [Fact]
    public async Task ImportIsScopedToCurrentActiveUserEvenWhenAccessControlIsOpen()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_personal_ratings_api_test").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString).Options;
        long firstUserId;
        long secondUserId;
        long adminUserId;
        await using (var db = new MediaDockDbContext(options))
        {
            await db.Database.MigrateAsync();
            var now = DateTimeOffset.UtcNow;

            User CreateUser(string email, string subject, string status = "active", string role = "user")
            {
                var user = new User
                {
                    NormalizedEmail = email,
                    GivenName = "Test",
                    FamilyName = "User",
                    Status = status,
                    Role = role,
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

            var firstUser = CreateUser("first@example.com", "ratings-first");
            var secondUser = CreateUser("second@example.com", "ratings-second");
            var deactivatedUser = CreateUser("disabled@example.com", "ratings-disabled", "deactivated");
            var adminUser = CreateUser("admin@example.com", "ratings-admin", role: "admin");
            db.Users.AddRange(firstUser, secondUser, deactivatedUser, adminUser);
            await db.SaveChangesAsync();
            firstUserId = firstUser.Id;
            secondUserId = secondUser.Id;
            adminUserId = adminUser.Id;
        }

        using var anonymousFactory = new ApiFactory(connectionString);
        using var anonymousClient = anonymousFactory.CreateClient();
        using var anonymousResponse = await anonymousClient.PostAsync(
            "/api/personal-ratings/import", CreateUpload("[{\"id\":\"tt1234567\",\"rating\":8}]"));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var unknownFactory = new ApiFactory(
            connectionString, CreatePrincipal("ratings-unknown", "unknown@example.com"));
        using var unknownClient = unknownFactory.CreateClient();
        using var unknownResponse = await unknownClient.PostAsync(
            "/api/personal-ratings/import", CreateUpload("[{\"id\":\"tt1234567\",\"rating\":8}]"));
        Assert.Equal(HttpStatusCode.Forbidden, unknownResponse.StatusCode);

        using var deactivatedFactory = new ApiFactory(
            connectionString, CreatePrincipal("ratings-disabled", "disabled@example.com"));
        using var deactivatedClient = deactivatedFactory.CreateClient();
        using var deactivatedResponse = await deactivatedClient.PostAsync(
            "/api/personal-ratings/import", CreateUpload("[{\"id\":\"tt1234567\",\"rating\":8}]"));
        Assert.Equal(HttpStatusCode.Forbidden, deactivatedResponse.StatusCode);

        using var firstFactory = new ApiFactory(
            connectionString, CreatePrincipal("ratings-first", "first@example.com"));
        using var firstClient = firstFactory.CreateClient();
        using var secondFactory = new ApiFactory(
            connectionString, CreatePrincipal("ratings-second", "second@example.com"));
        using var secondClient = secondFactory.CreateClient();
        using var adminFactory = new ApiFactory(
            connectionString, CreatePrincipal("ratings-admin", "admin@example.com"));
        using var adminClient = adminFactory.CreateClient();
        var firstToken = await GetAntiforgeryTokenAsync(firstClient);
        var secondToken = await GetAntiforgeryTokenAsync(secondClient);
        var adminToken = await GetAntiforgeryTokenAsync(adminClient);

        using var missingTokenResponse = await firstClient.PostAsync(
            "/api/personal-ratings/import", CreateUpload("[{\"id\":\"tt1234567\",\"rating\":8}]"));
        Assert.Equal(HttpStatusCode.BadRequest, missingTokenResponse.StatusCode);

        using var firstImportResponse = await ImportAsync(firstClient,
            $"/api/personal-ratings/import?userId={secondUserId}",
            "[{\"id\":\"tt1234567\",\"rating\":8},{\"id\":\"tt7654321\",\"rating\":6}]",
            firstToken,
            secondUserId);
        Assert.Equal(HttpStatusCode.OK, firstImportResponse.StatusCode);
        var firstImport = await firstImportResponse.Content.ReadFromJsonAsync<PersonalRatingsImportResponse>();
        Assert.NotNull(firstImport);
        Assert.Equal(2, firstImport.Added);
        Assert.Equal(2, firstImport.TotalRatings);

        using var invalidImportResponse = await ImportAsync(
            firstClient,
            "/api/personal-ratings/import",
            "[{\"id\":\"tt9999999\",\"rating\":5},{\"id\":\"tt8888888\",\"rating\":11}]",
            firstToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalidImportResponse.StatusCode);

        using var secondImportResponse = await ImportAsync(
            secondClient, "/api/personal-ratings/import", "[{\"id\":\"tt1234567\",\"rating\":9}]", secondToken);
        Assert.Equal(HttpStatusCode.OK, secondImportResponse.StatusCode);
        var secondImport = await secondImportResponse.Content.ReadFromJsonAsync<PersonalRatingsImportResponse>();
        Assert.NotNull(secondImport);
        Assert.Equal(1, secondImport.Added);
        Assert.Equal(0, secondImport.Updated);
        Assert.Equal(1, secondImport.TotalRatings);

        using var adminImportResponse = await ImportAsync(
            adminClient, "/api/personal-ratings/import", "[{\"id\":\"tt1234567\",\"rating\":10}]", adminToken);
        Assert.Equal(HttpStatusCode.OK, adminImportResponse.StatusCode);
        var adminImport = await adminImportResponse.Content.ReadFromJsonAsync<PersonalRatingsImportResponse>();
        Assert.NotNull(adminImport);
        Assert.Equal(1, adminImport.Added);
        Assert.Equal(1, adminImport.TotalRatings);

        using var repeatImportResponse = await ImportAsync(
            firstClient,
            "/api/personal-ratings/import",
            "[{\"id\":\"tt1234567\",\"rating\":8},{\"id\":\"tt7654321\",\"rating\":6}]",
            firstToken);
        Assert.Equal(HttpStatusCode.OK, repeatImportResponse.StatusCode);
        var repeatImport = await repeatImportResponse.Content.ReadFromJsonAsync<PersonalRatingsImportResponse>();
        Assert.NotNull(repeatImport);
        Assert.Equal(2, repeatImport.Unchanged);
        Assert.Equal(2, repeatImport.TotalRatings);

        await using var verifyDb = new MediaDockDbContext(options);
        var ratings = await verifyDb.PersonalRatings.AsNoTracking()
            .OrderBy(rating => rating.UserId)
            .ThenBy(rating => rating.ImdbId)
            .ToListAsync();
        Assert.Equal(4, ratings.Count);
        Assert.DoesNotContain(ratings, rating => rating.ImdbId == "tt9999999");
        Assert.Equal(8, Assert.Single(ratings.Where(rating =>
            rating.UserId == firstUserId && rating.ImdbId == "tt1234567")).Rating);
        Assert.Equal(6, Assert.Single(ratings.Where(rating =>
            rating.UserId == firstUserId && rating.ImdbId == "tt7654321")).Rating);
        Assert.Equal(9, Assert.Single(ratings.Where(rating =>
            rating.UserId == secondUserId && rating.ImdbId == "tt1234567")).Rating);
        Assert.Equal(10, Assert.Single(ratings.Where(rating =>
            rating.UserId == adminUserId && rating.ImdbId == "tt1234567")).Rating);
        var imdbTitles = await verifyDb.ImdbTitles.AsNoTracking().ToListAsync();
        Assert.Equal(2, imdbTitles.Count);
        Assert.All(imdbTitles, title => Assert.Equal("pending", title.MetadataStatus));
    }

    private static MultipartFormDataContent CreateUpload(string json, long? userId = null)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(json));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Add(file, "File", "ratings.json");
        if (userId.HasValue) content.Add(new StringContent(userId.Value.ToString()), "userId");
        return content;
    }

    private static async Task<HttpResponseMessage> ImportAsync(
        HttpClient client,
        string path,
        string json,
        string requestToken,
        long? userId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = CreateUpload(json, userId)
        };
        request.Headers.Add("RequestVerificationToken", requestToken);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> EnqueueEnrichmentAsync(HttpClient client, string requestToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/personal-ratings/enrichment");
        request.Headers.Add("RequestVerificationToken", requestToken);
        return await client.SendAsync(request);
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/auth/antiforgery");
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<AntiforgeryTokenResponse>();
        return token!.RequestToken;
    }

    private static ClaimsPrincipal CreatePrincipal(string subject, string email) => new(new ClaimsIdentity(
    [
        new Claim(GoogleSignInSettings.IssuerClaimType, GoogleSignInSettings.GoogleIssuer),
        new Claim("sub", subject),
        new Claim("email", email),
        new Claim("email_verified", "true")
    ], "test-google"));

    private sealed class ApiFactory(
        string connectionString,
        ClaimsPrincipal? principal = null) : WebApplicationFactory<Program>
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
                        options => options.Principal = principal);
                services.PostConfigure<AuthenticationOptions>(options =>
                    options.DefaultAuthenticateScheme = TestAuthenticationOptions.Scheme);
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