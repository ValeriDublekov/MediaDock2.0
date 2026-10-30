using MediaDock.Api.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace MediaDock.Api.Middleware;

internal sealed class ApiRequestProtectionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IAntiforgery antiforgery)
    {
        var isApiRequest = context.Request.Path.StartsWithSegments("/api");
        if (!isApiRequest || !IsUnsafeMethod(context.Request.Method) || !RequiresAntiforgery(context.Request))
        {
            await next(context);
            return;
        }

        if (!IsSameOriginRequest(context.Request))
        {
            await ApiProblemResponse.WriteAsync(
                context,
                StatusCodes.Status400BadRequest,
                "A same-origin request is required.",
                "The request origin does not match the application origin.",
                context.RequestAborted);
            return;
        }

        if (!await IsAntiforgeryValidAsync(context, antiforgery))
        {
            await ApiProblemResponse.WriteAsync(
                context,
                StatusCodes.Status400BadRequest,
                "A valid anti-forgery token is required.",
                "A valid anti-forgery request token and cookie are required.",
                context.RequestAborted);
            return;
        }

        await next(context);
    }

    private static bool RequiresAntiforgery(HttpRequest request)
    {
        if (request.Path.StartsWithSegments("/api/auth") || IsUploadRequest(request.Path))
        {
            return true;
        }

        return request.Cookies.ContainsKey(GoogleSignInSettings.SessionCookieName);
    }

    private static bool IsUploadRequest(PathString path) =>
        string.Equals(path.Value, "/api/personal-ratings/import", StringComparison.OrdinalIgnoreCase)
        || string.Equals(path.Value, "/api/background-jobs/oscar-import", StringComparison.OrdinalIgnoreCase)
        || string.Equals(path.Value, "/api/background-jobs/golden-globe-import", StringComparison.OrdinalIgnoreCase)
        || string.Equals(path.Value, "/api/background-jobs/imdb-title-import", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnsafeMethod(string method) =>
        !HttpMethods.IsGet(method)
        && !HttpMethods.IsHead(method)
        && !HttpMethods.IsOptions(method)
        && !HttpMethods.IsTrace(method);

    private static bool IsSameOriginRequest(HttpRequest request)
    {
        var fetchSite = request.Headers["Sec-Fetch-Site"].ToString();
        if (string.Equals(fetchSite, "cross-site", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fetchSite, "same-site", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var origin = request.Headers.Origin.ToString();
        if (!string.IsNullOrWhiteSpace(origin))
        {
            return MatchesRequestOrigin(origin, request, requireOriginForm: true);
        }

        var referer = request.Headers.Referer.ToString();
        return string.IsNullOrWhiteSpace(referer)
            || MatchesRequestOrigin(referer, request, requireOriginForm: false);
    }

    private static bool MatchesRequestOrigin(string value, HttpRequest request, bool requireOriginForm)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var candidate)
            || !Uri.TryCreate($"{request.Scheme}://{request.Host.Value}", UriKind.Absolute, out var expected)
            || !string.IsNullOrEmpty(candidate.UserInfo)
            || requireOriginForm && (candidate.AbsolutePath != "/" || candidate.Query.Length > 0 || candidate.Fragment.Length > 0))
        {
            return false;
        }

        return string.Equals(candidate.Scheme, expected.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.IdnHost, expected.IdnHost, StringComparison.OrdinalIgnoreCase)
            && candidate.Port == expected.Port;
    }

    private static async Task<bool> IsAntiforgeryValidAsync(HttpContext context, IAntiforgery antiforgery)
    {
        try
        {
            return await antiforgery.IsRequestValidAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }
}

internal static class ApiProblemResponse
{
    public static async Task WriteAsync(
        HttpContext context,
        int statusCode,
        string title,
        string detail,
        CancellationToken cancellationToken)
    {
        var traceId = context.TraceIdentifier;
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        };
        problem.Extensions["traceId"] = traceId;
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        context.Response.Headers["X-MediaDock-TraceId"] = traceId;
        await context.Response.WriteAsJsonAsync(
            problem,
            contentType: "application/problem+json",
            options: null,
            cancellationToken: cancellationToken);
    }
}