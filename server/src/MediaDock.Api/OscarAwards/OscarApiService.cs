using MediaDock.Api.Common;
using MediaDock.Api.Middleware;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.Api.OscarAwards;

internal interface IOscarApiService
{
    Task<PageResponse<OscarFilmResponse>> GetOscarFilmsAsync(
        OscarCatalogQuery query,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken);
    Task<OscarFilmResponse> GetOscarFilmAsync(long id, CancellationToken cancellationToken);
    Task<IReadOnlyList<OscarFilmResponse>> GetByTitleAsync(long titleId, CancellationToken cancellationToken);
}

internal sealed class OscarApiService(MediaDockDbContext dbContext) : IOscarApiService
{
    public async Task<PageResponse<OscarFilmResponse>> GetOscarFilmsAsync(
        OscarCatalogQuery query,
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

        IQueryable<OscarFilm> films = dbContext.OscarFilms.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search.Trim()}%";
            films = films.Where(film => EF.Functions.ILike(film.FilmTitle, pattern)
                || EF.Functions.ILike(film.NormalizedTitle, pattern)
                || EF.Functions.ILike(film.Title.TitleText, pattern)
                || EF.Functions.ILike(film.Title.NormalizedTitle, pattern));
        }

        if (query.YearFrom is { } minimumYear)
        {
            films = films.Where(film => film.FilmYear >= minimumYear);
        }

        if (query.YearTo is { } maximumYear)
        {
            films = films.Where(film => film.FilmYear <= maximumYear);
        }

        var categoryValues = query.Categories is { Length: > 0 }
            ? query.Categories
            : query.Category is not null ? [query.Category] : Array.Empty<string>();
        var categoryFilterActive = query.CategoryFilter == true
            || query.Category is not null
            || query.Categories is { Length: > 0 };
        if (categoryValues.Any(category => category.Length > 100))
        {
            throw new ApiValidationException(new Dictionary<string, string[]>
            {
                [nameof(query.Categories)] = ["Each category must be 100 characters or fewer."]
            });
        }

        var categories = categoryFilterActive
            ? categoryValues
            .Select(category => category.Trim().ToUpperInvariant())
            .Where(category => category.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray()
            : null;
        var result = query.Result;
        if (categoryFilterActive || result is not null)
        {
            films = films.Where(film => film.Nominations.Any(nomination =>
                (categories == null || (categories.Length > 0 && categories.Contains(nomination.CanonicalCategory.ToUpper())))
                && (result == null
                    || (result == "winner" && nomination.IsWinner)
                    || (result == "nominee" && !nomination.IsWinner))));
        }

        if (!string.IsNullOrWhiteSpace(query.EnrichmentStatus))
        {
            films = films.Where(film => film.EnrichmentStatus == query.EnrichmentStatus);
        }

        var totalCount = await films.CountAsync(cancellationToken);
        var items = await films
            .Include(film => film.Title)
            .Include(film => film.Nominations)
            .OrderByDescending(film => film.FilmYear)
            .ThenBy(film => film.NormalizedTitle)
            .ThenBy(film => film.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return CreatePage(items.Select(ToResponse).ToArray(), page, pageSize, totalCount);
    }

    public async Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken) =>
        await dbContext.OscarNominations
            .AsNoTracking()
            .Select(nomination => nomination.CanonicalCategory)
            .Distinct()
            .OrderBy(category => category)
            .ToArrayAsync(cancellationToken);

    public async Task<OscarFilmResponse> GetOscarFilmAsync(long id, CancellationToken cancellationToken)
    {
        var film = await dbContext.OscarFilms
            .AsNoTracking()
            .Include(candidate => candidate.Title)
            .Include(candidate => candidate.Nominations)
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        return film is null
            ? throw new ApiNotFoundException("Oscar film not found.")
            : ToResponse(film);
    }

    public async Task<IReadOnlyList<OscarFilmResponse>> GetByTitleAsync(long titleId, CancellationToken cancellationToken)
    {
        if (!await dbContext.Titles.AnyAsync(title => title.Id == titleId, cancellationToken))
            throw new ApiNotFoundException("Title not found.");

        var films = await dbContext.OscarFilms.AsNoTracking()
            .Where(film => film.TitleId == titleId)
            .Include(film => film.Title)
            .Include(film => film.Nominations)
            .OrderBy(film => film.Id)
            .ToListAsync(cancellationToken);
        return films.Select(ToResponse).ToArray();
    }

    private static OscarFilmResponse ToResponse(OscarFilm film) => new(
        film.Id,
        film.TitleId,
        film.FilmTitle,
        film.Title.TitleText,
        film.Title.Year,
        film.FilmYear,
        film.ImdbId ?? film.Title.ImdbId,
        film.EnrichmentStatus,
        film.EnrichmentAttemptCount,
        film.LastEnrichmentAttemptAt,
        film.NextEnrichmentAttemptAt,
        film.LastEnrichmentError,
        film.Title.MediaType,
        film.Title.ImdbRating,
        film.Title.ImdbVotes,
        film.Title.Metascore,
        film.Title.Genres,
        film.Title.Countries,
        film.Title.Director,
        film.Title.Plot,
        film.Title.PosterUrl,
        film.Title.Runtime,
        film.Title.Awards,
        film.Title.BoxOffice,
        film.Nominations
            .OrderBy(nomination => nomination.Ceremony)
            .ThenBy(nomination => nomination.CanonicalCategory)
            .ThenBy(nomination => nomination.Id)
            .Select(nomination => new OscarNominationResponse(
                nomination.Id,
                nomination.Ceremony,
                nomination.Class,
                nomination.CanonicalCategory,
                nomination.Category,
                nomination.Name,
                nomination.Nominees,
                nomination.NomineeIds,
                nomination.Detail,
                nomination.IsWinner))
            .ToArray());

    private static PageResponse<T> CreatePage<T>(IReadOnlyList<T> items, int page, int pageSize, int totalCount) =>
        new(items, page, pageSize, totalCount, totalCount == 0 ? 0 : (totalCount + pageSize - 1) / pageSize);
}