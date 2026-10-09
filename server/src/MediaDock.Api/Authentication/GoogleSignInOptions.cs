using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using MediaDock.Infrastructure.Users;

namespace MediaDock.Api.Authentication;

public sealed record GoogleSignInSettings(bool Enabled, string ClientId, string ClientSecret, Uri? CallbackUri)
{
    public const string CookieScheme = "MediaDock.Session";
    public const string GoogleScheme = "Google";
    public const string IssuerClaimType = "mediadock:google:issuer";
    public const string GoogleIssuer = "https://accounts.google.com";

    public static GoogleSignInSettings FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("Authentication:Google");
        var enabled = section.GetValue<bool>("Enabled");
        var clientId = section["ClientId"] ?? string.Empty;
        var clientSecret = section["ClientSecret"] ?? string.Empty;
        var callbackValue = section["CallbackUri"];

        if (!enabled)
        {
            return new GoogleSignInSettings(false, clientId, clientSecret, null);
        }

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException(
                "Google sign-in is enabled but Authentication:Google:ClientId or ClientSecret is missing.");
        }

        if (!Uri.TryCreate(callbackValue, UriKind.Absolute, out var callbackUri)
            || !IsAllowedCallback(callbackUri))
        {
            throw new InvalidOperationException(
                "Google sign-in requires CallbackUri to be the localhost callback or an HTTPS hostname callback ending in /signin-oidc.");
        }

        return new GoogleSignInSettings(true, clientId, clientSecret, callbackUri);
    }

    public bool MatchesRequestOrigin(HttpRequest request)
    {
        if (CallbackUri is null
            || !Uri.TryCreate($"{request.Scheme}://{request.Host.Value}", UriKind.Absolute, out var requestUri))
        {
            return false;
        }

        return string.Equals(requestUri.Scheme, CallbackUri.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(requestUri.Authority, CallbackUri.Authority, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAllowedCallback(Uri callbackUri)
    {
        if (!string.IsNullOrEmpty(callbackUri.UserInfo)
            || !string.IsNullOrEmpty(callbackUri.Query)
            || !string.IsNullOrEmpty(callbackUri.Fragment)
            || !string.Equals(callbackUri.AbsolutePath, "/signin-oidc", StringComparison.Ordinal))
        {
            return false;
        }

        var localhostCallback = callbackUri.Scheme == Uri.UriSchemeHttp
            && string.Equals(callbackUri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            && callbackUri.Port == 8080;
        var secureHostnameCallback = callbackUri.Scheme == Uri.UriSchemeHttps
            && !string.Equals(callbackUri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            && !System.Net.IPAddress.TryParse(callbackUri.Host, out _);

        return localhostCallback || secureHostnameCallback;
    }
}

public sealed record ValidatedGoogleIdentity(
    string Issuer,
    string Subject,
    string Email,
    string NormalizedEmail,
    string? GivenName,
    string? FamilyName)
{
    public static bool TryCreate(ClaimsPrincipal principal, out ValidatedGoogleIdentity? identity)
        => TryCreate(principal, out identity, out _);

    public static bool TryCreate(
        ClaimsPrincipal principal,
        out ValidatedGoogleIdentity? identity,
        out string failureCode)
    {
        identity = null;
        failureCode = "invalid_google_identity";
        if (principal.Identity?.IsAuthenticated != true)
        {
            failureCode = "not_authenticated";
            return false;
        }

        if (!TrySingleClaim(principal, GoogleSignInSettings.IssuerClaimType, required: true, out var issuer))
        {
            failureCode = "missing_or_ambiguous_issuer";
            return false;
        }

        if (!string.Equals(issuer, GoogleSignInSettings.GoogleIssuer, StringComparison.Ordinal))
        {
            failureCode = "unsupported_issuer";
            return false;
        }

        if (!TrySingleClaim(principal, "sub", required: true, out var subject))
        {
            failureCode = "missing_or_ambiguous_subject";
            return false;
        }

        if (!TrySingleClaim(principal, "email", required: true, out var email))
        {
            failureCode = "missing_or_ambiguous_email";
            return false;
        }

        if (!TrySingleClaim(principal, "email_verified", required: true, out var verified))
        {
            failureCode = "missing_or_ambiguous_email_verification";
            return false;
        }

        if (!string.Equals(verified, "true", StringComparison.OrdinalIgnoreCase))
        {
            failureCode = "email_not_verified";
            return false;
        }

        if (!TrySingleClaim(principal, "given_name", required: false, out var givenName))
        {
            failureCode = "ambiguous_given_name";
            return false;
        }

        if (!TrySingleClaim(principal, "family_name", required: false, out var familyName))
        {
            failureCode = "ambiguous_family_name";
            return false;
        }

        if (subject.Length > 255)
        {
            failureCode = "subject_too_long";
            return false;
        }

        try
        {
            var normalizedEmail = UserEmail.Normalize(email);
            identity = new ValidatedGoogleIdentity(
                issuer,
                subject,
                email.Trim(),
                normalizedEmail,
                NormalizeName(givenName),
                NormalizeName(familyName));
            failureCode = string.Empty;
            return true;
        }
        catch (ArgumentException)
        {
            failureCode = "invalid_email";
            return false;
        }
    }

    private static bool TrySingleClaim(
        ClaimsPrincipal principal,
        string claimType,
        bool required,
        out string value)
    {
        var claims = principal.FindAll(claimType).ToArray();
        if (claims.Length != (required ? 1 : Math.Min(claims.Length, 1)))
        {
            value = string.Empty;
            return false;
        }

        value = claims.Length == 0 ? string.Empty : claims[0].Value;
        return !required || !string.IsNullOrWhiteSpace(value);
    }

    private static string? NormalizeName(string value)
    {
        var normalized = value.Trim();
        return normalized.Length is > 0 and <= 200 ? normalized : null;
    }
}

public static class GoogleSignInServiceCollectionExtensions
{
    public static IServiceCollection AddGoogleSignIn(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = GoogleSignInSettings.FromConfiguration(configuration);
        services.AddSingleton(settings);
        services.AddScoped<GoogleSessionService>();
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "RequestVerificationToken";
            options.Cookie.Name = "MediaDock.Antiforgery";
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.IsEssential = true;
        });

        var authentication = services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = GoogleSignInSettings.CookieScheme;
            options.DefaultSignInScheme = GoogleSignInSettings.CookieScheme;
            options.DefaultChallengeScheme = GoogleSignInSettings.CookieScheme;
        });
        authentication.AddCookie(GoogleSignInSettings.CookieScheme, options =>
        {
            options.Cookie.Name = "__Host-MediaDock.Session";
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.IsEssential = true;
            options.ExpireTimeSpan = TimeSpan.FromHours(12);
            options.SlidingExpiration = false;
        });

        if (settings.Enabled)
        {
            authentication.AddGoogleOpenIdConnect(settings);
        }

        return services;
    }

    public static AuthenticationBuilder AddGoogleOpenIdConnect(
        this AuthenticationBuilder authentication,
        GoogleSignInSettings settings)
    {
        if (!settings.Enabled || settings.CallbackUri is null)
        {
            throw new ArgumentException("Google sign-in settings must be enabled and include a callback URI.", nameof(settings));
        }

        authentication.AddOpenIdConnect(GoogleSignInSettings.GoogleScheme, options =>
        {
            options.Authority = GoogleSignInSettings.GoogleIssuer;
            options.ClientId = settings.ClientId;
            options.ClientSecret = settings.ClientSecret;
            options.CallbackPath = "/signin-oidc";
            options.SignInScheme = GoogleSignInSettings.CookieScheme;
            options.ResponseType = "code";
            options.UsePkce = true;
            options.SaveTokens = false;
            options.GetClaimsFromUserInfoEndpoint = true;
            options.MapInboundClaims = false;
            options.RequireHttpsMetadata = true;
            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Scope.Add("email");
            options.Scope.Add("profile");
            options.ClaimActions.MapUniqueJsonKey("email", "email");
            options.ClaimActions.MapUniqueJsonKey("email_verified", "email_verified");
            options.ClaimActions.MapUniqueJsonKey("given_name", "given_name");
            options.ClaimActions.MapUniqueJsonKey("family_name", "family_name");
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = GoogleSignInSettings.GoogleIssuer,
                ValidateAudience = true,
                ValidAudience = settings.ClientId,
                ValidateIssuerSigningKey = true,
                RequireSignedTokens = true,
                RequireExpirationTime = true,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2)
            };
            options.Events = new OpenIdConnectEvents
            {
                OnTokenValidated = context =>
                {
                    var issuer = context.SecurityToken.Issuer;
                    if (!string.Equals(issuer, GoogleSignInSettings.GoogleIssuer, StringComparison.Ordinal)
                        || context.Principal?.Identity is not ClaimsIdentity identity)
                    {
                        context.Fail("Google identity validation failed.");
                        return Task.CompletedTask;
                    }

                    identity.AddClaim(new Claim(GoogleSignInSettings.IssuerClaimType, issuer));
                    if (!ValidatedGoogleIdentity.TryCreate(context.Principal, out _))
                    {
                        context.Fail("Google must provide an identity with a verified email address.");
                    }

                    return Task.CompletedTask;
                },
                OnRedirectToIdentityProvider = context =>
                {
                    if (!settings.MatchesRequestOrigin(context.Request))
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                        return Task.CompletedTask;
                    }

                    context.ProtocolMessage.RedirectUri = settings.CallbackUri.AbsoluteUri;
                    return Task.CompletedTask;
                },
                OnMessageReceived = context =>
                {
                    if (!settings.MatchesRequestOrigin(context.Request))
                    {
                        context.Fail("The sign-in callback origin does not match its configured URI.");
                    }

                    return Task.CompletedTask;
                },
                OnRemoteFailure = context =>
                {
                    context.HandleResponse();
                    context.Response.Redirect("/?signin=failed");
                    return Task.CompletedTask;
                }
            };
        });

        return authentication;
    }
}