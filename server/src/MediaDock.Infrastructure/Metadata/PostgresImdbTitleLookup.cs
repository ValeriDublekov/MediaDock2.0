using MediaDock.Application.Metadata;
using MediaDock.Application.PersonalRatings;
using MediaDock.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.Infrastructure.Metadata;

public sealed class PostgresImdbTitleLookup(MediaDockDbContext dbContext) : IImdbTitleLookup
{
    private static readonly string[] MovieMediaTypes = ["movie", "documentary", "short"];

    public async Task<IReadOnlyList<ImdbTitleLookupCandidate>> FindByTitleAndYearAsync(
        string title,
        int year,
        string sourceType,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(sourceType, "movie", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var titleVariants = new[]
            {
                title.Trim(),
                title.Trim().ToLowerInvariant(),
                title.Trim().ToUpperInvariant()
            }
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (titleVariants.Length == 0)
        {
            return [];
        }

        var matches = await dbContext.ImdbTitles
            .AsNoTracking()
            .Where(candidate => candidate.TitleText != null
                && titleVariants.Contains(candidate.TitleText)
                && candidate.Year >= year - 1
                && candidate.Year <= year + 1
                && candidate.MediaType != null
                && MovieMediaTypes.Contains(candidate.MediaType))
            .ToListAsync(cancellationToken);

        return matches
            .Select(candidate => new ImdbTitleLookupCandidate(
                candidate.MetadataStatus == ImdbTitleEnrichmentStatusValues.Enriched,
                ToMetadata(candidate)))
            .ToArray();
    }

    private static MetadataDetails ToMetadata(Persistence.Entities.ImdbTitle title)
    {
        var mediaType = title.MediaType!;
        var contentKind = mediaType switch
        {
            "documentary" => "documentary",
            "short" => "short",
            _ => "standard"
        };

        return new MetadataDetails(
            title.TitleText!,
            title.Year,
            title.ImdbId,
            mediaType,
            "movie",
            contentKind,
            null,
            title.ImdbRating,
            title.ImdbVotes,
            title.Metascore,
            title.Genres,
            title.Countries,
            title.Director,
            title.Plot,
            title.PosterUrl,
            title.Runtime,
            title.Awards,
            title.BoxOffice);
    }
}