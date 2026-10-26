using MediaDock.Infrastructure.Persistence;
using MediaDock.Application.PersonalRatings;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.Api.Administration;

internal interface IMovieTablesDashboardApiService
{
    Task<MovieTablesDashboardResponse> GetDashboardSummaryAsync(CancellationToken cancellationToken);
}

internal sealed class MovieTablesDashboardApiService(
    MediaDockDbContext dbContext,
    TimeProvider timeProvider) : IMovieTablesDashboardApiService
{
    public async Task<MovieTablesDashboardResponse> GetDashboardSummaryAsync(CancellationToken cancellationToken)
    {
        var imdbTotal = await dbContext.ImdbTitles.AsNoTracking().CountAsync(cancellationToken);
        var imdbByMediaType = await dbContext.ImdbTitles.AsNoTracking()
            .GroupBy(title => title.MediaType)
            .Select(group => new { Type = group.Key ?? "unspecified", Count = group.Count() })
            .ToDictionaryAsync(item => item.Type, item => item.Count, cancellationToken);
        var imdbByMetadataStatus = await dbContext.ImdbTitles.AsNoTracking()
            .GroupBy(title => title.MetadataStatus)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Status, item => item.Count, cancellationToken);

        imdbByMetadataStatus.TryGetValue(ImdbTitleEnrichmentStatusValues.Enriched, out var imdbEnriched);
        imdbByMetadataStatus.TryGetValue(ImdbTitleEnrichmentStatusValues.Pending, out var imdbPending);
        imdbByMetadataStatus.TryGetValue(ImdbTitleEnrichmentStatusValues.NotFound, out var imdbNotFound);
        imdbByMetadataStatus.TryGetValue(ImdbTitleEnrichmentStatusValues.TemporaryError, out var imdbTemporaryError);

        var imdbSummary = new ImdbTitlesSummary(
            TotalCount: imdbTotal,
            ByMediaType: imdbByMediaType,
            ByMetadataStatus: imdbByMetadataStatus,
            EnrichedCount: imdbEnriched,
            PendingCount: imdbPending,
            NotFoundCount: imdbNotFound,
            TemporaryErrorCount: imdbTemporaryError);

        var goldenGlobeTotal = await dbContext.GoldenGlobeTitles.AsNoTracking().CountAsync(cancellationToken);
        var goldenGlobeByNomineeType = await dbContext.GoldenGlobeTitles.AsNoTracking()
            .GroupBy(title => title.NomineeType)
            .Select(group => new { Type = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Type, item => item.Count, cancellationToken);
        var goldenGlobeByEnrichmentStatus = await dbContext.GoldenGlobeTitles.AsNoTracking()
            .GroupBy(title => title.EnrichmentStatus)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Status, item => item.Count, cancellationToken);
        var goldenGlobeWithImdb = await dbContext.GoldenGlobeTitles.AsNoTracking()
            .CountAsync(title => title.ImdbId != null, cancellationToken);
        var goldenGlobeNominations = await dbContext.GoldenGlobeNominations.AsNoTracking()
            .CountAsync(cancellationToken);

        var goldenGlobesSummary = new GoldenGlobeTitlesSummary(
            TotalCount: goldenGlobeTotal,
            ByNomineeType: goldenGlobeByNomineeType,
            ByEnrichmentStatus: goldenGlobeByEnrichmentStatus,
            WithImdbLinkCount: goldenGlobeWithImdb,
            WithoutImdbLinkCount: goldenGlobeTotal - goldenGlobeWithImdb,
            TotalNominationsCount: goldenGlobeNominations);

        var oscarTotal = await dbContext.OscarMovies.AsNoTracking().CountAsync(cancellationToken);
        var oscarByEnrichmentStatus = await dbContext.OscarMovies.AsNoTracking()
            .GroupBy(movie => movie.EnrichmentStatus)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Status, item => item.Count, cancellationToken);
        var oscarWithImdb = await dbContext.OscarMovies.AsNoTracking()
            .CountAsync(movie => movie.ImdbId != null, cancellationToken);
        var oscarNominations = await dbContext.OscarNominations.AsNoTracking()
            .CountAsync(cancellationToken);

        var oscarSummary = new OscarMoviesSummary(
            TotalCount: oscarTotal,
            ByEnrichmentStatus: oscarByEnrichmentStatus,
            WithImdbLinkCount: oscarWithImdb,
            WithoutImdbLinkCount: oscarTotal - oscarWithImdb,
            TotalNominationsCount: oscarNominations);

        return new MovieTablesDashboardResponse(
            Imdb: imdbSummary,
            GoldenGlobes: goldenGlobesSummary,
            Oscar: oscarSummary,
            GeneratedAtUtc: timeProvider.GetUtcNow());
    }
}