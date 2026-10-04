using MediaDock.Api.Common;
using MediaDock.Api.Middleware;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.Api.GoldenGlobes;

internal interface IGoldenGlobeApiService
{
    Task<PageResponse<GoldenGlobeFilmResponse>> GetFilmsAsync(GoldenGlobeCatalogQuery query, CancellationToken cancellationToken);
}

internal sealed class GoldenGlobeApiService(MediaDockDbContext dbContext) : IGoldenGlobeApiService
{
    public async Task<PageResponse<GoldenGlobeFilmResponse>> GetFilmsAsync(GoldenGlobeCatalogQuery query, CancellationToken cancellationToken)
    {
        var page = query.Page ?? 1;
        var pageSize = query.PageSize ?? 25;
        if (query.YearFrom is { } from && query.YearTo is { } to && from > to)
            throw new ApiValidationException(new Dictionary<string, string[]> { [nameof(query.YearTo)] = ["YearTo must be greater than or equal to YearFrom."] });

        IQueryable<GoldenGlobeNomination> nominations = dbContext.GoldenGlobeNominations.AsNoTracking().Include(x => x.Award);
        if (!string.IsNullOrWhiteSpace(query.Search))
            nominations = nominations.Where(x => EF.Functions.ILike(x.Title, $"%{query.Search.Trim()}%"));
        if (query.YearFrom is { } yearFrom) nominations = nominations.Where(x => x.Year >= yearFrom);
        if (query.YearTo is { } yearTo) nominations = nominations.Where(x => x.Year <= yearTo);
        if (!string.IsNullOrWhiteSpace(query.Award))
            nominations = nominations.Where(x => EF.Functions.ILike(x.Award.Name, query.Award.Trim()));
        if (query.Result == "winner") nominations = nominations.Where(x => x.Winner);
        if (query.Result == "nominee") nominations = nominations.Where(x => !x.Winner);

        var rows = await nominations.OrderByDescending(x => x.Year).ThenBy(x => x.Title).ThenBy(x => x.Id).ToListAsync(cancellationToken);
        var groups = rows.GroupBy(x => new { x.Title, x.Year })
            .Select(group => new GoldenGlobeFilmResponse(
                $"{group.Key.Year}:{group.Key.Title}", group.Key.Title, group.Key.Year, group.Select(x => x.ImdbId).FirstOrDefault(x => x != null),
                group.Select(x => x.EnrichmentStatus).Distinct().SingleOrDefault() ?? "pending",
                group.Select(x => x.LastEnrichmentError).FirstOrDefault(x => x != null),
                group.OrderBy(x => x.Award.Name).ThenBy(x => x.Id).Select(x => new GoldenGlobeNominationResponse(x.Id, x.Year, x.Award.Name, x.Winner)).ToArray()))
            .OrderByDescending(x => x.Year).ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase).ToArray();
        return new PageResponse<GoldenGlobeFilmResponse>(groups.Skip((page - 1) * pageSize).Take(pageSize).ToArray(), page, pageSize, groups.Length, groups.Length == 0 ? 0 : (groups.Length + pageSize - 1) / pageSize);
    }
}
