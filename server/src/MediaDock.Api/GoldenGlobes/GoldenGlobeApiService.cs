using MediaDock.Api.Common;
using MediaDock.Api.Middleware;
using MediaDock.Application.GoldenGlobes;
using MediaDock.Application.Metadata;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.Api.GoldenGlobes;

internal interface IGoldenGlobeApiService
{
    Task<PageResponse<GoldenGlobeFilmResponse>> GetFilmsAsync(GoldenGlobeCatalogQuery query, CancellationToken cancellationToken);
}

internal sealed class GoldenGlobeApiService(MediaDockDbContext dbContext, IMetadataCacheStore metadataCacheStore) : IGoldenGlobeApiService
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
        var groups = rows.GroupBy(x => new { x.Title, x.Year, x.NomineeType })
            .Select(group => new GoldenGlobeFilmResponse(
                $"{group.Key.Year}:{group.Key.NomineeType}:{group.Key.Title}", group.Key.Title, group.Key.Year, group.Key.NomineeType,
                group.Select(x => x.ImdbId).FirstOrDefault(x => x != null),
                null, null,
                GetEnrichmentStatus(group.Select(x => x.EnrichmentStatus)),
                group.Select(x => x.LastEnrichmentError).FirstOrDefault(x => x != null),
                group.OrderBy(x => x.Award.Name).ThenBy(x => x.Id).Select(x => new GoldenGlobeNominationResponse(x.Id, x.Year, x.Award.Name, x.Winner)).ToArray()))
            .OrderByDescending(x => x.Year).ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.NomineeType, StringComparer.Ordinal).ToArray();
        var filteredGroups = string.IsNullOrWhiteSpace(query.EnrichmentStatus)
            ? groups
            : groups.Where(film => film.EnrichmentStatus == query.EnrichmentStatus).ToArray();
        var pageFilms = filteredGroups.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        var items = new List<GoldenGlobeFilmResponse>(pageFilms.Length);
        foreach (var film in pageFilms)
        {
            if (film.EnrichmentStatus != GoldenGlobeEnrichmentStatuses.Enriched || film.ImdbId is null)
            {
                items.Add(film);
                continue;
            }

            var metadata = await metadataCacheStore.GetByTitleAsync(NormalizeTitle(film.Title), "movie", cancellationToken);
            var details = metadata?.Metadata is { } candidate
                && ImdbIdNormalizer.IsCompatible(film.ImdbId, candidate.ImdbId)
                    ? candidate
                    : null;
            items.Add(film with { PosterUrl = details?.PosterUrl, ImdbRating = details?.ImdbRating });
        }

        return new PageResponse<GoldenGlobeFilmResponse>(items, page, pageSize, filteredGroups.Length, filteredGroups.Length == 0 ? 0 : (filteredGroups.Length + pageSize - 1) / pageSize);
    }

    private static string GetEnrichmentStatus(IEnumerable<string> statuses)
    {
        var distinct = statuses.ToHashSet(StringComparer.Ordinal);
        foreach (var status in new[]
        {
            GoldenGlobeEnrichmentStatuses.Pending,
            GoldenGlobeEnrichmentStatuses.TemporaryError,
            GoldenGlobeEnrichmentStatuses.Problem,
            GoldenGlobeEnrichmentStatuses.NotFound,
            GoldenGlobeEnrichmentStatuses.Enriched
        })
        {
            if (distinct.Contains(status)) return status;
        }

        return GoldenGlobeEnrichmentStatuses.Pending;
    }

    private static string NormalizeTitle(string title) =>
        string.Join(' ', title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
}
