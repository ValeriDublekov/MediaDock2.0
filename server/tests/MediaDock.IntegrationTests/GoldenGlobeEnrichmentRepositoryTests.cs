using MediaDock.Application.GoldenGlobes;
using MediaDock.Application.Metadata;
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
        var movieGroup = CreateGroup("Grouped Film", 2025);
        var seriesGroup = CreateGroup("Grouped Film", 2025, nomineeType: "series");
        var otherYearGroup = CreateGroup("Grouped Film", 2024);
        db.GoldenGlobeNominations.AddRange(
            CreateNomination("group-a", movieGroup, award),
            CreateNomination("group-b", movieGroup, award),
            CreateNomination("group-series", seriesGroup, award),
            CreateNomination("other-year", otherYearGroup, award));
        await db.SaveChangesAsync();

        var attemptedAt = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        await new PostgresGoldenGlobeEnrichmentRepository(db).SaveOutcomeAsync(
            "Grouped Film",
            2025,
            "movie",
            new GoldenGlobeEnrichmentUpdate(
                "enriched", 2, attemptedAt, null, null, "TT12345678",
                Metadata: new MetadataDetails(
                    "Canonical Grouped Film", 2024, "tt12345678", "movie", "movie", "standard", null,
                    8.4m, 12345, 81m, ["Drama"], ["United States"], "Director", "Verified plot.",
                    "https://example.test/grouped.jpg", "101 min", "1 win", "$10")));

        var updatedNominations = await db.GoldenGlobeNominations.AsNoTracking()
            .Include(row => row.GoldenGlobeTitle)
            .Where(row => row.GoldenGlobeTitle!.Title == "Grouped Film" && row.GoldenGlobeTitle.Year == 2025)
            .ToListAsync();
        Assert.Equal(3, updatedNominations.Count);
        Assert.All(updatedNominations.Where(row => row.GoldenGlobeTitle!.NomineeType == "movie"), row =>
        {
            Assert.Equal("enriched", row.GoldenGlobeTitle!.EnrichmentStatus);
            Assert.Equal(2, row.GoldenGlobeTitle.EnrichmentAttemptCount);
            Assert.Equal(attemptedAt, row.GoldenGlobeTitle.LastEnrichmentAttemptAt);
            Assert.Equal("tt12345678", row.GoldenGlobeTitle.ImdbId);
        });
        var seriesNomination = updatedNominations.Single(row => row.GoldenGlobeTitle!.NomineeType == "series");
        Assert.Equal("pending", seriesNomination.GoldenGlobeTitle!.EnrichmentStatus);
        Assert.Equal(0, seriesNomination.GoldenGlobeTitle.EnrichmentAttemptCount);

        var otherYear = await db.GoldenGlobeTitles.AsNoTracking()
            .SingleAsync(row => row.Title == "Grouped Film" && row.Year == 2024);
        Assert.Equal("pending", otherYear.EnrichmentStatus);
        Assert.Equal(0, otherYear.EnrichmentAttemptCount);
        Assert.Null(otherYear.ImdbId);

        var canonicalMetadata = await db.ImdbTitles.AsNoTracking().SingleAsync(row => row.ImdbId == "tt12345678");
        Assert.Equal("Canonical Grouped Film", canonicalMetadata.TitleText);
        Assert.Equal(2024, canonicalMetadata.Year);
        Assert.Equal(8.4m, canonicalMetadata.ImdbRating);
        Assert.Equal(12345, canonicalMetadata.ImdbVotes);
        Assert.Equal(81m, canonicalMetadata.Metascore);
        Assert.Equal(new[] { "Drama" }, canonicalMetadata.Genres);
        Assert.Equal(new[] { "United States" }, canonicalMetadata.Countries);
        Assert.Equal("Director", canonicalMetadata.Director);
        Assert.Equal("Verified plot.", canonicalMetadata.Plot);
        Assert.Equal("https://example.test/grouped.jpg", canonicalMetadata.PosterUrl);
        Assert.Equal("101 min", canonicalMetadata.Runtime);
        Assert.Equal("1 win", canonicalMetadata.Awards);
        Assert.Equal("$10", canonicalMetadata.BoxOffice);
        Assert.Equal("enriched", canonicalMetadata.MetadataStatus);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new PostgresGoldenGlobeEnrichmentRepository(db).SaveOutcomeAsync(
            "Grouped Film",
            2025,
            "movie",
            new GoldenGlobeEnrichmentUpdate("enriched", 3, attemptedAt, null, null, null)));
    }

    [Fact]
    public async Task SaveOutcomeCorrectsTheTypeForTheMatchedNominationGroup()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_golden_globe_type_correction_test").Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var db = new MediaDockDbContext(options);
        await db.Database.MigrateAsync();

        var award = new GoldenGlobeAward { Name = "Television Motion Picture" };
        var matchedGroup = CreateGroup("Too Big To Fail", 2012, "not_found", 1, nomineeType: "series");
        var otherYearGroup = CreateGroup("Too Big To Fail", 2011, "not_found", 1, nomineeType: "series");
        db.GoldenGlobeNominations.AddRange(
            CreateNomination("type-a", matchedGroup, award),
            CreateNomination("type-b", matchedGroup, award),
            CreateNomination("other-year", otherYearGroup, award));
        await db.SaveChangesAsync();

        var attemptedAt = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        await new PostgresGoldenGlobeEnrichmentRepository(db).SaveOutcomeAsync(
            "Too Big To Fail",
            2012,
            "series",
            new GoldenGlobeEnrichmentUpdate("enriched", 3, attemptedAt, null, null, "tt1742683", "movie"));

        var nominations = await db.GoldenGlobeNominations.AsNoTracking()
            .Include(row => row.GoldenGlobeTitle)
            .Where(row => row.GoldenGlobeTitle!.Title == "Too Big To Fail")
            .OrderBy(row => row.GoldenGlobeTitle!.Year)
            .ThenBy(row => row.ImportKey)
            .ToListAsync();
        Assert.All(nominations.Where(row => row.GoldenGlobeTitle!.Year == 2012), row =>
        {
            Assert.Equal("movie", row.GoldenGlobeTitle!.NomineeType);
            Assert.Equal("enriched", row.GoldenGlobeTitle.EnrichmentStatus);
            Assert.Equal("tt1742683", row.GoldenGlobeTitle.ImdbId);
        });
        var otherYear = Assert.Single(nominations, row => row.GoldenGlobeTitle!.Year == 2011);
        Assert.Equal("series", otherYear.GoldenGlobeTitle!.NomineeType);
        Assert.Equal("not_found", otherYear.GoldenGlobeTitle.EnrichmentStatus);
        Assert.Null(otherYear.GoldenGlobeTitle.ImdbId);
    }

    [Fact]
    public async Task SaveOutcomeMergesIntoExistingManualTargetWithoutReplacingItsLink()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_golden_globe_manual_type_merge_test").Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var db = new MediaDockDbContext(options);
        await db.Database.MigrateAsync();

        var award = new GoldenGlobeAward { Name = "Manual Type Merge Award" };
        var sourceGroup = CreateGroup("Type Merge Film", 2012, "not_found", 1, nomineeType: "series");
        var targetGroup = CreateGroup("Type Merge Film", 2012);
        targetGroup.ImdbId = "tt12345678";
        targetGroup.IsImdbIdManual = true;
        targetGroup.ImdbIdVersion = 5;
        db.ImdbTitles.Add(new ImdbTitle { ImdbId = "tt12345678", UpdatedAt = DateTimeOffset.UtcNow });
        db.GoldenGlobeNominations.AddRange(
            CreateNomination("type-merge-series", sourceGroup, award),
            CreateNomination("type-merge-movie", targetGroup, award));
        await db.SaveChangesAsync();

        await new PostgresGoldenGlobeEnrichmentRepository(db).SaveOutcomeAsync(
            "Type Merge Film",
            2012,
            "series",
            new GoldenGlobeEnrichmentUpdate("enriched", 2, DateTimeOffset.UtcNow, null, null, "tt87654321", "movie"));

        var groups = await db.GoldenGlobeTitles.AsNoTracking()
            .Include(group => group.Nominations)
            .Where(group => group.Title == "Type Merge Film" && group.Year == 2012)
            .ToListAsync();
        var mergedGroup = Assert.Single(groups);
        Assert.Equal("movie", mergedGroup.NomineeType);
        Assert.Equal("tt12345678", mergedGroup.ImdbId);
        Assert.True(mergedGroup.IsImdbIdManual);
        Assert.Equal(5, mergedGroup.ImdbIdVersion);
        Assert.Equal("pending", mergedGroup.EnrichmentStatus);
        Assert.Equal(2, mergedGroup.Nominations.Count);
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
        var healthyEnriched = CreateGroup("Healthy Enriched Film", 2027, "enriched", 4);
        healthyEnriched.ImdbId = "tt12345678";
        var pinnedProblem = CreateGroup("Pinned Problem", 2026, "problem", 3);
        pinnedProblem.ImdbId = "tt87654321";
        pinnedProblem.IsImdbIdManual = true;
        var manualPendingWithMetadata = CreateGroup("Manual Pending Canonical Row", 2026);
        manualPendingWithMetadata.ImdbId = "tt77777771";
        manualPendingWithMetadata.IsImdbIdManual = true;
        manualPendingWithMetadata.ImdbIdVersion = 1;
        var manualEnrichedWithPendingMetadata = CreateGroup("A Manual Enriched Pending Metadata", 2026, "enriched", 1);
        manualEnrichedWithPendingMetadata.ImdbId = "tt77777773";
        manualEnrichedWithPendingMetadata.IsImdbIdManual = true;
        manualEnrichedWithPendingMetadata.ImdbIdVersion = 3;
        var manualDueTemporaryError = CreateGroup("Manual Due Retry", 2026, "temporary_error", 2, now);
        manualDueTemporaryError.ImdbId = "tt77777774";
        manualDueTemporaryError.IsImdbIdManual = true;
        manualDueTemporaryError.ImdbIdVersion = 4;
        var dueTemporaryError = CreateGroup("A New Film", 2026, "temporary_error", 3, now);
        db.ImdbTitles.AddRange(
            new ImdbTitle { ImdbId = "tt12345678", UpdatedAt = now },
            new ImdbTitle { ImdbId = "tt87654321", UpdatedAt = now },
            new ImdbTitle { ImdbId = "tt77777771", MetadataStatus = "pending", UpdatedAt = now },
            new ImdbTitle { ImdbId = "tt77777773", MetadataStatus = "pending", UpdatedAt = now },
            new ImdbTitle { ImdbId = "tt77777774", MetadataStatus = "temporary_error", UpdatedAt = now });
        db.GoldenGlobeNominations.AddRange(
            CreateNomination("new-a-due", dueTemporaryError, award),
            CreateNomination("manual-pending-with-metadata", manualPendingWithMetadata, award),
            CreateNomination("manual-enriched-with-pending-metadata", manualEnrichedWithPendingMetadata, award),
            CreateNomination("manual-due-temporary-error", manualDueTemporaryError, award),
            CreateNomination("new-a-series", CreateGroup("A New Film", 2026, nomineeType: "series"), award),
            CreateNomination("new-series", CreateGroup("A New Series", 2026, nomineeType: "series"), award),
            CreateNomination("new-z-pending", CreateGroup("Z New Film", 2026, "pending", 4), award),
            CreateNomination("older-retry", CreateGroup("Older Film", 2025, "temporary_error", 2), award),
            CreateNomination("old-not-found", CreateGroup("Old Not Found", 2026, "not_found", 5), award),
            CreateNomination("old-problem", CreateGroup("Old Problem", 2026, "problem", 6), award),
            CreateNomination("pinned-problem", pinnedProblem, award),
            CreateNomination("broken-enriched", CreateGroup("Enriched Film", 2027, "enriched", 8), award),
            CreateNomination("healthy-enriched", healthyEnriched, award));
        await db.SaveChangesAsync();

        var candidates = await new PostgresGoldenGlobeEnrichmentRepository(db)
            .GetEligibleCandidatesAsync(now);

        Assert.Equal(
            [
                new GoldenGlobeEnrichmentCandidate("Enriched Film", 2027, 8),
                new GoldenGlobeEnrichmentCandidate("A Manual Enriched Pending Metadata", 2026, 1, "movie", "tt77777773", 3),
                new GoldenGlobeEnrichmentCandidate("A New Film", 2026, 3),
                new GoldenGlobeEnrichmentCandidate("A New Film", 2026, 0, "series"),
                new GoldenGlobeEnrichmentCandidate("A New Series", 2026, 0, "series"),
                new GoldenGlobeEnrichmentCandidate("Manual Due Retry", 2026, 2, "movie", "tt77777774", 4),
                new GoldenGlobeEnrichmentCandidate("Manual Pending Canonical Row", 2026, 0, "movie", "tt77777771", 1),
                new GoldenGlobeEnrichmentCandidate("Old Not Found", 2026, 5),
                new GoldenGlobeEnrichmentCandidate("Old Problem", 2026, 6),
                new GoldenGlobeEnrichmentCandidate("Z New Film", 2026, 4),
                new GoldenGlobeEnrichmentCandidate("Older Film", 2025, 2)
            ],
            candidates);
    }

    [Fact]
    public async Task ManualLinkVersionPreventsStaleAndAutomaticOutcomesFromReplacingTheSelection()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_golden_globe_manual_version_test").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<MediaDockDbContext>().UseNpgsql(postgres.GetConnectionString()).Options;
        await using var db = new MediaDockDbContext(options);
        await db.Database.MigrateAsync();
        var award = new GoldenGlobeAward { Name = "Version Guard Award" };
        var group = CreateGroup("Versioned Film", 2025);
        group.ImdbId = "tt12345678";
        group.IsImdbIdManual = true;
        group.ImdbIdVersion = 3;
        db.ImdbTitles.Add(new ImdbTitle { ImdbId = "tt12345678", UpdatedAt = DateTimeOffset.UtcNow });
        db.GoldenGlobeNominations.Add(CreateNomination("versioned", group, award));
        await db.SaveChangesAsync();

        var repository = new PostgresGoldenGlobeEnrichmentRepository(db);
        var attemptedAt = DateTimeOffset.UtcNow;
        var staleCandidate = new GoldenGlobeManualRefreshCandidate("Versioned Film", 2025, "movie", "tt12345678", 1, 0);
        var applied = await repository.SaveManualOutcomeAsync(staleCandidate,
            new GoldenGlobeEnrichmentUpdate("enriched", 1, attemptedAt, null, null, "tt12345678"));
        await repository.SaveOutcomeAsync("Versioned Film", 2025, "movie",
            new GoldenGlobeEnrichmentUpdate("enriched", 1, attemptedAt, null, null, "tt87654321"));

        Assert.False(applied);
        var stored = await db.GoldenGlobeTitles.AsNoTracking().SingleAsync();
        Assert.Equal("tt12345678", stored.ImdbId);
        Assert.True(stored.IsImdbIdManual);
        Assert.Equal(3, stored.ImdbIdVersion);
        Assert.Equal("pending", stored.EnrichmentStatus);

        var verifiedMetadata = new MetadataDetails(
            "Versioned Film", 2024, "tt12345678", "movie", "movie", "standard", null,
            8.1m, 1000, null, [], [], null, null, "https://example.test/versioned.jpg", null, null, null);
        var currentCandidate = staleCandidate with { LinkVersion = 3 };
        var currentApplied = await repository.SaveManualOutcomeAsync(currentCandidate,
            new GoldenGlobeEnrichmentUpdate("enriched", 2, attemptedAt, null, null, "tt12345678", Metadata: verifiedMetadata));

        Assert.True(currentApplied);
        var updatedCanonicalMetadata = await db.ImdbTitles.AsNoTracking().SingleAsync(row => row.ImdbId == "tt12345678");
        Assert.Equal(8.1m, updatedCanonicalMetadata.ImdbRating);
        Assert.Equal("https://example.test/versioned.jpg", updatedCanonicalMetadata.PosterUrl);
        var enrichedGroup = await db.GoldenGlobeTitles.AsNoTracking().SingleAsync();
        Assert.Equal("enriched", enrichedGroup.EnrichmentStatus);
    }

    private static GoldenGlobeNomination CreateNomination(
        string importKey,
        GoldenGlobeTitle group,
        GoldenGlobeAward award) => new()
    {
        ImportKey = importKey,
        GoldenGlobeTitle = group,
        Award = award
    };

    private static GoldenGlobeTitle CreateGroup(
        string title,
        int year,
        string status = "pending",
        int attemptCount = 0,
        DateTimeOffset? nextAttemptAt = null,
        string nomineeType = "movie") => new()
    {
        Title = title,
        Year = year,
        EnrichmentStatus = status,
        EnrichmentAttemptCount = attemptCount,
        NextEnrichmentAttemptAt = nextAttemptAt,
        NomineeType = nomineeType
    };
}