using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.IntegrationTests;

public sealed class PersistenceTests
{
    [Fact]
    [Trait("Category", "Persistence")]
    public async Task SystemSourceProfileMigrationPreservesUrlsAndMapsLegacySeriesToOngoing()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_source_profiles_migration_test").Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var db = new MediaDockDbContext(options);
        await db.Database.MigrateAsync("20261001115421_AllowBackgroundJobPayloadCleanup");

        db.Sources.AddRange(
            new Source
            {
                StableKey = "movie-old",
                Name = "My movies",
                FeedType = "movie",
                Url = "https://feed.rutracker.cc/movie.atom",
                IsEnabled = false
            },
            new Source
            {
                StableKey = "legacy-series",
                Name = "Legacy series",
                FeedType = "series",
                Url = "https://feed.rutracker.cc/legacy.atom",
                IsEnabled = false
            });
        await db.SaveChangesAsync();
        await db.Database.MigrateAsync("20261001134646_AddRssItemProcessingStates");
        db.ChangeTracker.Clear();

        db.Sources.AddRange(
            new Source
            {
                StableKey = "complete-old",
                Name = "My season packs",
                FeedType = "series_complete",
                Url = "https://feed.rutracker.cc/complete.atom",
                IsEnabled = false
            },
            new Source
            {
                StableKey = "ongoing-old",
                Name = "My episodes",
                FeedType = "series_ongoing",
                Url = "https://feed.rutracker.cc/ongoing.atom",
                IsEnabled = false
            });
        await db.SaveChangesAsync();

        var expectedUrls = await db.Sources.AsNoTracking()
            .ToDictionaryAsync(source => source.StableKey, source => source.Url);
        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();

        var migratedSources = await db.Sources.AsNoTracking().ToListAsync();
        Assert.Equal(expectedUrls, migratedSources.ToDictionary(source => source.StableKey, source => source.Url));
        Assert.Equal("series_ongoing", migratedSources.Single(source => source.StableKey == "legacy-series").FeedType);
        Assert.All(migratedSources, source => Assert.True(source.IsEnabled));
        Assert.Equal(
            new[] { "Complete seasons", "Movies", "Ongoing episodes", "Ongoing episodes" },
            migratedSources.Select(source => source.Name).OrderBy(name => name));
        await AssertRejectedAsync(db, new Source
        {
            StableKey = "invalid-profile",
            Name = "Invalid",
            FeedType = "series",
            Url = "https://feed.rutracker.cc/invalid.atom"
        });
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public async Task MigrationCreatesSchemaAndOccurrenceIdentityIsUnique()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_test").Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;

        await using var db = new MediaDockDbContext(options);
        await db.Database.MigrateAsync();

        var backgroundJobIndexes = db.Model.FindEntityType(typeof(BackgroundJob))!
            .GetIndexes()
            .Select(index => index.GetDatabaseName())
            .ToArray();
        Assert.Contains("ux_background_jobs_active_rss_scan", backgroundJobIndexes);
        Assert.Contains("ux_background_jobs_active_oscar_enrichment", backgroundJobIndexes);
        Assert.Contains("ux_background_jobs_active_golden_globe_enrichment", backgroundJobIndexes);

        var appliedMigrations = await db.Database.GetAppliedMigrationsAsync();
        Assert.Contains("20260930122500_InitialRelationalSchema", appliedMigrations);
        Assert.Contains("20261004200000_AddGoldenGlobeEnrichment", appliedMigrations);
        Assert.Contains("20261016071457_AddUserOwnershipToFavoritesAndRatings", appliedMigrations);
        Assert.Contains("20261016160138_AddSharedImdbTitles", appliedMigrations);
        Assert.Contains("20261020122556_EnforceTitleMetadataOwnership", appliedMigrations);

