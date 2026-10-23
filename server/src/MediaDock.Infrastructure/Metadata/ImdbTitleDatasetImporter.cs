using System.Globalization;
using System.Text;
using MediaDock.Application.Metadata;
using MediaDock.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;

namespace MediaDock.Infrastructure.Metadata;

public sealed record ImdbTitleImportSummary(
    int RowsRead,
    int RowsSkippedByType,
    int RowsInserted,
    int RowsIgnored);

internal sealed record ImdbTitleSeed(
    string ImdbId,
    string Title,
    int? Year,
    string MediaType,
    decimal Rating,
    long Votes);

public sealed class ImdbTitleDatasetImporter(MediaDockDbContext dbContext)
{
    private const int BatchSize = 500;

    private static readonly IReadOnlyDictionary<string, string> MediaTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["movie"] = "movie",
            ["tvMovie"] = "movie",
            ["tvSeries"] = "series",
            ["tvMiniSeries"] = "series"
        };

    public async Task<ImdbTitleImportSummary> ImportAsync(
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        if (content.Length == 0)
        {
            throw new InvalidDataException("IMDb title dataset is empty.");
        }

        using var parser = new TextFieldParser(
            new MemoryStream(content, writable: false),
            Encoding.UTF8,
            detectEncoding: true)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters("\t");

        var rawHeaders = parser.ReadFields()
            ?? throw new InvalidDataException("IMDb title dataset has no header row.");
        var headers = rawHeaders.Select(value => value.Trim().TrimStart('\uFEFF')).ToArray();
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < headers.Length; index++)
        {
            if (!columns.TryAdd(headers[index], index))
            {
                throw new InvalidDataException($"IMDb title dataset has a duplicate column: {headers[index]}.");
            }
        }

        foreach (var required in new[]
        {
            "tconst", "averageRating", "numVotes", "titleType", "primaryTitle", "startYear"
        })
        {
            if (!columns.ContainsKey(required))
            {
                throw new InvalidDataException($"IMDb title dataset is missing required column: {required}.");
            }
        }

        var rows = new List<ImdbTitleSeed>();
        var rowsRead = 0;
        var rowsSkippedByType = 0;
        while (!parser.EndOfData)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fields = parser.ReadFields();
            if (fields is null)
            {
                continue;
            }

            rowsRead++;
            if (fields.Length != headers.Length)
            {
                throw new InvalidDataException(
                    $"IMDb title dataset row {parser.LineNumber} has an invalid field count.");
            }

            string Field(string name) => fields[columns[name]].Trim();

            if (!MediaTypes.TryGetValue(Field("titleType"), out var mediaType))
            {
                rowsSkippedByType++;
                continue;
            }

            var imdbId = ImdbIdNormalizer.Normalize(Field("tconst"));
            if (!ImdbIdNormalizer.IsValid(imdbId))
            {
                throw new InvalidDataException($"IMDb title dataset row {parser.LineNumber} has an invalid IMDb ID.");
            }

            var title = Field("primaryTitle");
            if (string.IsNullOrWhiteSpace(title))
            {
                throw new InvalidDataException($"IMDb title dataset row {parser.LineNumber} has no title.");
            }

            if (!decimal.TryParse(
                    Field("averageRating"),
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var rating)
                || rating is < 0 or > 10)
            {
                throw new InvalidDataException($"IMDb title dataset row {parser.LineNumber} has an invalid rating.");
            }

            if (!long.TryParse(
                    Field("numVotes"),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var votes)
                || votes < 0)
            {
                throw new InvalidDataException($"IMDb title dataset row {parser.LineNumber} has an invalid vote count.");
            }

            var year = ParseYear(Field("startYear"), parser.LineNumber);
            rows.Add(new(imdbId!, title, year, mediaType, rating, votes));
        }

        var inserted = 0;
        var rowsToImport = rows.DistinctBy(row => row.ImdbId, StringComparer.Ordinal).ToArray();
        if (rowsToImport.Length > 0)
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var updatedAt = DateTimeOffset.UtcNow;
            foreach (var batch in rowsToImport.Chunk(BatchSize))
            {
                var imdbIds = batch.Select(row => row.ImdbId).ToArray();
                var titles = batch.Select(row => row.Title).ToArray();
                var years = batch.Select(row => row.Year ?? 0).ToArray();
                var mediaTypes = batch.Select(row => row.MediaType).ToArray();
                var ratings = batch.Select(row => row.Rating).ToArray();
                var votes = batch.Select(row => row.Votes).ToArray();

                inserted += await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO imdb_titles
                        (imdb_id, title, year, media_type, imdb_rating, imdb_votes,
                         metadata_status, enrichment_attempt_count, updated_at)
                    SELECT seed.imdb_id, seed.title, NULLIF(seed.year, 0), seed.media_type,
                           seed.imdb_rating, seed.imdb_votes, 'pending', 0, {updatedAt}
                    FROM unnest(
                        {imdbIds}::text[],
                        {titles}::text[],
                        {years}::integer[],
                        {mediaTypes}::text[],
                        {ratings}::numeric[],
                        {votes}::bigint[]
                    ) AS seed(imdb_id, title, year, media_type, imdb_rating, imdb_votes)
                    ON CONFLICT (imdb_id) DO NOTHING
                    """,
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        return new(rowsRead, rowsSkippedByType, inserted, rows.Count - inserted);
    }

    private static int? ParseYear(string value, long lineNumber)
    {
        if (value is "" or "\\N")
        {
            return null;
        }

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            || year is < 1 or > 9999)
        {
            throw new InvalidDataException($"IMDb title dataset row {lineNumber} has an invalid start year.");
        }

        return year;
    }
}