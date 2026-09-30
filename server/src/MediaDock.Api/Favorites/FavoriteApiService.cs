using MediaDock.Api.Common;
using MediaDock.Api.Middleware;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.Api.Favorites;

internal sealed class FavoriteApiService(MediaDockDbContext dbContext)
{
    public async Task<PageResponse<FavoriteMovieResponse>> GetAsync(FavoriteQuery query, CancellationToken cancellationToken)
    {
        var page = query.Page ?? 1;
        var pageSize = query.PageSize ?? 25;
        IQueryable<FavoriteMovie> favorites = dbContext.FavoriteMovies.AsNoTracking();
        if (query.Status == "to_watch") favorites = favorites.Where(favorite => favorite.ToWatch);
        if (query.Status == "to_download") favorites = favorites.Where(favorite => favorite.ToDownload);

        var totalCount = await favorites.CountAsync(cancellationToken);
        var items = await Project(favorites.OrderByDescending(favorite => favorite.UpdatedAt)
                .ThenByDescending(favorite => favorite.TitleId).Skip((page - 1) * pageSize).Take(pageSize))
            .ToListAsync(cancellationToken);
        return new PageResponse<FavoriteMovieResponse>(items, page, pageSize, totalCount,
            totalCount == 0 ? 0 : (totalCount + pageSize - 1) / pageSize);
    }

    public async Task<FavoriteMovieResponse> AddAsync(CreateFavoriteRequest request, CancellationToken cancellationToken)
    {
        var fromOscar = request.From == "oscar";
        var fromCatalog = request.From == "catalog";
        var now = DateTimeOffset.UtcNow;
        var changed = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO favorite_movies (title_id, to_watch, to_download, added_from_oscar, added_from_catalog, created_at, updated_at)
            SELECT id, {fromOscar}, {fromCatalog}, {fromOscar}, {fromCatalog}, {now}, {now}
            FROM titles WHERE id = {request.TitleId} AND media_type = 'movie'
              AND (({fromOscar} AND EXISTS (SELECT 1 FROM oscar_films WHERE title_id = titles.id))
                OR ({fromCatalog} AND EXISTS (SELECT 1 FROM occurrences WHERE title_id = titles.id)))
            ON CONFLICT (title_id) DO UPDATE SET
              to_watch = favorite_movies.to_watch OR (EXCLUDED.added_from_oscar AND NOT favorite_movies.added_from_oscar),
              to_download = favorite_movies.to_download OR (EXCLUDED.added_from_catalog AND NOT favorite_movies.added_from_catalog),
              added_from_oscar = favorite_movies.added_from_oscar OR EXCLUDED.added_from_oscar,
              added_from_catalog = favorite_movies.added_from_catalog OR EXCLUDED.added_from_catalog,
              updated_at = EXCLUDED.updated_at
            """, cancellationToken);
        if (changed == 0) throw new ApiNotFoundException("Movie or catalog source not found.");
        return await GetOneAsync(request.TitleId, cancellationToken);
    }

    public async Task<FavoriteMovieResponse> UpdateAsync(long titleId, UpdateFavoriteRequest request, CancellationToken cancellationToken)
    {
        if (request.ToWatch is null && request.ToDownload is null)
            throw new ApiValidationException(new Dictionary<string, string[]> { ["body"] = ["Provide at least one marker."] });

        var favorites = dbContext.FavoriteMovies.Where(favorite => favorite.TitleId == titleId);
        if (request.ToDownload == true && !await dbContext.Occurrences.AnyAsync(item => item.TitleId == titleId, cancellationToken))
        {
            if (!await favorites.AnyAsync(cancellationToken)) throw new ApiNotFoundException("Favorite not found.");
            throw new ApiValidationException(new Dictionary<string, string[]> { [nameof(request.ToDownload)] = ["A torrent occurrence is required."] });
        }

        var updated = await favorites.ExecuteUpdateAsync(setters => setters
            .SetProperty(favorite => favorite.ToWatch, favorite => request.ToWatch ?? favorite.ToWatch)
            .SetProperty(favorite => favorite.ToDownload, favorite => request.ToDownload ?? favorite.ToDownload)
            .SetProperty(favorite => favorite.UpdatedAt, DateTimeOffset.UtcNow), cancellationToken);
        if (updated == 0) throw new ApiNotFoundException("Favorite not found.");
        return await GetOneAsync(titleId, cancellationToken);
    }

    public Task<int> DeleteAsync(long titleId, CancellationToken cancellationToken) =>
        dbContext.FavoriteMovies.Where(favorite => favorite.TitleId == titleId).ExecuteDeleteAsync(cancellationToken);

    private async Task<FavoriteMovieResponse> GetOneAsync(long titleId, CancellationToken cancellationToken) =>
        await Project(dbContext.FavoriteMovies.AsNoTracking().Where(favorite => favorite.TitleId == titleId))
            .FirstOrDefaultAsync(cancellationToken) ?? throw new ApiNotFoundException("Favorite not found.");

    private IQueryable<FavoriteMovieResponse> Project(IQueryable<FavoriteMovie> favorites) =>
        favorites.Select(favorite => new FavoriteMovieResponse(
            favorite.TitleId, favorite.Title.TitleText, favorite.Title.Year, favorite.Title.MediaType,
            favorite.Title.ImdbRating, favorite.Title.PosterUrl, favorite.ToWatch, favorite.ToDownload,
            favorite.AddedFromOscar, favorite.AddedFromCatalog, favorite.CreatedAt, favorite.UpdatedAt,
            dbContext.OscarFilms.Count(film => film.TitleId == favorite.TitleId),
            dbContext.OscarNominations.Count(nomination => nomination.OscarFilm.TitleId == favorite.TitleId),
            dbContext.OscarNominations.Count(nomination => nomination.OscarFilm.TitleId == favorite.TitleId && nomination.IsWinner),
            dbContext.Occurrences.Count(occurrence => occurrence.TitleId == favorite.TitleId),
            dbContext.Occurrences.Where(occurrence => occurrence.TitleId == favorite.TitleId)
                .Max(occurrence => (DateTimeOffset?)occurrence.LastSeenAt)));
}