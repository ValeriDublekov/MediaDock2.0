using MediaDock.Api.Common;
using MediaDock.Api.BackgroundJobs;
using MediaDock.Api.Middleware;
using MediaDock.Application.GoldenGlobes;
using MediaDock.Application.Metadata;
using MediaDock.Infrastructure.Metadata;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace MediaDock.Api.GoldenGlobes;

internal interface IGoldenGlobeApiService
{
    Task<PageResponse<GoldenGlobeFilmResponse>> GetFilmsAsync(GoldenGlobeCatalogQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken);
    Task<GoldenGlobeImdbLinkResponse> SetImdbIdAsync(GoldenGlobeImdbLinkRequest request, CancellationToken cancellationToken);
}

internal sealed class GoldenGlobeApiService(MediaDockDbContext dbContext, TimeProvider timeProvider) : IGoldenGlobeApiService
{
    public async Task<PageResponse<GoldenGlobeFilmResponse>> GetFilmsAsync(GoldenGlobeCatalogQuery query, CancellationToken cancellationToken)
    {
        var page = query.Page ?? 1;
        var pageSize = query.PageSize ?? 25;
        if (query.YearFrom is { } from && query.YearTo is { } to && from > to)
            throw new ApiValidationException(new Dictionary<string, string[]> { [nameof(query.YearTo)] = ["YearTo must be greater than or equal to YearFrom."] });
        if (query.ImdbRatingFrom is { } ratingFrom && query.ImdbRatingTo is { } ratingTo && ratingFrom > ratingTo)
            throw new ApiValidationException(new Dictionary<string, string[]> { [nameof(query.ImdbRatingTo)] = ["ImdbRatingTo must be greater than or equal to ImdbRatingFrom."] });

        var yearBounds = await dbContext.GoldenGlobeTitles
            .AsNoTracking()
            .Where(group => group.Nominations.Any())
            .GroupBy(_ => 1)
            .Select(group => new YearBounds(group.Min(film => film.Year), group.Max(film => film.Year)))
            .FirstOrDefaultAsync(cancellationToken);
        var (metadataByFilmId, imdbRatingBounds) = await GetRatingMetadataAsync(cancellationToken);

        IQueryable<GoldenGlobeNomination> nominations = dbContext.GoldenGlobeNominations
            .AsNoTracking()
            .Include(x => x.Award)
            .Include(x => x.GoldenGlobeTitle)
            .Where(x => x.GoldenGlobeTitleId != null);
        if (!string.IsNullOrWhiteSpace(query.Search))
            nominations = nominations.Where(x => EF.Functions.ILike(x.GoldenGlobeTitle!.Title, $"%{query.Search.Trim()}%"));
        if (query.YearFrom is { } yearFrom) nominations = nominations.Where(x => x.GoldenGlobeTitle!.Year >= yearFrom);
        if (query.YearTo is { } yearTo) nominations = nominations.Where(x => x.GoldenGlobeTitle!.Year <= yearTo);
        if (!string.IsNullOrWhiteSpace(query.Award))
            nominations = nominations.Where(x => EF.Functions.ILike(x.Award.Name, $"%{query.Award.Trim()}%"));
        var rows = await nominations
            .OrderByDescending(x => x.GoldenGlobeTitle!.Year)
            .ThenBy(x => x.GoldenGlobeTitle!.Title)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var categoryValues = query.Categories ?? Array.Empty<string>();
        var categoryFilterActive = query.CategoryFilter == true
            || query.Categories is { Length: > 0 };
        if (categoryValues.Any(award => award.Length > 100))
            throw new ApiValidationException(new Dictionary<string, string[]> { [nameof(query.Categories)] = ["Each award category must be 100 characters or fewer."] });

        var awards = categoryFilterActive
            ? categoryValues
            .Select(award => award.Trim())
            .Where(award => award.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;
        if (!categoryFilterActive && query.Result == "winner") rows = rows.Where(x => x.Winner).ToList();
        if (!categoryFilterActive && query.Result == "nominee") rows = rows.Where(x => !x.Winner).ToList();
        var matchingGroups = rows
            .Where(row => (awards == null || awards.Contains(row.Award.Name))
                && (categoryFilterActive
                    ? query.Result == null
                    || (query.Result == "winner" && row.Winner)
                    || (query.Result == "nominee" && !row.Winner)
                    : true))
            .Select(row => row.GoldenGlobeTitleId!.Value)
            .ToHashSet();
        var groups = rows.GroupBy(x => x.GoldenGlobeTitleId!.Value)
            .Where(group => !categoryFilterActive || matchingGroups.Contains(group.Key))
            .Select(group =>
            {
                var film = group.First().GoldenGlobeTitle!;
                var imdbId = ImdbIdNormalizer.Normalize(film.ImdbId);
                if (!ImdbIdNormalizer.IsValid(imdbId)) imdbId = null;
                var enrichmentStatus = film.EnrichmentStatus == GoldenGlobeEnrichmentStatuses.Enriched && imdbId is null
                    ? GoldenGlobeEnrichmentStatuses.Pending
                    : film.EnrichmentStatus;
                return new GoldenGlobeFilmResponse(
                    $"{film.Year}:{film.NomineeType}:{film.Title}", film.Title, film.Year, film.NomineeType,
                    imdbId,
                    film.IsImdbIdManual,
                    null, null,
                    enrichmentStatus,
                    film.LastEnrichmentError,
                    group.OrderBy(x => x.Award.Name).ThenBy(x => x.Id)
                        .Select(x => new GoldenGlobeNominationResponse(x.Id, film.Year, x.Award.Name, x.Winner)).ToArray(),
                    null);
            })
            .OrderByDescending(x => x.Year).ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.NomineeType, StringComparer.Ordinal).ToArray();
        var filteredGroups = string.IsNullOrWhiteSpace(query.EnrichmentStatus)
            ? groups
            : groups.Where(film => film.EnrichmentStatus == query.EnrichmentStatus).ToArray();
        if (query.ImdbRatingFrom is { } minimumRating)
            filteredGroups = filteredGroups.Where(film => metadataByFilmId.TryGetValue(film.FilmId, out var metadata)
                && metadata?.ImdbRating is { } rating && rating >= minimumRating).ToArray();
        if (query.ImdbRatingTo is { } maximumRating)
            filteredGroups = filteredGroups.Where(film => metadataByFilmId.TryGetValue(film.FilmId, out var metadata)
                && metadata?.ImdbRating is { } rating && rating <= maximumRating).ToArray();
        var pageFilms = filteredGroups.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        var items = new List<GoldenGlobeFilmResponse>(pageFilms.Length);
        foreach (var film in pageFilms)
        {
            metadataByFilmId.TryGetValue(film.FilmId, out var details);
            items.Add(film with
            {
                PosterUrl = details?.PosterUrl,
                ImdbRating = details?.ImdbRating,
                MetadataStatus = details?.MetadataStatus
            });
        }

        return new PageResponse<GoldenGlobeFilmResponse>(
            items,
            page,
            pageSize,
            filteredGroups.Length,
            filteredGroups.Length == 0 ? 0 : (filteredGroups.Length + pageSize - 1) / pageSize,
            yearBounds,
            imdbRatingBounds);
    }