        await db.Database.OpenConnectionAsync();
        await using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'";
            await using var reader = await command.ExecuteReaderAsync();
            var tables = new HashSet<string>(StringComparer.Ordinal);
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }

            Assert.All(
                new[]
                {
                    "titles", "sources", "occurrences", "scan_runs", "parse_logs", "settings", "metadata_cache",
                    "oscar_films", "oscar_nominations", "oscar_enrichment_runs", "omdb_daily_usage",
                    "golden_globe_awards", "golden_globe_nominations", "favorite_movies", "personal_ratings", "users",
                    "imdb_titles"
                },
                tableName => Assert.Contains(tableName, tables));
        }

        var observedAt = new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);
        var source = new Source
        {
            StableKey = "movies-main",
            Name = "Movies",
            FeedType = "movie",
            Url = "https://feed.example/movies"
        };
        var title = new Title
        {
            TitleText = "Example Film",
            NormalizedTitle = "example film",
            MediaType = "movie",
            SourceType = "movie",
            ContentKind = "standard",
            FirstSeenAt = observedAt,
            LastSeenAt = observedAt,
            UpdatedAt = observedAt
        };
        db.AddRange(source, title);
        await db.SaveChangesAsync();

        var itemKey = SourceItemKey.From(" guid-123 ", "https://feed.example/topic/1");
        db.Occurrences.Add(new Occurrence
        {
            TitleId = title.Id,
            SourceId = source.Id,
            SourceItemKey = itemKey,
            FeedEntryId = "guid-123",
            TorrentUrl = "https://feed.example/topic/1",
            RawTitle = "Example.Film.2026",
            SourceFeedName = source.Name,
            FeedType = source.FeedType,
            ObservedAt = observedAt,
            FirstSeenAt = observedAt,
            LastSeenAt = observedAt
        });
        await db.SaveChangesAsync();

        Assert.Equal(itemKey, SourceItemKey.From("guid-123", "https://feed.example/topic/changed"));
    db.ChangeTracker.Clear();
        db.Occurrences.Add(new Occurrence
        {
            TitleId = title.Id,
            SourceId = source.Id,
            SourceItemKey = SourceItemKey.From("guid-123", "https://feed.example/topic/changed"),
            FeedEntryId = "guid-123",
            TorrentUrl = "https://feed.example/topic/changed",
            RawTitle = "Example.Film.2026",
            SourceFeedName = source.Name,
            FeedType = source.FeedType,
            ObservedAt = observedAt.AddHours(1),
            FirstSeenAt = observedAt,
            LastSeenAt = observedAt.AddHours(1)
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.Occurrences.CountAsync());
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public async Task DatabaseEnforcesIdentityTimestampSingletonAndStatusConstraints()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_model_constraints_test").Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var db = new MediaDockDbContext(options);
        await db.Database.MigrateAsync();

        var now = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);
        var imdbTitle = new ImdbTitle { ImdbId = "tt12345678", UpdatedAt = now };
        var title = CreateTitle("Canonical", "canonical", "tt12345678", now);
        title.ImdbTitle = imdbTitle;
        db.ImdbTitles.Add(imdbTitle);
        db.Titles.Add(title);
        await db.SaveChangesAsync();

        await AssertRejectedAsync(db, CreateTitle("Duplicate ID", "duplicate id", "tt12345678", now));

        var firstSameFilmImdbTitle = new ImdbTitle { ImdbId = "tt20000001", UpdatedAt = now };
        var secondSameFilmImdbTitle = new ImdbTitle { ImdbId = "tt20000002", UpdatedAt = now };
        var firstSameFilmTitle = CreateTitle("Same Film", "same film", "tt20000001", now);
        firstSameFilmTitle.ImdbTitle = firstSameFilmImdbTitle;
        var secondSameFilmTitle = CreateTitle("Same Film", "same film", "tt20000002", now);
        secondSameFilmTitle.ImdbTitle = secondSameFilmImdbTitle;
        db.ImdbTitles.AddRange(firstSameFilmImdbTitle, secondSameFilmImdbTitle);
        db.Titles.AddRange(firstSameFilmTitle, secondSameFilmTitle);
        await db.SaveChangesAsync();
        Assert.Equal(3, await db.Titles.CountAsync());

        await AssertRejectedAsync(db, CreateTitle("Partial Range", "partial range", null, now, now, null));
        await AssertRejectedAsync(
            db,
            CreateTitle("Reversed Range", "reversed range", null, now, now.AddHours(1), now));

        var source = new Source
        {
            StableKey = "constraint-test",
            Name = "Constraint Test",
            FeedType = "movie",
            Url = "https://feed.example/movies"
        };
        db.Sources.Add(source);
        await db.SaveChangesAsync();
        await AssertRejectedAsync(db, new Occurrence
        {
            TitleId = title.Id,
            SourceId = source.Id,
            SourceItemKey = "reversed-occurrence",
            TorrentUrl = "https://feed.example/topic/1",
            RawTitle = "Canonical (2026)",
            SourceFeedName = source.Name,
            FirstSeenAt = now.AddHours(1),
            LastSeenAt = now
        });

        db.Settings.Add(new AppSetting { Id = 1, UpdatedAt = now });
        await db.SaveChangesAsync();
        await AssertRejectedAsync(db, new AppSetting { Id = 2, UpdatedAt = now });

        db.OscarFilms.Add(new OscarFilm
        {
            TitleId = title.Id,
            StableKey = "invalid-status",
            FilmTitle = title.TitleText,
            NormalizedTitle = title.NormalizedTitle,
            FilmYear = 2026,
            EnrichmentStatus = "unknown",
            ImportedAt = now,
            UpdatedAt = now
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        db.OscarFilms.Add(new OscarFilm
        {
            TitleId = title.Id,
            StableKey = "negative-attempts",
            FilmTitle = title.TitleText,
            NormalizedTitle = title.NormalizedTitle,
            FilmYear = 2026,
            EnrichmentStatus = "pending",
            EnrichmentAttemptCount = -1,
            ImportedAt = now,
            UpdatedAt = now
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        await AssertRejectedAsync(db, new ScanRun
        {
            StartedAt = now,
            FinishedAt = now,
            Status = "running",
            Trigger = "manual"
        });
        await AssertRejectedAsync(db, new ScanRun
        {
            StartedAt = now,
            Status = "succeeded",
            Trigger = "manual"
        });
        await AssertRejectedAsync(db, new OscarEnrichmentRun
        {
            StartedAt = now,
            FinishedAt = now,
            Status = "running",
            Trigger = "manual"
        });
        await AssertRejectedAsync(db, new OscarEnrichmentRun
        {
            StartedAt = now,
            Status = "succeeded",
            Trigger = "manual"
        });
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public async Task SharedImdbMigrationBackfillsNormalizedIdsAndPreservesLocalMetadata()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_shared_imdb_migration_test").Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var db = new MediaDockDbContext(options);
        await db.Database.MigrateAsync("20261016071457_AddUserOwnershipToFavoritesAndRatings");

        var now = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
        var firstUser = CreateUser("first@example.com", now);
        var secondUser = CreateUser("second@example.com", now);
        db.Users.AddRange(firstUser, secondUser);

        var firstTitle = CreateTitle("Earlier title", "earlier title", " TT12345678 ", now.AddDays(-1));
        firstTitle.ImdbRating = 7.1m;
        var duplicateTitle = CreateTitle("Latest metadata", "latest metadata", "tt12345678", now);
        duplicateTitle.ImdbRating = 8.4m;
        duplicateTitle.ImdbVotes = 1200;
        duplicateTitle.Genres = ["Drama"];
        duplicateTitle.Plot = "Backfilled plot";
        var localTitle = CreateTitle("Local-only title", "local-only title", null, now);
        localTitle.ImdbRating = 6.1m;
        localTitle.Genres = ["Local genre"];
        localTitle.Plot = "Keep this title-local metadata";
        db.Titles.AddRange(firstTitle, duplicateTitle, localTitle);
        await db.SaveChangesAsync();

        db.PersonalRatings.AddRange(
            new PersonalRating
            {
                UserId = firstUser.Id,
                ImdbId = " TT87654321 ",
                Rating = 5,
                UpdatedAt = now.AddDays(-1)
            },
            new PersonalRating
            {
                UserId = firstUser.Id,
                ImdbId = "tt87654321",
                Rating = 8,
                UpdatedAt = now
            },
            new PersonalRating
            {
                UserId = secondUser.Id,
                ImdbId = "TT87654321",
                Rating = 6,
                UpdatedAt = now
            });
        db.OscarFilms.Add(new OscarFilm
        {
            TitleId = firstTitle.Id,
            StableKey = "legacy-imdb-film",
            FilmTitle = "Legacy film",
            NormalizedTitle = "legacy film",
            FilmYear = 2025,
            ImdbId = "TT12345678",
            ImportedAt = now,
            UpdatedAt = now
        });
        var award = new GoldenGlobeAward { Name = "Legacy award" };
        db.GoldenGlobeAwards.Add(award);
        db.GoldenGlobeNominations.Add(new GoldenGlobeNomination
        {
            ImportKey = "legacy-imdb-nomination",
            Year = 2025,
            Award = award,
            Title = "Legacy nominee",
            NomineeType = "movie",
            ImdbId = " TT87654321 ",
            EnrichmentStatus = "pending"
        });
        await db.SaveChangesAsync();

        await db.Database.MigrateAsync("20261016160138_AddSharedImdbTitles");
        var lateImdbTitle = new ImdbTitle
        {
            ImdbId = "tt88888888",
            MetadataStatus = "pending",
            UpdatedAt = now
        };
        var lateLinkedTitle = CreateTitle("Late linked title", "late linked title", "tt88888888", now);
        lateLinkedTitle.ImdbRating = 6.7m;
        lateLinkedTitle.Genres = ["Documentary"];
        lateLinkedTitle.Plot = "Backfilled after the shared-IMDb migration.";
        db.ImdbTitles.Add(lateImdbTitle);
        db.Titles.Add(lateLinkedTitle);
        await db.SaveChangesAsync();

        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();

        var imdbTitles = await db.ImdbTitles.AsNoTracking().OrderBy(title => title.ImdbId).ToListAsync();
        Assert.Equal(new[] { "tt12345678", "tt87654321", "tt88888888" }, imdbTitles.Select(title => title.ImdbId));
        var backfilled = imdbTitles.Single(title => title.ImdbId == "tt12345678");
        Assert.Equal("Latest metadata", backfilled.TitleText);
        Assert.Equal(8.4m, backfilled.ImdbRating);
        Assert.Equal(1200, backfilled.ImdbVotes);
        Assert.Equal(new[] { "Drama" }, backfilled.Genres);
        Assert.Equal("Backfilled plot", backfilled.Plot);
        Assert.Equal("enriched", backfilled.MetadataStatus);
        Assert.Equal("pending", imdbTitles.Single(title => title.ImdbId == "tt87654321").MetadataStatus);
        var lateBackfilled = imdbTitles.Single(title => title.ImdbId == "tt88888888");
        Assert.Equal(6.7m, lateBackfilled.ImdbRating);
        Assert.Equal(new[] { "Documentary" }, lateBackfilled.Genres);
        Assert.Equal("Backfilled after the shared-IMDb migration.", lateBackfilled.Plot);
        Assert.Equal("enriched", lateBackfilled.MetadataStatus);

        var migratedTitles = await db.Titles.AsNoTracking().OrderBy(title => title.Id).ToListAsync();
        Assert.Equal("tt12345678", migratedTitles[0].ImdbId);
        Assert.Null(migratedTitles[0].ImdbRating);
        Assert.Null(migratedTitles[0].ImdbVotes);
        Assert.Empty(migratedTitles[0].Genres);
        Assert.Null(migratedTitles[0].Plot);
        Assert.Null(migratedTitles[1].ImdbId);
        Assert.Null(migratedTitles[2].ImdbId);
        Assert.Equal(6.1m, migratedTitles[2].ImdbRating);
        Assert.Equal(new[] { "Local genre" }, migratedTitles[2].Genres);
        Assert.Equal("Keep this title-local metadata", migratedTitles[2].Plot);
        Assert.Equal("tt88888888", migratedTitles[3].ImdbId);
        Assert.Null(migratedTitles[3].ImdbRating);
        Assert.Empty(migratedTitles[3].Genres);
        Assert.Null(migratedTitles[3].Plot);

        var duplicateMetadataTitle = CreateTitle("Duplicate metadata", "duplicate metadata", "tt87654321", now);
        duplicateMetadataTitle.ImdbRating = 5.0m;
        db.Titles.Add(duplicateMetadataTitle);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var ratings = await db.PersonalRatings.AsNoTracking().OrderBy(rating => rating.UserId).ToListAsync();
        Assert.Equal(2, ratings.Count);
        Assert.All(ratings, rating => Assert.Equal("tt87654321", rating.ImdbId));
        Assert.Equal(8, ratings.Single(rating => rating.UserId == firstUser.Id).Rating);
        Assert.Equal(6, ratings.Single(rating => rating.UserId == secondUser.Id).Rating);
        Assert.Equal("tt12345678", (await db.OscarFilms.AsNoTracking().SingleAsync()).ImdbId);
        Assert.Equal("tt87654321", (await db.GoldenGlobeNominations.AsNoTracking().SingleAsync()).ImdbId);

        var noIdTitle = CreateTitle("Nullable IMDb", "nullable imdb", null, now);
        db.Titles.Add(noIdTitle);
        var noIdOscarFilm = new OscarFilm
        {
            Title = noIdTitle,
            StableKey = "nullable-imdb-film",
            FilmTitle = noIdTitle.TitleText,
            NormalizedTitle = noIdTitle.NormalizedTitle,
            FilmYear = 2025,
            ImportedAt = now,
            UpdatedAt = now
        };
        db.OscarFilms.Add(noIdOscarFilm);
        db.GoldenGlobeNominations.Add(new GoldenGlobeNomination
        {
            ImportKey = "nullable-imdb-nomination",
            Year = 2025,
            AwardId = award.Id,
            Title = "Nullable nominee",
            NomineeType = "movie",
            EnrichmentStatus = "pending"
        });
        await db.SaveChangesAsync();

        db.PersonalRatings.Add(new PersonalRating
        {
            UserId = firstUser.Id,
            ImdbId = "tt99999999",
            Rating = 7,
            UpdatedAt = now
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var referencedImdbTitle = await db.ImdbTitles.SingleAsync(title => title.ImdbId == "tt12345678");
        db.ImdbTitles.Remove(referencedImdbTitle);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static Title CreateTitle(
        string title,
        string normalizedTitle,
        string? imdbId,
        DateTimeOffset updatedAt,
        DateTimeOffset? firstSeenAt = null,
        DateTimeOffset? lastSeenAt = null) => new()
    {
        TitleText = title,
        NormalizedTitle = normalizedTitle,
        Year = 2026,
        MediaType = "movie",
        ImdbId = imdbId,
        FirstSeenAt = firstSeenAt,
        LastSeenAt = lastSeenAt,
        UpdatedAt = updatedAt
    };

    private static User CreateUser(string email, DateTimeOffset now) => new()
    {
        NormalizedEmail = email,
        GivenName = "Given",
        FamilyName = "Family",
        Status = "pending",
        CreatedAt = now,
        UpdatedAt = now
    };

    private static async Task AssertRejectedAsync(MediaDockDbContext db, object entity)
    {
        db.Add(entity);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
    }
}