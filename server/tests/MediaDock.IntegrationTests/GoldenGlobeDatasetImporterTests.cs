using System.Text;
using MediaDock.Infrastructure.GoldenGlobes;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.IntegrationTests;

[Trait("Category", "Persistence")]
public sealed class GoldenGlobeDatasetImporterTests
{
    [Fact]
    public async Task ImportAcceptsCsvAndTsvFiltersRowsAndIsIdempotent()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_golden_globe_import_test").Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var db = new MediaDockDbContext(options);
        await db.Database.MigrateAsync();

        const string csv = """
            nominee_type,year,winner,award,title
            film,2025,true,Best Motion Picture,"A Film, Extended"
            tv-show,2024,false,Best Television Series,Series Title
            tv-show,2023,false,Best Television Series,Auto Type Correction
            person,2025,true,Best Actor,Ignored Person
            film,1980,true,Best Motion Picture,Too Old
            film,2025,false,Best Actor,
            """;
        const string tsv = """
            nominee_type	year	winner	award	title
            movie	2026	1	Best Motion Picture	TSV Film
            """;
        var importer = new GoldenGlobeDatasetImporter(db);

        var firstImport = await importer.ImportAsync(Encoding.UTF8.GetBytes(csv));

        Assert.Equal(6, firstImport.RowsRead);
        Assert.Equal(1, firstImport.RowsSkippedByYear);
        Assert.Equal(1, firstImport.RowsSkippedByType);
        Assert.Equal(1, firstImport.RowsSkippedWithoutTitle);
        Assert.Equal(2, firstImport.AwardsCreated);
        Assert.Equal(3, firstImport.NominationsCreated);

        var preservedNomination = await db.GoldenGlobeNominations.Include(row => row.GoldenGlobeTitle)
            .SingleAsync(row => row.GoldenGlobeTitle!.Title == "A Film, Extended");
        var preservedGroup = preservedNomination.GoldenGlobeTitle!;
        preservedGroup.EnrichmentStatus = "enriched";
        preservedGroup.EnrichmentAttemptCount = 1;
        preservedGroup.ImdbId = "tt12345678";
        preservedGroup.IsImdbIdManual = true;
        preservedGroup.ImdbIdVersion = 2;
        var legacySeriesNomination = await db.GoldenGlobeNominations.Include(row => row.GoldenGlobeTitle)
            .SingleAsync(row => row.GoldenGlobeTitle!.Title == "Series Title");
        legacySeriesNomination.GoldenGlobeTitle!.NomineeType = "movie";
        legacySeriesNomination.GoldenGlobeTitle.EnrichmentStatus = "not_found";
        legacySeriesNomination.GoldenGlobeTitle.EnrichmentAttemptCount = 1;
        legacySeriesNomination.GoldenGlobeTitle.ImdbId = "tt00000001";
        legacySeriesNomination.GoldenGlobeTitle.IsImdbIdManual = true;
        legacySeriesNomination.GoldenGlobeTitle.ImdbIdVersion = 2;
        var autoTypeCorrection = await db.GoldenGlobeNominations.Include(row => row.GoldenGlobeTitle)
            .SingleAsync(row => row.GoldenGlobeTitle!.Title == "Auto Type Correction");
        autoTypeCorrection.GoldenGlobeTitle!.NomineeType = "movie";
        autoTypeCorrection.GoldenGlobeTitle.EnrichmentStatus = "enriched";
        autoTypeCorrection.GoldenGlobeTitle.ImdbId = "tt12345679";
        db.ImdbTitles.AddRange(
            new ImdbTitle { ImdbId = "tt12345678", UpdatedAt = DateTimeOffset.UtcNow },
            new ImdbTitle { ImdbId = "tt00000001", UpdatedAt = DateTimeOffset.UtcNow },
            new ImdbTitle { ImdbId = "tt12345679", UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var repeatedImport = await importer.ImportAsync(Encoding.UTF8.GetBytes(csv));
        var tsvImport = await importer.ImportAsync(Encoding.UTF8.GetBytes(tsv));

        Assert.Equal(0, repeatedImport.AwardsCreated);
        Assert.Equal(0, repeatedImport.NominationsCreated);
        Assert.Equal(0, tsvImport.AwardsCreated);
        Assert.Equal(1, tsvImport.NominationsCreated);
        Assert.Equal(4, await db.GoldenGlobeNominations.CountAsync());
        var preservedAfterReimport = await db.GoldenGlobeTitles.SingleAsync(row => row.Title == "A Film, Extended");
        Assert.Equal("enriched", preservedAfterReimport.EnrichmentStatus);
        Assert.Equal("tt12345678", preservedAfterReimport.ImdbId);
        Assert.True(preservedAfterReimport.IsImdbIdManual);
        Assert.Equal(2, preservedAfterReimport.ImdbIdVersion);
        var reclassifiedSeries = await db.GoldenGlobeTitles.SingleAsync(row => row.Title == "Series Title");
        Assert.Equal("series", reclassifiedSeries.NomineeType);
        Assert.Equal("pending", reclassifiedSeries.EnrichmentStatus);
        Assert.Equal(0, reclassifiedSeries.EnrichmentAttemptCount);
        Assert.Null(reclassifiedSeries.ImdbId);
        Assert.False(reclassifiedSeries.IsImdbIdManual);
        Assert.Equal(3, reclassifiedSeries.ImdbIdVersion);
        var autoTypeCorrected = await db.GoldenGlobeTitles.SingleAsync(row => row.Title == "Auto Type Correction");
        Assert.Equal("movie", autoTypeCorrected.NomineeType);
        Assert.Equal("enriched", autoTypeCorrected.EnrichmentStatus);
        Assert.Equal("tt12345679", autoTypeCorrected.ImdbId);
        Assert.True((await db.GoldenGlobeNominations.Include(row => row.GoldenGlobeTitle)
            .SingleAsync(row => row.GoldenGlobeTitle!.Title == "TSV Film")).Winner);
    }

    [Fact]
    public async Task ImportAddsNominationToEnrichedGroupWithoutResettingSharedState()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_golden_globe_import_enriched_group_test").Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var db = new MediaDockDbContext(options);
        await db.Database.MigrateAsync();

        const string initialCsv = """
            nominee_type,year,winner,award,title
            film,2025,true,Best Motion Picture,Shared Group Film
            """;
        const string updatedCsv = """
            nominee_type,year,winner,award,title
            film,2025,true,Best Motion Picture,Shared Group Film
            film,2025,false,Best Director,Shared Group Film
            """;
        var importer = new GoldenGlobeDatasetImporter(db);
        await importer.ImportAsync(Encoding.UTF8.GetBytes(initialCsv));

        var group = await db.GoldenGlobeTitles.SingleAsync(row => row.Title == "Shared Group Film");
        group.ImdbId = "tt12345678";
        group.IsImdbIdManual = true;
        group.ImdbIdVersion = 4;
        group.EnrichmentStatus = "enriched";
        group.EnrichmentAttemptCount = 3;
        group.LastEnrichmentError = null;
        var updatedAt = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        db.ImdbTitles.Add(new ImdbTitle
        {
            ImdbId = "tt12345678",
            TitleText = "Shared Group Film (canonical)",
            ImdbRating = 8.3m,
            PosterUrl = "https://example.test/shared-group.jpg",
            MetadataStatus = "enriched",
            UpdatedAt = updatedAt
        });
        await db.SaveChangesAsync();

        var import = await importer.ImportAsync(Encoding.UTF8.GetBytes(updatedCsv));

        Assert.Equal(1, import.NominationsCreated);
        var nominations = await db.GoldenGlobeNominations.AsNoTracking()
            .Include(row => row.GoldenGlobeTitle)
            .Include(row => row.Award)
            .Where(row => row.GoldenGlobeTitle!.Title == "Shared Group Film")
            .ToListAsync();
        Assert.Equal(2, nominations.Count);
        Assert.Single(nominations.Select(row => row.GoldenGlobeTitleId).Distinct());

        var storedGroup = await db.GoldenGlobeTitles.AsNoTracking().SingleAsync(row => row.Title == "Shared Group Film");
        Assert.Equal("tt12345678", storedGroup.ImdbId);
        Assert.True(storedGroup.IsImdbIdManual);
        Assert.Equal(4, storedGroup.ImdbIdVersion);
        Assert.Equal("enriched", storedGroup.EnrichmentStatus);
        Assert.Equal(3, storedGroup.EnrichmentAttemptCount);
        var metadata = await db.ImdbTitles.AsNoTracking().SingleAsync(row => row.ImdbId == "tt12345678");
        Assert.Equal("Shared Group Film (canonical)", metadata.TitleText);
        Assert.Equal(8.3m, metadata.ImdbRating);
        Assert.Equal("https://example.test/shared-group.jpg", metadata.PosterUrl);
    }

