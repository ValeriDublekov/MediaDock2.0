using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MediaDock.Api.Authentication;

internal static class AuthenticationEndpoints
{
    public static WebApplication MapAuthenticationEndpoints(this WebApplication app)
    {
        app.MapGet("/api/auth/google/login", (HttpContext context, GoogleSignInSettings settings) =>
        {
            if (!settings.Enabled)
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Google sign-in is disabled.");
            }

            if (!settings.MatchesRequestOrigin(context.Request))
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Google sign-in is not configured for this application origin.");
            }

            return Results.Challenge(
                new AuthenticationProperties { RedirectUri = "/" },
                [GoogleSignInSettings.GoogleScheme]);
        })
            .WithName("StartGoogleSignIn")
            .WithSummary("Start the optional Google sign-in flow.")
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        app.MapGet("/api/auth/session", async Task<Results<Ok<CurrentSessionResponse>, UnauthorizedHttpResult>> (
            HttpContext context,
            GoogleSignInSettings signInSettings,
            GoogleSessionService sessionService,
            CancellationToken cancellationToken) =>
        {
            if (context.User.Identity?.IsAuthenticated != true)
            {
                return TypedResults.Ok(CurrentSessionResponse.Anonymous with
                {
                    SignInEnabled = signInSettings.Enabled
                });
            }

            if (!ValidatedGoogleIdentity.TryCreate(context.User, out var identity))
            {
                return TypedResults.Unauthorized();
            }

            var session = await sessionService.GetCurrentSessionAsync(identity!, cancellationToken);
            return TypedResults.Ok(session with { SignInEnabled = signInSettings.Enabled });
        })
            .WithName("GetCurrentSession")
            .WithSummary("Return the optional Google identity associated with this browser session.")
            .Produces<CurrentSessionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        app.MapGet("/api/auth/antiforgery", (HttpContext context, IAntiforgery antiforgery) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var tokens = antiforgery.GetAndStoreTokens(context);
            return TypedResults.Ok(new AntiforgeryTokenResponse(tokens.RequestToken!));
        })
            .WithName("GetAntiforgeryToken")
            .WithSummary("Issue a request token for cookie-authenticated state changes.");

        app.MapPost("/api/auth/google/link", async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> (
            HttpContext context,
            IAntiforgery antiforgery,
            GoogleSessionService sessionService,
            CancellationToken cancellationToken) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "A valid anti-forgery token is required.");
            }

            if (context.User.Identity?.IsAuthenticated != true
                || !ValidatedGoogleIdentity.TryCreate(context.User, out var identity))
            {
                return TypedResults.Unauthorized();
            }

            if (!await sessionService.LinkMatchingAccountAsync(identity!, cancellationToken))
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "This Google identity can no longer be linked to the matching account.");
            }

            return TypedResults.NoContent();
        })
            .WithName("LinkGoogleAccount")
            .WithSummary("Confirm one-time linking to the account matching the verified Google email.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);

        app.MapPost("/api/auth/registration-requests", async Task<Results<Ok<RegistrationRequestResponse>, UnauthorizedHttpResult, ProblemHttpResult>> (
            HttpContext context,
            IAntiforgery antiforgery,
            GoogleSessionService sessionService,
            CancellationToken cancellationToken) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "A valid anti-forgery token is required.");
            }

            if (context.User.Identity?.IsAuthenticated != true
                || !ValidatedGoogleIdentity.TryCreate(context.User, out var identity))
            {
                return TypedResults.Unauthorized();
            }

            var result = await sessionService.RequestRegistrationAsync(identity!, cancellationToken);
            if (result.Kind == RegistrationRequestResultKind.ProfileIncomplete)
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Your Google profile must include both a given name and family name to request registration.");
            }

            if (result.Kind == RegistrationRequestResultKind.NotEligible)
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "This Google identity is not eligible to request registration.");
            }

            return TypedResults.Ok(result.Request!);
        })
            .WithName("RequestRegistration")
            .WithSummary("Submit an idempotent registration request for the validated Google identity.")
            .Produces<RegistrationRequestResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);

        app.MapPost("/api/auth/logout", async Task<Results<NoContent, ProblemHttpResult>> (
            HttpContext context,
            IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "A valid anti-forgery token is required.");
            }

            await context.SignOutAsync(GoogleSignInSettings.CookieScheme);
            return TypedResults.NoContent();
        })
            .WithName("Logout")
            .WithSummary("Clear the MediaDock browser session.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }
}

public sealed record AntiforgeryTokenResponse(string RequestToken);

public sealed record CurrentSessionIdentity(
    string Issuer,
    string Subject,
    string Email,
    string? GivenName,
    string? FamilyName)
{
    public bool ProfileComplete => GivenName is not null && FamilyName is not null;
}

public sealed record CurrentSessionUser(string Email, string GivenName, string FamilyName, string Status);
public sealed record CurrentSessionRegistrationRequest(string Status, DateTimeOffset RequestedAt, DateTimeOffset? DecidedAt);

public sealed record CurrentSessionResponse(
    bool Authenticated,
    CurrentSessionIdentity? Identity,
    CurrentSessionUser? User,
    string AccountState)
{
    public static CurrentSessionResponse Anonymous { get; } = new(false, null, null, "anonymous");
    public bool SignInEnabled { get; init; }
    public CurrentSessionRegistrationRequest? RegistrationRequest { get; init; }
}

public sealed record RegistrationRequestResponse(string Status, DateTimeOffset RequestedAt, DateTimeOffset? DecidedAt);

internal static class ValidatedGoogleIdentityResponseExtensions
{
    public static CurrentSessionIdentity ToResponse(this ValidatedGoogleIdentity identity) =>
        new(identity.Issuer, identity.Subject, identity.Email, identity.GivenName, identity.FamilyName);
}