using MediaDock.Api.Catalog;
using MediaDock.Api.Common;
using MediaDock.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.Api.Operations;

internal interface IOperationalHistoryApiService
{
    Task<PageResponse<ParseLogResponse>> GetParseLogsAsync(
        ParseLogQuery query,
        CancellationToken cancellationToken);

    Task<PageResponse<ScanRunResponse>> GetScanRunsAsync(
        ScanRunQuery query,
        CancellationToken cancellationToken);

    Task<OperationalHistoryCleanupResponse> DeleteOldHistoryAsync(
        int olderThanDays,
        CancellationToken cancellationToken);
}

internal sealed class OperationalHistoryApiService(
    MediaDockDbContext dbContext,
    TimeProvider timeProvider) : IOperationalHistoryApiService
{
    public async Task<PageResponse<ParseLogResponse>> GetParseLogsAsync(
        ParseLogQuery query,
        CancellationToken cancellationToken)
    {
        var page = query.Page ?? 1;
        var pageSize = query.PageSize ?? 25;
        var logs = dbContext.ParseLogs.AsNoTracking();
        if (query.SourceId is { } sourceId)
        {
            logs = logs.Where(log => log.SourceId == sourceId);
        }

        if (query.ScanRunId is { } scanRunId)
        {
            logs = logs.Where(log => log.ScanRunId == scanRunId);
        }

        if (query.ParsedSuccessfully is { } parsedSuccessfully)
        {
            logs = logs.Where(log => log.ParsedSuccessfully == parsedSuccessfully);
        }

        if (query.Ignored is { } ignored)
        {
            logs = logs.Where(log => log.Ignored == ignored);
        }

        if (query.Ignored is true)
        {
            logs = logs.Where(log => log.EventKind != "feed");
        }

        if (query.RetryState is not null)
        {
            logs = logs.Where(log => log.RetryState == query.RetryState);
        }

        if (query.UnresolvedMoviesOnly is true)
        {
            logs = logs.Where(log => log.RetryState != "resolved"
                && log.EventKind != "feed"
                && (log.FeedType == "movie"
                    || (log.FeedType == null && log.Source != null && log.Source.FeedType == "movie")));
        }

        if (query.Ignored is true || query.UnresolvedMoviesOnly is true)
        {
            logs = logs.Where(log => log.SourceId == null || log.SourceItemKey == null
                || !dbContext.ParseLogs.Any(newer => newer.SourceId == log.SourceId
                    && newer.SourceItemKey == log.SourceItemKey
                    && (newer.ProcessedAt > log.ProcessedAt
                        || (newer.ProcessedAt == log.ProcessedAt && newer.Id > log.Id))));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search.Trim()}%";
            logs = logs.Where(log => EF.Functions.ILike(log.RawTitle, pattern)
                || EF.Functions.ILike(log.FeedName, pattern)
                || (log.ParsedTitle != null && EF.Functions.ILike(log.ParsedTitle, pattern)));
        }

        var totalCount = await logs.CountAsync(cancellationToken);
        var rows = await logs
            .OrderByDescending(log => log.ProcessedAt)
            .ThenByDescending(log => log.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(log => new
            {
                Response = new ParseLogResponse(
                    log.Id,
                    log.ScanRunId,
                    log.SourceId,
                    log.Source == null ? null : log.Source.Name,
                    log.SourceItemKey,
                    log.RawTitle,
                    log.FeedName,
                    log.ParsedSuccessfully,
                    log.ParsedTitle,
                    log.ParsedYear,
                    log.LookupTitles,
                    log.OmdbStatus,
                    log.Ignored,
                    log.IgnoreReason,
                    log.ErrorMessage,
                    log.Decision,
                    log.ProcessedAt,
                    log.RetryState,
                    log.AttemptCount,
                    log.LastAttemptAt,
                    log.FeedType,
                    log.SourcePublishedAt,
                    log.ObservedAt,
                    log.EventKind),
                log.TorrentUrl,
                HasSource = log.Source != null,
                SourceFeedType = log.Source == null ? null : log.Source.FeedType,
                HasNewerLog = log.SourceId != null && log.SourceItemKey != null
                    && dbContext.ParseLogs.Any(newer => newer.SourceId == log.SourceId
                        && newer.SourceItemKey == log.SourceItemKey
                        && (newer.ProcessedAt > log.ProcessedAt
                            || (newer.ProcessedAt == log.ProcessedAt && newer.Id > log.Id)))
            })
            .ToListAsync(cancellationToken);
        var items = rows.Select(row => row.Response with
        {
            CanRetryMatch = CanRetryMatch(
                row.Response,
                row.HasSource,
                row.HasNewerLog,
                row.TorrentUrl,
                row.SourceFeedType)
        }).ToList();

        return CreatePage(items, page, pageSize, totalCount);
    }

    public async Task<PageResponse<ScanRunResponse>> GetScanRunsAsync(
        ScanRunQuery query,
        CancellationToken cancellationToken)
    {
        var page = query.Page ?? 1;
        var pageSize = query.PageSize ?? 25;
        var runs = dbContext.ScanRuns.AsNoTracking();
        if (query.Status is not null)
        {
            runs = runs.Where(run => run.Status == query.Status);
        }

        if (query.Trigger is not null)
        {
            runs = runs.Where(run => run.Trigger == query.Trigger);
        }

        var totalCount = await runs.CountAsync(cancellationToken);
        var items = await runs
            .OrderByDescending(run => run.StartedAt)
            .ThenByDescending(run => run.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(run => new ScanRunResponse(
                run.Id,
                run.StartedAt,
                run.FinishedAt,
                run.Status,
                run.Trigger,
                run.FeedsProcessed,
                run.EntriesSeen,
                run.KnownEntriesSkipped,
                run.TitlesCreated,
                run.OccurrencesCreated,
                run.CacheHits,
                run.OmdbRequests,
                run.IgnoredEntries,
                run.ErrorCount,
                run.ErrorSummary))
            .ToListAsync(cancellationToken);

        return CreatePage(items, page, pageSize, totalCount);
    }

    public async Task<OperationalHistoryCleanupResponse> DeleteOldHistoryAsync(
        int olderThanDays,
        CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow().AddDays(-olderThanDays);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var oldScanRuns = dbContext.ScanRuns
            .Where(run => run.StartedAt < cutoff && run.Status != "running");
        var oldScanRunIds = oldScanRuns.Select(run => run.Id);

        var deletedParseLogs = await dbContext.ParseLogs
            .Where(log => log.ProcessedAt < cutoff
                && !dbContext.ScanRuns.Any(run => run.Id == log.ScanRunId && run.Status == "running"))
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.ParseLogs
            .Where(log => log.ScanRunId.HasValue && oldScanRunIds.Contains(log.ScanRunId.Value))
            .ExecuteUpdateAsync(setters => setters.SetProperty(log => log.ScanRunId, (long?)null), cancellationToken);
        await dbContext.BackgroundJobs
            .Where(job => job.ScanRunId.HasValue && oldScanRunIds.Contains(job.ScanRunId.Value))
            .ExecuteUpdateAsync(setters => setters.SetProperty(job => job.ScanRunId, (long?)null), cancellationToken);

        var deletedScanRuns = await oldScanRuns.ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new OperationalHistoryCleanupResponse(olderThanDays, cutoff, deletedScanRuns, deletedParseLogs);
    }

    private static PageResponse<T> CreatePage<T>(IReadOnlyList<T> items, int page, int pageSize, int totalCount) =>
        new(items, page, pageSize, totalCount, totalCount == 0 ? 0 : (totalCount + pageSize - 1) / pageSize);

    private static bool CanRetryMatch(
        ParseLogResponse response,
        bool hasSource,
        bool hasNewerLog,
        string? torrentUrl,
        string? sourceFeedType)
    {
        var feedType = response.FeedType ?? sourceFeedType;
        return response.RetryState != "resolved"
            && !hasNewerLog
            && hasSource
            && feedType is "movie" or "series_complete" or "series_ongoing"
            && IsHttpUrl(torrentUrl);
    }

    private static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme is "http" or "https");
}