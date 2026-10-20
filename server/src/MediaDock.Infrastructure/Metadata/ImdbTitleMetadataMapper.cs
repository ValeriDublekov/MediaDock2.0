using MediaDock.Application.Metadata;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.Infrastructure.Metadata;

public static class ImdbTitleMetadataMapper
{
    public static async Task<IReadOnlyDictionary<string, ImdbTitle>> EnsureAsync(
        MediaDockDbContext dbContext,
        IEnumerable<string?> imdbIds,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        var normalizedIds = imdbIds
            .Select(ImdbIdNormalizer.Normalize)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var titlesById = dbContext.ImdbTitles.Local
            .Where(title => normalizedIds.Contains(title.ImdbId, StringComparer.Ordinal))
            .ToDictionary(title => title.ImdbId, StringComparer.Ordinal);
        var idsToEnsure = normalizedIds
            .Where(imdbId => !titlesById.ContainsKey(imdbId))
            .ToArray();
        if (idsToEnsure.Length == 0)
        {
            return titlesById;
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO imdb_titles (imdb_id, metadata_status, enrichment_attempt_count, updated_at)
            SELECT requested.imdb_id, 'pending', 0, {updatedAt}
            FROM unnest({idsToEnsure}) AS requested(imdb_id)
            ON CONFLICT (imdb_id) DO NOTHING
            """,
            cancellationToken);

        var persistedTitles = await dbContext.ImdbTitles
            .Where(title => idsToEnsure.Contains(title.ImdbId))
            .ToListAsync(cancellationToken);
        foreach (var title in persistedTitles)
        {
            titlesById.Add(title.ImdbId, title);
        }

        return titlesById;
    }

    public static void Apply(ImdbTitle title, MetadataDetails metadata, DateTimeOffset updatedAt)
    {
        var metadataImdbId = ImdbIdNormalizer.Normalize(metadata.ImdbId);
        if (metadataImdbId is not null && metadataImdbId != title.ImdbId)
        {
            throw new InvalidOperationException("IMDb metadata identity conflicts with its canonical row.");
        }

        if (!string.IsNullOrWhiteSpace(metadata.Title)) title.TitleText = metadata.Title;
        if (metadata.Year.HasValue) title.Year = metadata.Year;
        if (!string.IsNullOrWhiteSpace(metadata.MediaType)) title.MediaType = metadata.MediaType;
        if (metadata.ImdbRating.HasValue) title.ImdbRating = metadata.ImdbRating;
        if (metadata.ImdbVotes.HasValue) title.ImdbVotes = metadata.ImdbVotes;
        if (metadata.Metascore.HasValue) title.Metascore = metadata.Metascore;
        if (metadata.Genres.Length > 0) title.Genres = metadata.Genres;
        if (metadata.Countries.Length > 0) title.Countries = metadata.Countries;
        if (!string.IsNullOrWhiteSpace(metadata.Director)) title.Director = metadata.Director;
        if (!string.IsNullOrWhiteSpace(metadata.Plot)) title.Plot = metadata.Plot;
        if (!string.IsNullOrWhiteSpace(metadata.PosterUrl)) title.PosterUrl = metadata.PosterUrl;
        if (!string.IsNullOrWhiteSpace(metadata.Runtime)) title.Runtime = metadata.Runtime;
        if (!string.IsNullOrWhiteSpace(metadata.Awards)) title.Awards = metadata.Awards;
        if (!string.IsNullOrWhiteSpace(metadata.BoxOffice)) title.BoxOffice = metadata.BoxOffice;
        title.MetadataStatus = "enriched";
        title.NextEnrichmentAttemptAt = null;
        title.LastEnrichmentError = null;
        title.UpdatedAt = updatedAt;
    }

    public static void MoveTitleFallbackMetadata(ImdbTitle imdbTitle, Title title)
    {
        var titleImdbId = ImdbIdNormalizer.Normalize(title.ImdbId);
        if (titleImdbId is not null && titleImdbId != imdbTitle.ImdbId)
        {
            throw new InvalidOperationException("Title fallback metadata identity conflicts with its canonical row.");
        }

        var hasMetadata = title.ImdbRating.HasValue
            || title.ImdbVotes.HasValue
            || title.Metascore.HasValue
            || title.Genres.Length > 0
            || title.Countries.Length > 0
            || !string.IsNullOrWhiteSpace(title.Director)
            || !string.IsNullOrWhiteSpace(title.Plot)
            || !string.IsNullOrWhiteSpace(title.PosterUrl)
            || !string.IsNullOrWhiteSpace(title.Runtime)
            || !string.IsNullOrWhiteSpace(title.Awards)
            || !string.IsNullOrWhiteSpace(title.BoxOffice);

        imdbTitle.ImdbRating ??= title.ImdbRating;
        imdbTitle.ImdbVotes ??= title.ImdbVotes;
        imdbTitle.Metascore ??= title.Metascore;
        if (imdbTitle.Genres.Length == 0 && title.Genres.Length > 0) imdbTitle.Genres = title.Genres;
        if (imdbTitle.Countries.Length == 0 && title.Countries.Length > 0) imdbTitle.Countries = title.Countries;
        if (string.IsNullOrWhiteSpace(imdbTitle.Director)) imdbTitle.Director = title.Director;
        if (string.IsNullOrWhiteSpace(imdbTitle.Plot)) imdbTitle.Plot = title.Plot;
        if (string.IsNullOrWhiteSpace(imdbTitle.PosterUrl)) imdbTitle.PosterUrl = title.PosterUrl;
        if (string.IsNullOrWhiteSpace(imdbTitle.Runtime)) imdbTitle.Runtime = title.Runtime;
        if (string.IsNullOrWhiteSpace(imdbTitle.Awards)) imdbTitle.Awards = title.Awards;
        if (string.IsNullOrWhiteSpace(imdbTitle.BoxOffice)) imdbTitle.BoxOffice = title.BoxOffice;

        if (hasMetadata)
        {
            imdbTitle.MetadataStatus = "enriched";
            imdbTitle.NextEnrichmentAttemptAt = null;
            imdbTitle.LastEnrichmentError = null;
            if (title.UpdatedAt > imdbTitle.UpdatedAt) imdbTitle.UpdatedAt = title.UpdatedAt;
        }

        TitleMetadataMapper.ClearProviderMetadata(title);
    }
}