using Microsoft.AspNetCore.Hosting;

namespace MediaDock.Api.Middleware;

internal sealed class ApiResponseHeadersMiddleware(
    RequestDelegate next,
    IWebHostEnvironment environment,
    IConfiguration configuration)
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; form-action 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: https:; connect-src 'self'; font-src 'self' data:";

    public async Task InvokeAsync(HttpContext context)
    {
        var isPrivateResponse = context.Request.Path.StartsWithSegments("/api")
            || context.Request.Path.StartsWithSegments("/signin-oidc", StringComparison.OrdinalIgnoreCase);
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            headers["Content-Security-Policy-Report-Only"] = ContentSecurityPolicy;

            if (environment.IsProduction()
                && configuration.GetValue<bool>("SecurityHeaders:Hsts:Enabled")
                && context.Request.IsHttps)
            {
                headers["Strict-Transport-Security"] = "max-age=31536000";
            }

            if (isPrivateResponse
                || context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true)
            {
                headers.CacheControl = "no-store";
            }

            return Task.CompletedTask;
        });

        await next(context);
    }
}