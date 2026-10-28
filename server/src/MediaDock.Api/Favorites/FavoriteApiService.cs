using MediaDock.Api.Common;
using MediaDock.Api.Middleware;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.Api.Favorites;

internal sealed class FavoriteApiService(MediaDockDbContext dbContext)
{
    public async Task<PageResponse<FavoriteMovieResponse>> GetAsync(
        long userId,
        FavoriteQuery query,
        CancellationToken cancellationToken)
    {
        var page = query.Page ?? 1;
        var pageSize = query.PageSize ?? 25;
        IQueryable<ImdbFavoriteMovie> favorites = dbContext.ImdbFavoriteMovies.AsNoTracking()
            .Where(favorite => favorite.UserId == userId);
        if (query.Status == "to_watch") favorites = favorites.Where(favorite => favorite.ToWatch);
        if (query.Status == "to_download") favorites = favorites.Where(favorite => favorite.ToDownload);

        var totalCount = await favorites.CountAsync(cancellationToken);
        var items = await Project(favorites.OrderByDescending(favorite => favorite.UpdatedAt)
            .ThenByDescending(favorite => favorite.ImdbId).Skip((page - 1) * pageSize).Take(pageSize))
            .ToListAsync(cancellationToken);
        return new PageResponse<FavoriteMovieResponse>(items, page, pageSize, totalCount,
            totalCount == 0 ? 0 : (totalCount + pageSize - 1) / pageSize);
    }

    public async Task<FavoriteMovieResponse> AddAsync(
        long userId,
        CreateFavoriteRequest request,
        CancellationToken cancellationToken)
    {
        var imdbId = request.ImdbId.Trim().ToLowerInvariant();
        var fromOscar = request.From == "oscar";
        var fromCatalog = request.From == "catalog";
        var fromRecommendations = request.From == "recommendation";
        var toWatch = fromOscar || fromRecommendations;
        var now = DateTimeOffset.UtcNow;
        var changed = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO favorite_movies (user_id, imdb_id, to_watch, to_download, added_from_oscar, added_from_catalog, added_from_recommendations, created_at, updated_at)
            SELECT {userId}, imdb_id, {toWatch}, {fromCatalog}, {fromOscar}, {fromCatalog}, {fromRecommendations}, {now}, {now}
            FROM imdb_titles
            WHERE imdb_id = {imdbId}
                AND metadata_status = 'enriched'
                AND (media_type = 'movie' OR ({fromRecommendations} AND media_type = 'series'))
            ON CONFLICT (user_id, imdb_id) DO UPDATE SET
                to_watch = favorite_movies.to_watch OR EXCLUDED.added_from_recommendations OR (EXCLUDED.added_from_oscar AND NOT favorite_movies.added_from_oscar),
                to_download = favorite_movies.to_download OR (EXCLUDED.added_from_catalog AND NOT favorite_movies.added_from_catalog),
                added_from_oscar = favorite_movies.added_from_oscar OR EXCLUDED.added_from_oscar,
                added_from_catalog = favorite_movies.added_from_catalog OR EXCLUDED.added_from_catalog,
                added_from_recommendations = favorite_movies.added_from_recommendations OR EXCLUDED.added_from_recommendations,
                updated_at = EXCLUDED.updated_at
            """, cancellationToken);
                if (changed == 0) throw new ApiNotFoundException("Enriched IMDb title not found.");
                return await GetOneAsync(userId, imdbId, cancellationToken);
    }

    public async Task<FavoriteMovieResponse> UpdateAsync(
        long userId,
                string imdbId,
        UpdateFavoriteRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ToWatch is null && request.ToDownload is null)
            throw new ApiValidationException(new Dictionary<string, string[]> { ["body"] = ["Provide at least one marker."] });

        imdbId = imdbId.Trim().ToLowerInvariant();
        var favorites = dbContext.ImdbFavoriteMovies
            .Where(favorite => favorite.UserId == userId && favorite.ImdbId == imdbId);

        var updated = await favorites.ExecuteUpdateAsync(setters => setters
            .SetProperty(favorite => favorite.ToWatch, favorite => request.ToWatch ?? favorite.ToWatch)
            .SetProperty(favorite => favorite.ToDownload, favorite => request.ToDownload ?? favorite.ToDownload)
            .SetProperty(favorite => favorite.UpdatedAt, DateTimeOffset.UtcNow), cancellationToken);
        if (updated == 0) throw new ApiNotFoundException("Favorite not found.");
        return await GetOneAsync(userId, imdbId, cancellationToken);
    }

    public Task<int> DeleteAsync(long userId, string imdbId, CancellationToken cancellationToken) =>
        dbContext.ImdbFavoriteMovies
            .Where(favorite => favorite.UserId == userId && favorite.ImdbId == imdbId.Trim().ToLowerInvariant())
            .ExecuteDeleteAsync(cancellationToken);

    private async Task<FavoriteMovieResponse> GetOneAsync(
        long userId,
        string imdbId,
        CancellationToken cancellationToken) =>
        await Project(dbContext.ImdbFavoriteMovies.AsNoTracking()
                .Where(favorite => favorite.UserId == userId && favorite.ImdbId == imdbId))
            .FirstOrDefaultAsync(cancellationToken) ?? throw new ApiNotFoundException("Favorite not found.");

    private IQueryable<FavoriteMovieResponse> Project(IQueryable<ImdbFavoriteMovie> favorites) =>
        favorites.Select(favorite => new FavoriteMovieResponse(
            favorite.ImdbId,
            dbContext.TorrentMovies.Where(movie => movie.ImdbId == favorite.ImdbId)
                .Select(movie => (long?)movie.Id).FirstOrDefault(),
            favorite.ImdbTitle.TitleText ?? favorite.ImdbId,
            favorite.ImdbTitle.Year,
            favorite.ImdbTitle.MediaType ?? "movie",
            favorite.ImdbTitle.MetadataStatus,
            favorite.ImdbTitle.ImdbRating,
            favorite.ImdbTitle.ImdbVotes,
            favorite.ImdbTitle.Metascore,
            favorite.ImdbTitle.Genres,
            favorite.ImdbTitle.Countries,
            favorite.ImdbTitle.Director,
            favorite.ImdbTitle.Plot,
            favorite.ImdbTitle.Runtime,
            favorite.ImdbTitle.Awards,
            favorite.ImdbTitle.BoxOffice,
            favorite.ImdbTitle.PosterUrl,
            favorite.ToWatch, favorite.ToDownload,
            favorite.AddedFromOscar, favorite.AddedFromCatalog, favorite.AddedFromRecommendations,
            favorite.CreatedAt, favorite.UpdatedAt,
            dbContext.OscarMovies.Count(film => film.ImdbId == favorite.ImdbId),
            dbContext.OscarNominations.Count(nomination => nomination.OscarMovie != null
                && nomination.OscarMovie.ImdbId == favorite.ImdbId),
            dbContext.OscarNominations.Count(nomination => nomination.OscarMovie != null
                && nomination.OscarMovie.ImdbId == favorite.ImdbId && nomination.IsWinner),
            dbContext.TorrentReleases.Count(release => release.TorrentMovie.ImdbId == favorite.ImdbId),
            dbContext.TorrentReleases.Where(release => release.TorrentMovie.ImdbId == favorite.ImdbId)
                .Max(release => (DateTimeOffset?)release.LastSeenAt)));
}