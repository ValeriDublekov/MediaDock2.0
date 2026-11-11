using System.Globalization;
using System.Text;
using ICSharpCode.SharpZipLib;
using ICSharpCode.SharpZipLib.GZip;
using MediaDock.Application.Metadata;

namespace MediaDock.Infrastructure.Metadata;

public sealed record ImdbRatingDatasetValue(decimal Rating, long Votes);

public sealed record ImdbRatingDatasetParseResult(
    long RowsScanned,
    long CompressedBytesRead,
    long DecompressedBytesRead,
    IReadOnlyDictionary<string, ImdbRatingDatasetValue> Matches);

public sealed class ImdbRatingSnapshotParser
{
    public async Task<ImdbRatingDatasetParseResult> ParseAsync(
        Stream compressedDataset,
        IReadOnlySet<string> selectedImdbIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(compressedDataset);
        ArgumentNullException.ThrowIfNull(selectedImdbIds);
        if (!compressedDataset.CanRead)
        {
            throw new ArgumentException("IMDb ratings dataset stream must be readable.", nameof(compressedDataset));
        }

        var compressedCounter = new CountingReadStream(compressedDataset);
        using var gzip = new GZipInputStream(compressedCounter) { IsStreamOwner = false };
        var decompressedCounter = new CountingReadStream(gzip);
        using var reader = new StreamReader(
            decompressedCounter,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 16 * 1024,
            leaveOpen: true);

        var headerLine = await ReadLineAsync(reader, cancellationToken);
        if (headerLine is null)
        {
            throw new InvalidDataException("IMDb ratings dataset is empty or has no header row.");
        }

        var headers = headerLine.Split('\t');
        headers[0] = headers[0].TrimStart('\uFEFF');
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < headers.Length; index++)
        {
            if (!columns.TryAdd(headers[index], index))
            {
                throw new InvalidDataException($"IMDb ratings dataset has a duplicate column: {headers[index]}.");
            }
        }

        var tconstColumn = GetRequiredColumn(columns, "tconst");
        var ratingColumn = GetRequiredColumn(columns, "averageRating");
        var votesColumn = GetRequiredColumn(columns, "numVotes");
        var matches = new Dictionary<string, ImdbRatingDatasetValue>(StringComparer.Ordinal);
        string? previousImdbId = null;
        long rowsScanned = 0;
        long lineNumber = 1;

        while (await ReadLineAsync(reader, cancellationToken) is { } line)
        {
            lineNumber++;
            rowsScanned++;
            var fields = line.Split('\t');
            if (fields.Length != headers.Length)
            {
                throw new InvalidDataException(
                    $"IMDb ratings dataset row {lineNumber} has {fields.Length} fields; expected {headers.Length}.");
            }

            var imdbId = fields[tconstColumn].Trim().ToLowerInvariant();
            if (!ImdbIdNormalizer.IsValid(imdbId))
            {
                throw new InvalidDataException($"IMDb ratings dataset row {lineNumber} has an invalid tconst value.");
            }

            if (previousImdbId is not null)
            {
                var order = string.CompareOrdinal(previousImdbId, imdbId);
                if (order == 0)
                {
                    throw new InvalidDataException(
                        $"IMDb ratings dataset contains duplicate ID {imdbId} at row {lineNumber}.");
                }

                if (order > 0)
                {
                    throw new InvalidDataException(
                        $"IMDb ratings dataset is not sorted by tconst at row {lineNumber}.");
                }
            }

            previousImdbId = imdbId;
            if (!decimal.TryParse(
                    fields[ratingColumn].Trim(),
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out var rating)
                || rating is < 0 or > 10)
            {
                throw new InvalidDataException($"IMDb ratings dataset row {lineNumber} has an invalid averageRating.");
            }

            if (!long.TryParse(
                    fields[votesColumn].Trim(),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var votes)
                || votes < 0)
            {
                throw new InvalidDataException($"IMDb ratings dataset row {lineNumber} has an invalid numVotes.");
            }

            if (selectedImdbIds.Contains(imdbId))
            {
                matches.Add(imdbId, new ImdbRatingDatasetValue(rating, votes));
            }
        }

        if (rowsScanned == 0)
        {
            throw new InvalidDataException("IMDb ratings dataset contains no data rows.");
        }

        return new ImdbRatingDatasetParseResult(
            rowsScanned,
            compressedCounter.BytesRead,
            decompressedCounter.BytesRead,
            matches);
    }

    private static int GetRequiredColumn(IReadOnlyDictionary<string, int> columns, string name) =>
        columns.TryGetValue(name, out var index)
            ? index
            : throw new InvalidDataException($"IMDb ratings dataset is missing required column: {name}.");

    private static async Task<string?> ReadLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            return await reader.ReadLineAsync(cancellationToken);
        }
        catch (SharpZipBaseException exception)
        {
            throw new InvalidDataException(
                "IMDb ratings snapshot is not a valid, complete gzip file.",
                exception);
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("IMDb ratings snapshot is not a valid, complete gzip file.", exception);
        }
    }

    private sealed class CountingReadStream(Stream innerStream) : Stream
    {
        public long BytesRead { get; private set; }

        public override bool CanRead => innerStream.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var bytesRead = innerStream.Read(buffer, offset, count);
            BytesRead += bytesRead;
            return bytesRead;
        }

        public override int Read(Span<byte> buffer)
        {
            var bytesRead = innerStream.Read(buffer);
            BytesRead += bytesRead;
            return bytesRead;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var bytesRead = await innerStream.ReadAsync(buffer, cancellationToken);
            BytesRead += bytesRead;
            return bytesRead;
        }

        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            var bytesRead = await innerStream.ReadAsync(buffer, offset, count, cancellationToken);
            BytesRead += bytesRead;
            return bytesRead;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}