using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace MediaDock.Api.Middleware;

internal static class ApiRateLimiting
{
    public static IServiceCollection AddApiRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var isOneShotHostOperation = configuration.GetValue<bool>("migrate")
            || configuration.GetValue<bool>("bootstrap-admin");
        services.AddRateLimiter(options =>
        {
            var settings = ApiRateLimitSettings.FromConfiguration(
                configuration,
                requireExplicitValues: !environment.IsDevelopment() && !isOneShotHostOperation);
            var requestRateLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var policy = FindPolicy(context, settings);
                if (policy is null)
                {
                    return RateLimitPartition.GetNoLimiter<string>("unlimited");
                }

                var partitionKey = $"{policy.Name}:{GetClientKey(context)}";
                return RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey,
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = policy.Settings.PermitLimit,
                        Window = TimeSpan.FromSeconds(policy.Settings.WindowSeconds),
                        SegmentsPerWindow = Math.Min(policy.Settings.WindowSeconds, 4),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });
            var concurrencyLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var policy = FindPolicy(context, settings);
                if (policy?.Settings.ConcurrencyLimit is not > 0)
                {
                    return RateLimitPartition.GetNoLimiter<string>("unlimited");
                }

                var partitionKey = $"{policy.Name}:{GetClientKey(context)}";
                return RateLimitPartition.GetConcurrencyLimiter(
                    partitionKey,
                    _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = policy.Settings.ConcurrencyLimit.Value,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    });
            });

            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(requestRateLimiter, concurrencyLimiter);
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (rejected, cancellationToken) =>
            {
                var retryAfterSeconds = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
                    : 1;
                rejected.HttpContext.Response.Headers["Retry-After"] = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
                await ApiProblemResponse.WriteAsync(
                    rejected.HttpContext,
                    StatusCodes.Status429TooManyRequests,
                    "Too many requests.",
                    "Retry after the indicated interval.",
                    cancellationToken);
            };
        });

        return services;
    }

    private static RateLimitPolicy? FindPolicy(HttpContext context, ApiRateLimitSettings settings)
    {
        var path = context.Request.Path;
        var method = context.Request.Method;
        if (path.Equals("/api/auth/registration-requests", StringComparison.OrdinalIgnoreCase))
        {
            return new RateLimitPolicy("registration", settings.Registration);
        }

        if (path.StartsWithSegments("/api/auth"))
        {
            return new RateLimitPolicy("authentication", settings.Authentication);
        }

        if (IsUploadPath(path) && HttpMethods.IsPost(method))
        {
            return new RateLimitPolicy("uploads", settings.Uploads);
        }

        if (HttpMethods.IsPost(method) && IsDispatchPath(path))
        {
            return new RateLimitPolicy("dispatch", settings.JobDispatch);
        }

        if (IsExpensiveRead(method, path))
        {
            return new RateLimitPolicy("search", settings.Search);
        }

        return null;
    }

    private static bool IsDispatchPath(PathString path) =>
        path.StartsWithSegments("/api/background-jobs")
        || path.Equals("/api/personal-ratings/enrichment", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/api/deployment/run", StringComparison.OrdinalIgnoreCase)
        || path.Value?.StartsWith("/api/recommendations/", StringComparison.OrdinalIgnoreCase) == true
            && path.Value.EndsWith("/watched", StringComparison.OrdinalIgnoreCase);

    private static bool IsExpensiveRead(string method, PathString path)
    {
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method))
        {
            return path.StartsWithSegments("/api/catalog")
                || path.StartsWithSegments("/api/oscars")
                || path.StartsWithSegments("/api/golden-globes")
                || path.StartsWithSegments("/api/movie-awards")
                || path.StartsWithSegments("/api/recommendations")
                || path.StartsWithSegments("/api/parse-logs");
        }

        if (HttpMethods.IsPost(method))
        {
            var value = path.Value ?? string.Empty;
            return value.EndsWith("/imdb-candidates", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith("/imdb-candidate", StringComparison.OrdinalIgnoreCase)
                || path.StartsWithSegments("/api/catalog/rss")
                    && value.EndsWith("/refresh", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsUploadPath(PathString path) =>
        path.Equals("/api/personal-ratings/import", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/api/background-jobs/oscar-import", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/api/background-jobs/golden-globe-import", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/api/background-jobs/imdb-title-import", StringComparison.OrdinalIgnoreCase);

    private static string GetClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private sealed record RateLimitPolicy(string Name, ApiRateLimitBucket Settings);
}

internal sealed record ApiRateLimitBucket(int PermitLimit, int WindowSeconds, int? ConcurrencyLimit);

internal sealed record ApiRateLimitSettings(
    ApiRateLimitBucket Authentication,
    ApiRateLimitBucket Registration,
    ApiRateLimitBucket Search,
    ApiRateLimitBucket Uploads,
    ApiRateLimitBucket JobDispatch)
{
    public static ApiRateLimitSettings FromConfiguration(IConfiguration configuration, bool requireExplicitValues) => new(
        ReadBucket(configuration, "Authentication", 120, 60, null, requireExplicitValues),
        ReadBucket(configuration, "Registration", 5, 3600, null, requireExplicitValues),
        ReadBucket(configuration, "Search", 60, 60, null, requireExplicitValues),
        ReadBucket(configuration, "Uploads", 10, 60, 2, requireExplicitValues),
        ReadBucket(configuration, "JobDispatch", 30, 60, 4, requireExplicitValues));

    private static ApiRateLimitBucket ReadBucket(
        IConfiguration configuration,
        string name,
        int defaultPermitLimit,
        int defaultWindowSeconds,
        int? defaultConcurrencyLimit,
        bool requireExplicitValues)
    {
        var section = configuration.GetSection($"AbuseControls:RateLimits:{name}");
        var permitLimit = ReadValue(section, "PermitLimit", defaultPermitLimit, requireExplicitValues, name);
        var windowSeconds = ReadValue(section, "WindowSeconds", defaultWindowSeconds, requireExplicitValues, name);
        var concurrencyLimit = section.GetValue<int?>("ConcurrencyLimit");
        if (defaultConcurrencyLimit is not null && requireExplicitValues && concurrencyLimit is null)
        {
            throw MissingSetting(name, "ConcurrencyLimit");
        }

        concurrencyLimit ??= defaultConcurrencyLimit;
        if (permitLimit is < 1 or > 100_000
            || windowSeconds is < 1 or > 86_400
            || concurrencyLimit is < 1 or > 10_000)
        {
            throw new InvalidOperationException(
                $"AbuseControls:RateLimits:{name} contains a value outside its supported range.");
        }

        return new ApiRateLimitBucket(permitLimit, windowSeconds, concurrencyLimit);
    }

    private static int ReadValue(
        IConfigurationSection section,
        string property,
        int fallback,
        bool required,
        string name)
    {
        var configuredValue = section.GetValue<int?>(property);
        if (required && configuredValue is null)
        {
            throw MissingSetting(name, property);
        }

        return configuredValue ?? fallback;
    }

    private static InvalidOperationException MissingSetting(string name, string property) =>
        new($"Non-Development API startup requires AbuseControls:RateLimits:{name}:{property} to be configured.");
}