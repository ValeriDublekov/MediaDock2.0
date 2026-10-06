using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using MediaDock.Api.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace MediaDock.IntegrationTests;

[Trait("Category", "Api")]
public sealed class GoogleOidcProtocolTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("wrong-state")]
    [InlineData("wrong-nonce")]
    [InlineData("wrong-issuer")]
    [InlineData("wrong-audience")]
    [InlineData("wrong-signature")]
    [InlineData("expired")]
    [InlineData("unverified-email")]
    public async Task OidcCallbackAcceptsOnlyValidLocalTestTokens(string scenario)
    {
        using var factory = new OidcApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://app.test"),
            AllowAutoRedirect = false
        });

        using var challengeResponse = await client.GetAsync("/api/auth/google/login");
        Assert.True(
            challengeResponse.StatusCode == HttpStatusCode.Redirect,
            await challengeResponse.Content.ReadAsStringAsync());
        Assert.NotNull(challengeResponse.Headers.Location);
        var authorizationRequest = challengeResponse.Headers.Location!;
        Assert.Equal("https://app.test/signin-oidc", GetQueryValue(authorizationRequest, "redirect_uri"));
        Assert.Equal("code", GetQueryValue(authorizationRequest, "response_type"));
        Assert.Contains("openid", GetQueryValue(authorizationRequest, "scope"));
        Assert.False(string.IsNullOrWhiteSpace(GetQueryValue(authorizationRequest, "code_challenge")));

        var state = GetQueryValue(authorizationRequest, "state");
        var nonce = GetQueryValue(authorizationRequest, "nonce");
        factory.TokenHandler.TokenFactory = () => CreateIdToken(
            factory.SigningKey,
            nonce,
            scenario,
            includeNames: scenario != "missing-names");
        var callbackState = scenario == "wrong-state" ? "invalid-state" : state;
        using var callbackResponse = await client.GetAsync(
            $"/signin-oidc?code=test-code&state={Uri.EscapeDataString(callbackState)}");

        if (scenario == "valid")
        {
            Assert.Equal(HttpStatusCode.Redirect, callbackResponse.StatusCode);
            Assert.Equal("/", callbackResponse.Headers.Location?.OriginalString);
            var sessionCookie = Assert.Single(callbackResponse.Headers.GetValues("Set-Cookie"), value =>
                value.StartsWith("__Host-MediaDock.Session=", StringComparison.Ordinal));
            Assert.Contains("httponly", sessionCookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("secure", sessionCookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=lax", sessionCookie, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("expires=", sessionCookie, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("access_token", sessionCookie, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("id_token", sessionCookie, StringComparison.OrdinalIgnoreCase);

            using var antiforgeryResponse = await client.GetAsync("/api/auth/antiforgery");
            var token = await antiforgeryResponse.Content.ReadFromJsonAsync<AntiforgeryTokenResponse>();
            using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
            logoutRequest.Headers.Add("RequestVerificationToken", token!.RequestToken);
            using var logoutResponse = await client.SendAsync(logoutRequest);
            Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);
            Assert.Contains(logoutResponse.Headers.GetValues("Set-Cookie"), value =>
                value.StartsWith("__Host-MediaDock.Session=", StringComparison.Ordinal)
                && value.Contains("expires=", StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            Assert.Equal(HttpStatusCode.Redirect, callbackResponse.StatusCode);
            Assert.Equal("/?signin=failed", callbackResponse.Headers.Location?.OriginalString);
            Assert.True(
                !callbackResponse.Headers.TryGetValues("Set-Cookie", out var cookies)
                    || !cookies.Any(value => value.StartsWith("__Host-MediaDock.Session=", StringComparison.Ordinal)));
        }
    }

    private static string CreateIdToken(RSA signingKey, string nonce, string scenario, bool includeNames)
    {
        var now = DateTime.UtcNow;
        var issuer = scenario == "wrong-issuer" ? "https://issuer.invalid" : GoogleSignInSettings.GoogleIssuer;
        var audience = scenario == "wrong-audience" ? "another-client" : "test-google-client";
        var key = scenario == "wrong-signature" ? RSA.Create(2048) : signingKey;
        var claims = new List<Claim>
        {
            new("sub", "test-google-subject"),
            new("email", "person@example.com"),
            new("email_verified", scenario == "unverified-email" ? "false" : "true"),
            new("nonce", scenario == "wrong-nonce" ? "invalid-nonce" : nonce)
        };
        if (includeNames)
        {
            claims.Add(new Claim("given_name", "Test"));
            claims.Add(new Claim("family_name", "User"));
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = scenario == "expired" ? now.AddMinutes(-20) : now,
            NotBefore = scenario == "expired" ? now.AddMinutes(-20) : now.AddMinutes(-1),
            Expires = scenario == "expired" ? now.AddMinutes(-10) : now.AddMinutes(10),
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = new SigningCredentials(
                new RsaSecurityKey(key) { KeyId = "test-signing-key" },
                SecurityAlgorithms.RsaSha256)
        };

        try
        {
            return new JwtSecurityTokenHandler().CreateEncodedJwt(descriptor);
        }
        finally
        {
            if (!ReferenceEquals(key, signingKey))
            {
                key.Dispose();
            }
        }
    }

    private static string GetQueryValue(Uri uri, string key)
    {
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in query)
        {
            var separator = pair.IndexOf('=');
            if (separator >= 0 && Uri.UnescapeDataString(pair[..separator]) == key)
            {
                return Uri.UnescapeDataString(pair[(separator + 1)..].Replace('+', ' '));
            }
        }

        return string.Empty;
    }

    private sealed class OidcApiFactory : WebApplicationFactory<Program>
    {
        public RSA SigningKey { get; } = RSA.Create(2048);
        public TestTokenHandler TokenHandler { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MediaDock"] = "Host=127.0.0.1;Database=mediadock_test;Username=test;Password=test",
                    ["Authentication:Google:Enabled"] = "true",
                    ["Authentication:Google:ClientId"] = "test-google-client",
                    ["Authentication:Google:ClientSecret"] = "test-client-secret",
                    ["Authentication:Google:CallbackUri"] = "http://localhost:8080/signin-oidc"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<GoogleSignInSettings>();
                var googleSettings = new GoogleSignInSettings(
                    true,
                    "test-google-client",
                    "test-client-secret",
                    new Uri("https://app.test/signin-oidc"));
                services.AddSingleton(googleSettings);
                services.AddAuthentication().AddGoogleOpenIdConnect(googleSettings);
                services.PostConfigure<OpenIdConnectOptions>(GoogleSignInSettings.GoogleScheme, options =>
                {
                    var configuration = new OpenIdConnectConfiguration
                    {
                        Issuer = GoogleSignInSettings.GoogleIssuer,
                        AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth",
                        TokenEndpoint = "https://accounts.google.com/token",
                        UserInfoEndpoint = "https://openidconnect.googleapis.com/v1/userinfo"
                    };
                    configuration.SigningKeys.Add(new RsaSecurityKey(SigningKey)
                    {
                        KeyId = "test-signing-key"
                    });
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                    options.Backchannel = new HttpClient(TokenHandler, disposeHandler: false);
                });
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                SigningKey.Dispose();
                TokenHandler.Dispose();
            }
        }
    }

    private sealed class TestTokenHandler : HttpMessageHandler
    {
        public Func<string>? TokenFactory { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("/token", StringComparison.Ordinal) == true)
            {
                var token = TokenFactory?.Invoke()
                    ?? throw new InvalidOperationException("The test ID token has not been configured.");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        access_token = "test-access-token",
                        expires_in = 3600,
                        id_token = token,
                        token_type = "Bearer"
                    })
                });
            }

            if (request.RequestUri?.AbsolutePath.EndsWith("/userinfo", StringComparison.Ordinal) == true)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        sub = "test-google-subject",
                        email = "person@example.com",
                        email_verified = true,
                        given_name = "Test",
                        family_name = "User"
                    })
                });
            }

            throw new InvalidOperationException($"Unexpected OIDC backchannel request: {request.RequestUri}");
        }
    }
}