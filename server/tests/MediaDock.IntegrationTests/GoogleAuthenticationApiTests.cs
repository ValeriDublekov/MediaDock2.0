using System.Net;
using System.Net.Http.Json;
using MediaDock.Api.Authentication;
using MediaDock.Api.Common;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static MediaDock.IntegrationTests.GoogleAuthenticationApiTestSupport;

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
        Assert.False(session.AuthorizationEnforced);

        using var versionResponse = await client.GetAsync("/api/version");
        Assert.Equal(HttpStatusCode.OK, versionResponse.StatusCode);

        using var loginResponse = await client.GetAsync("/api/auth/google/login");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, loginResponse.StatusCode);

        using var logoutResponse = await client.PostAsync("/api/auth/logout", new StringContent(string.Empty));
        Assert.Equal(HttpStatusCode.BadRequest, logoutResponse.StatusCode);

        using var tokenResponse = await client.GetAsync("/api/auth/antiforgery");
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        Assert.Equal("no-store", tokenResponse.Headers.CacheControl?.ToString());
        Assert.Contains(
            tokenResponse.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("MediaDock.Antiforgery=", StringComparison.Ordinal)
                && value.Contains("httponly", StringComparison.OrdinalIgnoreCase)
                && value.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));
        var token = await tokenResponse.Content.ReadFromJsonAsync<AntiforgeryTokenResponse>();
        Assert.NotNull(token);

        using var sameSiteCrossOriginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        sameSiteCrossOriginRequest.Headers.Add("RequestVerificationToken", token.RequestToken);
        sameSiteCrossOriginRequest.Headers.Add("Origin", "http://attacker.localhost");
        sameSiteCrossOriginRequest.Headers.Add("Sec-Fetch-Site", "same-site");
        using var sameSiteCrossOriginResponse = await client.SendAsync(sameSiteCrossOriginRequest);
        Assert.Equal(HttpStatusCode.BadRequest, sameSiteCrossOriginResponse.StatusCode);

        using var invalidTokenRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        invalidTokenRequest.Headers.Add("RequestVerificationToken", "invalid-token");
        invalidTokenRequest.Headers.Add("Origin", client.BaseAddress!.GetLeftPart(UriPartial.Authority));
        using var invalidTokenResponse = await client.SendAsync(invalidTokenRequest);
        Assert.Equal(HttpStatusCode.BadRequest, invalidTokenResponse.StatusCode);

        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logoutRequest.Headers.Add("RequestVerificationToken", token.RequestToken);
        logoutRequest.Headers.Add("Origin", client.BaseAddress!.GetLeftPart(UriPartial.Authority));
        using var validLogoutResponse = await client.SendAsync(logoutRequest);
        Assert.Equal(HttpStatusCode.NoContent, validLogoutResponse.StatusCode);

        using var cookieMutation = new HttpRequestMessage(HttpMethod.Post, "/api/favorites")
        {
            Content = JsonContent.Create(new { imdbId = "tt12345678", from = "catalog" })
        };
        cookieMutation.Headers.Add("Cookie", $"{GoogleSignInSettings.SessionCookieName}=test-session");
        using var cookieMutationResponse = await client.SendAsync(cookieMutation);
        Assert.Equal(HttpStatusCode.BadRequest, cookieMutationResponse.StatusCode);
    }

    [Fact]
    public async Task AuthenticationRateLimitReturns429AndRecoversAfterItsWindow()
    {
        using var factory = new ApiFactory(
            "Host=127.0.0.1;Database=mediadock_test;Username=test;Password=test",
            additionalConfiguration: new Dictionary<string, string?>
            {
                ["AbuseControls:RateLimits:Authentication:PermitLimit"] = "1",
                ["AbuseControls:RateLimits:Authentication:WindowSeconds"] = "1"
            });
        using var client = factory.CreateClient();

        using var firstResponse = await client.GetAsync("/api/auth/antiforgery");
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        using var limitedResponse = await client.GetAsync("/api/auth/antiforgery");
        Assert.Equal(HttpStatusCode.TooManyRequests, limitedResponse.StatusCode);
        Assert.Equal("no-store", limitedResponse.Headers.CacheControl?.ToString());
        Assert.NotEmpty(limitedResponse.Headers.GetValues("Retry-After"));
        var limitedBody = await limitedResponse.Content.ReadAsStringAsync();
        Assert.Contains("Too many requests.", limitedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("PermitLimit", limitedBody, StringComparison.Ordinal);

        await Task.Delay(TimeSpan.FromMilliseconds(1200));
        using var recoveredResponse = await client.GetAsync("/api/auth/antiforgery");
        Assert.Equal(HttpStatusCode.OK, recoveredResponse.StatusCode);
    }

    [Fact]
    public async Task UnexpectedExceptionsAreRedactedFromResponsesAndRetainedInLogs()
    {
        const string sentinel = "private-exception-sentinel";
        var loggerProvider = new TestLoggerProvider();
        using var factory = new ApiFactory(
            "Host=127.0.0.1;Database=mediadock_test;Username=test;Password=test",
            loggerProvider: loggerProvider);
        var exceptionHandler = factory.Services.GetRequiredService<IExceptionHandler>();
        await using var responseBody = new MemoryStream();
        var context = new DefaultHttpContext
        {
            RequestServices = factory.Services,
            TraceIdentifier = "trace-sentinel"
        };
        context.Request.Path = "/api/test";
        context.Response.Body = responseBody;

        Assert.True(await exceptionHandler.TryHandleAsync(
            context,
            new InvalidOperationException(sentinel),
            CancellationToken.None));

        responseBody.Position = 0;
        using var reader = new StreamReader(responseBody);
        var body = await reader.ReadToEndAsync();
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Contains("trace-sentinel", body, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, body, StringComparison.Ordinal);
        Assert.Equal("trace-sentinel", context.Response.Headers["X-MediaDock-TraceId"]);
        Assert.Contains(loggerProvider.Exceptions, message => message.Contains(sentinel, StringComparison.Ordinal));
    }

    [Fact]
    public async Task OversizedRequestsUseAGeneric413ProblemResponse()
    {
        const string sentinel = "private-size-sentinel";
        using var factory = new ApiFactory("Host=127.0.0.1;Database=mediadock_test;Username=test;Password=test");
        var exceptionHandler = factory.Services.GetRequiredService<IExceptionHandler>();
        await using var responseBody = new MemoryStream();
        var context = new DefaultHttpContext
        {
            RequestServices = factory.Services,
            TraceIdentifier = "trace-size"
        };
        context.Request.Path = "/api/upload";
        context.Response.Body = responseBody;

        Assert.True(await exceptionHandler.TryHandleAsync(
            context,
            new BadHttpRequestException(sentinel, StatusCodes.Status413PayloadTooLarge),
            CancellationToken.None));

        responseBody.Position = 0;
        using var reader = new StreamReader(responseBody);
        var body = await reader.ReadToEndAsync();
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
        Assert.Contains("The request body exceeds the allowed size.", body, StringComparison.Ordinal);
        Assert.Contains("trace-size", body, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DevelopmentLocalMockUsesTheOnlyActiveLinkedUser()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_local_mock_auth_test").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using (var db = new MediaDockDbContext(options))
        {
            await db.Database.MigrateAsync();
            var now = DateTimeOffset.UtcNow;
            var user = new User
            {
                NormalizedEmail = "local-admin@example.com",
                GivenName = "Local",
                FamilyName = "Admin",
                Status = "active",
                Role = "admin",
                CreatedAt = now,
                UpdatedAt = now
            };
            user.ExternalIdentities.Add(new ExternalIdentity
            {
                User = user,
                Issuer = GoogleSignInSettings.GoogleIssuer,
                Subject = "local-mock-subject",
                CreatedAt = now
            });
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var loggerProvider = new TestLoggerProvider();
        using var factory = new ApiFactory(
            connectionString,
            loggerProvider: loggerProvider,
            accessControlEnforced: true,
            localMockEnabled: true);
        Assert.Equal(
            "True",
            factory.Services.GetRequiredService<IConfiguration>()["Authentication:LocalMock:Enabled"]);
        var schemes = await factory.Services.GetRequiredService<IAuthenticationSchemeProvider>()
            .GetAllSchemesAsync();
        Assert.Contains(schemes, scheme => scheme.Name == "MediaDock.LocalMock");
        Assert.Equal(
            "MediaDock.LocalMock",
            factory.Services.GetRequiredService<IOptions<AuthenticationOptions>>().Value.DefaultAuthenticateScheme);
        using var client = factory.CreateClient();

        using var sessionResponse = await client.GetAsync("/api/auth/session");
        Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);
        var session = await sessionResponse.Content.ReadFromJsonAsync<CurrentSessionResponse>();
        Assert.NotNull(session);
        Assert.True(session.Authenticated, string.Join(" | ", loggerProvider.Messages));
        Assert.Equal("linked", session.AccountState);
        Assert.Equal("local-mock-subject", session.Identity?.Subject);
        Assert.Equal("admin", session.User?.Role);

        using var catalogResponse = await client.GetAsync("/api/catalog");
        Assert.Equal(HttpStatusCode.OK, catalogResponse.StatusCode);

        using var favoritesResponse = await client.GetAsync("/api/favorites");
        Assert.Equal(HttpStatusCode.OK, favoritesResponse.StatusCode);
    }

    [Fact]
    public void LocalMockIsRejectedOutsideDevelopment()
    {
        using var factory = new ApiFactory(
            "Host=127.0.0.1;Database=mediadock_test;Username=test;Password=test",
            localMockEnabled: true,
            environmentName: "Production");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            factory.Services.GetRequiredService<IOptions<AuthenticationOptions>>().Value);

        Assert.Contains("Development", exception.Message);
    }

    [Fact]
    public void LocalMockIsRejectedDuringProductionServiceRegistration()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Production
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:LocalMock:Enabled"] = "true",
            ["ConnectionStrings:MediaDock"] = "Host=127.0.0.1;Database=mediadock_test;Username=test;Password=test"
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            builder.Services.AddGoogleSignIn(builder.Configuration, builder.Environment));

        Assert.Contains("Development", exception.Message);
    }

    [Fact]
    public void ProductionStartupRequiresGoogleSignIn()
    {
        using var factory = new ApiFactory(
            "Host=127.0.0.1;Database=mediadock_test;Username=test;Password=test",
            accessControlEnforced: true,
            environmentName: "Production");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            factory.Services.GetRequiredService<IConfiguration>());

        Assert.Contains("Google sign-in", exception.Message);
    }

    [Fact]
    public void ProductionStartupRequiresAuthorizationEnforcement()
    {
        using var factory = new ApiFactory(
            "Host=127.0.0.1;Database=mediadock_test;Username=test;Password=test",
            environmentName: "Production",
            googleSignInEnabled: true);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            factory.Services.GetRequiredService<IConfiguration>());

        Assert.Contains("AccessControl:Enforced=true", exception.Message);
    }

    [Fact]
    public void NonDevelopmentStartupRequiresExplicitRateLimitValues()
    {
        using var factory = new ApiFactory(
            "Host=127.0.0.1;Database=mediadock_test;Username=test;Password=test",
            accessControlEnforced: true,
            environmentName: "Production",
            googleSignInEnabled: true);

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains("AbuseControls:RateLimits:Authentication:PermitLimit", exception.ToString());
    }

    [Fact]
    public void LocalMockRequiresLoopbackDatabase()
    {
        using var factory = new ApiFactory(
            "Host=db.example.invalid;Database=mediadock_test;Username=test;Password=test",
            localMockEnabled: true);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            factory.Services.GetRequiredService<IOptions<AuthenticationOptions>>().Value);

        Assert.Contains("loopback", exception.Message);
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
        Assert.Equal(IPAddress.Parse("203.0.113.10"), trustedContext.Connection.RemoteIpAddress);

        var untrustedContext = CreateForwardedRequest("192.168.1.10");
        await middleware.Invoke(untrustedContext);
        Assert.Equal("http", untrustedContext.Request.Scheme);
        Assert.Equal(IPAddress.Parse("192.168.1.10"), untrustedContext.Connection.RemoteIpAddress);
    }

    private static DefaultHttpContext CreateForwardedRequest(string remoteAddress)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.10";
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
            CreatePrincipal(
                emailVerificationClaimCount: claimCount,
                conflictingEmailVerificationClaims: claimCount > 1),
            loggerProvider);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/auth/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var warning = Assert.Single(loggerProvider.Messages, message =>
            message.Contains("ReasonCode missing_or_ambiguous_email_verification", StringComparison.Ordinal));
        Assert.Contains($"EmailVerificationClaimCount {claimCount}", warning, StringComparison.Ordinal);
        Assert.Contains($"EmailVerificationClaimState {expectedClaimState}", warning, StringComparison.Ordinal);
        Assert.Contains("TraceId", warning, StringComparison.Ordinal);
        if (claimCount > 1)
        {
            Assert.Contains("EmailVerificationClaimValuesAgree False", warning, StringComparison.Ordinal);
        }

        Assert.False(warning.Contains("Person@Example.com", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void IdenticalEmailVerificationClaimsAreAcceptedButConflictingClaimsAreRejected()
    {
        var accepted = ValidatedGoogleIdentity.TryCreate(
            CreatePrincipal(emailVerificationClaimCount: 2),
            out var identity,
            out var acceptedFailureCode);

        Assert.True(accepted);
        Assert.NotNull(identity);
        Assert.Empty(acceptedFailureCode);

        var rejected = ValidatedGoogleIdentity.TryCreate(
            CreatePrincipal(emailVerificationClaimCount: 2, conflictingEmailVerificationClaims: true),
            out _,
            out var rejectedFailureCode);

        Assert.False(rejected);
        Assert.Equal("missing_or_ambiguous_email_verification", rejectedFailureCode);
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
}
