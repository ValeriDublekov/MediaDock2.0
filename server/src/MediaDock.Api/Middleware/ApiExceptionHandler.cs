using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace MediaDock.Api.Middleware;

internal sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ProblemDetails problemDetails = exception switch
        {
            ApiValidationException validationException => new ValidationProblemDetails(validationException.Errors)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
                Instance = httpContext.Request.Path
            },
            ApiNotFoundException notFoundException => new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Not Found",
                Detail = notFoundException.Message,
                Instance = httpContext.Request.Path
            },
            ApiConflictException conflictException => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Conflict",
                Detail = conflictException.Message,
                Instance = httpContext.Request.Path
            },
            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Internal Server Error",
                Detail = exception.Message,
                Instance = httpContext.Request.Path
            }
        };

        var traceId = httpContext.TraceIdentifier;
        problemDetails.Extensions["traceId"] = traceId;
        logger.LogError(exception, "API request failed with status {StatusCode}. TraceId {TraceId}.", problemDetails.Status, traceId);
        httpContext.Response.StatusCode = problemDetails.Status ?? StatusCodes.Status500InternalServerError;
        httpContext.Response.ContentType = "application/problem+json";
        httpContext.Response.Headers["X-MediaDock-TraceId"] = traceId;
        await httpContext.Response.WriteAsJsonAsync(
            problemDetails,
            contentType: "application/problem+json",
            options: null,
            cancellationToken: cancellationToken);
        return true;
    }
}

internal sealed class ApiValidationException(IReadOnlyDictionary<string, string[]> errors) : Exception
{
    public IDictionary<string, string[]> Errors { get; } = new Dictionary<string, string[]>(errors);
}

internal sealed class ApiNotFoundException(string message) : Exception(message);

internal sealed class ApiConflictException(string message) : Exception(message);
