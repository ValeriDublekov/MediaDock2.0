using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using MediaDock.Api.Administration;
using MediaDock.Api.Authentication;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
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
        Assert.Contains(
            tokenResponse.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("MediaDock.Antiforgery=", StringComparison.Ordinal)
                && value.Contains("httponly", StringComparison.OrdinalIgnoreCase)
                && value.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));
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
    public async Task ForwardedHttpsSchemeIsAcceptedOnlyFromConfiguredProxyNetwork()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [TrustedForwardedHeaders.TrustedNetworkConfigurationKey] = "172.20.0.0/16"
            })
            .Build();
        var options = TrustedForwardedHeaders.CreateOptions(configuration);
        Assert.NotNull(options);

        using var loggerFactory = LoggerFactory.Create(_ => { });
        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, loggerFactory, Options.Create(options));

        var trustedContext = CreateForwardedRequest("172.20.0.3");
        await middleware.Invoke(trustedContext);
        Assert.Equal("https", trustedContext.Request.Scheme);

        var untrustedContext = CreateForwardedRequest("192.168.1.10");
        await middleware.Invoke(untrustedContext);
        Assert.Equal("http", untrustedContext.Request.Scheme);
    }

    private static DefaultHttpContext CreateForwardedRequest(string remoteAddress)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteAddress);
        return context;
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
    public async Task RegistrationRequestUsesValidatedIdentityAndIsIdempotent()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_registration_request_test").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using (var db = new MediaDockDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        using var factory = new ApiFactory(connectionString, CreatePrincipal());
        using var client = factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);

        using var missingTokenResponse = await client.PostAsync(
            "/api/auth/registration-requests",
            new StringContent(string.Empty));
        Assert.Equal(HttpStatusCode.BadRequest, missingTokenResponse.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/registration-requests")
        {
            Content = new StringContent(
                "{\"email\":\"attacker@example.com\",\"givenName\":\"Forged\",\"subject\":\"forged-subject\"}",
                System.Text.Encoding.UTF8,
                "application/json")
        };
        request.Headers.Add("RequestVerificationToken", token);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RegistrationRequestResponse>();
        Assert.NotNull(result);
        Assert.Equal("pending", result.Status);

        using var repeatRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/registration-requests");
        repeatRequest.Headers.Add("RequestVerificationToken", token);
        using var repeatResponse = await client.SendAsync(repeatRequest);
        Assert.Equal(HttpStatusCode.OK, repeatResponse.StatusCode);
        var repeatedResult = await repeatResponse.Content.ReadFromJsonAsync<RegistrationRequestResponse>();
        Assert.Equal(result, repeatedResult);

        using var sessionResponse = await client.GetAsync("/api/auth/session");
        var session = await sessionResponse.Content.ReadFromJsonAsync<CurrentSessionResponse>();
        Assert.Equal("linked", session?.AccountState);
        Assert.Equal("person@example.com", session?.User?.Email);
        Assert.Equal("Google", session?.User?.GivenName);
        Assert.Equal("Profile", session?.User?.FamilyName);
        Assert.Equal("pending", session?.RegistrationRequest?.Status);

        await using var verifyDb = new MediaDockDbContext(options);
        Assert.Equal("person@example.com", await verifyDb.Users.Select(user => user.NormalizedEmail).SingleAsync());
        Assert.Equal("Google", await verifyDb.Users.Select(user => user.GivenName).SingleAsync());
        Assert.Equal("Profile", await verifyDb.Users.Select(user => user.FamilyName).SingleAsync());
        Assert.Equal("https://accounts.google.com", await verifyDb.ExternalIdentities.Select(identity => identity.Issuer).SingleAsync());
        Assert.Equal("google-subject-1", await verifyDb.ExternalIdentities.Select(identity => identity.Subject).SingleAsync());
        Assert.Single(await verifyDb.RegistrationRequests.ToListAsync());
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
        Assert.Contains("email_not_verified", await rejectedResponse.Content.ReadAsStringAsync());
        Assert.True(rejectedResponse.Headers.TryGetValues("X-MediaDock-TraceId", out var sessionTraceIds));
        Assert.NotEmpty(sessionTraceIds!);

        using var logoutResponse = await unverifiedClient.PostAsync("/api/auth/logout", new StringContent(string.Empty));
        Assert.Equal(HttpStatusCode.BadRequest, logoutResponse.StatusCode);
        Assert.True(logoutResponse.Headers.TryGetValues("X-MediaDock-TraceId", out var logoutTraceIds));
        Assert.NotEmpty(logoutTraceIds!);

        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_google_names_test").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using (var db = new MediaDockDbContext(options))
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

        var token = await GetAntiforgeryTokenAsync(incompleteClient);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/registration-requests");
        request.Headers.Add("RequestVerificationToken", token);
        using var response = await incompleteClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var verifyDb = new MediaDockDbContext(options);
        Assert.Empty(await verifyDb.Users.ToListAsync());
        Assert.Empty(await verifyDb.RegistrationRequests.ToListAsync());
    }

    [Theory]
    [InlineData(0, "missing")]
    [InlineData(2, "multiple")]
    public async Task InvalidEmailVerificationClaimShapeIsLoggedWithoutClaimValues(
        int claimCount,
        string expectedClaimState)
    {
        using var loggerProvider = new TestLoggerProvider();
        using var factory = new ApiFactory(
            "Host=127.0.0.1;Database=mediadock_test;Username=test;Password=test",
            CreatePrincipal(emailVerificationClaimCount: claimCount),
            loggerProvider);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/auth/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var warning = Assert.Single(loggerProvider.Messages.Where(message =>
            message.Contains("ReasonCode missing_or_ambiguous_email_verification", StringComparison.Ordinal)));
        Assert.Contains($"EmailVerificationClaimCount {claimCount}", warning, StringComparison.Ordinal);
        Assert.Contains($"EmailVerificationClaimState {expectedClaimState}", warning, StringComparison.Ordinal);
        Assert.Contains("TraceId", warning, StringComparison.Ordinal);
        Assert.False(warning.Contains("Person@Example.com", StringComparison.OrdinalIgnoreCase));
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

    [Fact]
    public async Task UserAdministrationRequiresAnActiveAdminAndApprovalActivatesTheRequester()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_user_admin_test").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        long requestId;
        long adminId;
        long requesterId;
        await using (var db = new MediaDockDbContext(options))
        {
            await db.Database.MigrateAsync();
            var admin = new User
            {
                NormalizedEmail = "admin@example.com",
                GivenName = "Admin",
                FamilyName = "Account",
                Status = "active",
                Role = "admin",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            admin.ExternalIdentities.Add(new ExternalIdentity
            {
                User = admin,
                Issuer = GoogleSignInSettings.GoogleIssuer,
                Subject = "admin-subject",
                CreatedAt = DateTimeOffset.UtcNow
            });
            var requester = new User
            {
                NormalizedEmail = "new@example.com",
                GivenName = "New",
                FamilyName = "User",
                Status = "pending",
                Role = null,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            requester.ExternalIdentities.Add(new ExternalIdentity
            {
                User = requester,
                Issuer = GoogleSignInSettings.GoogleIssuer,
                Subject = "requester-subject",
                CreatedAt = DateTimeOffset.UtcNow
            });
            var registrationRequest = new RegistrationRequest
            {
                User = requester,
                RequestedAt = DateTimeOffset.UtcNow,
                Status = "pending"
            };
            db.Users.AddRange(admin, requester);
            db.RegistrationRequests.Add(registrationRequest);
            await db.SaveChangesAsync();
            requestId = registrationRequest.Id;
            adminId = admin.Id;
            requesterId = requester.Id;
        }

        using var anonymousFactory = new ApiFactory(connectionString);
        using var anonymousClient = anonymousFactory.CreateClient();
        using var anonymousResponse = await anonymousClient.GetAsync("/api/admin/users");
        Assert.Equal(HttpStatusCode.OK, anonymousResponse.StatusCode);
        var anonymousUsers = await anonymousResponse.Content.ReadFromJsonAsync<AdminUsersPageResponse>();
        Assert.NotNull(anonymousUsers);
        Assert.Equal(2, anonymousUsers.TotalCount);
        Assert.Contains(anonymousUsers.Items, user => user.Email == "new@example.com");

        using var regularUserFactory = new ApiFactory(connectionString, CreatePrincipal(subject: "requester-subject"));
        using var regularUserClient = regularUserFactory.CreateClient();
        using var regularUserResponse = await regularUserClient.GetAsync("/api/admin/users");
        Assert.Equal(HttpStatusCode.OK, regularUserResponse.StatusCode);

        using var adminFactory = new ApiFactory(connectionString, CreatePrincipal(email: "admin@example.com", subject: "admin-subject"));
        using var adminClient = adminFactory.CreateClient();
        using var listResponse = await adminClient.GetAsync("/api/admin/users?state=requested");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var users = await listResponse.Content.ReadFromJsonAsync<AdminUsersPageResponse>();
        Assert.NotNull(users);
        var listedRequester = Assert.Single(users.Items);
        Assert.Equal("new@example.com", listedRequester.Email);
        Assert.Equal("pending", listedRequester.RegistrationRequest?.Status);

        var token = await GetAntiforgeryTokenAsync(anonymousClient);
        using var approveRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/registration-requests/{requestId}/decision")
        {
            Content = JsonContent.Create(new AdminRegistrationDecisionRequest("approve"))
        };
        approveRequest.Headers.Add("RequestVerificationToken", token);
        using var approveResponse = await anonymousClient.SendAsync(approveRequest);
        Assert.Equal(HttpStatusCode.NoContent, approveResponse.StatusCode);

        await using (var verifyDb = new MediaDockDbContext(options))
        {
            var approvedUser = await verifyDb.Users.SingleAsync(user => user.NormalizedEmail == "new@example.com");
            var approvedRequest = await verifyDb.RegistrationRequests.SingleAsync(request => request.Id == requestId);
            Assert.Equal("active", approvedUser.Status);
            Assert.Equal("user", approvedUser.Role);
            Assert.Equal("approved", approvedRequest.Status);
            Assert.Equal("open-mode", approvedRequest.DecidedBy);
            Assert.NotNull(approvedRequest.DecidedAt);
            Assert.Equal(1, await verifyDb.Users.CountAsync(user => user.Id == adminId && user.Role == "admin"));
        }

        var statusToken = await GetAntiforgeryTokenAsync(anonymousClient);
        using var deactivateRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{requesterId}/status")
        {
            Content = JsonContent.Create(new AdminUserStatusRequest("deactivated"))
        };
        deactivateRequest.Headers.Add("RequestVerificationToken", statusToken);
        using var deactivateResponse = await anonymousClient.SendAsync(deactivateRequest);
        Assert.Equal(HttpStatusCode.NoContent, deactivateResponse.StatusCode);

        await using var statusDb = new MediaDockDbContext(options);
        Assert.Equal("deactivated", await statusDb.Users
            .Where(user => user.Id == requesterId)
            .Select(user => user.Status)
            .SingleAsync());
    }

    [Fact]
    public async Task LastActiveAdministratorCannotBeDeactivated()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_last_admin_test").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        long adminId;
        await using (var db = new MediaDockDbContext(options))
        {
            await db.Database.MigrateAsync();
            var admin = new User
            {
                NormalizedEmail = "admin@example.com",
                GivenName = "Admin",
                FamilyName = "Account",
                Status = "active",
                Role = "admin",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            admin.ExternalIdentities.Add(new ExternalIdentity
            {
                User = admin,
                Issuer = GoogleSignInSettings.GoogleIssuer,
                Subject = "admin-subject",
                CreatedAt = DateTimeOffset.UtcNow
            });
            db.Users.Add(admin);
            await db.SaveChangesAsync();
            adminId = admin.Id;
        }

        using var factory = new ApiFactory(connectionString, CreatePrincipal(email: "admin@example.com", subject: "admin-subject"));
        using var client = factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{adminId}/status")
        {
            Content = JsonContent.Create(new AdminUserStatusRequest("deactivated"))
        };
        request.Headers.Add("RequestVerificationToken", token);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var verifyDb = new MediaDockDbContext(options);
        var adminUser = await verifyDb.Users.SingleAsync(user => user.Id == adminId);
        Assert.Equal("active", adminUser.Status);
        Assert.Equal("admin", adminUser.Role);
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/auth/antiforgery");
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<AntiforgeryTokenResponse>();
        return token!.RequestToken;
    }

    private static ClaimsPrincipal CreatePrincipal(
        bool emailVerified = true,
        bool includeNames = true,
        string email = "Person@Example.com",
        string subject = "google-subject-1",
        int emailVerificationClaimCount = 1)
    {
        var claims = new List<Claim>
        {
            new(GoogleSignInSettings.IssuerClaimType, GoogleSignInSettings.GoogleIssuer),
            new("sub", subject),
            new("email", email)
        };
        for (var index = 0; index < emailVerificationClaimCount; index++)
        {
            claims.Add(new Claim("email_verified", emailVerified ? "true" : "false"));
        }

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

    private sealed class ApiFactory(
        string connectionString,
        ClaimsPrincipal? principal = null,
        ILoggerProvider? loggerProvider = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            if (loggerProvider is not null)
            {
                builder.ConfigureLogging(logging => logging.AddProvider(loggerProvider));
            }

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

    private sealed class TestLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IReadOnlyCollection<string> Messages => _messages.ToArray();

        public ILogger CreateLogger(string categoryName) => new TestLogger(_messages);

        public void Dispose()
        {
        }

        private sealed class TestLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                messages.Enqueue(formatter(state, exception));
            }
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