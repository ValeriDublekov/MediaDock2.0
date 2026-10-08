using MediaDock.Api.Common;
using MediaDock.Api.BackgroundJobs;
using MediaDock.Api.Middleware;
using MediaDock.Application.GoldenGlobes;
using MediaDock.Application.Metadata;
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

internal sealed class GoldenGlobeApiService(MediaDockDbContext dbContext, IMetadataCacheStore metadataCacheStore, TimeProvider timeProvider) : IGoldenGlobeApiService
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
            nominations = nominations.Where(x => EF.Functions.ILike(x.Award.Name, $"%{query.Award.Trim()}%"));
        var rows = await nominations.OrderByDescending(x => x.Year).ThenBy(x => x.Title).ThenBy(x => x.Id).ToListAsync(cancellationToken);
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
            .Select(row => (row.Title, row.Year, row.NomineeType))
            .ToHashSet();
        var groups = rows.GroupBy(x => new { x.Title, x.Year, x.NomineeType })
            .Where(group => !categoryFilterActive || matchingGroups.Contains((group.Key.Title, group.Key.Year, group.Key.NomineeType)))
            .Select(group =>
            {
                var imdbId = group.Select(x => ImdbIdNormalizer.Normalize(x.ImdbId))
                    .FirstOrDefault(ImdbIdNormalizer.IsValid);
                var hasUnhealthyEnrichedRow = group.Any(x =>
                    x.EnrichmentStatus == GoldenGlobeEnrichmentStatuses.Enriched
                    && !ImdbIdNormalizer.IsValid(x.ImdbId));
                return new GoldenGlobeFilmResponse(
                    $"{group.Key.Year}:{group.Key.NomineeType}:{group.Key.Title}", group.Key.Title, group.Key.Year, group.Key.NomineeType,
                    imdbId,
                    group.Any(x => x.IsImdbIdManual),
                    null, null,
                    GetEnrichmentStatus(group.Select(x => x.EnrichmentStatus), hasUnhealthyEnrichedRow),
                    group.Select(x => x.LastEnrichmentError).FirstOrDefault(x => x != null),
                    group.OrderBy(x => x.Award.Name).ThenBy(x => x.Id).Select(x => new GoldenGlobeNominationResponse(x.Id, x.Year, x.Award.Name, x.Winner)).ToArray());
            })
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

            var metadata = film.IsImdbIdManual
                ? await metadataCacheStore.GetByImdbIdAsync(film.ImdbId!, film.NomineeType, cancellationToken)
                : await metadataCacheStore.GetByTitleAsync(NormalizeTitle(film.Title), film.NomineeType, cancellationToken);
            var details = GetMatchingMetadata(film, metadata);
            if (details is null && !film.IsImdbIdManual)
            {
                metadata = await metadataCacheStore.GetByImdbIdAsync(film.ImdbId!, film.NomineeType, cancellationToken);
                details = GetMatchingMetadata(film, metadata);
            }
            items.Add(film with { PosterUrl = details?.PosterUrl, ImdbRating = details?.ImdbRating });
        }

        return new PageResponse<GoldenGlobeFilmResponse>(items, page, pageSize, filteredGroups.Length, filteredGroups.Length == 0 ? 0 : (filteredGroups.Length + pageSize - 1) / pageSize);
    }

    public async Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken) =>
        await dbContext.GoldenGlobeAwards
            .AsNoTracking()
            .Select(award => award.Name)
            .Distinct()
            .OrderBy(award => award)
            .ToArrayAsync(cancellationToken);

    private static MetadataDetails? GetMatchingMetadata(GoldenGlobeFilmResponse film, MetadataCacheValue? metadata) =>
        metadata?.Metadata is { } candidate
        && string.Equals(ImdbIdNormalizer.Normalize(film.ImdbId), ImdbIdNormalizer.Normalize(candidate.ImdbId), StringComparison.Ordinal)
        && (string.Equals(film.NomineeType, candidate.SourceType, StringComparison.Ordinal)
            || (film.IsImdbIdManual && candidate.SourceType is ("movie" or "series")))
            ? candidate
            : null;

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
        var rows = await dbContext.GoldenGlobeNominations
            .Where(row => row.Title == title && row.Year == ceremonyYear && row.NomineeType == sourceType)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0) throw new ApiNotFoundException("Golden Globes film group not found.");

        if (imdbId is null)
        {
            var version = checked(rows.Max(row => row.ImdbIdVersion) + 1);
            foreach (var row in rows)
            {
                row.ImdbId = null;
                row.IsImdbIdManual = false;
                row.ImdbIdVersion = version;
                ResetEnrichment(row);
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(null, null);
        }

        var sameManualLink = rows.All(row => row.IsImdbIdManual && row.ImdbId == imdbId);
        var linkVersion = sameManualLink ? rows.Max(row => row.ImdbIdVersion) : checked(rows.Max(row => row.ImdbIdVersion) + 1);
        if (!sameManualLink)
        {
            foreach (var row in rows)
            {
                row.ImdbId = imdbId;
                row.IsImdbIdManual = true;
                row.ImdbIdVersion = linkVersion;
                ResetEnrichment(row);
            }
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
            attemptCount = rows.Max(row => row.EnrichmentAttemptCount)
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

    private static void ResetEnrichment(GoldenGlobeNomination row)
    {
        row.EnrichmentStatus = GoldenGlobeEnrichmentStatuses.Pending;
        row.EnrichmentAttemptCount = 0;
        row.LastEnrichmentAttemptAt = null;
        row.NextEnrichmentAttemptAt = null;
        row.LastEnrichmentError = null;
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

    private static string GetEnrichmentStatus(IEnumerable<string> statuses, bool hasUnhealthyEnrichedRow)
    {
        if (hasUnhealthyEnrichedRow) return GoldenGlobeEnrichmentStatuses.Pending;

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
