using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.Infrastructure.Users;

public sealed record InitialAdminProfile(
    string Email,
    string GivenName,
    string FamilyName,
    string Issuer,
    string Subject);

public sealed class InitialAdminBootstrapper
{
    private const long BootstrapLockId = 867530921;
    private readonly MediaDockDbContext _db;

    public InitialAdminBootstrapper(MediaDockDbContext db)
    {
        _db = db;
    }

    public async Task BootstrapAsync(InitialAdminProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var email = UserEmail.Normalize(profile.Email);
        var givenName = RequireName(profile.GivenName, nameof(profile.GivenName));
        var familyName = RequireName(profile.FamilyName, nameof(profile.FamilyName));
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Subject);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await _db.Database.ExecuteSqlRawAsync(
            $"SELECT pg_advisory_xact_lock({BootstrapLockId})",
            cancellationToken);

        if (await _db.Users.AnyAsync(user => user.Role == "admin", cancellationToken))
        {
            throw new InvalidOperationException("An administrator is already provisioned.");
        }

        if (await _db.Users.AnyAsync(user => user.NormalizedEmail == email, cancellationToken))
        {
            throw new InvalidOperationException("The email already belongs to a provisioned user.");
        }

        if (await _db.ExternalIdentities.AnyAsync(
                identity => identity.Issuer == profile.Issuer && identity.Subject == profile.Subject,
                cancellationToken))
        {
            throw new InvalidOperationException("The Google identity is already linked to a user.");
        }

        var now = DateTimeOffset.UtcNow;
        var user = new User
        {
            NormalizedEmail = email,
            GivenName = givenName,
            FamilyName = familyName,
            Status = "active",
            Role = "admin",
            CreatedAt = now,
            UpdatedAt = now
        };
        user.ExternalIdentities.Add(new ExternalIdentity
        {
            User = user,
            Issuer = profile.Issuer,
            Subject = profile.Subject,
            CreatedAt = now
        });
        _db.Users.Add(user);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static string RequireName(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var name = value.Trim();
        if (name.Length > 200)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Name exceeds the maximum supported length.");
        }

        return name;
    }
}