using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.IntegrationTests;

[Trait("Category", "Persistence")]
public sealed class RecommendationVoteInvalidationTests
{
    [Fact]
    public async Task ScoringFormulaMigrationMarksExistingMovieWeightsDirty()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_vote_formula_migration_test").Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var db = new MediaDockDbContext(options);
        await db.Database.MigrateAsync("20261022140000_AddRecommendationVoteInvalidationTriggers");
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE recommendation_award_weights SET win_points = 8.5, nomination_points = 1.25 WHERE award_source = 'golden_globes' AND category_key = 'best_motion_picture_drama'");
        db.ImdbTitles.Add(new ImdbTitle
        {
            ImdbId = "tt98765001",
            MediaType = "movie",
            Year = 2025,
            ImdbVotes = 100,
            NormalizedVoteWeight = 0.3d,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE general_recommendation_settings SET requires_vote_recalculation = FALSE WHERE id = 1");

        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();

        Assert.True(await IsDirtyAsync(db));
        var preservedOverride = await db.RecommendationAwardWeights.AsNoTracking()
            .SingleAsync(weight => weight.AwardSource == "golden_globes"
                && weight.CategoryKey == "best_motion_picture_drama");
        Assert.True(preservedOverride.IsOverride);
        Assert.Equal(8.5m, preservedOverride.WinPoints);
        Assert.Equal(1.25m, preservedOverride.NominationPoints);
    }

    [Fact]
    public async Task MovieInputsAndMedianPeriodChangesMarkVoteWeightsDirty()
    {
        await using var host = await CatalogApiTestHost.CreateAsync("mediadock_vote_invalidation_test");
        var db = host.Db;
        const string imdbId = "tt98765432";

        Assert.False(await IsDirtyAsync(db));
        db.ImdbTitles.Add(new ImdbTitle
        {
            ImdbId = imdbId,
            MediaType = "movie",
            Year = 1998,
            ImdbVotes = 10,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        Assert.True(await IsDirtyAsync(db));

        await SetCleanAsync(db);
        var movie = await db.ImdbTitles.SingleAsync(title => title.ImdbId == imdbId);
        movie.Year = 1999;
        await db.SaveChangesAsync();
        Assert.True(await IsDirtyAsync(db));

        await SetCleanAsync(db);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE imdb_titles SET imdb_votes = 20 WHERE imdb_id = {imdbId}");
        Assert.True(await IsDirtyAsync(db));

        await SetCleanAsync(db);
        movie.MediaType = "series";
        await db.SaveChangesAsync();
        Assert.True(await IsDirtyAsync(db));

        await SetCleanAsync(db);
        movie.Year = 2000;
        await db.SaveChangesAsync();
        Assert.False(await IsDirtyAsync(db));

        movie.MediaType = "movie";
        await db.SaveChangesAsync();
        Assert.True(await IsDirtyAsync(db));

        await SetCleanAsync(db);
        await db.GeneralRecommendationSettings
            .Where(settings => settings.Id == 1)
            .ExecuteUpdateAsync(setters => setters.SetProperty(settings => settings.MedianPeriodYears, 10));
        Assert.True(await IsDirtyAsync(db));

        await SetCleanAsync(db);
        await db.GeneralRecommendationSettings
            .Where(settings => settings.Id == 1)
            .ExecuteUpdateAsync(setters => setters.SetProperty(settings => settings.MedianPeriodYears, 10));
        Assert.False(await IsDirtyAsync(db));
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
}