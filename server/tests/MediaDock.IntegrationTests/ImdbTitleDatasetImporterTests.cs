using System.Text;
using MediaDock.Infrastructure.Metadata;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.IntegrationTests;

[Trait("Category", "Persistence")]
public sealed class ImdbTitleDatasetImporterTests
{
    [Fact]
    public async Task ImportAsyncSeedsNewTitlesAndPreservesExistingCanonicalRows()
    {
        await using var postgres = PostgreSqlTestContainerBuilder.Create("mediadock_imdb_dataset_import_test").Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<MediaDockDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var dbContext = new MediaDockDbContext(options);
        await dbContext.Database.MigrateAsync();
        dbContext.ImdbTitles.Add(new ImdbTitle
        {
            ImdbId = "tt1111111",
            TitleText = "Existing Canonical Title",
            Year = 1999,
            MediaType = "movie",
            ImdbRating = 9.1m,
            ImdbVotes = 100_000,
            MetadataStatus = "enriched",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var tsv = string.Join(
            '\n',
            "tconst\taverageRating\tnumVotes\ttitleType\tprimaryTitle\tstartYear\tgenres",
            "tt1111111\t7.1\t60001\tmovie\tChanged Existing Title\t2001\tDrama",
            "tt2222222\t8.4\t100000\ttvMovie\tTelevision Film\t2012\tDrama",
            "tt2222222\t8.0\t90000\tmovie\tDuplicate Input Title\t2012\tDrama",
            "tt3333333\t8.5\t150000\ttvSeries\tSeries\t2020\tComedy,Drama",
            "tt4444444\t7.8\t80000\ttvMiniSeries\tMini Series\t\\N\tDrama",
            "tt5555555\t8.0\t90000\ttvEpisode\tUnsupported Episode\t2021\tDrama");
        var importer = new ImdbTitleDatasetImporter(dbContext);

        var summary = await importer.ImportAsync(Encoding.UTF8.GetBytes(tsv));

        Assert.Equal(6, summary.RowsRead);
        Assert.Equal(1, summary.RowsSkippedByType);
        Assert.Equal(3, summary.RowsInserted);
        Assert.Equal(2, summary.RowsIgnored);

        var existing = await dbContext.ImdbTitles.AsNoTracking().SingleAsync(title => title.ImdbId == "tt1111111");
        Assert.Equal("Existing Canonical Title", existing.TitleText);
        Assert.Equal(1999, existing.Year);
        Assert.Equal(9.1m, existing.ImdbRating);
        Assert.Equal("enriched", existing.MetadataStatus);

        var imported = await dbContext.ImdbTitles.AsNoTracking().ToDictionaryAsync(title => title.ImdbId);
        Assert.Equal("movie", imported["tt2222222"].MediaType);
        Assert.Equal("series", imported["tt3333333"].MediaType);
        Assert.Equal("series", imported["tt4444444"].MediaType);
        Assert.Equal(8.4m, imported["tt2222222"].ImdbRating);
        Assert.Equal(100_000, imported["tt2222222"].ImdbVotes);
        Assert.Null(imported["tt4444444"].Year);
        Assert.All(
            imported.Values.Where(title => title.ImdbId != "tt1111111"),
            title => Assert.Equal("pending", title.MetadataStatus));

        var repeated = await importer.ImportAsync(Encoding.UTF8.GetBytes(tsv));
        Assert.Equal(0, repeated.RowsInserted);
        Assert.Equal(5, repeated.RowsIgnored);
    }
}