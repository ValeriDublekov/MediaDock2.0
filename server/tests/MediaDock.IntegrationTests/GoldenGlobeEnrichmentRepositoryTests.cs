using MediaDock.Application.GoldenGlobes;
using MediaDock.Infrastructure.GoldenGlobes;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.IntegrationTests;

[Trait("Category", "Persistence")]
public sealed class GoldenGlobeEnrichmentRepositoryTests
{
    [Fact]
    public async Task SaveOutcomeUpdatesEveryNominationForOnlyTheSelectedFilmAndYear()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_golden_globe_enrichment_save_test").Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var db = new MediaDockDbContext(options);
        await db.Database.MigrateAsync();

        var award = new GoldenGlobeAward { Name = "Best Picture" };
        db.GoldenGlobeNominations.AddRange(
            CreateNomination("group-a", "Grouped Film", 2025, "pending", 0, null, award),
            CreateNomination("group-b", "Grouped Film", 2025, "temporary_error", 1, null, award),
            CreateNomination("group-series", "Grouped Film", 2025, "pending", 0, null, award, "series"),
            CreateNomination("other-year", "Grouped Film", 2024, "pending", 0, null, award));
        await db.SaveChangesAsync();

        var attemptedAt = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        await new PostgresGoldenGlobeEnrichmentRepository(db).SaveOutcomeAsync(
            "Grouped Film",
            2025,
            "movie",
            new GoldenGlobeEnrichmentUpdate("enriched", 2, attemptedAt, null, null, "tt12345678"));

        var updatedNominations = await db.GoldenGlobeNominations.AsNoTracking()
            .Where(row => row.Title == "Grouped Film" && row.Year == 2025)
            .ToListAsync();
        Assert.Equal(3, updatedNominations.Count);
        Assert.All(updatedNominations.Where(row => row.NomineeType == "movie"), row =>
        {
            Assert.Equal("enriched", row.EnrichmentStatus);
            Assert.Equal(2, row.EnrichmentAttemptCount);
            Assert.Equal(attemptedAt, row.LastEnrichmentAttemptAt);
            Assert.Equal("tt12345678", row.ImdbId);
        });
        var seriesNomination = updatedNominations.Single(row => row.NomineeType == "series");
        Assert.Equal("pending", seriesNomination.EnrichmentStatus);
        Assert.Equal(0, seriesNomination.EnrichmentAttemptCount);

        var otherYear = await db.GoldenGlobeNominations.AsNoTracking()
            .SingleAsync(row => row.Title == "Grouped Film" && row.Year == 2024);
        Assert.Equal("pending", otherYear.EnrichmentStatus);
        Assert.Equal(0, otherYear.EnrichmentAttemptCount);
        Assert.Null(otherYear.ImdbId);
    }

    [Fact]
    public async Task GetEligibleCandidatesGroupsEligibleNominationsAndOrdersNewestFirst()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_golden_globe_enrichment_test").Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var db = new MediaDockDbContext(options);
        await db.Database.MigrateAsync();

        var now = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        var award = new GoldenGlobeAward { Name = "Best Motion Picture" };
        db.GoldenGlobeNominations.AddRange(
            CreateNomination("new-a-pending", "A New Film", 2026, "pending", 1, now.AddHours(1), award),
            CreateNomination("new-a-due", "A New Film", 2026, "temporary_error", 3, now, award),
            CreateNomination("new-a-future", "A New Film", 2026, "temporary_error", 10, now.AddHours(1), award),
            CreateNomination("new-series", "A New Series", 2026, "pending", 0, null, award, "series"),
            CreateNomination("new-z-pending", "Z New Film", 2026, "pending", 4, null, award),
            CreateNomination("older-retry", "Older Film", 2025, "temporary_error", 2, null, award),
            CreateNomination("already-enriched", "Enriched Film", 2027, "enriched", 8, null, award));
        await db.SaveChangesAsync();

        var candidates = await new PostgresGoldenGlobeEnrichmentRepository(db)
            .GetEligibleCandidatesAsync(now);

        Assert.Equal(
            [
                new GoldenGlobeEnrichmentCandidate("A New Film", 2026, 3),
                new GoldenGlobeEnrichmentCandidate("A New Series", 2026, 0, "series"),
                new GoldenGlobeEnrichmentCandidate("Z New Film", 2026, 4),
                new GoldenGlobeEnrichmentCandidate("Older Film", 2025, 2)
            ],
            candidates);
    }

    private static GoldenGlobeNomination CreateNomination(
        string importKey,
        string title,
        int year,
        string status,
        int attemptCount,
        DateTimeOffset? nextAttemptAt,
        GoldenGlobeAward award,
        string nomineeType = "movie") => new()
    {
        ImportKey = importKey,
        Title = title,
        Year = year,
        EnrichmentStatus = status,
        EnrichmentAttemptCount = attemptCount,
        NextEnrichmentAttemptAt = nextAttemptAt,
        NomineeType = nomineeType,
        Award = award
    };
}