using MediaDock.Api.Authentication;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.Api.Administration;

public sealed record AdministratorActor(long UserId);

public sealed record AdminRegistrationRequestResponse(
    long Id,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? DecidedAt);

public sealed record AdminUserResponse(
    long Id,
    string Email,
    string GivenName,
    string FamilyName,
    string Status,
    string? Role,
    DateTimeOffset CreatedAt,
    AdminRegistrationRequestResponse? RegistrationRequest);

public sealed record AdminUsersPageResponse(
    IReadOnlyList<AdminUserResponse> Items,
    int Page,
    int PageSize,
    long TotalCount,
    int TotalPages);

public enum UserAdministrationResult
{
    Succeeded,
    Forbidden,
    NotFound,
    Conflict
}

public sealed class UserAdministrationApiService(MediaDockDbContext db)
{
    private const string AdminMutationLockName = "mediadock-user-administration";

    public Task<AdministratorActor?> ResolveAdministratorAsync(
        ValidatedGoogleIdentity identity,
        CancellationToken cancellationToken) =>
        db.ExternalIdentities
            .AsNoTracking()
            .Where(externalIdentity => externalIdentity.Issuer == identity.Issuer
                && externalIdentity.Subject == identity.Subject
                && externalIdentity.User.Status == "active"
                && externalIdentity.User.Role == "admin")
            .Select(externalIdentity => new AdministratorActor(externalIdentity.UserId))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<AdminUsersPageResponse> GetUsersAsync(
        AdminUsersQuery query,
        CancellationToken cancellationToken)
    {
        var page = query.Page ?? 1;
        var pageSize = query.PageSize ?? 50;
        var users = db.Users.AsNoTracking().AsQueryable();
        switch (query.State?.Trim().ToLowerInvariant() ?? "all")
        {
            case "all":
                break;
            case "requested":
                users = users.Where(user => user.RegistrationRequests.Any(request => request.Status == "pending"));
                break;
            case "active":
                users = users.Where(user => user.Status == "active");
                break;
            case "deactivated":
                users = users.Where(user => user.Status == "deactivated");
                break;
            default:
                throw new ArgumentException("State must be all, requested, active, or deactivated.", nameof(query));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search.Trim()}%";
            users = users.Where(user =>
                EF.Functions.ILike(user.NormalizedEmail, pattern)
                || EF.Functions.ILike(user.GivenName, pattern)
                || EF.Functions.ILike(user.FamilyName, pattern));
        }

        var totalCount = await users.LongCountAsync(cancellationToken);
        var items = await users
            .OrderBy(user => user.Status == "pending" ? 0 : user.Status == "active" ? 1 : 2)
            .ThenBy(user => user.NormalizedEmail)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(user => new AdminUserResponse(
                user.Id,
                user.NormalizedEmail,
                user.GivenName,
                user.FamilyName,
                user.Status,
                user.Role,
                user.CreatedAt,
                user.RegistrationRequests
                    .OrderByDescending(request => request.RequestedAt)
                    .Select(request => new AdminRegistrationRequestResponse(
                        request.Id,
                        request.Status,
                        request.RequestedAt,
                        request.DecidedAt))
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return new AdminUsersPageResponse(
            items,
            page,
            pageSize,
            totalCount,
            totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize));
    }

    public async Task<UserAdministrationResult> DecideRegistrationRequestAsync(
        long requestId,
        string decision,
        long actorId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await AcquireMutationLockAsync(cancellationToken);
        var actorEmail = await GetActiveAdministratorEmailAsync(actorId, cancellationToken);
        if (actorEmail is null)
        {
            return UserAdministrationResult.Forbidden;
        }

        var request = await db.RegistrationRequests
            .Include(registrationRequest => registrationRequest.User)
            .SingleOrDefaultAsync(registrationRequest => registrationRequest.Id == requestId, cancellationToken);
        if (request is null)
        {
            return UserAdministrationResult.NotFound;
        }

        if (request.Status != "pending" || request.User.Status != "pending")
        {
            return UserAdministrationResult.Conflict;
        }

        var now = DateTimeOffset.UtcNow;
        request.Status = decision == "approve" ? "approved" : "rejected";
        request.DecidedAt = now;
        request.DecidedBy = actorEmail;
        if (decision == "approve")
        {
            request.User.Status = "active";
            request.User.Role = "user";
        }

        request.User.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return UserAdministrationResult.Succeeded;
    }

    public async Task<UserAdministrationResult> SetUserStatusAsync(
        long userId,
        string status,
        long actorId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await AcquireMutationLockAsync(cancellationToken);
        if (await GetActiveAdministratorEmailAsync(actorId, cancellationToken) is null)
        {
            return UserAdministrationResult.Forbidden;
        }

        var user = await db.Users
            .FromSqlInterpolated($"SELECT * FROM users WHERE id = {userId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return UserAdministrationResult.NotFound;
        }

        if (user.Status == "pending")
        {
            return UserAdministrationResult.Conflict;
        }

        if (user.Status == status)
        {
            return UserAdministrationResult.Succeeded;
        }

        if (user.Role == "admin" && user.Status == "active" && status == "deactivated"
            && await db.Users.CountAsync(
                candidate => candidate.Status == "active" && candidate.Role == "admin",
                cancellationToken) <= 1)
        {
            return UserAdministrationResult.Conflict;
        }

        user.Status = status;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return UserAdministrationResult.Succeeded;
    }

    private async Task<string?> GetActiveAdministratorEmailAsync(long actorId, CancellationToken cancellationToken) =>
        await db.Users
            .Where(user => user.Id == actorId && user.Status == "active" && user.Role == "admin")
            .Select(user => user.NormalizedEmail)
            .SingleOrDefaultAsync(cancellationToken);

    private Task<int> AcquireMutationLockAsync(CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({AdminMutationLockName}, 0))",
            cancellationToken);
}