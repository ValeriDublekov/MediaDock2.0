using System.IO.Compression;
using System.Text;
using MediaDock.Infrastructure.Metadata;

namespace MediaDock.UnitTests;

public sealed class ImdbRatingSnapshotParserTests
{
    private readonly ImdbRatingSnapshotParser _parser = new();

    [Fact]
    public async Task ParseAsyncScansSnapshotAndCollectsOnlySelectedIds()
    {
        using var dataset = Compress(
            "tconst\taverageRating\tnumVotes\n"
            + "tt0000001\t5.7\t2239\n"
            + "tt0000002\t5.4\t328\n"
            + "tt0000003\t6.4\t2407\n");

        var result = await _parser.ParseAsync(
            dataset,
            new HashSet<string>(["tt0000002"], StringComparer.Ordinal));

        Assert.Equal(3, result.RowsScanned);
        Assert.Equal(dataset.Length, result.CompressedBytesRead);
        Assert.True(result.DecompressedBytesRead > 0);
        var match = Assert.Single(result.Matches);
        Assert.Equal("tt0000002", match.Key);
        Assert.Equal(new ImdbRatingDatasetValue(5.4m, 328), match.Value);
    }

    [Fact]
    public async Task ParseAsyncReportsMissingRequiredColumn()
    {
        using var dataset = Compress("tconst\taverageRating\n");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => _parser.ParseAsync(dataset, new HashSet<string>(StringComparer.Ordinal)));

        Assert.Contains("missing required column: numVotes", exception.Message);
    }

    [Fact]
    public async Task ParseAsyncRejectsSnapshotWithoutDataRows()
    {
        using var dataset = Compress("tconst\taverageRating\tnumVotes\n");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => _parser.ParseAsync(dataset, new HashSet<string>(StringComparer.Ordinal)));

        Assert.Contains("contains no data rows", exception.Message);
    }

    [Fact]
    public async Task ParseAsyncReportsMalformedRowNumber()
    {
        using var dataset = Compress("tconst\taverageRating\tnumVotes\ntt0000001\t5.7\n");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => _parser.ParseAsync(dataset, new HashSet<string>(StringComparer.Ordinal)));

        Assert.Contains("row 2", exception.Message);
        Assert.Contains("expected 3", exception.Message);
    }

    [Fact]
    public async Task ParseAsyncRejectsDuplicateIds()
    {
        using var dataset = Compress(
            "tconst\taverageRating\tnumVotes\n"
            + "tt0000001\t5.7\t2239\n"
            + "tt0000001\t5.8\t2240\n");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => _parser.ParseAsync(dataset, new HashSet<string>(StringComparer.Ordinal)));

        Assert.Contains("duplicate ID tt0000001 at row 3", exception.Message);
    }

    [Fact]
    public async Task ParseAsyncRejectsOutOfOrderIds()
    {
        using var dataset = Compress(
            "tconst\taverageRating\tnumVotes\n"
            + "tt0000002\t5.7\t2239\n"
            + "tt0000001\t5.8\t2240\n");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => _parser.ParseAsync(dataset, new HashSet<string>(StringComparer.Ordinal)));

        Assert.Contains("not sorted by tconst at row 3", exception.Message);
    }

    [Theory]
    [InlineData("not-a-rating", "5.7", "averageRating")]
    [InlineData("5.7", "not-a-vote-count", "numVotes")]
    public async Task ParseAsyncReportsInvalidNumericValues(string rating, string votes, string field)
    {
        using var dataset = Compress($"tconst\taverageRating\tnumVotes\ntt0000001\t{rating}\t{votes}\n");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => _parser.ParseAsync(dataset, new HashSet<string>(StringComparer.Ordinal)));

        Assert.Contains($"row 2 has an invalid {field}", exception.Message);
    }

    [Fact]
    public async Task ParseAsyncReportsInvalidGzipTrailer()
    {
        using var validDataset = Compress("tconst\taverageRating\tnumVotes\n");
        var corruptedBytes = validDataset.ToArray();
        corruptedBytes[^1] ^= 0xFF;
        using var corruptedDataset = new MemoryStream(corruptedBytes);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => _parser.ParseAsync(corruptedDataset, new HashSet<string>(StringComparer.Ordinal)));

        Assert.Contains("valid, complete gzip file", exception.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(12)]
    public async Task ParseAsyncRejectsTruncatedGzip(int bytesRemoved)
    {
        using var dataset = Compress("tconst\taverageRating\tnumVotes\ntt0000001\t8.1\t600\n");
        var bytes = dataset.ToArray();
        using var truncated = new MemoryStream(bytes, 0, bytes.Length - bytesRemoved);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => _parser.ParseAsync(truncated, new HashSet<string>(["tt0000001"], StringComparer.Ordinal)));
    }

    private static MemoryStream Compress(string contents)
    {
        var stream = new MemoryStream();
        using (var gzip = new GZipStream(stream, CompressionLevel.Optimal, leaveOpen: true))
        using (var writer = new StreamWriter(gzip, new UTF8Encoding(false), 1024, leaveOpen: true))
        {
            writer.Write(contents);
        }

        stream.Position = 0;
        return stream;
    }
}