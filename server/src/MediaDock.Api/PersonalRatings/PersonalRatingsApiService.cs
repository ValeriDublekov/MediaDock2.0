using MediaDock.Api.Middleware;
using MediaDock.Application.PersonalRatings;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.Api.PersonalRatings;

internal sealed class PersonalRatingsApiService(MediaDockDbContext dbContext)
{
    public const int MaximumUploadBytes = 5 * 1024 * 1024;

    public async Task<PersonalRatingsImportResponse> ImportAsync(
        PersonalRatingsImportForm form,
        CancellationToken cancellationToken)
    {
        var file = form.File;
        if (file is null || file.Length == 0)
        {
            throw ValidationError("Select a non-empty JSON ratings file.");
        }

        if (file.Length > MaximumUploadBytes)
        {
            throw ValidationError($"The ratings file cannot exceed {MaximumUploadBytes / (1024 * 1024)} MiB.");
        }

        if (!string.Equals(Path.GetExtension(file.FileName), ".json", StringComparison.OrdinalIgnoreCase))
        {
            throw ValidationError("The selected file must have a .json extension.");
        }

        await using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > MaximumUploadBytes)
        {
            throw ValidationError($"The ratings file cannot exceed {MaximumUploadBytes / (1024 * 1024)} MiB.");
        }

        PersonalRatingsParseResult parsedFile;
        try
        {
            parsedFile = PersonalRatingsJsonParser.Parse(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
        }
        catch (PersonalRatingsImportException exception)
        {
            throw ValidationError(exception.Message);
        }

        var importedRatings = parsedFile.Ratings;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var ids = importedRatings.Keys.ToArray();
        var existingRatings = await dbContext.PersonalRatings
            .Where(rating => ids.Contains(rating.ImdbId))
            .ToDictionaryAsync(rating => rating.ImdbId, cancellationToken);
        var importedAt = DateTimeOffset.UtcNow;
        var added = 0;
        var updated = 0;
        var unchanged = 0;

        foreach (var (imdbId, rating) in importedRatings)
        {
            if (existingRatings.TryGetValue(imdbId, out var existing))
            {
                if (existing.Rating == rating)
                {
                    unchanged++;
                    continue;
                }

                existing.Rating = rating;
                existing.UpdatedAt = importedAt;
                updated++;
                continue;
            }

            dbContext.PersonalRatings.Add(new PersonalRating
            {
                ImdbId = imdbId,
                Rating = rating,
                UpdatedAt = importedAt
            });
            added++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        var totalRatings = await dbContext.PersonalRatings.CountAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new PersonalRatingsImportResponse(
            importedRatings.Count,
            added,
            updated,
            unchanged,
            totalRatings,
            importedAt,
            parsedFile.MissingRatingIds
                .Select(id => new PersonalRatingsImportError(id, "Missing rating."))
                .ToArray());
    }

    private static ApiValidationException ValidationError(string message) =>
        new(new Dictionary<string, string[]> { ["File"] = [message] });
}