using System.Security.Cryptography;
using System.Text;
using MediaDock.Api.Middleware;
using MediaDock.Application.Ingestion;
using MediaDock.Application.Metadata;
using MediaDock.Application.Parsing;
using MediaDock.Infrastructure.Metadata;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.Api.Catalog;

internal interface ITorrentTitleMatchApiService
{
    Task<ImdbCandidateSearchResponse> SearchCatalogTitleAsync(
        long titleId,
        string title,
        int page,
        CancellationToken cancellationToken);

    Task<ImdbCandidateSearchResponse> LookupCatalogTitleImdbCandidateAsync(
        long titleId,
        string imdbId,
        CancellationToken cancellationToken);

    Task<ImdbCandidateSearchResponse> SearchParseLogImdbCandidatesAsync(
        long parseLogId,
        string title,
        int page,
        CancellationToken cancellationToken);

    Task<ImdbCandidateSearchResponse> LookupParseLogImdbCandidateAsync(
        long parseLogId,
        string imdbId,
        CancellationToken cancellationToken);

    Task<AppliedImdbCandidateResponse> ApplyCatalogTitleMatchAsync(
        long titleId,
        string imdbId,
        CancellationToken cancellationToken);

    Task<AppliedImdbCandidateResponse> ApplyParseLogMatchAsync(
        long parseLogId,
        string imdbId,
        CancellationToken cancellationToken);
}

