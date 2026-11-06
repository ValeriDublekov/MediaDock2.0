using System.Globalization;
using MediaDock.Api.Middleware;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.Api.ImdbTitles;

internal sealed class ImdbTitlesApiService(MediaDockDbContext dbContext)
{
    private static readonly string[] MetadataStatusOptions = ["pending", "enriched", "not_found", "temporary_error"];

    public async Task<ImdbTitlesPageResponse> GetAsync(
        ImdbTitlesQuery query,
        long? userId,
        CancellationToken cancellationToken)
    {
        var page = query.Page ?? 1;
        var pageSize = query.PageSize ?? 25;
        ValidateRanges(query);

        var allTitles = dbContext.ImdbTitles.AsNoTracking();
        var yearBounds = await allTitles
            .Where(title => title.Year.HasValue)
            .GroupBy(_ => 1)
            .Select(group => new ImdbTitleYearBounds(
                group.Min(title => title.Year),
                group.Max(title => title.Year)))
            .FirstOrDefaultAsync(cancellationToken)
            ?? new ImdbTitleYearBounds(null, null);
        var ratingBounds = await allTitles
            .Where(title => title.ImdbRating.HasValue)
            .GroupBy(_ => 1)
            .Select(group => new ImdbTitleRatingBounds(
                group.Min(title => title.ImdbRating),
                group.Max(title => title.ImdbRating)))
            .FirstOrDefaultAsync(cancellationToken)
            ?? new ImdbTitleRatingBounds(null, null);
        var mediaTypes = await allTitles
            .Where(title => title.MediaType != null && title.MediaType != string.Empty)
            .Select(title => title.MediaType!)
            .Distinct()
            .OrderBy(mediaType => mediaType)
            .ToListAsync(cancellationToken);
        var hasUntypedTitles = await allTitles.AnyAsync(title => title.MediaType == null, cancellationToken);
        var genres = await allTitles
            .SelectMany(title => title.Genres)
            .Where(genre => genre != string.Empty)
            .Distinct()
            .OrderBy(genre => genre)
            .ToListAsync(cancellationToken);

        IQueryable<ImdbTitle> titles = allTitles;
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            var pattern = $"%{search}%";
            var yearSearch = int.TryParse(search, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedYear)
                ? parsedYear
                : (int?)null;
            titles = titles.Where(title => EF.Functions.ILike(title.ImdbId, pattern)
                || (title.TitleText != null && EF.Functions.ILike(title.TitleText, pattern))
                || (yearSearch.HasValue && title.Year == yearSearch.Value));
        }

        if (!string.IsNullOrWhiteSpace(query.MediaType))
        {
            titles = titles.Where(title => title.MediaType == query.MediaType.Trim());
        }

        if (query.MediaTypeIsNull == true)
        {
            titles = titles.Where(title => title.MediaType == null);
        }

