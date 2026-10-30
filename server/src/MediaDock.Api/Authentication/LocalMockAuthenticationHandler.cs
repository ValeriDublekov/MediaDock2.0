using System.Net;
using System.Security.Claims;
using MediaDock.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace MediaDock.Api.Authentication;

internal sealed class LocalMockAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    System.Text.Encodings.Web.UrlEncoder encoder,
    IConfiguration configuration,
    IHostEnvironment environment)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "MediaDock.LocalMock";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!environment.IsDevelopment()
            || !configuration.GetValue<bool>("Authentication:LocalMock:Enabled"))
        {
            return AuthenticateResult.NoResult();
        }

        if (Context.Connection.RemoteIpAddress is { } remoteAddress
            && !IPAddress.IsLoopback(remoteAddress))
        {
            return AuthenticateResult.NoResult();
        }

        var identities = await Context.RequestServices.GetRequiredService<MediaDockDbContext>()
            .ExternalIdentities
            .AsNoTracking()
            .Where(identity => identity.Issuer == GoogleSignInSettings.GoogleIssuer
                && identity.User.Status == "active"
                && (identity.User.Role == "user" || identity.User.Role == "admin"))
            .OrderBy(identity => identity.UserId)
            .Select(identity => new
            {
                identity.Issuer,
                identity.Subject,
                identity.User.NormalizedEmail,
                identity.User.GivenName,
                identity.User.FamilyName
            })
            .Take(2)
            .ToListAsync(Context.RequestAborted);

        if (identities.Count != 1)
        {
            return AuthenticateResult.Fail(
                "Local mock authentication requires exactly one active Google-linked user.");
        }

        var user = identities[0];
        var claims = new List<Claim>
        {
            new(GoogleSignInSettings.IssuerClaimType, user.Issuer),
            new("sub", user.Subject),
            new("email", user.NormalizedEmail),
            new("email_verified", "true")
        };
        if (!string.IsNullOrWhiteSpace(user.GivenName))
        {
            claims.Add(new Claim("given_name", user.GivenName));
        }

        if (!string.IsNullOrWhiteSpace(user.FamilyName))
        {
            claims.Add(new Claim("family_name", user.FamilyName));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme));
    }
}