using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using MediaDock.Api.Authentication;
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