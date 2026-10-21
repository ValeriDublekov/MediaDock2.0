using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using MediaDock.Infrastructure.Users;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.IntegrationTests;

public sealed class IdentityPersistenceTests
{
    [Fact]
    [Trait("Category", "Persistence")]
    public async Task UserOwnershipMigrationDeletesLegacyRowsWhenNoUsersExist()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_identity_upgrade_test").Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var db = new MediaDockDbContext(options);
        var legacyOptions = new DbContextOptionsBuilder<LegacyPersistenceTestContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var legacyDb = new LegacyPersistenceTestContext(legacyOptions);
        await db.Database.MigrateAsync("20261005120000_AddGoldenGlobeNomineeType");

        var now = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        await SeedLegacyFavoriteAndRatingAsync(db, legacyDb, now.AddDays(-1), now);

        await db.Database.MigrateAsync("20261016071457_AddUserOwnershipToFavoritesAndRatings");
        db.ChangeTracker.Clear();

        Assert.Empty(await legacyDb.FavoriteMovies.AsNoTracking().ToListAsync());
        Assert.Empty(await db.PersonalRatings.AsNoTracking().ToListAsync());
        Assert.Empty(await db.Users.ToListAsync());
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public async Task UserOwnershipMigrationBackfillsLegacyRowsToTheOnlyUser()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_single_user_ownership_test").Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var db = new MediaDockDbContext(options);
        var legacyOptions = new DbContextOptionsBuilder<LegacyPersistenceTestContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var legacyDb = new LegacyPersistenceTestContext(legacyOptions);
        await db.Database.MigrateAsync("20261007170000_AddParseLogFeedPayload");

        var now = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
        var user = CreateUser("deactivated@example.com", now);
        user.Status = "deactivated";
        user.Role = "user";
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var title = await SeedLegacyFavoriteAndRatingAsync(db, legacyDb, now.AddDays(-1), now);

        await db.Database.MigrateAsync("20261016071457_AddUserOwnershipToFavoritesAndRatings");
        db.ChangeTracker.Clear();

        var favorite = await legacyDb.FavoriteMovies.AsNoTracking().SingleAsync();
        Assert.Equal(user.Id, favorite.UserId);
        Assert.Equal(title.Id, favorite.TitleId);
        Assert.False(favorite.ToWatch);
        Assert.True(favorite.ToDownload);
        Assert.True(favorite.AddedFromOscar);
        Assert.False(favorite.AddedFromCatalog);
        Assert.Equal(now.AddDays(-1), favorite.CreatedAt);
        Assert.Equal(now, favorite.UpdatedAt);

        var rating = await db.PersonalRatings.AsNoTracking().SingleAsync();
        Assert.Equal(user.Id, rating.UserId);
        Assert.Equal("tt12345678", rating.ImdbId);
        Assert.Equal(8, rating.Rating);
        Assert.Equal(now, rating.UpdatedAt);

        db.Users.Remove(await db.Users.SingleAsync(existingUser => existingUser.Id == user.Id));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public async Task UserOwnershipMigrationCopiesLegacyRowsToEveryUserAndEnforcesCompositeKeys()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_multi_user_ownership_test").Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var db = new MediaDockDbContext(options);
        var legacyOptions = new DbContextOptionsBuilder<LegacyPersistenceTestContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var legacyDb = new LegacyPersistenceTestContext(legacyOptions);
        await db.Database.MigrateAsync("20261007170000_AddParseLogFeedPayload");

        var now = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
        var firstUser = CreateUser("first@example.com", now);
        var secondUser = CreateUser("deactivated@example.com", now);
        secondUser.Status = "deactivated";
        secondUser.Role = "user";
        db.Users.AddRange(firstUser, secondUser);
        await db.SaveChangesAsync();
        var title = await SeedLegacyFavoriteAndRatingAsync(db, legacyDb, now.AddDays(-1), now);

        await db.Database.MigrateAsync("20261016071457_AddUserOwnershipToFavoritesAndRatings");
        db.ChangeTracker.Clear();

        var favorites = await legacyDb.FavoriteMovies.AsNoTracking().OrderBy(favorite => favorite.UserId).ToListAsync();
        Assert.Equal(new[] { firstUser.Id, secondUser.Id }.OrderBy(id => id), favorites.Select(favorite => favorite.UserId));
        Assert.All(favorites, favorite =>
        {
            Assert.Equal(title.Id, favorite.TitleId);
            Assert.False(favorite.ToWatch);
            Assert.True(favorite.ToDownload);
            Assert.True(favorite.AddedFromOscar);
            Assert.False(favorite.AddedFromCatalog);
            Assert.Equal(now.AddDays(-1), favorite.CreatedAt);
            Assert.Equal(now, favorite.UpdatedAt);
        });

        var ratings = await db.PersonalRatings.AsNoTracking().OrderBy(rating => rating.UserId).ToListAsync();
        Assert.Equal(new[] { firstUser.Id, secondUser.Id }.OrderBy(id => id), ratings.Select(rating => rating.UserId));
        Assert.All(ratings, rating =>
        {
            Assert.Equal("tt12345678", rating.ImdbId);
            Assert.Equal(8, rating.Rating);
            Assert.Equal(now, rating.UpdatedAt);
        });

        legacyDb.FavoriteMovies.Add(new LegacyFavoriteMovieRow
        {
            UserId = firstUser.Id,
            TitleId = title.Id,
            ToWatch = true,
            AddedFromOscar = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => legacyDb.SaveChangesAsync());
        legacyDb.ChangeTracker.Clear();

        db.PersonalRatings.Add(new PersonalRating
        {
            UserId = firstUser.Id,
            ImdbId = "tt12345678",
            Rating = 9,
            UpdatedAt = now
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
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

    private static async Task<LegacyTitleRow> SeedLegacyFavoriteAndRatingAsync(
        MediaDockDbContext db,
        LegacyPersistenceTestContext legacyDb,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        var title = new LegacyTitleRow
        {
            TitleText = "Shared title",
            NormalizedTitle = "shared title",
            MediaType = "movie",
            ImdbId = "tt12345678",
            UpdatedAt = updatedAt
        };
        legacyDb.Titles.Add(title);
        await legacyDb.SaveChangesAsync();

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO favorite_movies (title_id, to_watch, to_download, added_from_oscar, added_from_catalog, created_at, updated_at) VALUES ({title.Id}, {false}, {true}, {true}, {false}, {createdAt}, {updatedAt})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO personal_ratings (imdb_id, rating, updated_at) VALUES ({"tt12345678"}, {8}, {updatedAt})");

        return title;
    }

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