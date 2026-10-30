using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using MediaDock.Api.Administration;
using MediaDock.Api.Authentication;
using MediaDock.Api.Common;
using MediaDock.Api.Favorites;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediaDock.IntegrationTests;

internal static class GoogleAuthenticationApiTestSupport
{
    internal static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/auth/antiforgery");
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<AntiforgeryTokenResponse>();
        return token!.RequestToken;
    }

    internal static ClaimsPrincipal CreatePrincipal(
        bool emailVerified = true,
        bool includeNames = true,
        string email = "Person@Example.com",
        string subject = "google-subject-1",
        int emailVerificationClaimCount = 1,
        bool conflictingEmailVerificationClaims = false)
    {
        var claims = new List<Claim>
        {
            new(GoogleSignInSettings.IssuerClaimType, GoogleSignInSettings.GoogleIssuer),
            new("sub", subject),
            new("email", email)
        };
        for (var index = 0; index < emailVerificationClaimCount; index++)
        {
            var claimValue = conflictingEmailVerificationClaims && index > 0
                ? emailVerified ? "false" : "true"
                : emailVerified ? "true" : "false";
            claims.Add(new Claim("email_verified", claimValue));
        }

        if (includeNames)
        {
            claims.Add(new Claim("given_name", "Google"));
            claims.Add(new Claim("family_name", "Profile"));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test-google"));
    }

    internal static GoogleSignInSettings CreateSettings(string callbackUri)
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

    internal sealed class ApiFactory(
        string connectionString,
        ClaimsPrincipal? principal = null,
        ILoggerProvider? loggerProvider = null,
        bool accessControlEnforced = false,
        bool localMockEnabled = false,
        string environmentName = "Development") : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environmentName);
            if (loggerProvider is not null)
            {
                builder.ConfigureLogging(logging => logging.AddProvider(loggerProvider));
            }

            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MediaDock"] = connectionString,
                    ["Authentication:Google:Enabled"] = "false",
                    ["Authentication:LocalMock:Enabled"] = localMockEnabled.ToString(),
                    ["AccessControl:Enforced"] = accessControlEnforced.ToString()
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                if (!localMockEnabled)
                {
                    services.AddAuthentication()
                        .AddScheme<TestGoogleOptions, TestGoogleHandler>(TestGoogleOptions.Scheme, options =>
                        {
                            options.Principal = principal;
                        });
                    services.PostConfigure<AuthenticationOptions>(options =>
                    {
                        options.DefaultAuthenticateScheme = TestGoogleOptions.Scheme;
                    });
                }
            });
        }
    }

    internal sealed class TestLoggerProvider : ILoggerProvider
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

    internal sealed class TestGoogleOptions : AuthenticationSchemeOptions
    {
        public const string Scheme = "TestGoogle";
        public ClaimsPrincipal? Principal { get; set; }
    }

    internal sealed class TestGoogleHandler(
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