        var selectedGenres = (query.Genres ?? [])
            .Where(genre => !string.IsNullOrWhiteSpace(genre))
            .Select(genre => genre.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (query.GenreFilter == true && selectedGenres.Length == 0)
        {
            titles = titles.Where(_ => false);
        }
        else if (selectedGenres.Length > 0)
        {
            titles = titles.Where(title => title.Genres.Any(genre => selectedGenres.Contains(genre)));
        }

        var selectedCountries = (query.Countries ?? [])
            .Where(country => !string.IsNullOrWhiteSpace(country))
            .Select(country => country.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (query.CountryFilter == true && selectedCountries.Length == 0)
        {
            titles = titles.Where(_ => false);
        }
        else if (selectedCountries.Length > 0)
        {
            titles = titles.Where(title => title.Countries.Any(country => selectedCountries.Contains(country)));
        }

        if (query.YearFrom is { } yearFrom)
        {
            titles = titles.Where(title => title.Year >= yearFrom);
        }

        if (query.YearTo is { } yearTo)
        {
            titles = titles.Where(title => title.Year <= yearTo);
        }

        if (query.ImdbRatingFrom is { } ratingFrom)
        {
            titles = titles.Where(title => title.ImdbRating >= ratingFrom);
        }

        if (query.ImdbRatingTo is { } ratingTo)
        {
            titles = titles.Where(title => title.ImdbRating <= ratingTo);
        }

        if (query.MetadataStatus is not null)
        {
            titles = titles.Where(title => title.MetadataStatus == query.MetadataStatus);
        }

        if (userId is long ownerId)
        {
            if (query.ShowIgnored == false)
            {
                titles = titles.Where(title => !dbContext.UserIgnoredMovies
                    .Any(ignored => ignored.UserId == ownerId && ignored.ImdbId == title.ImdbId));
            }

            if (query.ShowWatched == false)
            {
                titles = titles.Where(title => !dbContext.PersonalRatings
                    .Any(rating => rating.UserId == ownerId && rating.ImdbId == title.ImdbId));
            }
        }

        var totalCount = await titles.CountAsync(cancellationToken);
        var orderedTitles = query.Sort switch
        {
            "rating" => titles.OrderBy(title => title.ImdbRating == null)
                .ThenByDescending(title => title.ImdbRating),
            "votes" => titles.OrderBy(title => title.ImdbVotes == null)
                .ThenByDescending(title => title.ImdbVotes),
            _ => titles.OrderBy(title => title.Year == null)
                .ThenByDescending(title => title.Year)
        };
        orderedTitles = orderedTitles
            .ThenBy(title => title.TitleText ?? string.Empty)
            .ThenBy(title => title.ImdbId);

        IQueryable<ImdbTitleCatalogItem> projectedTitles;
        if (userId is long activeUserId)
        {
            projectedTitles = orderedTitles.Select(title => new ImdbTitleCatalogItem(
                title.ImdbId,
                title.TitleText ?? title.ImdbId,
                title.Year,
                title.MediaType,
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
                title.BoxOffice,
                title.MetadataStatus,
                dbContext.ImdbFavoriteMovies.Any(favorite =>
                    favorite.UserId == activeUserId && favorite.ImdbId == title.ImdbId),
                dbContext.ImdbFavoriteMovies.Where(favorite =>
                        favorite.UserId == activeUserId && favorite.ImdbId == title.ImdbId)
                    .Select(favorite => favorite.ToWatch)
                    .FirstOrDefault(),
                dbContext.ImdbFavoriteMovies.Where(favorite =>
                        favorite.UserId == activeUserId && favorite.ImdbId == title.ImdbId)
                    .Select(favorite => favorite.ToDownload)
                    .FirstOrDefault(),
                dbContext.UserIgnoredMovies.Any(ignored =>
                    ignored.UserId == activeUserId && ignored.ImdbId == title.ImdbId),
                dbContext.PersonalRatings.Where(rating =>
                        rating.UserId == activeUserId && rating.ImdbId == title.ImdbId)
                    .Select(rating => (int?)rating.Rating)
                    .FirstOrDefault()));
        }
        else
        {
            projectedTitles = orderedTitles.Select(title => new ImdbTitleCatalogItem(
                title.ImdbId,
                title.TitleText ?? title.ImdbId,
                title.Year,
                title.MediaType,
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
                title.BoxOffice,
                title.MetadataStatus,
                false,
                false,
                false,
                false,
                null));
        }

        var items = await projectedTitles
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (totalCount + pageSize - 1) / pageSize;
        return new ImdbTitlesPageResponse(
            items,
            page,
            pageSize,
            totalCount,
            totalPages,
            yearBounds,
            ratingBounds,
            mediaTypes,
            hasUntypedTitles,
            genres,
            MetadataStatusOptions);
    }

    private static void ValidateRanges(ImdbTitlesQuery query)
    {
        if (query.YearFrom is { } yearFrom && query.YearTo is { } yearTo && yearFrom > yearTo)
        {
            throw new ApiValidationException(new Dictionary<string, string[]>
            {
                [nameof(query.YearTo)] = ["YearTo must be greater than or equal to YearFrom."]
            });
        }

        if (query.ImdbRatingFrom is { } ratingFrom
            && query.ImdbRatingTo is { } ratingTo
            && ratingFrom > ratingTo)
        {
            throw new ApiValidationException(new Dictionary<string, string[]>
            {
                [nameof(query.ImdbRatingTo)] = ["ImdbRatingTo must be greater than or equal to ImdbRatingFrom."]
            });
        }

        if (!string.IsNullOrWhiteSpace(query.MediaType) && query.MediaTypeIsNull == true)
        {
            throw new ApiValidationException(new Dictionary<string, string[]>
            {
                [nameof(query.MediaType)] = ["MediaType and MediaTypeIsNull cannot be used together."]
            });
        }
    }
}