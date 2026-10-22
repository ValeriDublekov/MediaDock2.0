using MediaDock.Api.Common;
using MediaDock.Api.Middleware;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.Api.Catalog;

internal interface ICatalogApiService
{
    Task<PageResponse<CatalogTitleResponse>> GetCatalogAsync(
        CatalogQuery query,
        CancellationToken cancellationToken);

    Task<TitleDetailsResponse> GetTitleAsync(long id, CancellationToken cancellationToken);

    Task<DeleteTitleResponse> DeleteTitleAsync(
        long id,
        bool deleteImdbInfo,
        CancellationToken cancellationToken);

    Task<PageResponse<OccurrenceResponse>> GetOccurrencesAsync(
        long titleId,
        OccurrencesQuery query,
        CancellationToken cancellationToken);
}

internal sealed class CatalogApiService(MediaDockDbContext dbContext) : ICatalogApiService
{
    public async Task<PageResponse<CatalogTitleResponse>> GetCatalogAsync(
        CatalogQuery query,
        CancellationToken cancellationToken)
    {
        var page = query.Page ?? 1;
        var pageSize = query.PageSize ?? 25;
        if (query.YearFrom is { } yearFrom && query.YearTo is { } yearTo && yearFrom > yearTo)
        {
            throw new ApiValidationException(new Dictionary<string, string[]>
            {
                [nameof(query.YearTo)] = ["YearTo must be greater than or equal to YearFrom."]
            });
        }
        if (query.ImdbRatingFrom is { } ratingFrom && query.ImdbRatingTo is { } ratingTo && ratingFrom > ratingTo)
        {
            throw new ApiValidationException(new Dictionary<string, string[]>
            {
                [nameof(query.ImdbRatingTo)] = ["ImdbRatingTo must be greater than or equal to ImdbRatingFrom."]
            });
        }

        var yearBounds = await dbContext.TorrentMovies
            .AsNoTracking()
            .Where(movie => movie.Releases.Any() && movie.Year.HasValue)
            .GroupBy(_ => 1)
            .Select(group => new YearBounds(
                group.Min(movie => movie.Year!.Value),
                group.Max(movie => movie.Year!.Value)))
            .FirstOrDefaultAsync(cancellationToken);
        var imdbRatingBounds = await dbContext.TorrentMovies
            .AsNoTracking()
            .Where(movie => movie.Releases.Any()
                && movie.ImdbTitle != null
                && movie.ImdbTitle.ImdbRating.HasValue)
            .GroupBy(_ => 1)
            .Select(group => new ImdbRatingBounds(
                group.Min(movie => movie.ImdbTitle!.ImdbRating!.Value),
                group.Max(movie => movie.ImdbTitle!.ImdbRating!.Value)))
            .FirstOrDefaultAsync(cancellationToken);

        IQueryable<TorrentMovie> movies = dbContext.TorrentMovies
            .AsNoTracking()
            .Where(movie => movie.Releases.Any());

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search.Trim()}%";
            movies = movies.Where(movie => EF.Functions.ILike(movie.TitleText, pattern)
                || EF.Functions.ILike(movie.NormalizedTitle, pattern)
                || (movie.ImdbTitle != null && movie.ImdbTitle.TitleText != null
                    && EF.Functions.ILike(movie.ImdbTitle.TitleText, pattern)));
        }

        if (query.MediaType is not null)
        {
            movies = movies.Where(movie => movie.MediaType == query.MediaType);
        }

        if (query.SourceType is not null)
        {
            movies = movies.Where(movie => movie.SourceType == query.SourceType);
        }

        if (query.FeedTypes is not null)
        {
            var feedTypes = query.FeedTypes.Split(',');
            movies = movies.Where(movie => movie.Releases.Any(release =>
                release.FeedType != null && feedTypes.Contains(release.FeedType)));
        }

        if (query.ContentKind is not null)
        {
            movies = movies.Where(movie => movie.ContentKind == query.ContentKind);
        }

        if (query.YearFrom is { } minimumYear)
        {
            movies = movies.Where(movie => movie.Year >= minimumYear);
        }

        if (query.YearTo is { } maximumYear)
        {
            movies = movies.Where(movie => movie.Year <= maximumYear);
        }

        if (query.ImdbRatingFrom is { } minimumRating)
        {
            movies = movies.Where(movie => movie.ImdbTitle != null && movie.ImdbTitle.ImdbRating >= minimumRating);
        }

        if (query.ImdbRatingTo is { } maximumRating)
        {
            movies = movies.Where(movie => movie.ImdbTitle != null && movie.ImdbTitle.ImdbRating <= maximumRating);
        }

        if (!string.IsNullOrWhiteSpace(query.Genre))
        {
            var genre = query.Genre.Trim();
            movies = movies.Where(movie => movie.ImdbTitle != null && movie.ImdbTitle.Genres.Contains(genre));
        }

        if (!string.IsNullOrWhiteSpace(query.Country))
        {
            var country = query.Country.Trim();
            movies = movies.Where(movie => movie.ImdbTitle != null && movie.ImdbTitle.Countries.Contains(country));
        }

        if (query.SourceId is { } sourceId)
        {
            movies = movies.Where(movie => movie.Releases.Any(release => release.SourceId == sourceId));
        }

        var totalCount = await movies.CountAsync(cancellationToken);
        var items = await movies
            .OrderByDescending(movie => movie.LastSeenAt)
            .ThenByDescending(movie => movie.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(movie => new CatalogTitleResponse(
                movie.Id,
                movie.TitleText,
                movie.Year,
                movie.MediaType,
                movie.SourceType,
                movie.ContentKind,
                movie.ImdbId,
                movie.ImdbTitle == null ? null : movie.ImdbTitle.ImdbRating,
                movie.ImdbTitle == null ? null : movie.ImdbTitle.PosterUrl,
                movie.ImdbTitle == null ? Array.Empty<string>() : movie.ImdbTitle.Genres,
                movie.ImdbTitle == null ? Array.Empty<string>() : movie.ImdbTitle.Countries,
                movie.LastSeenAt,
                movie.Releases.Count,
                movie.ImdbTitle == null ? null : movie.ImdbTitle.MetadataStatus))
            .ToListAsync(cancellationToken);

        return CreatePage(items, page, pageSize, totalCount, yearBounds, imdbRatingBounds);
    }

    public async Task<TitleDetailsResponse> GetTitleAsync(long id, CancellationToken cancellationToken)
    {
        var title = await dbContext.TorrentMovies
            .AsNoTracking()
            .Where(entity => entity.Id == id)
            .Select(entity => new TitleDetailsResponse(
                entity.Id,
                entity.TitleText,
                entity.Year,
                entity.MediaType,
                entity.SourceType,
                entity.ContentKind,
                entity.BroadcastRangeStartYear,
                entity.BroadcastRangeEndYear,
                entity.BroadcastRangeRaw,
                entity.ImdbId,
            entity.ImdbTitle == null ? null : entity.ImdbTitle.ImdbRating,
            entity.ImdbTitle == null ? null : entity.ImdbTitle.ImdbVotes,
            entity.ImdbTitle == null ? null : entity.ImdbTitle.Metascore,
            entity.ImdbTitle == null ? Array.Empty<string>() : entity.ImdbTitle.Genres,
            entity.ImdbTitle == null ? Array.Empty<string>() : entity.ImdbTitle.Countries,
            entity.ImdbTitle == null ? null : entity.ImdbTitle.Director,
            entity.ImdbTitle == null ? null : entity.ImdbTitle.Plot,
            entity.ImdbTitle == null ? null : entity.ImdbTitle.PosterUrl,
            entity.ImdbTitle == null ? null : entity.ImdbTitle.Runtime,
            entity.ImdbTitle == null ? null : entity.ImdbTitle.Awards,
            entity.ImdbTitle == null ? null : entity.ImdbTitle.BoxOffice,
                entity.FirstSeenAt,
                entity.LastSeenAt,
                entity.UpdatedAt,
                entity.Releases.Count,
                entity.ImdbTitle == null ? null : entity.ImdbTitle.MetadataStatus))
            .FirstOrDefaultAsync(cancellationToken);

        return title ?? throw new ApiNotFoundException("Title not found.");
    }

    public async Task<DeleteTitleResponse> DeleteTitleAsync(
        long id,
        bool deleteImdbInfo,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var title = await dbContext.TorrentMovies
            .Include(movie => movie.ImdbTitle)
            .SingleOrDefaultAsync(movie => movie.Id == id, cancellationToken);
        if (title is null)
        {
            throw new ApiNotFoundException("Title not found.");
        }

        var releases = await dbContext.TorrentReleases
            .Where(release => release.TorrentMovieId == id)
            .ToListAsync(cancellationToken);
        var itemStates = await dbContext.RssItemStates
            .Where(state => dbContext.TorrentReleases.Any(release =>
                release.TorrentMovieId == id
                && release.SourceId == state.SourceId
                && release.SourceItemKey == state.SourceItemKey))
            .ToListAsync(cancellationToken);

        dbContext.RssItemStates.RemoveRange(itemStates);
        dbContext.TorrentReleases.RemoveRange(releases);
        dbContext.TorrentMovies.Remove(title);

        var imdbInfoDeleted = false;
        if (deleteImdbInfo && title.ImdbTitle is { } imdbTitle)
        {
            var imdbId = imdbTitle.ImdbId;
            var imdbInfoIsShared = await dbContext.TorrentMovies.AnyAsync(
                    movie => movie.Id != id && movie.ImdbId == imdbId,
                    cancellationToken)
                || await dbContext.OscarMovies.AnyAsync(movie => movie.ImdbId == imdbId, cancellationToken)
                || await dbContext.GoldenGlobeTitles.AnyAsync(movie => movie.ImdbId == imdbId, cancellationToken)
                || await dbContext.ImdbFavoriteMovies.AnyAsync(movie => movie.ImdbId == imdbId, cancellationToken)
                || await dbContext.PersonalRatings.AnyAsync(rating => rating.ImdbId == imdbId, cancellationToken);

            if (!imdbInfoIsShared)
            {
                dbContext.ImdbTitles.Remove(imdbTitle);
                imdbInfoDeleted = true;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new DeleteTitleResponse(imdbInfoDeleted);
    }

    public async Task<PageResponse<OccurrenceResponse>> GetOccurrencesAsync(
        long titleId,
        OccurrencesQuery query,
        CancellationToken cancellationToken)
    {
        var page = query.Page ?? 1;
        var pageSize = query.PageSize ?? 25;
        var titleExists = await dbContext.TorrentMovies.AnyAsync(title => title.Id == titleId, cancellationToken);
        if (!titleExists)
        {
            throw new ApiNotFoundException("Title not found.");
        }

        var occurrences = dbContext.TorrentReleases
            .AsNoTracking()
            .Where(occurrence => occurrence.TorrentMovieId == titleId);
        var totalCount = await occurrences.CountAsync(cancellationToken);
        var items = await occurrences
            .OrderByDescending(occurrence => occurrence.LastSeenAt)
            .ThenByDescending(occurrence => occurrence.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(occurrence => new OccurrenceResponse(
                occurrence.Id,
                occurrence.TorrentMovieId,
                occurrence.SourceId,
                occurrence.Source.Name,
                occurrence.SourceItemKey,
                occurrence.FeedEntryId,
                occurrence.TorrentUrl,
                occurrence.RawTitle,
                occurrence.SourceFeedName,
                occurrence.FeedType,
                occurrence.SourcePublishedAt,
                occurrence.ObservedAt,
                occurrence.Quality,
                occurrence.RipType,
                occurrence.FirstSeenAt,
                occurrence.LastSeenAt))
            .ToListAsync(cancellationToken);

        return CreatePage(items, page, pageSize, totalCount);
    }

    private static PageResponse<T> CreatePage<T>(
        IReadOnlyList<T> items,
        int page,
        int pageSize,
        int totalCount,
        YearBounds? yearBounds = null,
        ImdbRatingBounds? imdbRatingBounds = null) =>
        new(items, page, pageSize, totalCount, totalCount == 0 ? 0 : (totalCount + pageSize - 1) / pageSize, yearBounds, imdbRatingBounds);
}