using MediaDock.Api.Common;
using MediaDock.Api.Middleware;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.Api.IgnoredMovies;

internal sealed class IgnoredMoviesApiService(MediaDockDbContext dbContext)
{
    public async Task<PageResponse<IgnoredMovieResponse>> GetAsync(
        long userId,
        IgnoredMoviesQuery query,
        CancellationToken cancellationToken)
    {
        var page = query.Page ?? 1;
        var pageSize = query.PageSize ?? 25;
        var ignoredMovies = dbContext.UserIgnoredMovies.AsNoTracking()
            .Where(movie => movie.UserId == userId);
        var totalCount = await ignoredMovies.CountAsync(cancellationToken);
        var items = await Project(ignoredMovies
                .OrderByDescending(movie => movie.CreatedAt)
                .ThenBy(movie => movie.ImdbId)
                .Skip((page - 1) * pageSize)
                .Take(pageSize))
            .ToListAsync(cancellationToken);

        return new PageResponse<IgnoredMovieResponse>(
            items,
            page,
            pageSize,
            totalCount,
            totalCount == 0 ? 0 : (totalCount + pageSize - 1) / pageSize);
    }

    public async Task<IgnoredMovieResponse> AddAsync(
        long userId,
        CreateIgnoredMovieRequest request,
        CancellationToken cancellationToken)
    {
        var imdbId = request.ImdbId.Trim().ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO user_ignored_movies (user_id, imdb_id, created_at)
            SELECT {userId}, imdb_id, {now}
            FROM imdb_titles
            WHERE imdb_id = {imdbId} AND media_type = 'movie'
            ON CONFLICT (user_id, imdb_id) DO NOTHING
            """, cancellationToken);

        return await GetOneAsync(userId, imdbId, cancellationToken)
            ?? throw new ApiNotFoundException("IMDb movie not found.");
    }

    public Task<int> DeleteAsync(long userId, string imdbId, CancellationToken cancellationToken) =>
        dbContext.UserIgnoredMovies
            .Where(movie => movie.UserId == userId && movie.ImdbId == imdbId.Trim().ToLowerInvariant())
            .ExecuteDeleteAsync(cancellationToken);

    private async Task<IgnoredMovieResponse?> GetOneAsync(
        long userId,
        string imdbId,
        CancellationToken cancellationToken) =>
        await Project(dbContext.UserIgnoredMovies.AsNoTracking()
                .Where(movie => movie.UserId == userId && movie.ImdbId == imdbId))
            .FirstOrDefaultAsync(cancellationToken);

    private static IQueryable<IgnoredMovieResponse> Project(IQueryable<UserIgnoredMovie> ignoredMovies) =>
        ignoredMovies.Select(movie => new IgnoredMovieResponse(
            movie.ImdbId,
            movie.ImdbTitle.TitleText ?? movie.ImdbId,
            movie.ImdbTitle.Year,
            movie.ImdbTitle.MediaType,
            movie.ImdbTitle.ImdbRating,
            movie.ImdbTitle.PosterUrl,
            movie.CreatedAt));
}