internal sealed class TorrentTitleMatchApiService(
    MediaDockDbContext dbContext,
    IOmdbClientFactory omdbClientFactory,
    IMetadataCacheStore metadataCache,
    IRssIngestionRepository ingestionRepository,
    TimeProvider timeProvider) : ITorrentTitleMatchApiService
{
    public async Task<ImdbCandidateSearchResponse> SearchCatalogTitleAsync(
        long titleId,
        string title,
        int page,
        CancellationToken cancellationToken)
    {
        var catalogTitle = await dbContext.TorrentMovies.AsNoTracking()
            .Where(movie => movie.Id == titleId)
            .Select(movie => new { movie.SourceType })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ApiNotFoundException("Catalog title not found.");

        return await SearchAsync(title, catalogTitle.SourceType, page, cancellationToken);
    }

    public async Task<ImdbCandidateSearchResponse> LookupCatalogTitleImdbCandidateAsync(
        long titleId,
        string imdbId,
        CancellationToken cancellationToken)
    {
        var catalogTitle = await dbContext.TorrentMovies.AsNoTracking()
            .Where(movie => movie.Id == titleId)
            .Select(movie => new { movie.SourceType })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ApiNotFoundException("Catalog title not found.");

        return await LookupImdbCandidateAsync(catalogTitle.SourceType, imdbId, cancellationToken);
    }

    public async Task<ImdbCandidateSearchResponse> SearchParseLogImdbCandidatesAsync(
        long parseLogId,
        string title,
        int page,
        CancellationToken cancellationToken)
    {
        var log = await dbContext.ParseLogs.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == parseLogId, cancellationToken)
            ?? throw new ApiNotFoundException("Parse-log entry not found.");
        var feedType = log.FeedType;
        return await SearchAsync(title, SourceTypeForFeed(feedType), page, cancellationToken);
    }

    public async Task<ImdbCandidateSearchResponse> LookupParseLogImdbCandidateAsync(
        long parseLogId,
        string imdbId,
        CancellationToken cancellationToken)
    {
        var log = await dbContext.ParseLogs.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == parseLogId, cancellationToken)
            ?? throw new ApiNotFoundException("Parse-log entry not found.");

        return await LookupImdbCandidateAsync(SourceTypeForFeed(log.FeedType), imdbId, cancellationToken);
    }

    public async Task<AppliedImdbCandidateResponse> ApplyCatalogTitleMatchAsync(
        long titleId,
        string imdbId,
        CancellationToken cancellationToken)
    {
        var normalizedImdbId = NormalizeImdbId(imdbId);
        var title = await dbContext.TorrentMovies
            .SingleOrDefaultAsync(movie => movie.Id == titleId, cancellationToken)
            ?? throw new ApiNotFoundException("Catalog title not found.");
        var metadata = await ResolveMetadataAsync(normalizedImdbId, cancellationToken);
        ValidateSourceType(title.SourceType, metadata.SourceType);

        var now = timeProvider.GetUtcNow();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var existingTitle = await dbContext.TorrentMovies
            .SingleOrDefaultAsync(movie => movie.ImdbId == normalizedImdbId && movie.Id != titleId, cancellationToken);
        if (existingTitle is null)
        {
            title.ImdbId = normalizedImdbId;
        }
        else
        {
            var releases = await dbContext.TorrentReleases
                .Where(release => release.TorrentMovieId == titleId)
                .ToListAsync(cancellationToken);
            foreach (var release in releases)
            {
                release.TorrentMovieId = existingTitle.Id;
            }

            if (title.FirstSeenAt is { } firstSeenAt
                && (existingTitle.FirstSeenAt is null || firstSeenAt < existingTitle.FirstSeenAt))
            {
                existingTitle.FirstSeenAt = firstSeenAt;
            }

            if (title.LastSeenAt is { } lastSeenAt
                && (existingTitle.LastSeenAt is null || lastSeenAt > existingTitle.LastSeenAt))
            {
                existingTitle.LastSeenAt = lastSeenAt;
            }

            dbContext.TorrentMovies.Remove(title);
            title = existingTitle;
        }

        var imdbTitle = (await ImdbTitleMetadataMapper.EnsureAsync(
            dbContext,
            [normalizedImdbId],
            now,
            cancellationToken))[normalizedImdbId];
        ImdbTitleMetadataMapper.Apply(imdbTitle, metadata, now);
        title.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new AppliedImdbCandidateResponse(title.Id, normalizedImdbId);
    }

    public async Task<AppliedImdbCandidateResponse> ApplyParseLogMatchAsync(
        long parseLogId,
        string imdbId,
        CancellationToken cancellationToken)
    {
        var log = await dbContext.ParseLogs
            .SingleOrDefaultAsync(value => value.Id == parseLogId, cancellationToken)
            ?? throw new ApiNotFoundException("Parse-log entry not found.");
        if (log.RetryState == "resolved")
        {
            throw new ApiConflictException("This parse-log entry is already resolved.");
        }

        var source = log.SourceId is { } sourceId
            ? await dbContext.Sources.SingleOrDefaultAsync(value => value.Id == sourceId, cancellationToken)
            : null;
        if (source is null || string.IsNullOrWhiteSpace(log.TorrentUrl) || !IsHttpUrl(log.TorrentUrl))
        {
            throw new ApiConflictException("This parse-log entry lacks the source or torrent URL required to apply a match.");
        }

        var feedType = log.FeedType ?? source.FeedType;
        var sourceType = SourceTypeForFeed(feedType)
            ?? throw new ApiConflictException("This parse-log entry has an unsupported feed type.");

        string sourceItemKey;
        try
        {
            sourceItemKey = string.IsNullOrWhiteSpace(log.SourceItemKey)
                ? SourceItemIdentity.From(log.FeedEntryId, log.TorrentUrl)
                : log.SourceItemKey;
        }
        catch (ArgumentException)
        {
            throw new ApiConflictException("This parse-log entry has no stable source item identity.");
        }

        var normalizedTorrentUrl = TorrentUrlIdentity.Normalize(log.TorrentUrl);
        var latestLog = await dbContext.ParseLogs.AsNoTracking()
            .Where(value => value.SourceId == source.Id && value.SourceItemKey == sourceItemKey)
            .OrderByDescending(value => value.ProcessedAt)
            .ThenByDescending(value => value.Id)
            .Select(value => new { value.Id, value.RetryState, value.TorrentUrl })
            .FirstOrDefaultAsync(cancellationToken);
        if (latestLog is not null && latestLog.Id != log.Id)
        {
            var latestTorrentUrl = latestLog.TorrentUrl;
            if (latestTorrentUrl is null || !string.Equals(
                    TorrentUrlIdentity.Normalize(latestTorrentUrl), normalizedTorrentUrl, StringComparison.Ordinal))
            {
                throw new ApiConflictException(latestLog.RetryState == "resolved"
                    ? "This torrent entry has already been resolved."
                    : "A newer parse-log entry exists for this torrent.");
            }
        }

        var parsed = RutrackerTitleParser.Parse(log.RawTitle, feedType);
        if (string.IsNullOrWhiteSpace(parsed.Title) && !string.IsNullOrWhiteSpace(log.ParsedTitle))
        {
            parsed = RutrackerTitleParser.Parse(log.ParsedTitle, feedType) with { Year = log.ParsedYear };
        }

        var normalizedImdbId = NormalizeImdbId(imdbId);
        var metadata = await ResolveMetadataAsync(normalizedImdbId, cancellationToken);
        ValidateSourceType(sourceType, metadata.SourceType);
        if (string.IsNullOrWhiteSpace(parsed.Title))
        {
            var metadataTitle = metadata.Title.Trim();
            if (string.IsNullOrWhiteSpace(metadataTitle))
            {
                throw ValidationError(nameof(log.RawTitle), "The selected IMDb record does not provide a usable title.");
            }

            parsed = parsed with
            {
                Title = metadataTitle,
                NormalizedTitle = metadataTitle.ToLowerInvariant(),
                Year = metadata.Year,
                IsSeries = metadata.SourceType == "series"
            };
        }

        var now = timeProvider.GetUtcNow();
        var ingestionSource = new IngestionSource(source.Id, source.Name, feedType, source.Url);
        var feedItem = new IngestionFeedItem(log.RawTitle, log.FeedEntryId, log.TorrentUrl, log.SourcePublishedAt);
        await ingestionRepository.UpsertCatalogItemAsync(
            ingestionSource,
            feedItem,
            sourceItemKey,
            CreateManualFingerprint(source.Id, sourceItemKey, feedItem),
            parsed,
            metadata,
            now,
            cancellationToken);

        var attemptCount = checked(log.AttemptCount + 1);
        var pendingLogs = await dbContext.ParseLogs
            .Where(value => value.RetryState != "resolved"
                && value.TorrentUrl != null
                && (value.TorrentUrl == normalizedTorrentUrl
                    || (EF.Functions.ILike(value.TorrentUrl, "%rutracker%")
                        && EF.Functions.ILike(value.TorrentUrl, "%/forum/viewtopic.php%"))))
            .ToListAsync(cancellationToken);
        foreach (var pendingLog in pendingLogs.Where(value =>
                     string.Equals(
                         TorrentUrlIdentity.Normalize(value.TorrentUrl!),
                         normalizedTorrentUrl,
                         StringComparison.Ordinal)))
        {
            pendingLog.ParsedSuccessfully = true;
            pendingLog.ParsedTitle = parsed.Title;
            pendingLog.ParsedYear = parsed.Year;
            pendingLog.LookupTitles = [parsed.Title];
            pendingLog.OmdbStatus = "found";
            pendingLog.Ignored = false;
            pendingLog.IgnoreReason = null;
            pendingLog.ErrorMessage = null;
            pendingLog.Decision = "manual_imdb_match";
            pendingLog.RetryState = "resolved";
            pendingLog.AttemptCount = checked(pendingLog.AttemptCount + 1);
            pendingLog.LastAttemptAt = now;
        }

        dbContext.ParseLogs.Add(new ParseLog
        {
            ScanRunId = log.ScanRunId,
            SourceId = source.Id,
            SourceItemKey = sourceItemKey,
            RawTitle = log.RawTitle,
            FeedName = source.Name,
            ParsedSuccessfully = true,
            ParsedTitle = parsed.Title,
            ParsedYear = parsed.Year,
            LookupTitles = [parsed.Title],
            OmdbStatus = "found",
            Ignored = false,
            Decision = "manual_imdb_match",
            ProcessedAt = now,
            RetryState = "resolved",
            AttemptCount = attemptCount,
            LastAttemptAt = now,
            FeedType = feedType,
            SourcePublishedAt = log.SourcePublishedAt,
            ObservedAt = now,
            EventKind = "manual_retry",
            FeedEntryId = log.FeedEntryId,
            TorrentUrl = log.TorrentUrl
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        var torrentTitleId = await dbContext.TorrentReleases.AsNoTracking()
            .Where(release => release.SourceId == source.Id && release.SourceItemKey == sourceItemKey)
            .Select(release => release.TorrentMovieId)
            .SingleAsync(cancellationToken);
        return new AppliedImdbCandidateResponse(torrentTitleId, normalizedImdbId);
    }

    private async Task<ImdbCandidateSearchResponse> SearchAsync(
        string title,
        string? sourceType,
        int page,
        CancellationToken cancellationToken)
    {
        title = title.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            throw ValidationError(nameof(title), "A title is required for IMDb search.");
        }

        var omdbClient = await CreateOmdbClientAsync(cancellationToken);
        var result = await omdbClient.SearchAsync(
            title,
            sourceType,
            page,
            cancellationToken,
            OmdbRequestPurpose.RssIngestion);
        var candidates = result.Candidates
            .Select(candidate => new ImdbCandidateResponse(
                candidate.Title,
                candidate.Year,
                candidate.ImdbId,
                candidate.SourceType))
            .ToArray();
        return new ImdbCandidateSearchResponse(
            SearchStatus(result.Status),
            candidates,
            result.TotalResults,
            result.ErrorCode);
    }

    private async Task<ImdbCandidateSearchResponse> LookupImdbCandidateAsync(
        string? expectedSourceType,
        string imdbId,
        CancellationToken cancellationToken)
    {
        var normalizedImdbId = NormalizeImdbId(imdbId);
        var metadata = await ResolveMetadataAsync(normalizedImdbId, cancellationToken);
        ValidateSourceType(expectedSourceType, metadata.SourceType);
        return new ImdbCandidateSearchResponse(
            "found",
            [new ImdbCandidateResponse(metadata.Title, metadata.Year, normalizedImdbId, metadata.SourceType)],
            1,
            null);
    }

    private async Task<MetadataDetails> ResolveMetadataAsync(string imdbId, CancellationToken cancellationToken)
    {
        var omdbClient = await CreateOmdbClientAsync(cancellationToken);
        var metadataResolver = new MetadataResolver(omdbClient, metadataCache);
        var resolution = await metadataResolver.ResolveByImdbIdAsync(
            imdbId,
            timeProvider.GetUtcNow(),
            cancellationToken,
            OmdbRequestPurpose.RssIngestion);
        if (resolution.Status != MetadataLookupStatus.Found
            || resolution.Metadata is null
            || !string.Equals(ImdbIdNormalizer.Normalize(resolution.Metadata.ImdbId), imdbId, StringComparison.Ordinal))
        {
            throw new ApiConflictException("OMDb could not verify the selected IMDb record.");
        }

        return resolution.Metadata;
    }

    private async Task<IOmdbClient> CreateOmdbClientAsync(CancellationToken cancellationToken)
    {
        var settings = await dbContext.Settings.AsNoTracking()
            .Where(value => value.Id == 1)
            .Select(value => new { value.OmdbApiKey, value.OmdbDailyRequestLimit })
            .SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(settings?.OmdbApiKey) || settings.OmdbDailyRequestLimit <= 0)
        {
            throw new ApiConflictException("Configure valid OMDb provider settings before matching torrent titles.");
        }

        return omdbClientFactory.Create(settings.OmdbApiKey, settings.OmdbDailyRequestLimit);
    }

    private static string NormalizeImdbId(string imdbId)
    {
        var normalized = ImdbIdNormalizer.Normalize(imdbId);
        if (!ImdbIdNormalizer.IsValid(normalized))
        {
            throw ValidationError(nameof(imdbId), "A valid IMDb ID is required.");
        }

        return normalized!;
    }

    private static void ValidateSourceType(string? expected, string actual)
    {
        if (actual is not ("movie" or "series")
            || expected is not null && !string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new ApiConflictException("The selected IMDb record has an incompatible media type.");
        }
    }

    private static string? SourceTypeForFeed(string? feedType) => feedType switch
    {
        RssFeedTypes.Movie => "movie",
        RssFeedTypes.CompleteSeason or RssFeedTypes.OngoingSeries => "series",
        _ => null
    };

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    private static string CreateManualFingerprint(long sourceId, string sourceItemKey, IngestionFeedItem feedItem)
    {
        var input = $"manual|{sourceId}|{sourceItemKey}|{feedItem.Title}|{feedItem.TorrentUrl}|{feedItem.PublishedAt:O}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }

    private static string SearchStatus(MetadataLookupStatus status) => status switch
    {
        MetadataLookupStatus.Found => "found",
        MetadataLookupStatus.ConfirmedNotFound => "not_found",
        MetadataLookupStatus.QuotaExceeded => "quota_exceeded",
        MetadataLookupStatus.RequestBudgetExhausted => "budget_exhausted",
        MetadataLookupStatus.TransportFailure => "transport_failure",
        MetadataLookupStatus.AuthenticationFailure => "authentication_failure",
        MetadataLookupStatus.InvalidRequest => "invalid_request",
        _ => "provider_failure"
    };

    private static ApiValidationException ValidationError(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}