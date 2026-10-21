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
    public async Task OscarFilmSourceImdbIdMustMatchLinkedTitleWhenPresent()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_oscar_imdb_identity_constraint_test").Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var db = new MediaDockDbContext(options);
        await db.Database.MigrateAsync();

        var now = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);
        db.ImdbTitles.AddRange(
            new ImdbTitle { ImdbId = "tt12345678", UpdatedAt = now },
            new ImdbTitle { ImdbId = "tt87654321", UpdatedAt = now });
        var title = CreateTitle("Linked Film", "linked film", "tt12345678", now);
        db.Titles.Add(title);
        await db.SaveChangesAsync();

        var mismatchedFilm = new OscarFilm
        {
            TitleId = title.Id,
            StableKey = "mismatched-imdb-film",
            FilmTitle = "Linked Film",
            NormalizedTitle = "linked film",
            FilmYear = 2025,
            ImdbId = "tt87654321",
            ImportedAt = now,
            UpdatedAt = now
        };
        db.OscarFilms.Add(mismatchedFilm);
        var mismatch = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(
            "fk_oscar_films_titles_title_id_imdb_id",
            (mismatch.InnerException as Npgsql.PostgresException)?.ConstraintName);
        db.ChangeTracker.Clear();

        db.OscarFilms.Add(new OscarFilm
        {
            TitleId = title.Id,
            StableKey = "missing-source-imdb-film",
            FilmTitle = "Linked Film",
            NormalizedTitle = "linked film",
            FilmYear = 2025,
            ImportedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        await AssertRejectedAsync(db, new OscarFilm
        {
            TitleId = long.MaxValue,
            StableKey = "missing-title-film",
            FilmTitle = "Missing Title Film",
            NormalizedTitle = "missing title film",
            FilmYear = 2025,
            ImportedAt = now,
            UpdatedAt = now
        });
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public async Task OscarIdentityMigrationRejectsPreexistingMismatchesWithoutChangingData()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_oscar_imdb_migration_preflight_test").Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var db = new MediaDockDbContext(options);
        await db.Database.MigrateAsync("20261020122556_EnforceTitleMetadataOwnership");

        var now = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);
        db.ImdbTitles.AddRange(
            new ImdbTitle { ImdbId = "tt12345678", UpdatedAt = now },
            new ImdbTitle { ImdbId = "tt87654321", UpdatedAt = now });
        var title = CreateTitle("Linked Film", "linked film", "tt12345678", now);
        db.Titles.Add(title);
        await db.SaveChangesAsync();

        db.OscarFilms.Add(new OscarFilm
        {
            TitleId = title.Id,
            StableKey = "legacy-mismatched-film",
            FilmTitle = "Linked Film",
            NormalizedTitle = "linked film",
            FilmYear = 2025,
            ImdbId = "tt87654321",
            ImportedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var migrationFailure = await Assert.ThrowsAsync<Npgsql.PostgresException>(
            () => db.Database.MigrateAsync());
        Assert.Contains("Cannot enforce Oscar film IMDb identity", migrationFailure.Message);
        Assert.DoesNotContain(
            "20261020125713_EnforceOscarFilmTitleIdentity",
            await db.Database.GetAppliedMigrationsAsync());

        var unchangedFilm = await db.OscarFilms
            .AsNoTracking()
            .Include(film => film.Title)
            .SingleAsync();
        Assert.Equal("tt87654321", unchangedFilm.ImdbId);
        Assert.Equal("tt12345678", unchangedFilm.Title.ImdbId);
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public async Task GoldenGlobeGroupMigrationRejectsConflictingLinksAndStatesUntilReconciled()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_golden_globe_group_migration_preflight_test").Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var db = new MediaDockDbContext(options);
        await db.Database.MigrateAsync("20261020140000_EnforceScanRunNonnegativeCounters");

        db.ImdbTitles.AddRange(
            new ImdbTitle { ImdbId = "tt12345678", UpdatedAt = DateTimeOffset.UtcNow },
            new ImdbTitle { ImdbId = "tt87654321", UpdatedAt = DateTimeOffset.UtcNow });
        var award = new GoldenGlobeAward { Name = "Migration Preflight Award" };
        db.GoldenGlobeAwards.Add(award);
        await db.SaveChangesAsync();

        async Task InsertLegacyNominationAsync(string importKey, string title, string? imdbId, string status)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO golden_globe_nominations (
                    import_key, year, winner, award_id, title, nominee_type, imdb_id,
                    is_imdb_id_manual, imdb_id_version, enrichment_status, enrichment_attempt_count)
                VALUES ({importKey}, 2025, false, {award.Id}, {title}, 'movie', {imdbId}, false, 0, {status}, 1)
                """);
        }

        await InsertLegacyNominationAsync("link-conflict-a", "Linked Legacy Film", "tt12345678", "enriched");
        await InsertLegacyNominationAsync("link-conflict-b", "Linked Legacy Film", "tt87654321", "enriched");
        await InsertLegacyNominationAsync("state-conflict-a", "State Legacy Film", null, "pending");
        await InsertLegacyNominationAsync("state-conflict-b", "State Legacy Film", null, "not_found");

        var linkConflict = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.MigrateAsync());
        Assert.Contains("Cannot normalize Golden Globes groups", linkConflict.Message);
        Assert.DoesNotContain("20261020150000_NormalizeGoldenGlobeGroups", await db.Database.GetAppliedMigrationsAsync());

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE golden_globe_nominations SET imdb_id = {"tt12345678"} WHERE import_key = {"link-conflict-b"}");
        var stateConflict = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.MigrateAsync());
        Assert.Contains("conflicting IMDb links or enrichment states", stateConflict.Message);
        Assert.DoesNotContain("20261020150000_NormalizeGoldenGlobeGroups", await db.Database.GetAppliedMigrationsAsync());

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE golden_globe_nominations SET enrichment_status = {"pending"} WHERE import_key = {"state-conflict-b"}");
        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();

        var groups = await db.GoldenGlobeGroups.AsNoTracking()
            .Include(group => group.Nominations)
            .OrderBy(group => group.Title)
            .ToListAsync();
        Assert.Equal(2, groups.Count);
        var linkedGroup = groups.Single(group => group.Title == "Linked Legacy Film");
        Assert.Equal("tt12345678", linkedGroup.ImdbId);
        Assert.Equal("enriched", linkedGroup.EnrichmentStatus);
        Assert.Equal(2, linkedGroup.Nominations.Count);
        var stateGroup = groups.Single(group => group.Title == "State Legacy Film");
        Assert.Null(stateGroup.ImdbId);
        Assert.Equal("pending", stateGroup.EnrichmentStatus);
        Assert.Equal(2, stateGroup.Nominations.Count);
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public async Task SourceOwnedMovieMigrationBackfillsDetailsAndTransfersOnlyEnrichedFavorites()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_source_owned_movie_migration_test").Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var db = new MediaDockDbContext(options);
        await db.Database.MigrateAsync("20261020150000_NormalizeGoldenGlobeGroups");

        var now = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        var user = CreateUser("source-owned@example.com", now);
        db.Users.Add(user);
        db.ImdbTitles.AddRange(
            new ImdbTitle
            {
                ImdbId = "tt12345678",
                TitleText = "Enriched Film",
                ImdbRating = 8.2m,
                MetadataStatus = "enriched",
                UpdatedAt = now
            },
            new ImdbTitle { ImdbId = "tt87654321", MetadataStatus = "pending", UpdatedAt = now });
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlRawAsync("DROP INDEX ux_titles_imdb_id");
        var firstFavoriteTitle = CreateTitle("Enriched Film", "enriched film", "tt12345678", now);
        var secondFavoriteTitle = CreateTitle("Alternate source title", "alternate source title", "tt12345678", now.AddHours(1));
        var pendingTitle = CreateTitle("Pending Film", "pending film", "tt87654321", now);
        var unmatchedTitle = CreateTitle("Unmatched Film", "unmatched film", null, now);
        unmatchedTitle.ImdbRating = 6.3m;
        unmatchedTitle.Genres = ["Drama"];
        unmatchedTitle.Plot = "Legacy local metadata";
        var releaseTitle = CreateTitle("Enriched Film", "enriched film", "tt12345678", now, now, now);
        db.Titles.AddRange(firstFavoriteTitle, secondFavoriteTitle, pendingTitle, unmatchedTitle, releaseTitle);
        await db.SaveChangesAsync();

        var source = new Source
        {
            StableKey = "source-owned-movies",
            Name = "Source-owned movies",
            FeedType = "movie",
            Url = "https://feed.example/source-owned"
        };
        db.Sources.Add(source);
        var oscarFilm = new OscarFilm
        {
            TitleId = firstFavoriteTitle.Id,
            StableKey = "source-owned-oscar-film",
            FilmTitle = "Enriched Film",
            NormalizedTitle = "enriched film",
            FilmYear = 2025,
            ImdbId = "tt12345678",
            EnrichmentStatus = "enriched",
            ImportedAt = now,
            UpdatedAt = now
        };
        var award = new GoldenGlobeAward { Name = "Source-owned award" };
        db.AddRange(oscarFilm, award);
        var movieGroup = new GoldenGlobeGroup
        {
            Title = "Enriched Film",
            Year = 2025,
            NomineeType = "movie",
            ImdbId = "tt12345678",
            EnrichmentStatus = "enriched"
        };
        var seriesGroup = new GoldenGlobeGroup
        {
            Title = "Example Series",
            Year = 2025,
            NomineeType = "series"
        };
        db.GoldenGlobeGroups.AddRange(movieGroup, seriesGroup);
        await db.SaveChangesAsync();

        var releaseTime = now.AddMinutes(-10);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO oscar_nominations (
                oscar_film_id, import_key, ceremony, class, canonical_category, category, name,
                nominees, nominee_ids, detail, is_winner, imported_at, updated_at)
            VALUES (
                {oscarFilm.Id}, {"source-owned-oscar-nomination"}, {98}, {"Feature Film"},
                {"best_picture"}, {"Best Picture"}, {"Source-owned film"}, {"Source-owned film"},
                {"film-id"}, {"detail"}, {true}, {now}, {now})
            """);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO golden_globe_nominations (import_key, winner, award_id, group_id)
            VALUES ({"source-owned-movie-nomination"}, {true}, {award.Id}, {movieGroup.Id}),
                   ({"source-owned-series-nomination"}, {false}, {award.Id}, {seriesGroup.Id})
            """);

        var releaseOccurrences = new[]
        {
            new Occurrence
            {
                TitleId = releaseTitle.Id,
                SourceId = source.Id,
                SourceItemKey = "source-item-one",
                FeedEntryId = "feed-entry-one",
                TorrentUrl = "https://feed.example/release/one",
                RawTitle = "Enriched.Film.2025.1080p",
                SourceFeedName = source.Name,
                FeedType = source.FeedType,
                SourcePublishedAt = releaseTime,
                ObservedAt = releaseTime,
                Quality = "1080p",
                RipType = "WEB-DL",
                FirstSeenAt = releaseTime,
                LastSeenAt = now
            },
            new Occurrence
            {
                TitleId = releaseTitle.Id,
                SourceId = source.Id,
                SourceItemKey = "source-item-two",
                FeedEntryId = "feed-entry-two",
                TorrentUrl = "https://feed.example/release/two",
                RawTitle = "Enriched.Film.2025.2160p",
                SourceFeedName = source.Name,
                FeedType = source.FeedType,
                SourcePublishedAt = releaseTime.AddMinutes(1),
                ObservedAt = releaseTime.AddMinutes(1),
                Quality = "2160p",
                RipType = "BluRay",
                FirstSeenAt = releaseTime,
                LastSeenAt = now
            },
            new Occurrence
            {
                TitleId = unmatchedTitle.Id,
                SourceId = source.Id,
                SourceItemKey = "source-item-three",
                FeedEntryId = "feed-entry-three",
                TorrentUrl = "https://feed.example/release/three",
                RawTitle = "Unmatched.Film.720p",
                SourceFeedName = source.Name,
                FeedType = source.FeedType,
                SourcePublishedAt = releaseTime.AddMinutes(2),
                ObservedAt = releaseTime.AddMinutes(2),
                Quality = "720p",
                RipType = "WEBRip",
                FirstSeenAt = releaseTime,
                LastSeenAt = now
            }
        };
        db.Occurrences.AddRange(releaseOccurrences);
        await db.SaveChangesAsync();

        async Task AddLegacyFavoriteAsync(Title title, bool toWatch, bool toDownload, bool addedFromOscar, bool addedFromCatalog)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO favorite_movies (
                    user_id, title_id, to_watch, to_download, added_from_oscar, added_from_catalog,
                    created_at, updated_at)
                VALUES (
                    {user.Id}, {title.Id}, {toWatch}, {toDownload}, {addedFromOscar}, {addedFromCatalog},
                    {now}, {now})
                """);
        }

        await AddLegacyFavoriteAsync(firstFavoriteTitle, true, false, true, false);
        await AddLegacyFavoriteAsync(secondFavoriteTitle, false, true, false, true);
        await AddLegacyFavoriteAsync(pendingTitle, true, false, true, false);
        await AddLegacyFavoriteAsync(unmatchedTitle, false, true, false, true);

        var preflightDiscardCount = await db.Database.SqlQuery<long>(
            $"""
            SELECT COUNT(*) AS "Value"
            FROM favorite_movies AS favorite
            JOIN titles AS title ON title.id = favorite.title_id
            LEFT JOIN imdb_titles AS imdb ON imdb.imdb_id = title.imdb_id
            WHERE imdb.metadata_status IS DISTINCT FROM 'enriched'
            """).SingleAsync();
        Assert.Equal(2L, preflightDiscardCount);
                var legacyFallbackCount = await db.Database.SqlQuery<long>(
                        $"""
                        SELECT COUNT(*) AS "Value"
                        FROM titles AS title
                        WHERE title.imdb_id IS NULL
                            AND (title.imdb_rating IS NOT NULL OR title.imdb_votes IS NOT NULL OR title.metascore IS NOT NULL
                                OR cardinality(title.genres) > 0 OR cardinality(title.countries) > 0
                                OR NULLIF(btrim(title.director), '') IS NOT NULL OR NULLIF(btrim(title.plot), '') IS NOT NULL
                                OR NULLIF(btrim(title.poster_url), '') IS NOT NULL OR NULLIF(btrim(title.runtime), '') IS NOT NULL
                                OR NULLIF(btrim(title.awards), '') IS NOT NULL OR NULLIF(btrim(title.box_office), '') IS NOT NULL)
                        """).SingleAsync();
                Assert.Equal(1L, legacyFallbackCount);

        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();

        var oscarMovie = await db.OscarMovies.AsNoTracking().SingleAsync();
        Assert.Equal(oscarFilm.StableKey, oscarMovie.StableKey);
        Assert.Equal("tt12345678", oscarMovie.ImdbId);
        Assert.Equal("enriched", oscarMovie.EnrichmentStatus);
        var oscarNomination = await db.OscarNominations.AsNoTracking().SingleAsync();
        Assert.Equal(oscarMovie.Id, oscarNomination.OscarMovieId);

        var goldenGlobeTitles = await db.GoldenGlobeTitles.AsNoTracking().OrderBy(title => title.Title).ToListAsync();
        Assert.Equal(new[] { "Enriched Film", "Example Series" }, goldenGlobeTitles.Select(title => title.Title));
        Assert.Equal("tt12345678", goldenGlobeTitles[0].ImdbId);
        Assert.Equal("series", goldenGlobeTitles[1].NomineeType);
        var migratedNominations = await db.GoldenGlobeNominations.AsNoTracking().OrderBy(nomination => nomination.ImportKey).ToListAsync();
        Assert.Equal(2, migratedNominations.Count);
        Assert.All(migratedNominations, nomination => Assert.Contains(goldenGlobeTitles, title => title.Id == nomination.GoldenGlobeTitleId));

        var torrentMovie = await db.TorrentMovies.AsNoTracking().SingleAsync(movie => movie.ImdbId == "tt12345678");
        var unmatchedTorrentMovie = await db.TorrentMovies.AsNoTracking().SingleAsync(movie => movie.TitleText == "Unmatched Film");
        Assert.Null(unmatchedTorrentMovie.ImdbId);
        var torrentMovieProviderColumns = await db.Database.SqlQuery<string>(
            $"""
            SELECT column_name AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'torrent_movies'
              AND column_name IN (
                  'imdb_rating', 'imdb_votes', 'metascore', 'genres', 'countries', 'director',
                  'plot', 'poster_url', 'runtime', 'awards', 'box_office')
            """).ToListAsync();
        Assert.Empty(torrentMovieProviderColumns);
        var migratedReleases = await db.TorrentReleases.AsNoTracking().OrderBy(release => release.SourceItemKey).ToListAsync();
        Assert.Equal(3, migratedReleases.Count);
        var firstRelease = migratedReleases.Single(release => release.SourceItemKey == "source-item-one");
        var secondRelease = migratedReleases.Single(release => release.SourceItemKey == "source-item-two");
        var fallbackRelease = migratedReleases.Single(release => release.SourceItemKey == "source-item-three");
        Assert.Equal(torrentMovie.Id, firstRelease.TorrentMovieId);
        Assert.Equal("1080p", firstRelease.Quality);
        Assert.Equal("WEB-DL", firstRelease.RipType);
        Assert.Equal("https://feed.example/release/one", firstRelease.TorrentUrl);
        Assert.Equal(torrentMovie.Id, secondRelease.TorrentMovieId);
        Assert.Equal("2160p", secondRelease.Quality);
        Assert.Equal("feed-entry-two", secondRelease.FeedEntryId);
        Assert.Equal(unmatchedTorrentMovie.Id, fallbackRelease.TorrentMovieId);
        Assert.Equal("720p", fallbackRelease.Quality);

        var favorite = await db.ImdbFavoriteMovies.AsNoTracking().SingleAsync();
        Assert.Equal(user.Id, favorite.UserId);
        Assert.Equal("tt12345678", favorite.ImdbId);
        Assert.True(favorite.ToWatch);
        Assert.True(favorite.ToDownload);
        Assert.True(favorite.AddedFromOscar);
        Assert.True(favorite.AddedFromCatalog);
        Assert.Equal(now, favorite.CreatedAt);
        Assert.Equal(now, favorite.UpdatedAt);
        var retainedLegacyFavorites = await db.FavoriteMovies.AsNoTracking().OrderBy(favorite => favorite.TitleId).ToListAsync();
        Assert.Equal(new[] { firstFavoriteTitle.Id, secondFavoriteTitle.Id }, retainedLegacyFavorites.Select(favorite => favorite.TitleId));

        await db.Database.MigrateAsync("20261020150000_NormalizeGoldenGlobeGroups");
        var rolledBackFavoriteCount = await db.Database.SqlQuery<long>(
            $"SELECT COUNT(*) AS \"Value\" FROM favorite_movies").SingleAsync();
        Assert.Equal(2L, rolledBackFavoriteCount);
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
        Assert.Contains("20261020125713_EnforceOscarFilmTitleIdentity", appliedMigrations);
        Assert.Contains("20261020140000_EnforceScanRunNonnegativeCounters", appliedMigrations);
        Assert.Contains("20261020150000_NormalizeGoldenGlobeGroups", appliedMigrations);
        Assert.Contains("20261021061711_SourceOwnedMovieIdentity", appliedMigrations);

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
                    "golden_globe_awards", "golden_globe_groups", "golden_globe_nominations", "favorite_movies", "personal_ratings", "users",
                    "imdb_titles", "oscar_movies", "golden_globe_titles", "torrent_movies", "torrent_releases",
                    "legacy_favorite_movies"
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
        await AssertRejectedAsync(db, new ScanRun
        {
            StartedAt = now,
            FinishedAt = now,
            Status = "succeeded",
            Trigger = "manual",
            FeedsProcessed = -1
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
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO golden_globe_nominations (
                import_key, year, winner, award_id, title, nominee_type, imdb_id,
                is_imdb_id_manual, imdb_id_version, enrichment_status, enrichment_attempt_count)
            VALUES ('legacy-imdb-nomination', 2025, false, {award.Id}, 'Legacy nominee', 'movie', ' TT87654321 ',
                    false, 0, 'pending', 0)
            """);

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
        var migratedGoldenGlobeGroup = await db.GoldenGlobeGroups.AsNoTracking()
            .Include(group => group.Nominations)
            .SingleAsync(group => group.Title == "Legacy nominee");
        Assert.Equal("tt87654321", migratedGoldenGlobeGroup.ImdbId);
        Assert.Equal("pending", migratedGoldenGlobeGroup.EnrichmentStatus);
        Assert.Equal("legacy-imdb-nomination", Assert.Single(migratedGoldenGlobeGroup.Nominations).ImportKey);

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
            AwardId = award.Id,
            Group = new GoldenGlobeGroup
            {
                Title = "Nullable nominee",
                Year = 2025,
                NomineeType = "movie"
            }
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