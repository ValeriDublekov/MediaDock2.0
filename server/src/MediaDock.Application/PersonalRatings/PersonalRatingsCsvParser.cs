using System.Globalization;
using System.Text;

namespace MediaDock.Application.PersonalRatings;

public static class PersonalRatingsCsvParser
{
    public const int MaximumRatings = 50_000;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static PersonalRatingsParseResult Parse(ReadOnlySpan<byte> csvBytes)
    {
        string csv;
        try
        {
            csv = StrictUtf8.GetString(csvBytes);
        }
        catch (DecoderFallbackException)
        {
            throw new PersonalRatingsImportException("The file must be a valid UTF-8 IMDb ratings CSV export.");
        }

        if (csv.Length > 0 && csv[0] == '\uFEFF')
        {
            csv = csv[1..];
        }

        var rows = ParseRows(csv);
        if (rows.Count < 2)
        {
            throw new PersonalRatingsImportException("The ratings file must contain at least one entry.");
        }

        if (rows.Count - 1 > MaximumRatings)
        {
            throw new PersonalRatingsImportException($"The file cannot contain more than {MaximumRatings} entries.");
        }

        var headers = rows[0];
        if (headers.Length != 3)
        {
            throw InvalidHeader();
        }

        var idColumn = FindColumn(headers, "Title ID");
        var ratingColumn = FindColumn(headers, "Rating");
        var modifiedDateColumn = FindColumn(headers, "Last Modified Date");
        if (idColumn < 0 || ratingColumn < 0 || modifiedDateColumn < 0)
        {
            throw InvalidHeader();
        }

        var ratings = new Dictionary<string, PersonalRatingImportEntry>(StringComparer.Ordinal);
        var missingRatingIds = new List<string>();
        foreach (var row in rows.Skip(1))
        {
            if (row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (row.Length != headers.Length)
            {
                throw new PersonalRatingsImportException("Each CSV row must contain exactly three columns.");
            }

            var imdbId = NormalizeImdbId(row[idColumn]);
            var dateRated = ParseDate(row[modifiedDateColumn]);
            var ratingText = row[ratingColumn].Trim();
            if (ratingText.Length == 0 || ratingText == "0")
            {
                missingRatingIds.Add(imdbId);
                continue;
            }

            if (!int.TryParse(ratingText, NumberStyles.None, CultureInfo.InvariantCulture, out var rating)
                || rating is < 1 or > 10)
            {
                throw new PersonalRatingsImportException("Ratings must be whole numbers from 1 to 10.");
            }

            var entry = new PersonalRatingImportEntry(rating, dateRated);
            if (ratings.TryGetValue(imdbId, out var existingEntry) && existingEntry != entry)
            {
                throw new PersonalRatingsImportException($"The file contains conflicting ratings or vote dates for {imdbId}.");
            }

            ratings[imdbId] = entry;
        }

        if (ratings.Count == 0 && missingRatingIds.Count == 0)
        {
            throw new PersonalRatingsImportException("The ratings file must contain at least one entry.");
        }

        return new PersonalRatingsParseResult(ratings, missingRatingIds);
    }

    private static int FindColumn(IReadOnlyList<string> headers, string name)
    {
        var matches = Enumerable.Range(0, headers.Count)
            .Where(index => string.Equals(headers[index].Trim(), name, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return matches.Length == 1 ? matches[0] : -1;
    }

    private static DateOnly? ParseDate(string value)
    {
        var dateText = value.Trim();
        if (dateText.Length == 0)
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(
                dateText,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var modifiedAt))
        {
            throw new PersonalRatingsImportException("Last Modified Date values must be valid ISO timestamps.");
        }

        return DateOnly.FromDateTime(modifiedAt.UtcDateTime);
    }

    private static List<string[]> ParseRows(string csv)
    {
        var rows = new List<string[]>();
        var fields = new List<string>();
        var value = new StringBuilder();
        var inQuotes = false;
        var afterQuote = false;

        for (var index = 0; index < csv.Length; index++)
        {
            var character = csv[index];
            if (inQuotes)
            {
                if (character != '"')
                {
                    value.Append(character);
                    continue;
                }

                if (index + 1 < csv.Length && csv[index + 1] == '"')
                {
                    value.Append('"');
                    index++;
                    continue;
                }

                inQuotes = false;
                afterQuote = true;
                continue;
            }

            if (afterQuote)
            {
                if (character == ',')
                {
                    AddField();
                    afterQuote = false;
                    continue;
                }

                if (character is '\r' or '\n')
                {
                    AddField();
                    AddRow();
                    afterQuote = false;
                    ConsumeLineEnding(ref index);
                    continue;
                }

                throw InvalidCsv();
            }

            if (character == '"')
            {
                if (value.Length != 0)
                {
                    throw InvalidCsv();
                }

                inQuotes = true;
            }
            else if (character == ',')
            {
                AddField();
            }
            else if (character is '\r' or '\n')
            {
                if (fields.Count > 0 || value.Length > 0)
                {
                    AddField();
                    AddRow();
                }

                ConsumeLineEnding(ref index);
            }
            else
            {
                value.Append(character);
            }
        }

        if (inQuotes)
        {
            throw InvalidCsv();
        }

        if (afterQuote || fields.Count > 0 || value.Length > 0)
        {
            AddField();
            AddRow();
        }

        return rows;

        void AddField()
        {
            fields.Add(value.ToString());
            value.Clear();
        }

        void AddRow()
        {
            rows.Add(fields.ToArray());
            fields.Clear();
        }

        void ConsumeLineEnding(ref int currentIndex)
        {
            if (csv[currentIndex] == '\r'
                && currentIndex + 1 < csv.Length
                && csv[currentIndex + 1] == '\n')
            {
                currentIndex++;
            }
        }
    }

    private static string NormalizeImdbId(string value)
    {
        var imdbId = value.Trim().ToLowerInvariant();
        if (imdbId.Length is < 9 or > 14
            || !imdbId.StartsWith("tt", StringComparison.Ordinal)
            || imdbId.AsSpan(2).ContainsAnyExceptInRange('0', '9'))
        {
            throw new PersonalRatingsImportException("Each id must be an IMDb title ID such as tt14452776.");
        }

        return imdbId;
    }

    private static PersonalRatingsImportException InvalidHeader() =>
        new("The CSV must contain Title ID, Rating, and Last Modified Date columns.");

    private static PersonalRatingsImportException InvalidCsv() =>
        new("The file must be a valid IMDb ratings CSV export.");
}

public sealed record PersonalRatingsParseResult(
    IReadOnlyDictionary<string, PersonalRatingImportEntry> Ratings,
    IReadOnlyList<string> MissingRatingIds);

public sealed record PersonalRatingImportEntry(int Rating, DateOnly? DateRated);

public sealed class PersonalRatingsImportException(string message) : Exception(message);