using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediaDock.Api.Authentication;

public sealed class GoogleSessionService(MediaDockDbContext db)
{
    public async Task<CurrentSessionResponse> GetCurrentSessionAsync(
        ValidatedGoogleIdentity identity,
        CancellationToken cancellationToken)
    {
        var linkedUser = await db.ExternalIdentities
            .AsNoTracking()
            .Where(externalIdentity => externalIdentity.Issuer == identity.Issuer
                && externalIdentity.Subject == identity.Subject)
            .Select(externalIdentity => externalIdentity.User)
            .SingleOrDefaultAsync(cancellationToken);

        if (linkedUser is not null)
        {
            return new CurrentSessionResponse(
                true,
                identity.ToResponse(),
                new CurrentSessionUser(linkedUser.NormalizedEmail, linkedUser.GivenName, linkedUser.FamilyName, linkedUser.Status),
                "linked");
        }

        var matchingUser = await db.Users
            .AsNoTracking()
            .Where(user => user.NormalizedEmail == identity.NormalizedEmail)
            .Select(user => new
            {
                user.NormalizedEmail,
                user.GivenName,
                user.FamilyName,
                user.Status,
                HasExternalIdentity = user.ExternalIdentities.Any()
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (matchingUser is null)
        {
            return new CurrentSessionResponse(true, identity.ToResponse(), null, "unmatched");
        }

        if (matchingUser.HasExternalIdentity)
        {
            return new CurrentSessionResponse(true, identity.ToResponse(), null, "account_conflict");
        }

        return new CurrentSessionResponse(
            true,
            identity.ToResponse(),
            new CurrentSessionUser(
                matchingUser.NormalizedEmail,
                matchingUser.GivenName,
                matchingUser.FamilyName,
                matchingUser.Status),
            "link_confirmation_required");
    }

    public async Task<bool> LinkMatchingAccountAsync(
        ValidatedGoogleIdentity identity,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var identityLockKey = $"{identity.Issuer}\n{identity.Subject}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({identityLockKey}, 0))",
            cancellationToken);

        if (await db.ExternalIdentities.AnyAsync(
                externalIdentity => externalIdentity.Issuer == identity.Issuer
                    && externalIdentity.Subject == identity.Subject,
                cancellationToken))
        {
            return false;
        }

        var matchingUser = await db.Users
            .FromSqlInterpolated($"SELECT * FROM users WHERE normalized_email = {identity.NormalizedEmail} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (matchingUser is null
            || await db.ExternalIdentities.AnyAsync(
                externalIdentity => externalIdentity.UserId == matchingUser.Id,
                cancellationToken))
        {
            return false;
        }

        db.ExternalIdentities.Add(new ExternalIdentity
        {
            UserId = matchingUser.Id,
            Issuer = identity.Issuer,
            Subject = identity.Subject,
            CreatedAt = DateTimeOffset.UtcNow
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsUniqueConflict(exception))
        {
            return false;
        }
    }

    private static bool IsUniqueConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}