    private async Task<(Dictionary<string, ImdbTitle?> MetadataByFilmId, ImdbRatingBounds? Bounds)> GetRatingMetadataAsync(
        CancellationToken cancellationToken)
    {
        var groups = await dbContext.GoldenGlobeTitles
            .AsNoTracking()
            .Where(group => group.Nominations.Any())
            .Select(group => new
            {
                group.Title,
                group.Year,
                group.NomineeType,
                group.ImdbId,
                group.IsImdbIdManual,
                group.EnrichmentStatus
            })
            .ToListAsync(cancellationToken);
        var films = groups
            .Select(group =>
            {
                var imdbId = ImdbIdNormalizer.Normalize(group.ImdbId);
                if (!ImdbIdNormalizer.IsValid(imdbId)) imdbId = null;
                var enrichmentStatus = group.EnrichmentStatus == GoldenGlobeEnrichmentStatuses.Enriched && imdbId is null
                    ? GoldenGlobeEnrichmentStatuses.Pending
                    : group.EnrichmentStatus;
                return new GoldenGlobeRatingGroup(
                    $"{group.Year}:{group.NomineeType}:{group.Title}",
                    group.Title,
                    group.Year,
                    group.NomineeType,
                    imdbId,
                    group.IsImdbIdManual,
                    enrichmentStatus);
            })
            .ToArray();
        var eligibleFilms = films
            .Where(film => film.EnrichmentStatus == GoldenGlobeEnrichmentStatuses.Enriched && film.ImdbId is not null)
            .ToArray();
        var imdbIds = eligibleFilms
            .Select(film => film.ImdbId!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var metadataById = await dbContext.ImdbTitles
            .AsNoTracking()
            .Where(title => imdbIds.Contains(title.ImdbId))
            .ToDictionaryAsync(title => title.ImdbId, cancellationToken);
        var metadataByFilmId = films.ToDictionary(
            film => film.FilmId,
            film => ResolveMetadata(film, metadataById));
        var ratings = metadataByFilmId.Values
            .Where(metadata => metadata?.ImdbRating is not null)
            .Select(metadata => metadata!.ImdbRating!.Value)
            .ToArray();
        var bounds = ratings.Length == 0 ? null : new ImdbRatingBounds(ratings.Min(), ratings.Max());
        return (metadataByFilmId, bounds);
    }

    public async Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken) =>
        await dbContext.GoldenGlobeAwards
            .AsNoTracking()
            .Select(award => award.Name)
            .Distinct()
            .OrderBy(award => award)
            .ToArrayAsync(cancellationToken);

