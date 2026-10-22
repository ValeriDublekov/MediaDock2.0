using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.IntegrationTests;

[Trait("Category", "Persistence")]
public sealed class RecommendationVoteInvalidationTests
{
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