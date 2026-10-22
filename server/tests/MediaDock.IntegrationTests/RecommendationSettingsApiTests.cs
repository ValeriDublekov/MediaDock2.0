using System.Net;
using System.Diagnostics;
using System.Net.Http.Json;
using MediaDock.Api.Recommendations;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediaDock.IntegrationTests;

[Trait("Category", "Api")]
public sealed class RecommendationSettingsApiTests
{
    [Fact]
    public async Task RecommendationSettingsCanBeUpdatedAndVoteWeightsRecalculated()
    {
        await using var host = await CatalogApiTestHost.CreateAsync("mediadock_recommendation_settings_api_test");
        var db = host.Db;
        var client = host.Client;

        using var initialResponse = await client.GetAsync("/api/settings/recommendations");
        Assert.Equal(HttpStatusCode.OK, initialResponse.StatusCode);
        var initialSettings = await initialResponse.Content.ReadFromJsonAsync<RecommendationSettingsResponse>();
        Assert.NotNull(initialSettings);
        Assert.Equal(5, initialSettings.MedianPeriodYears);
        Assert.False(initialSettings.RequiresVoteRecalculation);
        Assert.Null(initialSettings.LastVoteRecalculationAt);
        Assert.Contains(initialSettings.AwardWeights, weight =>
            weight.AwardSource == "golden_globes"
            && weight.CategoryKey == "best_motion_picture_drama"
            && weight.WinPoints == 2m
            && weight.NominationPoints == 0.5m);

        const string secret = "recommendation-api-secret-check";
        var providerSettings = await db.Settings.SingleAsync();
        providerSettings.OmdbApiKey = secret;
        await db.SaveChangesAsync();

        using var invalidUpdateResponse = await client.PutAsJsonAsync(
            "/api/settings/recommendations",
            new UpdateRecommendationSettingsRequest
            {
                MedianPeriodYears = 5,
                AwardWeights =
                [
                    new UpdateRecommendationAwardWeightRequest
                    {
                        AwardSource = "golden_globes",
                        CategoryKey = "best_motion_picture_drama",
                        WinPoints = -1m,
                        NominationPoints = 0.5m
                    }
                ]
            });
        Assert.Equal(HttpStatusCode.BadRequest, invalidUpdateResponse.StatusCode);

        using var updateResponse = await client.PutAsJsonAsync(
            "/api/settings/recommendations",
            new UpdateRecommendationSettingsRequest
            {
                MedianPeriodYears = 4,
                AwardWeights =
                [
                    new UpdateRecommendationAwardWeightRequest
                    {
                        AwardSource = "golden_globes",
                        CategoryKey = "best_motion_picture_drama",
                        WinPoints = 7m,
                        NominationPoints = 3m
                    },
                    new UpdateRecommendationAwardWeightRequest
                    {
                        AwardSource = "oscar",
                        CategoryKey = "BEST PICTURE",
                        WinPoints = 5m,
                        NominationPoints = 2m
                    }
                ]
            });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updateBody = await updateResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(secret, updateBody);
        var updatedSettings = await updateResponse.Content.ReadFromJsonAsync<RecommendationSettingsResponse>();
        Assert.NotNull(updatedSettings);
        Assert.Equal(4, updatedSettings.MedianPeriodYears);
        Assert.True(updatedSettings.RequiresVoteRecalculation);
        Assert.Contains(updatedSettings.AwardWeights, weight =>
            weight.AwardSource == "golden_globes"
            && weight.CategoryKey == "best_motion_picture_drama"
            && weight.WinPoints == 7m
            && weight.NominationPoints == 3m);

        db.ImdbTitles.AddRange(
            new ImdbTitle
            {
                ImdbId = "tt99887761",
                MediaType = "movie",
                Year = 2000,
                ImdbVotes = 100,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            new ImdbTitle
            {
                ImdbId = "tt99887762",
                MediaType = "movie",
                Year = 2001,
                ImdbVotes = 300,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            new ImdbTitle
            {
                ImdbId = "tt99887763",
                MediaType = "movie",
                ImdbVotes = 200,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            new ImdbTitle
            {
                ImdbId = "tt99887764",
                MediaType = "movie",
                Year = 1999,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        await db.SaveChangesAsync();

        using var recalculateResponse = await client.PostAsync(
            "/api/settings/recommendations/recalculate-votes",
            content: null);
        Assert.Equal(HttpStatusCode.OK, recalculateResponse.StatusCode);
        var recalculation = await recalculateResponse.Content
            .ReadFromJsonAsync<VoteWeightRecalculationResponse>();
        Assert.NotNull(recalculation);
        Assert.Equal(4, recalculation.MedianPeriodYears);
        Assert.Equal(4, recalculation.MovieCount);
        Assert.Equal(3, recalculation.MoviesWithVotesCount);
        Assert.Equal(2, recalculation.PeriodCount);
        Assert.Equal(1, recalculation.GlobalFallbackCount);
        Assert.Equal(1, recalculation.MissingVoteCount);
        Assert.True(recalculation.DurationMilliseconds >= 0);

        db.ChangeTracker.Clear();
        var storedWeights = await db.ImdbTitles.AsNoTracking()
            .Where(title => title.ImdbId.StartsWith("tt9988776"))
            .ToDictionaryAsync(title => title.ImdbId, title => title.NormalizedVoteWeight);
        Assert.Equal(Math.Log10(1.5d), storedWeights["tt99887761"], 12);
        Assert.Equal(Math.Log10(2.5d), storedWeights["tt99887762"], 12);
        Assert.Equal(Math.Log10(2d), storedWeights["tt99887763"], 12);
        Assert.Equal(0d, storedWeights["tt99887764"]);
        var savedSettings = await db.GeneralRecommendationSettings.AsNoTracking().SingleAsync();
        Assert.False(savedSettings.RequiresVoteRecalculation);
        Assert.NotNull(savedSettings.LastVoteRecalculationAt);
        Assert.Equal(4, savedSettings.LastVoteRecalculationPeriodYears);
    }

    [Fact]
    public async Task RecalculationTimeoutReturnsGatewayTimeoutAndPreservesPriorSuccess()
    {
        await using var host = await CatalogApiTestHost.CreateAsync("mediadock_recommendation_timeout_test");
        var db = host.Db;
        const string imdbId = "tt99887770";
        var previousCalculationAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        db.ImdbTitles.Add(new ImdbTitle
        {
            ImdbId = imdbId,
            MediaType = "movie",
            Year = 2000,
            ImdbVotes = 100,
            NormalizedVoteWeight = 6.25d,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var settings = await db.GeneralRecommendationSettings.SingleAsync();
        settings.RequiresVoteRecalculation = false;
        settings.LastVoteRecalculationAt = previousCalculationAt;
        settings.LastVoteRecalculationPeriodYears = 7;
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE FUNCTION test_delay_recommendation_vote_update()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $$
            BEGIN
                PERFORM pg_sleep(30);
                RETURN NEW;
            END;
            $$;
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TRIGGER test_delay_recommendation_vote_update
            BEFORE UPDATE OF normalized_vote_weight ON imdb_titles
            FOR EACH ROW
            EXECUTE FUNCTION test_delay_recommendation_vote_update();
            """);

        using var response = await host.Client.PostAsync(
                "/api/settings/recommendations/recalculate-votes",
                content: null)
            .WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        db.ChangeTracker.Clear();
        var storedMovie = await db.ImdbTitles.AsNoTracking().SingleAsync(title => title.ImdbId == imdbId);
        Assert.Equal(6.25d, storedMovie.NormalizedVoteWeight);
        var storedSettings = await db.GeneralRecommendationSettings.AsNoTracking().SingleAsync();
        Assert.True(storedSettings.RequiresVoteRecalculation);
        Assert.Equal(previousCalculationAt, storedSettings.LastVoteRecalculationAt);
        Assert.Equal(7, storedSettings.LastVoteRecalculationPeriodYears);
    }

    [Fact]
    public async Task WaitingMetadataWriteMarksVoteWeightsDirtyAfterRecalculationCommits()
    {
        await using var host = await CatalogApiTestHost.CreateAsync("mediadock_recommendation_lock_test");
        var db = host.Db;
        const string imdbId = "tt99887771";
        db.ImdbTitles.Add(new ImdbTitle
        {
            ImdbId = imdbId,
            MediaType = "movie",
            Year = 2000,
            ImdbVotes = 100,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        await SetCleanAsync(db);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE FUNCTION test_delay_recommendation_vote_update()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $$
            BEGIN
                PERFORM pg_sleep(1.5);
                RETURN NEW;
            END;
            $$;
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TRIGGER test_delay_recommendation_vote_update
            BEFORE UPDATE OF normalized_vote_weight ON imdb_titles
            FOR EACH ROW
            EXECUTE FUNCTION test_delay_recommendation_vote_update();
            """);

        var connectionString = host.ConnectionString;
        var recalculationTask = host.Client.PostAsync(
            "/api/settings/recommendations/recalculate-votes",
            content: null);
        await WaitForTableLockAsync(connectionString, "ShareRowExclusiveLock", granted: true);

        var writerOptions = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        var metadataWriteTask = UpdateMovieVotesAsync(writerOptions, imdbId, 101);
        await WaitForTableLockAsync(connectionString, "RowExclusiveLock", granted: false);

        using var response = await recalculationTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await metadataWriteTask.WaitAsync(TimeSpan.FromSeconds(5));

        db.ChangeTracker.Clear();
        Assert.True(await IsDirtyAsync(db));
        Assert.Equal(101, await db.ImdbTitles.AsNoTracking()
            .Where(title => title.ImdbId == imdbId)
            .Select(title => title.ImdbVotes)
            .SingleAsync());
    }

    private static async Task<bool> IsDirtyAsync(MediaDock.Infrastructure.Persistence.MediaDockDbContext db) =>
        await db.GeneralRecommendationSettings
            .AsNoTracking()
            .Where(settings => settings.Id == 1)
            .Select(settings => settings.RequiresVoteRecalculation)
            .SingleAsync();

    private static async Task SetCleanAsync(MediaDock.Infrastructure.Persistence.MediaDockDbContext db) =>
        await db.GeneralRecommendationSettings
            .Where(settings => settings.Id == 1)
            .ExecuteUpdateAsync(setters => setters.SetProperty(settings => settings.RequiresVoteRecalculation, false));

    private static async Task UpdateMovieVotesAsync(
        DbContextOptions<MediaDock.Infrastructure.Persistence.MediaDockDbContext> options,
        string imdbId,
        long votes)
    {
        await using var writerDb = new MediaDock.Infrastructure.Persistence.MediaDockDbContext(options);
        await writerDb.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE imdb_titles SET imdb_votes = {votes} WHERE imdb_id = {imdbId}");
    }

    private static async Task WaitForTableLockAsync(string connectionString, string mode, bool granted)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM pg_locks
                WHERE relation = 'imdb_titles'::regclass
                  AND mode = @mode
                  AND granted = @granted)
            """,
            connection);
        command.Parameters.AddWithValue("mode", mode);
        command.Parameters.AddWithValue("granted", granted);
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(5))
        {
            if (Convert.ToBoolean(await command.ExecuteScalarAsync()))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        Assert.True(false, $"Timed out waiting for {mode}, granted={granted}.");
    }
}