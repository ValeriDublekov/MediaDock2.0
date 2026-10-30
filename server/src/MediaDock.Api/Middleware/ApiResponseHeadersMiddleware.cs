namespace MediaDock.Api.Middleware;

internal sealed class ApiResponseHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.OnStarting(static state =>
            {
                var response = (HttpResponse)state;
                response.Headers.CacheControl = "no-store";
                return Task.CompletedTask;
            }, context.Response);
        }

        await next(context);
    }
}