    [Fact]
    public async Task TypeCorrectionReusesExistingTargetGroupAndPreservesItsManualLink()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_golden_globe_import_type_merge_test").Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var db = new MediaDockDbContext(options);
        await db.Database.MigrateAsync();

        const string initialCsv = """
            nominee_type,year,winner,award,title
            film,2025,true,Best Motion Picture,Type Shared Film
            series,2025,false,Best Television Series,Type Shared Film
            """;
        const string correctedCsv = """
            nominee_type,year,winner,award,title
            series,2025,true,Best Motion Picture,Type Shared Film
            series,2025,false,Best Television Series,Type Shared Film
            """;
        var importer = new GoldenGlobeDatasetImporter(db);
        await importer.ImportAsync(Encoding.UTF8.GetBytes(initialCsv));

        var targetGroup = await db.GoldenGlobeTitles.SingleAsync(row => row.Title == "Type Shared Film" && row.NomineeType == "series");
        targetGroup.ImdbId = "tt22334455";
        targetGroup.IsImdbIdManual = true;
        targetGroup.ImdbIdVersion = 7;
        targetGroup.EnrichmentStatus = "enriched";
        db.ImdbTitles.Add(new ImdbTitle { ImdbId = "tt22334455", UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var import = await importer.ImportAsync(Encoding.UTF8.GetBytes(correctedCsv));

        Assert.Equal(0, import.NominationsCreated);
        var groups = await db.GoldenGlobeTitles.AsNoTracking()
            .Include(group => group.Nominations)
            .Where(group => group.Title == "Type Shared Film" && group.Year == 2025)
            .ToListAsync();
        var mergedGroup = Assert.Single(groups);
        Assert.Equal("series", mergedGroup.NomineeType);
        Assert.Equal("tt22334455", mergedGroup.ImdbId);
        Assert.True(mergedGroup.IsImdbIdManual);
        Assert.Equal(7, mergedGroup.ImdbIdVersion);
        Assert.Equal("enriched", mergedGroup.EnrichmentStatus);
        Assert.Equal(2, mergedGroup.Nominations.Count);
    }
}