    private static ImdbTitle? ResolveMetadata(
        GoldenGlobeRatingGroup film,
        IReadOnlyDictionary<string, ImdbTitle> metadataById)
    {
        if (film.EnrichmentStatus != GoldenGlobeEnrichmentStatuses.Enriched || film.ImdbId is null)
            return null;

        if (!metadataById.TryGetValue(film.ImdbId, out var metadata))
            return null;

        var sourceType = GetSourceType(metadata.MediaType);
        return string.Equals(film.ImdbId, ImdbIdNormalizer.Normalize(metadata.ImdbId), StringComparison.Ordinal)
            && (string.Equals(film.NomineeType, sourceType, StringComparison.Ordinal)
                || (film.IsImdbIdManual && sourceType is "movie" or "series"))
            ? metadata
            : null;
    }

    private static string? GetSourceType(string? mediaType) => mediaType?.Trim().ToLowerInvariant() switch
    {
        "movie" or "documentary" or "short" => "movie",
        "series" => "series",
        _ => null
    };

    public async Task<GoldenGlobeImdbLinkResponse> SetImdbIdAsync(
        GoldenGlobeImdbLinkRequest request,
        CancellationToken cancellationToken)
    {
        var (title, ceremonyYear, sourceType) = ParseFilmId(request.FilmId);
        var imdbId = string.IsNullOrWhiteSpace(request.ImdbId) ? null : request.ImdbId.Trim().ToLowerInvariant();
        if (imdbId is not null && !ImdbIdNormalizer.IsValid(imdbId))
            throw new ApiValidationException(new Dictionary<string, string[]> { [nameof(request.ImdbId)] = ["IMDb ID must contain tt followed by 7 to 10 digits."] });

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({request.FilmId}, 0))",
            cancellationToken);
        var group = await dbContext.GoldenGlobeTitles
            .SingleOrDefaultAsync(row => row.Title == title && row.Year == ceremonyYear && row.NomineeType == sourceType, cancellationToken);
        if (group is null) throw new ApiNotFoundException("Golden Globes film group not found.");
        if (imdbId is not null)
        {
            await ImdbTitleMetadataMapper.EnsureAsync(
                dbContext, [imdbId], timeProvider.GetUtcNow(), cancellationToken);
        }

        if (imdbId is null)
        {
            group.ImdbId = null;
            group.IsImdbIdManual = false;
            group.ImdbIdVersion = checked(group.ImdbIdVersion + 1);
            ResetEnrichment(group);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(null, null);
        }

        var sameManualLink = group.IsImdbIdManual && group.ImdbId == imdbId;
        var linkVersion = sameManualLink ? group.ImdbIdVersion : checked(group.ImdbIdVersion + 1);
        if (!sameManualLink)
        {
            group.ImdbId = imdbId;
            group.IsImdbIdManual = true;
            group.ImdbIdVersion = linkVersion;
            ResetEnrichment(group);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var payload = new
        {
            filmId = request.FilmId,
            title,
            ceremonyYear,
            sourceType,
            imdbId,
            linkVersion,
            attemptCount = group.EnrichmentAttemptCount
        };
        var activeJobs = await dbContext.BackgroundJobs
            .Where(job => job.JobType == "golden_globe_manual_refresh" && (job.Status == "queued" || job.Status == "running"))
            .OrderByDescending(job => job.EnqueuedAt)
            .ToListAsync(cancellationToken);
        var matchingJob = activeJobs.FirstOrDefault(job => JobTargets(job.ResultSummary, request.FilmId, imdbId, linkVersion));
        if (matchingJob is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new(imdbId, ToAcceptedJob(matchingJob));
        }

        var now = timeProvider.GetUtcNow();
        var job = new BackgroundJob
        {
            JobType = "golden_globe_manual_refresh",
            Trigger = "manual",
            Status = "queued",
            EnqueuedAt = now,
            CurrentStage = "queued",
            ResultSummary = JsonSerializer.Serialize(payload)
        };
        job.Events.Add(new BackgroundJobEvent
        {
            OccurredAt = now,
            Level = "information",
            EventCode = "job_queued",
            Message = "Manual Golden Globes metadata refresh queued."
        });
        dbContext.BackgroundJobs.Add(job);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(imdbId, ToAcceptedJob(job));
    }

    private static (string Title, int CeremonyYear, string SourceType) ParseFilmId(string? filmId)
    {
        var parts = (filmId ?? string.Empty).Split(':', 3);
        if (parts.Length != 3
            || !int.TryParse(parts[0], out var year)
            || year is < 1800 or > 2200
            || parts[1] is not ("movie" or "series")
            || string.IsNullOrWhiteSpace(parts[2]))
            throw new ApiValidationException(new Dictionary<string, string[]> { ["filmId"] = ["FilmId must identify a valid Golden Globes group."] });
        return (parts[2], year, parts[1]);
    }

    private static void ResetEnrichment(GoldenGlobeTitle group)
    {
        group.EnrichmentStatus = GoldenGlobeEnrichmentStatuses.Pending;
        group.EnrichmentAttemptCount = 0;
        group.LastEnrichmentAttemptAt = null;
        group.NextEnrichmentAttemptAt = null;
        group.LastEnrichmentError = null;
    }

    private static bool JobTargets(string? payload, string filmId, string imdbId, int version)
    {
        if (payload is null) return false;
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            return root.GetProperty("filmId").GetString() == filmId
                && root.GetProperty("imdbId").GetString() == imdbId
                && root.GetProperty("linkVersion").GetInt32() == version;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static BackgroundJobAcceptedResponse ToAcceptedJob(BackgroundJob job)
    {
        var statusUrl = $"/api/background-jobs/{job.Id}";
        return new(job.Id, job.Status, statusUrl);
    }

    private sealed record GoldenGlobeRatingGroup(
        string FilmId,
        string Title,
        int Year,
        string NomineeType,
        string? ImdbId,
        bool IsImdbIdManual,
        string EnrichmentStatus);

}
