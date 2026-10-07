using System.Text;
using MediaDock.Infrastructure.GoldenGlobes;
using MediaDock.Infrastructure.Persistence;
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

        var preservedNomination = await db.GoldenGlobeNominations.SingleAsync(row => row.Title == "A Film, Extended");
        preservedNomination.EnrichmentStatus = "enriched";
        preservedNomination.EnrichmentAttemptCount = 1;
        preservedNomination.ImdbId = "tt12345678";
        preservedNomination.IsImdbIdManual = true;
        preservedNomination.ImdbIdVersion = 2;
        var legacySeriesNomination = await db.GoldenGlobeNominations.SingleAsync(row => row.Title == "Series Title");
        legacySeriesNomination.NomineeType = "movie";
        legacySeriesNomination.EnrichmentStatus = "not_found";
        legacySeriesNomination.EnrichmentAttemptCount = 1;
        legacySeriesNomination.ImdbId = "tt00000001";
        legacySeriesNomination.IsImdbIdManual = true;
        legacySeriesNomination.ImdbIdVersion = 2;
        var autoTypeCorrection = await db.GoldenGlobeNominations.SingleAsync(row => row.Title == "Auto Type Correction");
        autoTypeCorrection.NomineeType = "movie";
        autoTypeCorrection.EnrichmentStatus = "enriched";
        autoTypeCorrection.ImdbId = "tt12345679";
        await db.SaveChangesAsync();

        var repeatedImport = await importer.ImportAsync(Encoding.UTF8.GetBytes(csv));
        var tsvImport = await importer.ImportAsync(Encoding.UTF8.GetBytes(tsv));

        Assert.Equal(0, repeatedImport.AwardsCreated);
        Assert.Equal(0, repeatedImport.NominationsCreated);
        Assert.Equal(0, tsvImport.AwardsCreated);
        Assert.Equal(1, tsvImport.NominationsCreated);
        Assert.Equal(4, await db.GoldenGlobeNominations.CountAsync());
        var preservedAfterReimport = await db.GoldenGlobeNominations.SingleAsync(row => row.Title == "A Film, Extended");
        Assert.Equal("enriched", preservedAfterReimport.EnrichmentStatus);
        Assert.Equal("tt12345678", preservedAfterReimport.ImdbId);
        Assert.True(preservedAfterReimport.IsImdbIdManual);
        Assert.Equal(2, preservedAfterReimport.ImdbIdVersion);
        var reclassifiedSeries = await db.GoldenGlobeNominations.SingleAsync(row => row.Title == "Series Title");
        Assert.Equal("series", reclassifiedSeries.NomineeType);
        Assert.Equal("pending", reclassifiedSeries.EnrichmentStatus);
        Assert.Equal(0, reclassifiedSeries.EnrichmentAttemptCount);
        Assert.Null(reclassifiedSeries.ImdbId);
        Assert.False(reclassifiedSeries.IsImdbIdManual);
        Assert.Equal(3, reclassifiedSeries.ImdbIdVersion);
        var autoTypeCorrected = await db.GoldenGlobeNominations.SingleAsync(row => row.Title == "Auto Type Correction");
        Assert.Equal("movie", autoTypeCorrected.NomineeType);
        Assert.Equal("enriched", autoTypeCorrected.EnrichmentStatus);
        Assert.Equal("tt12345679", autoTypeCorrected.ImdbId);
        Assert.True((await db.GoldenGlobeNominations.SingleAsync(row => row.Title == "TSV Film")).Winner);
    }
}