using MediaDock.Api.Common;
using MediaDock.Api.Middleware;
using MediaDock.Application.PersonalRatings;
using MediaDock.Infrastructure.Metadata;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.Api.PersonalRatings;

internal sealed class PersonalRatingsApiService(MediaDockDbContext dbContext)
{
    public const int MaximumUploadBytes = 1024 * 1024;

    public async Task<PageResponse<PersonalRatingResponse>> GetAsync(
        long userId,
        PersonalRatingsQuery query,
        CancellationToken cancellationToken)
    {
        var page = query.Page ?? 1;
        var pageSize = query.PageSize ?? 25;
        var ratings = dbContext.PersonalRatings.AsNoTracking()
            .Where(rating => rating.UserId == userId
                && (query.ManuallyAddedOnly != true || rating.IsManuallyAdded));
        var totalCount = await ratings.CountAsync(cancellationToken);
        var orderedRatings = query.Sort switch
        {
            "asc" => ratings.OrderBy(rating => rating.Rating).ThenBy(rating => rating.ImdbId),
            "vote_date_asc" => ratings
                .OrderBy(rating => rating.DateRated)
                .ThenBy(rating => rating.ImdbId),
            "vote_date_desc" => ratings
                .OrderByDescending(rating => rating.DateRated)
                .ThenBy(rating => rating.ImdbId),
            _ => ratings.OrderByDescending(rating => rating.Rating).ThenBy(rating => rating.ImdbId)
        };
        var items = await orderedRatings
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(rating => new PersonalRatingResponse(
                rating.ImdbId,
                rating.Rating,
                rating.DateRated,
                rating.IsManuallyAdded,
                rating.ImdbTitle.TitleText ?? rating.ImdbId,
                rating.ImdbTitle.Year,
                rating.ImdbTitle.MediaType,
                rating.ImdbTitle.ImdbRating,
                rating.ImdbTitle.ImdbVotes,
                rating.ImdbTitle.Metascore,
                rating.ImdbTitle.Genres,
                rating.ImdbTitle.Countries,
                rating.ImdbTitle.Director,
                rating.ImdbTitle.Plot,
                rating.ImdbTitle.PosterUrl,
                rating.ImdbTitle.Runtime,
                rating.ImdbTitle.Awards,
                rating.ImdbTitle.BoxOffice,
                rating.ImdbTitle.MetadataStatus))
            .ToListAsync(cancellationToken);

        var totalPages = totalCount == 0 ? 0 : (totalCount + pageSize - 1) / pageSize;
        return new PageResponse<PersonalRatingResponse>(items, page, pageSize, totalCount, totalPages);
    }

    public async Task<bool> DeleteManuallyAddedAsync(
        long userId,
        string imdbId,
        CancellationToken cancellationToken) =>
        await dbContext.PersonalRatings
            .Where(rating => rating.UserId == userId
                && rating.ImdbId == imdbId
                && rating.IsManuallyAdded)
            .ExecuteDeleteAsync(cancellationToken) > 0;

    public async Task<PersonalRatingsImportResponse> ImportAsync(
        long userId,
        PersonalRatingsImportForm form,
        CancellationToken cancellationToken)
    {
        var file = form.File;
        if (file is null || file.Length == 0)
        {
            throw ValidationError("Select a non-empty CSV ratings file.");
        }

        if (file.Length > MaximumUploadBytes)
        {
            throw new ApiPayloadTooLargeException("The ratings file exceeds the configured size limit.");
        }

        if (!string.Equals(Path.GetExtension(file.FileName), ".csv", StringComparison.OrdinalIgnoreCase))
        {
            throw ValidationError("The selected file must have a .csv extension.");
        }

        await using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > MaximumUploadBytes)
        {
            throw new ApiPayloadTooLargeException("The ratings file exceeds the configured size limit.");
        }

        var importedAt = DateTimeOffset.UtcNow;
        PersonalRatingsParseResult parsedFile;
        try
        {
            parsedFile = PersonalRatingsCsvParser.Parse(
                buffer.GetBuffer().AsSpan(0, (int)buffer.Length),
                DateOnly.FromDateTime(importedAt.UtcDateTime));
        }
        catch (PersonalRatingsImportException exception)
        {
            throw ValidationError(exception.Message);
        }

        var importedRatings = parsedFile.Ratings;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var ids = importedRatings.Keys.ToArray();
        var existingRatings = await dbContext.PersonalRatings
            .Where(rating => rating.UserId == userId && ids.Contains(rating.ImdbId))
            .ToDictionaryAsync(rating => rating.ImdbId, cancellationToken);
        await ImdbTitleMetadataMapper.EnsureAsync(dbContext, ids, importedAt, cancellationToken);
        var added = 0;
        var updated = 0;
        var unchanged = 0;

        foreach (var (imdbId, entry) in importedRatings)
        {
            if (existingRatings.TryGetValue(imdbId, out var existing))
            {
                if (existing.Rating == entry.Rating && existing.DateRated == entry.DateRated)
                {
                    unchanged++;
                    continue;
                }

                existing.Rating = entry.Rating;
                existing.DateRated = entry.DateRated;
                existing.UpdatedAt = importedAt;
                updated++;
                continue;
            }

            dbContext.PersonalRatings.Add(new PersonalRating
            {
                UserId = userId,
                ImdbId = imdbId,
                Rating = entry.Rating,
                DateRated = entry.DateRated,
                UpdatedAt = importedAt
            });
            added++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        var totalRatings = await dbContext.PersonalRatings
            .CountAsync(rating => rating.UserId == userId, cancellationToken);
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