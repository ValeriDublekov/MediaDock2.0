using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using MediaDock.Infrastructure.Users;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.IntegrationTests;

public sealed class IdentityPersistenceTests
{
    [Fact]
    [Trait("Category", "Persistence")]
    public async Task IdentityMigrationPreservesExistingSharedFavoritesAndRatings()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_identity_upgrade_test").Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var db = new MediaDockDbContext(options);
        await db.Database.MigrateAsync("20261005120000_AddGoldenGlobeNomineeType");

        var now = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        var title = new Title
        {
            TitleText = "Shared title",
            NormalizedTitle = "shared title",
            MediaType = "movie",
            ImdbId = "tt12345678",
            UpdatedAt = now
        };
        db.Titles.Add(title);
        await db.SaveChangesAsync();
        db.FavoriteMovies.Add(new FavoriteMovie
        {
            TitleId = title.Id,
            ToWatch = false,
            ToDownload = true,
            AddedFromOscar = true,
            AddedFromCatalog = false,
            CreatedAt = now.AddDays(-1),
            UpdatedAt = now
        });
        db.PersonalRatings.Add(new PersonalRating
        {
            ImdbId = "tt12345678",
            Rating = 8,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();

        var favorite = await db.FavoriteMovies.AsNoTracking().SingleAsync();
        Assert.Equal(title.Id, favorite.TitleId);
        Assert.False(favorite.ToWatch);
        Assert.True(favorite.ToDownload);
        Assert.True(favorite.AddedFromOscar);
        Assert.False(favorite.AddedFromCatalog);
        Assert.Equal(now.AddDays(-1), favorite.CreatedAt);
        Assert.Equal(now, favorite.UpdatedAt);

        var rating = await db.PersonalRatings.AsNoTracking().SingleAsync();
        Assert.Equal("tt12345678", rating.ImdbId);
        Assert.Equal(8, rating.Rating);
        Assert.Equal(now, rating.UpdatedAt);
        Assert.Empty(await db.Users.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public async Task IdentityAndRequestConstraintsEnforceNormalizedUniqueAccounts()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_identity_constraints_test").Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var db = new MediaDockDbContext(options);
        await db.Database.MigrateAsync();

        var now = DateTimeOffset.UtcNow;
        var firstUser = CreateUser("first@example.com", now);
        var secondUser = CreateUser("second@example.com", now);
        db.Users.AddRange(firstUser, secondUser);
        await db.SaveChangesAsync();

        await AssertRejectedAsync(db, CreateUser("first@example.com", now));
        await AssertRejectedAsync(db, CreateUser("First@Example.com ", now));
        await AssertRejectedAsync(db, CreateUser("blank-name@example.com", now, givenName: " "));
        await AssertRejectedAsync(db, CreateUser("blank-family-name@example.com", now, familyName: " "));

        db.ExternalIdentities.Add(new ExternalIdentity
        {
            UserId = firstUser.Id,
            Issuer = "https://accounts.google.com",
            Subject = "google-subject-1",
            CreatedAt = now
        });
        await db.SaveChangesAsync();
        await AssertRejectedAsync(db, new ExternalIdentity
        {
            UserId = secondUser.Id,
            Issuer = "https://accounts.google.com",
            Subject = "google-subject-1",
            CreatedAt = now
        });

        db.RegistrationRequests.Add(new RegistrationRequest
        {
            UserId = firstUser.Id,
            RequestedAt = now,
            Status = "pending"
        });
        await db.SaveChangesAsync();
        await AssertRejectedAsync(db, new RegistrationRequest
        {
            UserId = firstUser.Id,
            RequestedAt = now.AddMinutes(1),
            Status = "pending"
        });

        db.RegistrationRequests.Add(new RegistrationRequest
        {
            UserId = firstUser.Id,
            RequestedAt = now.AddMinutes(2),
            Status = "approved",
            DecidedAt = now.AddMinutes(3),
            DecidedBy = "host-operator"
        });
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.RegistrationRequests.CountAsync(request => request.UserId == firstUser.Id));
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public async Task InitialAdminBootstrapIsAtomicAndOneTime()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_admin_bootstrap_test").Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using (var migrationDb = new MediaDockDbContext(options))
        {
            await migrationDb.Database.MigrateAsync();
        }

        var profile = new InitialAdminProfile(
            " Admin@Example.com ",
            " Given ",
            " Family ",
            "https://accounts.google.com",
            "verified-subject");

        var results = await Task.WhenAll(
            TryBootstrapAsync(options, profile),
            TryBootstrapAsync(options, profile));
        Assert.Single(results, succeeded => succeeded);
        Assert.Single(results, succeeded => !succeeded);

        await using var db = new MediaDockDbContext(options);
        var user = await db.Users.AsNoTracking().SingleAsync();
        Assert.Equal("admin@example.com", user.NormalizedEmail);
        Assert.Equal("Given", user.GivenName);
        Assert.Equal("Family", user.FamilyName);
        Assert.Equal("active", user.Status);
        Assert.Equal("admin", user.Role);
        var identity = await db.ExternalIdentities.AsNoTracking().SingleAsync();
        Assert.Equal(user.Id, identity.UserId);
        Assert.Equal("https://accounts.google.com", identity.Issuer);
        Assert.Equal("verified-subject", identity.Subject);

        await using var repeatDb = new MediaDockDbContext(options);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new InitialAdminBootstrapper(repeatDb).BootstrapAsync(profile));
        Assert.Single(await repeatDb.Users.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public async Task InitialAdminBootstrapRejectsEmailAndIdentityConflicts()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_admin_conflicts_test").Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var db = new MediaDockDbContext(options);
        await db.Database.MigrateAsync();

        var now = DateTimeOffset.UtcNow;
        var profile = new InitialAdminProfile(
            "person@example.com",
            "Person",
            "Example",
            "https://accounts.google.com",
            "existing-subject");
        var existingEmailUser = CreateUser("person@example.com", now);
        db.Users.Add(existingEmailUser);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new InitialAdminBootstrapper(db).BootstrapAsync(profile));

        var linkedUser = CreateUser("linked@example.com", now);
        db.Users.Add(linkedUser);
        await db.SaveChangesAsync();
        db.ExternalIdentities.Add(new ExternalIdentity
        {
            UserId = linkedUser.Id,
            Issuer = profile.Issuer,
            Subject = profile.Subject,
            CreatedAt = now
        });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new InitialAdminBootstrapper(db).BootstrapAsync(profile));
        Assert.Empty(await db.Users.Where(user => user.Role == "admin").ToListAsync());
    }

    private static User CreateUser(
        string normalizedEmail,
        DateTimeOffset now,
        string givenName = "Given",
        string familyName = "Family") => new()
    {
        NormalizedEmail = normalizedEmail,
        GivenName = givenName,
        FamilyName = familyName,
        Status = "pending",
        Role = null,
        CreatedAt = now,
        UpdatedAt = now
    };

    private static async Task<bool> TryBootstrapAsync(
        DbContextOptions<MediaDockDbContext> options,
        InitialAdminProfile profile)
    {
        await using var db = new MediaDockDbContext(options);
        try
        {
            await new InitialAdminBootstrapper(db).BootstrapAsync(profile);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static async Task AssertRejectedAsync(MediaDockDbContext db, object entity)
    {
        db.Add(entity);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
    }
}