using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.IntegrationTests;

public sealed class PersistenceTests
{
    [Fact]
    [Trait("Category", "Persistence")]
    public async Task MovieRecommendationMigrationSeedsDefaultsAndPreservesOwnedData()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_movie_recommendation_migration_test").Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var db = new MediaDockDbContext(options);
        await db.Database.MigrateAsync("20261021130541_DeduplicateTorrentUrls");

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO imdb_titles (imdb_id, title, year, media_type, imdb_votes, updated_at)
            VALUES ('tt12345678', 'Migration Movie', 2024, 'movie', 125000, TIMESTAMPTZ '2026-10-20 12:00:00+00');
            INSERT INTO golden_globe_awards (name)
            VALUES
                ('Best Motion Picture - Drama'),
                ('Best Motion Picture – Non-English Language'),
                ('Unmapped Category');
            """);

        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        var user = CreateUser("movie-recommendation-migration@example.com", now);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.PersonalRatings.Add(new PersonalRating
        {
            UserId = user.Id,
            ImdbId = "tt12345678",
            Rating = 8,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();

        var settings = await db.Settings.AsNoTracking().SingleAsync();
        Assert.Equal(1, settings.Id);
        Assert.Empty(settings.ExcludedGenres);
        Assert.Empty(settings.ExcludedCountries);
        Assert.Equal(0m, settings.MinMovieRating);
        Assert.Equal(0m, settings.MinSeriesRating);
        Assert.Equal(0, settings.MinImdbVotes);
        Assert.Null(settings.OmdbApiKey);
        Assert.Equal(0, settings.OmdbDailyRequestLimit);
        Assert.Equal(0, settings.OscarEnrichmentMaxFilmsPerRun);
        Assert.Equal(0, settings.OscarEnrichmentMaxRequestsPerDay);
        Assert.Null(settings.UpdatedAt);

        var recommendationSettings = await db.GeneralRecommendationSettings.AsNoTracking().SingleAsync();
        Assert.Equal(1, recommendationSettings.Id);
        Assert.Equal(5, recommendationSettings.MedianPeriodYears);
        Assert.Equal(5.5m, recommendationSettings.BayesianDefaultC);
        Assert.Equal(0.25m, recommendationSettings.BayesianMRatio);
        Assert.Equal(3m, recommendationSettings.OscarWinPoints);
        Assert.Equal(1m, recommendationSettings.OscarNomPoints);
        Assert.Equal(2m, recommendationSettings.GgWinPoints);
        Assert.Equal(0.5m, recommendationSettings.GgNomPoints);
        Assert.True(recommendationSettings.RequiresVoteRecalculation);
        Assert.Null(recommendationSettings.LastVoteRecalculationAt);
        Assert.Null(recommendationSettings.LastVoteRecalculationPeriodYears);

        var movie = await db.ImdbTitles.AsNoTracking().SingleAsync(title => title.ImdbId == "tt12345678");
        Assert.Equal("Migration Movie", movie.TitleText);
        Assert.Equal(2024, movie.Year);
        Assert.Equal("movie", movie.MediaType);
        Assert.Equal(125000, movie.ImdbVotes);
        Assert.Equal(0d, movie.NormalizedVoteWeight);

        var awardKeys = await db.GoldenGlobeAwards.AsNoTracking()
            .ToDictionaryAsync(award => award.Name, award => award.CategoryKey);
        Assert.Equal("best_motion_picture_drama", awardKeys["Best Motion Picture - Drama"]);
        Assert.Equal("foreign_film_foreign_language", awardKeys["Best Motion Picture – Non-English Language"]);
        Assert.Equal("award_other", awardKeys["Unmapped Category"]);
        Assert.Equal(
            "foreign_film_foreign_language",
            (await db.AwardCategoryAliases.AsNoTracking()
                .SingleAsync(alias => alias.RawName == "Foreign Film - Foreign Language")).CategoryKey);

        var oscarWeight = await db.RecommendationAwardWeights.AsNoTracking()
            .SingleAsync(weight => weight.AwardSource == "oscar" && weight.CategoryKey == "BEST PICTURE");
        Assert.Equal(3m, oscarWeight.WinPoints);
        Assert.Equal(1m, oscarWeight.NominationPoints);
        Assert.False(oscarWeight.IsOverride);
        var goldenGlobesWeight = await db.RecommendationAwardWeights.AsNoTracking()
            .SingleAsync(weight => weight.AwardSource == "golden_globes" && weight.CategoryKey == "best_motion_picture_drama");
        Assert.Equal(2m, goldenGlobesWeight.WinPoints);
        Assert.Equal(0.5m, goldenGlobesWeight.NominationPoints);
        Assert.False(goldenGlobesWeight.IsOverride);
        Assert.All(
            await db.RecommendationAwardWeights.AsNoTracking()
                .Where(weight => weight.CategoryKey == "award_other")
                .ToListAsync(),
            weight =>
            {
                Assert.Equal(0m, weight.WinPoints);
                Assert.Equal(0m, weight.NominationPoints);
            });

        var preservedRating = await db.PersonalRatings.AsNoTracking().SingleAsync();
        Assert.Equal(user.Id, preservedRating.UserId);
        Assert.Equal("tt12345678", preservedRating.ImdbId);
        Assert.Equal(8, preservedRating.Rating);
        Assert.Equal(now, preservedRating.UpdatedAt);

        await AssertRejectedAsync(db, new AwardCategoryAlias
        {
            AwardSource = "golden_globes",
            RawName = "Best Motion Picture - Drama",
            CategoryKey = "duplicate_alias"
        });
        await AssertRejectedAsync(db, new RecommendationAwardWeight
        {
            AwardSource = "oscar",
            CategoryKey = "BEST PICTURE",
            WinPoints = 3m,
            NominationPoints = 1m
        });
        await AssertRejectedAsync(db, new RecommendationAwardWeight
        {
            AwardSource = "golden_globes",
            CategoryKey = "negative_win",
            WinPoints = -1m,
            NominationPoints = 0m
        });
        await AssertRejectedAsync(db, new RecommendationAwardWeight
        {
            AwardSource = "golden_globes",
            CategoryKey = "negative_nomination",
            WinPoints = 0m,
            NominationPoints = -1m
        });

        var invalidPeriod = await db.GeneralRecommendationSettings.SingleAsync();
        invalidPeriod.MedianPeriodYears = 0;
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        await db.Database.MigrateAsync("20261021130541_DeduplicateTorrentUrls");
        var rolledBackUpdatedAt = await db.Database.SqlQuery<DateTimeOffset>(
            $"SELECT updated_at AS \"Value\" FROM settings WHERE id = 1")
            .SingleAsync();
        Assert.NotEqual(default, rolledBackUpdatedAt);
        Assert.DoesNotContain(
            "20261022122143_AddMovieRecommendationPersistence",
            await db.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public async Task MovieRecommendationMigrationPreservesExistingSettings()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_movie_recommendation_settings_migration_test").Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var db = new MediaDockDbContext(options);
        await db.Database.MigrateAsync("20261021130541_DeduplicateTorrentUrls");

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO settings (
                id,
                excluded_genres,
                excluded_countries,
                min_movie_rating,
                min_series_rating,
                min_imdb_votes,
                omdb_api_key,
                omdb_daily_request_limit,
                oscar_enrichment_max_films_per_run,
                oscar_enrichment_max_requests_per_day,
                updated_at)
            VALUES (
                1,
                ARRAY['Drama']::text[],
                ARRAY['Canada']::text[],
                6.5,
                7.5,
                1400,
                'preserved-test-key',
                37,
                12,
                345,
                TIMESTAMPTZ '2026-10-20 12:00:00+00');
            """);

        await db.Database.MigrateAsync("20261022122143_AddMovieRecommendationPersistence");
        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE settings
            SET median_period_years = 12,
                requires_vote_recalculation = TRUE,
                last_vote_recalculation_at = TIMESTAMPTZ '2026-10-19 10:30:00+00',
                last_vote_recalculation_period_years = 10
            WHERE id = 1;
            """);
        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();

        var settings = await db.Settings.AsNoTracking().SingleAsync();
        Assert.Equal(new[] { "Drama" }, settings.ExcludedGenres);
        Assert.Equal(new[] { "Canada" }, settings.ExcludedCountries);
        Assert.Equal(6.5m, settings.MinMovieRating);
        Assert.Equal(7.5m, settings.MinSeriesRating);
        Assert.Equal(1400, settings.MinImdbVotes);
        Assert.Equal("preserved-test-key", settings.OmdbApiKey);
        Assert.Equal(37, settings.OmdbDailyRequestLimit);
        Assert.Equal(12, settings.OscarEnrichmentMaxFilmsPerRun);
        Assert.Equal(345, settings.OscarEnrichmentMaxRequestsPerDay);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero), settings.UpdatedAt);

        var recommendationSettings = await db.GeneralRecommendationSettings.AsNoTracking().SingleAsync();
        Assert.Equal(12, recommendationSettings.MedianPeriodYears);
        Assert.True(recommendationSettings.RequiresVoteRecalculation);
        Assert.Equal(
            new DateTimeOffset(2026, 10, 19, 10, 30, 0, TimeSpan.Zero),
            recommendationSettings.LastVoteRecalculationAt);
        Assert.Equal(10, recommendationSettings.LastVoteRecalculationPeriodYears);
    }

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
    public async Task OscarMovieAllowsMissingImdbIdentityAndRequiresCanonicalReferenceWhenPresent()
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
        db.OscarMovies.AddRange(
            new OscarMovie
            {
                StableKey = "matched-oscar-film",
                FilmTitle = "Linked Film",
                NormalizedTitle = "linked film",
                FilmYear = 2025,
                ImdbId = "tt12345678",
                ImportedAt = now,
                UpdatedAt = now
            },
            new OscarMovie
            {
                StableKey = "unmatched-oscar-film",
                FilmTitle = "Unmatched Film",
                NormalizedTitle = "unmatched film",
                FilmYear = 2025,
                ImdbId = null,
                ImportedAt = now,
                UpdatedAt = now
            });
        await db.SaveChangesAsync();

        await AssertRejectedAsync(db, new OscarMovie
        {
            StableKey = "missing-imdb-title-film",
            FilmTitle = "Missing IMDb Title Film",
            NormalizedTitle = "missing imdb title film",
            FilmYear = 2025,
            ImdbId = "tt99999999",
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
        var legacyOptions = new DbContextOptionsBuilder<LegacyPersistenceTestContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var legacyDb = new LegacyPersistenceTestContext(legacyOptions);
        await db.Database.MigrateAsync("20261020122556_EnforceTitleMetadataOwnership");

        var now = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);
        legacyDb.ImdbTitles.AddRange(
            new LegacyImdbTitleRow { ImdbId = "tt12345678", UpdatedAt = now },
            new LegacyImdbTitleRow { ImdbId = "tt87654321", UpdatedAt = now });
        await legacyDb.SaveChangesAsync();
        var title = CreateLegacyTitle("Linked Film", "linked film", "tt12345678", now);
        legacyDb.Titles.Add(title);
        await legacyDb.SaveChangesAsync();

        legacyDb.OscarFilms.Add(new LegacyOscarFilmRow
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
        await legacyDb.SaveChangesAsync();

        var migrationFailure = await Assert.ThrowsAsync<Npgsql.PostgresException>(
            () => db.Database.MigrateAsync());
        Assert.Contains("Cannot enforce Oscar film IMDb identity", migrationFailure.Message);
        Assert.DoesNotContain(
            "20261020125713_EnforceOscarFilmTitleIdentity",
            await db.Database.GetAppliedMigrationsAsync());

        var unchangedFilm = await legacyDb.OscarFilms
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal("tt87654321", unchangedFilm.ImdbId);
        Assert.Equal("tt12345678", (await legacyDb.Titles.AsNoTracking().SingleAsync()).ImdbId);
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
        var legacyOptions = new DbContextOptionsBuilder<LegacyPersistenceTestContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var legacyDb = new LegacyPersistenceTestContext(legacyOptions);
        await db.Database.MigrateAsync("20261020140000_EnforceScanRunNonnegativeCounters");

        legacyDb.ImdbTitles.AddRange(
            new LegacyImdbTitleRow { ImdbId = "tt12345678", UpdatedAt = DateTimeOffset.UtcNow },
            new LegacyImdbTitleRow { ImdbId = "tt87654321", UpdatedAt = DateTimeOffset.UtcNow });
        var award = new LegacyGoldenGlobeAwardRow { Name = "Migration Preflight Award" };
        legacyDb.GoldenGlobeAwards.Add(award);
        await legacyDb.SaveChangesAsync();

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

        var groups = await db.GoldenGlobeTitles.AsNoTracking()
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
        var legacyOptions = new DbContextOptionsBuilder<LegacyPersistenceTestContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var legacyDb = new LegacyPersistenceTestContext(legacyOptions);
        await db.Database.MigrateAsync("20261020150000_NormalizeGoldenGlobeGroups");

        var now = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        var user = CreateUser("source-owned@example.com", now);
        db.Users.Add(user);
        legacyDb.ImdbTitles.AddRange(
            new LegacyImdbTitleRow
            {
                ImdbId = "tt12345678",
                TitleText = "Enriched Film",
                ImdbRating = 8.2m,
                MetadataStatus = "enriched",
                UpdatedAt = now
            },
            new LegacyImdbTitleRow { ImdbId = "tt87654321", MetadataStatus = "pending", UpdatedAt = now });
        await db.SaveChangesAsync();
        await legacyDb.SaveChangesAsync();

        await db.Database.ExecuteSqlRawAsync("DROP INDEX ux_titles_imdb_id");
        var firstFavoriteTitle = CreateLegacyTitle("Enriched Film", "enriched film", "tt12345678", now);
        var secondFavoriteTitle = CreateLegacyTitle("Alternate source title", "alternate source title", "tt12345678", now.AddHours(1));
        var pendingTitle = CreateLegacyTitle("Pending Film", "pending film", "tt87654321", now);
        var unmatchedTitle = CreateLegacyTitle("Unmatched Film", "unmatched film", null, now);
        unmatchedTitle.ImdbRating = 6.3m;
        unmatchedTitle.Genres = ["Drama"];
        unmatchedTitle.Plot = "Legacy local metadata";
        var releaseTitle = CreateLegacyTitle("Enriched Film", "enriched film", "tt12345678", now, now, now);
        legacyDb.Titles.AddRange(firstFavoriteTitle, secondFavoriteTitle, pendingTitle, unmatchedTitle, releaseTitle);
        await legacyDb.SaveChangesAsync();

        var source = new Source
        {
            StableKey = "source-owned-movies",
            Name = "Source-owned movies",
            FeedType = "movie",
            Url = "https://feed.example/source-owned"
        };
        db.Sources.Add(source);
        var oscarFilm = new LegacyOscarFilmRow
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
        var award = new LegacyGoldenGlobeAwardRow { Name = "Source-owned award" };
        legacyDb.OscarFilms.Add(oscarFilm);
        legacyDb.GoldenGlobeAwards.Add(award);
        await legacyDb.SaveChangesAsync();
        await db.SaveChangesAsync();
        var movieGroup = new LegacyGoldenGlobeGroupRow
        {
            Title = "Enriched Film",
            Year = 2025,
            NomineeType = "movie",
            ImdbId = "tt12345678",
            EnrichmentStatus = "enriched"
        };
        var seriesGroup = new LegacyGoldenGlobeGroupRow
        {
            Title = "Example Series",
            Year = 2025,
            NomineeType = "series"
        };
        legacyDb.GoldenGlobeGroups.AddRange(movieGroup, seriesGroup);
        await legacyDb.SaveChangesAsync();

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
            new LegacyOccurrenceRow
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
            new LegacyOccurrenceRow
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
            new LegacyOccurrenceRow
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
        legacyDb.Occurrences.AddRange(releaseOccurrences);
        await legacyDb.SaveChangesAsync();

        async Task AddLegacyFavoriteAsync(LegacyTitleRow title, bool toWatch, bool toDownload, bool addedFromOscar, bool addedFromCatalog)
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
        var legacyTables = await db.Database.SqlQuery<string>(
            $"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_name IN ('titles', 'occurrences', 'oscar_films', 'golden_globe_groups', 'legacy_favorite_movies')
            """).ToListAsync();
        Assert.Empty(legacyTables);
        var legacyNominationColumns = await db.Database.SqlQuery<string>(
            $"""
            SELECT column_name AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND ((table_name = 'oscar_nominations' AND column_name = 'oscar_film_id')
                OR (table_name = 'golden_globe_nominations' AND column_name = 'group_id'))
            """).ToListAsync();
        Assert.Empty(legacyNominationColumns);
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public async Task SourceOwnedNominationMigrationRefusesRollbackWhenLegacyLinksAreNull()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_source_owned_nomination_rollback_test").Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var db = new MediaDockDbContext(options);
        await db.Database.MigrateAsync();

        var now = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        var oscarMovie = new OscarMovie
        {
            StableKey = "rollback-oscar-film",
            FilmTitle = "Rollback Oscar Film",
            NormalizedTitle = "rollback oscar film",
            FilmYear = 2025,
            ImportedAt = now,
            UpdatedAt = now
        };
        var goldenGlobeTitle = new GoldenGlobeTitle
        {
            Title = "Rollback Golden Globe Film",
            Year = 2025,
            NomineeType = "movie"
        };
        var award = new GoldenGlobeAward { Name = "Rollback Award" };
        db.OscarNominations.Add(new OscarNomination
        {
            ImportKey = "rollback-oscar-nomination",
            Ceremony = 2025,
            Class = "film",
            CanonicalCategory = "best_picture",
            Category = "Best Picture",
            Name = "Rollback Oscar Film",
            Nominees = "Rollback Oscar Film",
            NomineeIds = string.Empty,
            Detail = string.Empty,
            OscarMovie = oscarMovie,
            ImportedAt = now,
            UpdatedAt = now
        });
        db.GoldenGlobeNominations.Add(new GoldenGlobeNomination
        {
            ImportKey = "rollback-golden-globe-nomination",
            Award = award,
            GoldenGlobeTitle = goldenGlobeTitle
        });
        await db.SaveChangesAsync();

        var rollbackError = await Assert.ThrowsAsync<Npgsql.PostgresException>(
            () => db.Database.MigrateAsync("20261021061711_SourceOwnedMovieIdentity"));

        Assert.Contains("Cannot restore required legacy nomination links", rollbackError.Message);
        Assert.Contains(
            "20261021080502_AllowSourceOwnedNominationLinks",
            await db.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public async Task MigrationCreatesSourceOwnedSchemaAndReleaseIdentityIsUnique()
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
        Assert.Contains("20261021080502_AllowSourceOwnedNominationLinks", appliedMigrations);
        Assert.Contains(
            appliedMigrations,
            migration => migration.EndsWith("_RemoveLegacyTitleIdentity", StringComparison.Ordinal));

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
                    "sources", "scan_runs", "parse_logs", "settings", "metadata_cache",
                    "oscar_nominations", "oscar_enrichment_runs", "omdb_daily_usage",
                    "golden_globe_awards", "golden_globe_nominations", "favorite_movies", "personal_ratings", "users",
                    "imdb_titles", "oscar_movies", "golden_globe_titles", "torrent_movies", "torrent_releases",
                    "background_jobs", "background_job_events"
                },
                tableName => Assert.Contains(tableName, tables));
            Assert.DoesNotContain("titles", tables);
            Assert.DoesNotContain("occurrences", tables);
            Assert.DoesNotContain("oscar_films", tables);
            Assert.DoesNotContain("golden_globe_groups", tables);
            Assert.DoesNotContain("legacy_favorite_movies", tables);
        }

        var favoriteForeignKeys = await db.Database.SqlQuery<string>(
            $"""
            SELECT constraint_name AS "Value"
            FROM information_schema.table_constraints
            WHERE table_schema = 'public' AND table_name = 'favorite_movies' AND constraint_type = 'FOREIGN KEY'
            """).ToListAsync();
        Assert.Contains("fk_favorite_movies_users_user_id", favoriteForeignKeys);
        Assert.Contains("fk_favorite_movies_imdb_titles_imdb_id", favoriteForeignKeys);

        var observedAt = new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);
        var source = new Source
        {
            StableKey = "movies-main",
            Name = "Movies",
            FeedType = "movie",
            Url = "https://feed.example/movies"
        };
        var movie = new TorrentMovie
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
        db.AddRange(source, movie);
        await db.SaveChangesAsync();

        var itemKey = SourceItemKey.From(" guid-123 ", "https://feed.example/topic/1");
        db.TorrentReleases.Add(new TorrentRelease
        {
            TorrentMovieId = movie.Id,
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
        db.TorrentReleases.Add(new TorrentRelease
        {
            TorrentMovieId = movie.Id,
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
        Assert.Equal(1, await db.TorrentReleases.CountAsync());
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
        db.ImdbTitles.Add(imdbTitle);
        var movie = new TorrentMovie
        {
            TitleText = "Canonical",
            NormalizedTitle = "canonical",
            MediaType = "movie",
            ImdbId = "tt12345678",
            FirstSeenAt = now,
            LastSeenAt = now,
            UpdatedAt = now
        };
        db.TorrentMovies.Add(movie);
        await db.SaveChangesAsync();

        var firstSameFilmImdbTitle = new ImdbTitle { ImdbId = "tt20000001", UpdatedAt = now };
        var secondSameFilmImdbTitle = new ImdbTitle { ImdbId = "tt20000002", UpdatedAt = now };
        var firstSameFilmTitle = new TorrentMovie
        {
            TitleText = "Same Film",
            NormalizedTitle = "same film",
            MediaType = "movie",
            ImdbId = "tt20000001",
            UpdatedAt = now
        };
        var secondSameFilmTitle = new TorrentMovie
        {
            TitleText = "Same Film",
            NormalizedTitle = "same film",
            MediaType = "movie",
            ImdbId = "tt20000002",
            UpdatedAt = now
        };
        db.ImdbTitles.AddRange(firstSameFilmImdbTitle, secondSameFilmImdbTitle);
        db.TorrentMovies.AddRange(firstSameFilmTitle, secondSameFilmTitle);
        await db.SaveChangesAsync();
        Assert.Equal(3, await db.TorrentMovies.CountAsync());

        await AssertRejectedAsync(db, new TorrentMovie
        {
            TitleText = "Duplicate ID",
            NormalizedTitle = "duplicate id",
            MediaType = "movie",
            ImdbId = "tt12345678",
            UpdatedAt = now
        });
        await AssertRejectedAsync(db, new TorrentMovie
        {
            TitleText = "Partial Range",
            NormalizedTitle = "partial range",
            MediaType = "movie",
            FirstSeenAt = now,
            UpdatedAt = now
        });
        await AssertRejectedAsync(
            db,
            new TorrentMovie
            {
                TitleText = "Reversed Range",
                NormalizedTitle = "reversed range",
                MediaType = "movie",
                FirstSeenAt = now.AddHours(1),
                LastSeenAt = now,
                UpdatedAt = now
            });

        var source = new Source
        {
            StableKey = "constraint-test",
            Name = "Constraint Test",
            FeedType = "movie",
            Url = "https://feed.example/movies"
        };
        db.Sources.Add(source);
        await db.SaveChangesAsync();
        await AssertRejectedAsync(db, new TorrentRelease
        {
            TorrentMovieId = movie.Id,
            SourceId = source.Id,
            SourceItemKey = "reversed-occurrence",
            TorrentUrl = "https://feed.example/topic/1",
            RawTitle = "Canonical (2026)",
            SourceFeedName = source.Name,
            FirstSeenAt = now.AddHours(1),
            LastSeenAt = now
        });

        var settings = await db.Settings.SingleAsync();
        Assert.Equal(1, settings.Id);
        settings.UpdatedAt = now;
        await db.SaveChangesAsync();
        await AssertRejectedAsync(db, new AppSetting { Id = 2, UpdatedAt = now });

        await AssertRejectedAsync(db, new OscarMovie
        {
            StableKey = "invalid-status",
            FilmTitle = "Invalid Status",
            NormalizedTitle = "invalid status",
            FilmYear = 2026,
            EnrichmentStatus = "unknown",
            ImportedAt = now,
            UpdatedAt = now
        });

        await AssertRejectedAsync(db, new OscarMovie
        {
            StableKey = "negative-attempts",
            FilmTitle = "Negative Attempts",
            NormalizedTitle = "negative attempts",
            FilmYear = 2026,
            EnrichmentStatus = "pending",
            EnrichmentAttemptCount = -1,
            ImportedAt = now,
            UpdatedAt = now
        });

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
        var legacyOptions = new DbContextOptionsBuilder<LegacyPersistenceTestContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var legacyDb = new LegacyPersistenceTestContext(legacyOptions);
        await db.Database.MigrateAsync("20261016071457_AddUserOwnershipToFavoritesAndRatings");

        var now = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
        var firstUser = CreateUser("first@example.com", now);
        var secondUser = CreateUser("second@example.com", now);
        db.Users.AddRange(firstUser, secondUser);
        await db.SaveChangesAsync();

        var firstTitle = CreateLegacyTitle("Earlier title", "earlier title", " TT12345678 ", now.AddDays(-1));
        firstTitle.ImdbRating = 7.1m;
        var duplicateTitle = CreateLegacyTitle("Latest metadata", "latest metadata", "tt12345678", now);
        duplicateTitle.ImdbRating = 8.4m;
        duplicateTitle.ImdbVotes = 1200;
        duplicateTitle.Genres = ["Drama"];
        duplicateTitle.Plot = "Backfilled plot";
        var localTitle = CreateLegacyTitle("Local-only title", "local-only title", null, now);
        localTitle.ImdbRating = 6.1m;
        localTitle.Genres = ["Local genre"];
        localTitle.Plot = "Keep this title-local metadata";
        legacyDb.Titles.AddRange(firstTitle, duplicateTitle, localTitle);
        await legacyDb.SaveChangesAsync();

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
        legacyDb.OscarFilms.Add(new LegacyOscarFilmRow
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
        await legacyDb.SaveChangesAsync();
        var award = new LegacyGoldenGlobeAwardRow { Name = "Legacy award" };
        legacyDb.GoldenGlobeAwards.Add(award);
        await legacyDb.SaveChangesAsync();
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
        var lateImdbTitle = new LegacyImdbTitleRow
        {
            ImdbId = "tt88888888",
            MetadataStatus = "pending",
            UpdatedAt = now
        };
        var lateLinkedTitle = CreateLegacyTitle("Late linked title", "late linked title", "tt88888888", now);
        lateLinkedTitle.ImdbRating = 6.7m;
        lateLinkedTitle.Genres = ["Documentary"];
        lateLinkedTitle.Plot = "Backfilled after the shared-IMDb migration.";
        legacyDb.ImdbTitles.Add(lateImdbTitle);
        await legacyDb.SaveChangesAsync();
        legacyDb.Titles.Add(lateLinkedTitle);
        await legacyDb.SaveChangesAsync();

        await db.Database.MigrateAsync("20261020150000_NormalizeGoldenGlobeGroups");
        db.ChangeTracker.Clear();

        var imdbTitles = await legacyDb.ImdbTitles.AsNoTracking().OrderBy(title => title.ImdbId).ToListAsync();
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

        var migratedTitles = await legacyDb.Titles.AsNoTracking().OrderBy(title => title.Id).ToListAsync();
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

        var duplicateMetadataTitle = CreateLegacyTitle("Duplicate metadata", "duplicate metadata", "tt87654321", now);
        duplicateMetadataTitle.ImdbRating = 5.0m;
        legacyDb.Titles.Add(duplicateMetadataTitle);
        await Assert.ThrowsAsync<DbUpdateException>(() => legacyDb.SaveChangesAsync());
        legacyDb.ChangeTracker.Clear();

        var ratings = await db.PersonalRatings.AsNoTracking().OrderBy(rating => rating.UserId).ToListAsync();
        Assert.Equal(2, ratings.Count);
        Assert.All(ratings, rating => Assert.Equal("tt87654321", rating.ImdbId));
        Assert.Equal(8, ratings.Single(rating => rating.UserId == firstUser.Id).Rating);
        Assert.Equal(6, ratings.Single(rating => rating.UserId == secondUser.Id).Rating);
        Assert.Equal("tt12345678", (await legacyDb.OscarFilms.AsNoTracking().SingleAsync()).ImdbId);
        var migratedGoldenGlobeGroup = await legacyDb.GoldenGlobeGroups.AsNoTracking()
            .SingleAsync(group => group.Title == "Legacy nominee");
        Assert.Equal("tt87654321", migratedGoldenGlobeGroup.ImdbId);
        Assert.Equal("pending", migratedGoldenGlobeGroup.EnrichmentStatus);
        var migratedNominationKeys = await db.Database.SqlQuery<string>(
            $"SELECT import_key AS \"Value\" FROM golden_globe_nominations").ToListAsync();
        Assert.Contains("legacy-imdb-nomination", migratedNominationKeys);

        var noIdTitle = CreateLegacyTitle("Nullable IMDb", "nullable imdb", null, now);
        legacyDb.Titles.Add(noIdTitle);
        await legacyDb.SaveChangesAsync();
        var noIdOscarFilm = new LegacyOscarFilmRow
        {
            TitleId = noIdTitle.Id,
            StableKey = "nullable-imdb-film",
            FilmTitle = noIdTitle.TitleText,
            NormalizedTitle = noIdTitle.NormalizedTitle,
            FilmYear = 2025,
            ImportedAt = now,
            UpdatedAt = now
        };
        legacyDb.OscarFilms.Add(noIdOscarFilm);
        var noIdGroup = new LegacyGoldenGlobeGroupRow
        {
            Title = "Nullable nominee",
            Year = 2025,
            NomineeType = "movie"
        };
        legacyDb.GoldenGlobeGroups.Add(noIdGroup);
        await legacyDb.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO golden_globe_nominations (import_key, winner, award_id, group_id) VALUES ({"nullable-imdb-nomination"}, {false}, {award.Id}, {noIdGroup.Id})");

        db.PersonalRatings.Add(new PersonalRating
        {
            UserId = firstUser.Id,
            ImdbId = "tt99999999",
            Rating = 7,
            UpdatedAt = now
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var referencedImdbTitle = await legacyDb.ImdbTitles.SingleAsync(title => title.ImdbId == "tt12345678");
        legacyDb.ImdbTitles.Remove(referencedImdbTitle);
        await Assert.ThrowsAsync<DbUpdateException>(() => legacyDb.SaveChangesAsync());
        legacyDb.ChangeTracker.Clear();

        await db.Database.MigrateAsync();
        var legacyTables = await db.Database.SqlQuery<string>(
            $"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'public' AND table_name IN ('titles', 'oscar_films', 'golden_globe_groups')
            """).ToListAsync();
        Assert.Empty(legacyTables);
        Assert.Equal(
            "tt12345678",
            (await db.OscarMovies.AsNoTracking().SingleAsync(movie => movie.StableKey == "legacy-imdb-film")).ImdbId);
        Assert.Contains(await db.GoldenGlobeTitles.AsNoTracking().ToListAsync(), title => title.Title == "Legacy nominee");
    }

    private static LegacyTitleRow CreateLegacyTitle(
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