using System.Text.Json;

namespace MediaDock.Application.PersonalRatings;

public static class PersonalRatingsJsonParser
{
    public const int MaximumRatings = 50_000;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static PersonalRatingsParseResult Parse(ReadOnlySpan<byte> json)
    {
        RatingRow?[]? rows;
        try
        {
            rows = JsonSerializer.Deserialize<RatingRow[]>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            throw new PersonalRatingsImportException("The file must contain a JSON array of IMDb IDs and integer ratings.");
        }

        if (rows is null || rows.Length == 0)
        {
            throw new PersonalRatingsImportException("The ratings file must contain at least one entry.");
        }

        if (rows.Length > MaximumRatings)
        {
            throw new PersonalRatingsImportException($"The file cannot contain more than {MaximumRatings} entries.");
        }

        var ratings = new Dictionary<string, int>(StringComparer.Ordinal);
        var missingRatingIds = new List<string>();
        foreach (var row in rows)
        {
            if (row is null)
            {
                throw new PersonalRatingsImportException("Each entry must include an IMDb ID and rating.");
            }

            var imdbId = NormalizeImdbId(row.Id);
            if (row.Rating is null)
            {
                missingRatingIds.Add(imdbId);
                continue;
            }

            var rating = row.Rating.Value;
            if (rating is < 1 or > 10)
            {
                throw new PersonalRatingsImportException("Ratings must be whole numbers from 1 to 10.");
            }

            if (ratings.TryGetValue(imdbId, out var existingRating) && existingRating != rating)
            {
                throw new PersonalRatingsImportException($"The file contains conflicting ratings for {imdbId}.");
            }

            ratings[imdbId] = rating;
        }

        return new PersonalRatingsParseResult(ratings, missingRatingIds);
    }

    private static string NormalizeImdbId(string? value)
    {
        var imdbId = value?.Trim().ToLowerInvariant();
        if (imdbId is null
            || imdbId.Length is < 9 or > 14
            || !imdbId.StartsWith("tt", StringComparison.Ordinal)
            || imdbId.AsSpan(2).ContainsAnyExceptInRange('0', '9'))
        {
            throw new PersonalRatingsImportException("Each id must be an IMDb title ID such as tt14452776.");
        }

        return imdbId;
    }

    private sealed class RatingRow
    {
        public string? Id { get; set; }
        public int? Rating { get; set; }
    }
}

public sealed record PersonalRatingsParseResult(
    IReadOnlyDictionary<string, int> Ratings,
    IReadOnlyList<string> MissingRatingIds);

public sealed class PersonalRatingsImportException(string message) : Exception(message);