using System.Security.Claims;
using MediaDock.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.Api.Authentication;

public static class AccessPolicies
{
    public const string Administrator = "Administrator";
    public const string OwnerScopedFavorites = "OwnerScopedFavorites";
    public const string OwnerScopedIgnoredMovies = "OwnerScopedIgnoredMovies";
    public const string OwnerScopedPersonalRatings = "OwnerScopedPersonalRatings";
    public const string OwnerScopedRecommendations = "OwnerScopedRecommendations";
}

public sealed record AccessControlSettings(bool Enforced)
{
    public static AccessControlSettings FromConfiguration(IConfiguration configuration) =>
        new(configuration.GetValue<bool>("AccessControl:Enforced"));
}

public sealed record CurrentUser(long Id, string Email, string Role);

public sealed class CurrentUserContext(MediaDockDbContext db)
{
    public async Task<CurrentUser?> ResolveAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (!ValidatedGoogleIdentity.TryCreate(principal, out var identity))
        {
            return null;
        }

        return await db.ExternalIdentities
            .AsNoTracking()
            .Where(externalIdentity => externalIdentity.Issuer == identity!.Issuer
                && externalIdentity.Subject == identity.Subject
                && externalIdentity.User.Status == "active"
                && (externalIdentity.User.Role == "user" || externalIdentity.User.Role == "admin"))
            .Select(externalIdentity => new CurrentUser(
                externalIdentity.UserId,
                externalIdentity.User.NormalizedEmail,
                externalIdentity.User.Role!))
            .SingleOrDefaultAsync(cancellationToken);
    }
}

public sealed record CurrentUserRequirement(
    string? RequiredRole = null,
    bool RequireActiveUserInOpenMode = false) : IAuthorizationRequirement;

public sealed class CurrentUserAuthorizationHandler(
    AccessControlSettings settings,
    CurrentUserContext currentUserContext)
    : IAuthorizationHandler
{
    public async Task HandleAsync(AuthorizationHandlerContext context)
    {
        foreach (var requirement in context.PendingRequirements.OfType<CurrentUserRequirement>().ToArray())
        {
            if (!settings.Enforced && !requirement.RequireActiveUserInOpenMode)
            {
                context.Succeed(requirement);
                continue;
            }

            if (context.Resource is not HttpContext httpContext)
            {
                continue;
            }

            var currentUser = await currentUserContext.ResolveAsync(context.User, httpContext.RequestAborted);
            if (currentUser is not null
                && (requirement.RequiredRole is null || currentUser.Role == requirement.RequiredRole))
            {
                context.Succeed(requirement);
            }
        }
    }
}

public static class AccessControlServiceCollectionExtensions
{
    public static IServiceCollection AddAccessControl(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(serviceProvider => AccessControlSettings.FromConfiguration(
            serviceProvider.GetRequiredService<IConfiguration>()));
        services.AddScoped<CurrentUserContext>();
        services.AddScoped<IAuthorizationHandler, CurrentUserAuthorizationHandler>();
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .AddRequirements(new CurrentUserRequirement())
                .Build();
            options.AddPolicy(
                AccessPolicies.Administrator,
                policy => policy.AddRequirements(new CurrentUserRequirement("admin")));
            options.AddPolicy(
                AccessPolicies.OwnerScopedFavorites,
                policy => policy.AddRequirements(new CurrentUserRequirement(RequireActiveUserInOpenMode: true)));
            options.AddPolicy(
                AccessPolicies.OwnerScopedIgnoredMovies,
                policy => policy.AddRequirements(new CurrentUserRequirement(RequireActiveUserInOpenMode: true)));
            options.AddPolicy(
                AccessPolicies.OwnerScopedPersonalRatings,
                policy => policy.AddRequirements(new CurrentUserRequirement(RequireActiveUserInOpenMode: true)));
            options.AddPolicy(
                AccessPolicies.OwnerScopedRecommendations,
                policy => policy.AddRequirements(new CurrentUserRequirement(RequireActiveUserInOpenMode: true)));
        });
        return services;
    }
}