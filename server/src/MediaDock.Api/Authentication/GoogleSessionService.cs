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
            var registrationRequest = await GetLatestRegistrationRequestAsync(linkedUser.Id, cancellationToken);
            return new CurrentSessionResponse(
                true,
                identity.ToResponse(),
                new CurrentSessionUser(linkedUser.NormalizedEmail, linkedUser.GivenName, linkedUser.FamilyName, linkedUser.Status),
                "linked")
            {
                RegistrationRequest = registrationRequest
            };
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

    public async Task<RegistrationRequestResult> RequestRegistrationAsync(
        ValidatedGoogleIdentity identity,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var identityLockKey = $"{identity.Issuer}\n{identity.Subject}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({identityLockKey}, 0))",
            cancellationToken);

        var linkedUserId = await db.ExternalIdentities
            .Where(externalIdentity => externalIdentity.Issuer == identity.Issuer
                && externalIdentity.Subject == identity.Subject)
            .Select(externalIdentity => (long?)externalIdentity.UserId)
            .SingleOrDefaultAsync(cancellationToken);
        if (linkedUserId.HasValue)
        {
            var existingRequest = await GetLatestRegistrationRequestAsync(linkedUserId.Value, cancellationToken);
            return existingRequest is null
                ? new RegistrationRequestResult(RegistrationRequestResultKind.NotEligible, null)
                : new RegistrationRequestResult(
                    RegistrationRequestResultKind.CreatedOrExisting,
                    new RegistrationRequestResponse(
                        existingRequest.Status,
                        existingRequest.RequestedAt,
                        existingRequest.DecidedAt));
        }

        if (identity.GivenName is null || identity.FamilyName is null)
        {
            return new RegistrationRequestResult(RegistrationRequestResultKind.ProfileIncomplete, null);
        }

        if (await db.Users.AnyAsync(user => user.NormalizedEmail == identity.NormalizedEmail, cancellationToken))
        {
            return new RegistrationRequestResult(RegistrationRequestResultKind.NotEligible, null);
        }

        var currentTime = DateTimeOffset.UtcNow;
        var now = new DateTimeOffset(currentTime.Ticks - currentTime.Ticks % 10, TimeSpan.Zero);
        var user = new User
        {
            NormalizedEmail = identity.NormalizedEmail,
            GivenName = identity.GivenName,
            FamilyName = identity.FamilyName,
            Status = "pending",
            Role = null,
            CreatedAt = now,
            UpdatedAt = now
        };
        var registrationRequest = new RegistrationRequest
        {
            User = user,
            RequestedAt = now,
            Status = "pending"
        };
        db.Users.Add(user);
        db.ExternalIdentities.Add(new ExternalIdentity
        {
            User = user,
            Issuer = identity.Issuer,
            Subject = identity.Subject,
            CreatedAt = now
        });
        db.RegistrationRequests.Add(registrationRequest);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new RegistrationRequestResult(
                RegistrationRequestResultKind.CreatedOrExisting,
                ToResponse(registrationRequest));
        }
        catch (DbUpdateException exception) when (IsUniqueConflict(exception))
        {
            return new RegistrationRequestResult(RegistrationRequestResultKind.NotEligible, null);
        }
    }

    private async Task<CurrentSessionRegistrationRequest?> GetLatestRegistrationRequestAsync(
        long userId,
        CancellationToken cancellationToken) =>
        await db.RegistrationRequests
            .AsNoTracking()
            .Where(request => request.UserId == userId)
            .OrderByDescending(request => request.RequestedAt)
            .Select(request => new CurrentSessionRegistrationRequest(
                request.Status,
                request.RequestedAt,
                request.DecidedAt))
            .FirstOrDefaultAsync(cancellationToken);

    private static RegistrationRequestResponse ToResponse(RegistrationRequest request) =>
        new(request.Status, request.RequestedAt, request.DecidedAt);

    private static bool IsUniqueConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}

public enum RegistrationRequestResultKind
{
    CreatedOrExisting,
    ProfileIncomplete,
    NotEligible
}

public sealed record RegistrationRequestResult(
    RegistrationRequestResultKind Kind,
    RegistrationRequestResponse? Request);