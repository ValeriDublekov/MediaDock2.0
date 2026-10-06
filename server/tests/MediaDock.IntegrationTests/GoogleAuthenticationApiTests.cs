using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using MediaDock.Api.Authentication;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediaDock.IntegrationTests;

[Trait("Category", "Api")]
public sealed class GoogleAuthenticationApiTests
{
    [Fact]
    public async Task AnonymousModeRemainsAvailableAndStateChangesRequireAntiforgery()
    {
        using var factory = new ApiFactory("Host=127.0.0.1;Database=mediadock_test;Username=test;Password=test");
        using var client = factory.CreateClient();

        using var sessionResponse = await client.GetAsync("/api/auth/session");
        Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);
        var session = await sessionResponse.Content.ReadFromJsonAsync<CurrentSessionResponse>();
        Assert.NotNull(session);
        Assert.False(session.Authenticated);
        Assert.Equal("anonymous", session.AccountState);

        using var versionResponse = await client.GetAsync("/api/version");
        Assert.Equal(HttpStatusCode.OK, versionResponse.StatusCode);

        using var loginResponse = await client.GetAsync("/api/auth/google/login");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, loginResponse.StatusCode);

        using var logoutResponse = await client.PostAsync("/api/auth/logout", new StringContent(string.Empty));
        Assert.Equal(HttpStatusCode.BadRequest, logoutResponse.StatusCode);

        using var tokenResponse = await client.GetAsync("/api/auth/antiforgery");
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        var token = await tokenResponse.Content.ReadFromJsonAsync<AntiforgeryTokenResponse>();
        Assert.NotNull(token);

        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logoutRequest.Headers.Add("RequestVerificationToken", token.RequestToken);
        using var validLogoutResponse = await client.SendAsync(logoutRequest);
        Assert.Equal(HttpStatusCode.NoContent, validLogoutResponse.StatusCode);
    }

    [Fact]
    public void CallbackConfigurationAllowsOnlyLocalhostHttpOrHttpsHostname()
    {
        var localSettings = CreateSettings("http://localhost:8080/signin-oidc");
        var request = new DefaultHttpContext().Request;
        request.Scheme = "http";
        request.Host = new HostString("localhost", 8080);
        Assert.True(localSettings.MatchesRequestOrigin(request));

        request.Host = new HostString("127.0.0.1", 8080);
        Assert.False(localSettings.MatchesRequestOrigin(request));
        Assert.Throws<InvalidOperationException>(() => CreateSettings("http://192.168.1.20/signin-oidc"));

        var secureSettings = CreateSettings("https://mediadock.example/signin-oidc");
        request.Scheme = "https";
        request.Host = new HostString("mediadock.example");
        Assert.True(secureSettings.MatchesRequestOrigin(request));
    }

    [Fact]
    public async Task VerifiedEmailMatchRequiresCsrfProtectedOneTimeAccountLink()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_google_link_test").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using (var db = new MediaDockDbContext(options))
        {
            await db.Database.MigrateAsync();
            db.Users.Add(new User
            {
                NormalizedEmail = "person@example.com",
                GivenName = "Stored",
                FamilyName = "Name",
                Status = "active",
                Role = "user",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var anonymousFactory = new ApiFactory(connectionString);
        using var anonymousClient = anonymousFactory.CreateClient();
        using var anonymousCatalogResponse = await anonymousClient.GetAsync("/api/catalog");
        Assert.Equal(HttpStatusCode.OK, anonymousCatalogResponse.StatusCode);

        using var factory = new ApiFactory(connectionString, CreatePrincipal());
        using var client = factory.CreateClient();

        using var catalogResponse = await client.GetAsync("/api/catalog");
        Assert.Equal(HttpStatusCode.OK, catalogResponse.StatusCode);

        using var sessionResponse = await client.GetAsync("/api/auth/session");
        var session = await sessionResponse.Content.ReadFromJsonAsync<CurrentSessionResponse>();
        Assert.NotNull(session);
        Assert.True(session.Authenticated);
        Assert.Equal("link_confirmation_required", session.AccountState);
        Assert.Equal("https://accounts.google.com", session.Identity?.Issuer);
        Assert.Equal("google-subject-1", session.Identity?.Subject);
        Assert.Equal("person@example.com", session.User?.Email);
        Assert.Equal("Stored", session.User?.GivenName);

        using var missingTokenResponse = await client.PostAsync("/api/auth/google/link", new StringContent(string.Empty));
        Assert.Equal(HttpStatusCode.BadRequest, missingTokenResponse.StatusCode);

        var token = await GetAntiforgeryTokenAsync(client);
        using var linkRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/google/link");
        linkRequest.Headers.Add("RequestVerificationToken", token);
        using var linkResponse = await client.SendAsync(linkRequest);
        Assert.Equal(HttpStatusCode.NoContent, linkResponse.StatusCode);

        using var repeatRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/google/link");
        repeatRequest.Headers.Add("RequestVerificationToken", token);
        using var repeatResponse = await client.SendAsync(repeatRequest);
        Assert.Equal(HttpStatusCode.Conflict, repeatResponse.StatusCode);

        using var linkedSessionResponse = await client.GetAsync("/api/auth/session");
        var linkedSession = await linkedSessionResponse.Content.ReadFromJsonAsync<CurrentSessionResponse>();
        Assert.NotNull(linkedSession);
        Assert.Equal("linked", linkedSession.AccountState);
        Assert.Equal("Stored", linkedSession.User?.GivenName);

        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logoutRequest.Headers.Add("RequestVerificationToken", token);
        using var logoutResponse = await client.SendAsync(logoutRequest);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);
        Assert.Contains(
            logoutResponse.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("__Host-MediaDock.Session=", StringComparison.Ordinal)
                && value.Contains("secure", StringComparison.OrdinalIgnoreCase));

        await using var verifyDb = new MediaDockDbContext(options);
        var identity = await verifyDb.ExternalIdentities.AsNoTracking().SingleAsync();
        Assert.Equal("https://accounts.google.com", identity.Issuer);
        Assert.Equal("google-subject-1", identity.Subject);
    }

    [Fact]
    public async Task UnverifiedEmailIsRejectedAndMissingNamesRemainIncomplete()
    {
        using var unverifiedFactory = new ApiFactory(
            "Host=127.0.0.1;Database=mediadock_test;Username=test;Password=test",
            CreatePrincipal(emailVerified: false));
        using var unverifiedClient = unverifiedFactory.CreateClient();

        using var rejectedResponse = await unverifiedClient.GetAsync("/api/auth/session");
        Assert.Equal(HttpStatusCode.Unauthorized, rejectedResponse.StatusCode);

        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_google_names_test").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        await using (var db = new MediaDockDbContext(
            new DbContextOptionsBuilder<MediaDockDbContext>().UseNpgsql(connectionString).Options))
        {
            await db.Database.MigrateAsync();
        }

        using var incompleteFactory = new ApiFactory(connectionString, CreatePrincipal(includeNames: false));
        using var incompleteClient = incompleteFactory.CreateClient();
        using var incompleteResponse = await incompleteClient.GetAsync("/api/auth/session");
        var incompleteSession = await incompleteResponse.Content.ReadFromJsonAsync<CurrentSessionResponse>();

        Assert.Equal(HttpStatusCode.OK, incompleteResponse.StatusCode);
        Assert.NotNull(incompleteSession?.Identity);
        Assert.False(incompleteSession.Identity.ProfileComplete);
        Assert.Null(incompleteSession.Identity.GivenName);
        Assert.Equal("unmatched", incompleteSession.AccountState);
    }

    [Fact]
    public async Task LinkIsRejectedWhenEmailBelongsToAnAlreadyLinkedAccount()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_google_conflict_test").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using (var db = new MediaDockDbContext(options))
        {
            await db.Database.MigrateAsync();
            var user = new User
            {
                NormalizedEmail = "person@example.com",
                GivenName = "Stored",
                FamilyName = "Name",
                Status = "active",
                Role = "user",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            user.ExternalIdentities.Add(new ExternalIdentity
            {
                User = user,
                Issuer = "https://accounts.google.com",
                Subject = "other-google-subject",
                CreatedAt = DateTimeOffset.UtcNow
            });
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        using var factory = new ApiFactory(connectionString, CreatePrincipal());
        using var client = factory.CreateClient();
        using var sessionResponse = await client.GetAsync("/api/auth/session");
        var session = await sessionResponse.Content.ReadFromJsonAsync<CurrentSessionResponse>();
        Assert.Equal("account_conflict", session?.AccountState);

        var token = await GetAntiforgeryTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/google/link");
        request.Headers.Add("RequestVerificationToken", token);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/auth/antiforgery");
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<AntiforgeryTokenResponse>();
        return token!.RequestToken;
    }

    private static ClaimsPrincipal CreatePrincipal(bool emailVerified = true, bool includeNames = true)
    {
        var claims = new List<Claim>
        {
            new(GoogleSignInSettings.IssuerClaimType, GoogleSignInSettings.GoogleIssuer),
            new("sub", "google-subject-1"),
            new("email", "Person@Example.com"),
            new("email_verified", emailVerified ? "true" : "false")
        };
        if (includeNames)
        {
            claims.Add(new Claim("given_name", "Google"));
            claims.Add(new Claim("family_name", "Profile"));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test-google"));
    }

    private static GoogleSignInSettings CreateSettings(string callbackUri)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Google:Enabled"] = "true",
                ["Authentication:Google:ClientId"] = "test-client",
                ["Authentication:Google:ClientSecret"] = "test-secret",
                ["Authentication:Google:CallbackUri"] = callbackUri
            })
            .Build();
        return GoogleSignInSettings.FromConfiguration(configuration);
    }

    private sealed class ApiFactory(string connectionString, ClaimsPrincipal? principal = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MediaDock"] = connectionString,
                    ["Authentication:Google:Enabled"] = "false"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddAuthentication()
                    .AddScheme<TestGoogleOptions, TestGoogleHandler>(TestGoogleOptions.Scheme, options =>
                    {
                        options.Principal = principal;
                    });
                services.PostConfigure<AuthenticationOptions>(options =>
                    options.DefaultAuthenticateScheme = TestGoogleOptions.Scheme);
            });
        }
    }

    private sealed class TestGoogleOptions : AuthenticationSchemeOptions
    {
        public const string Scheme = "TestGoogle";
        public ClaimsPrincipal? Principal { get; set; }
    }

    private sealed class TestGoogleHandler(
        IOptionsMonitor<TestGoogleOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<TestGoogleOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Options.Principal is null)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(Options.Principal, Scheme.Name)));
        }
    }
}