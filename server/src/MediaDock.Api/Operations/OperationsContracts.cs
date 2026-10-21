using System.ComponentModel.DataAnnotations;

namespace MediaDock.Api.Operations;

/// <summary>Validated pagination and filters for parse-log history.</summary>
public sealed record ParseLogQuery
{
    [Range(1, 1_000_000)]
    public int? Page { get; init; }

    [Range(1, 100)]
    public int? PageSize { get; init; }

    [Range(1L, long.MaxValue)]
    public long? SourceId { get; init; }

    [Range(1L, long.MaxValue)]
    public long? ScanRunId { get; init; }

    public bool? ParsedSuccessfully { get; init; }

    public bool? Ignored { get; init; }

    [RegularExpression("^(retryable|terminal|resolved)$")]
    public string? RetryState { get; init; }

    [MaxLength(200)]
    public string? Search { get; init; }
}

/// <summary>Validated pagination and filters for scan-run history.</summary>
public sealed record ScanRunQuery
{
    [Range(1, 1_000_000)]
    public int? Page { get; init; }

    [Range(1, 100)]
    public int? PageSize { get; init; }

    [RegularExpression("^(running|succeeded|partial|failed)$")]
    public string? Status { get; init; }

    [RegularExpression("^(schedule|manual|local)$")]
    public string? Trigger { get; init; }
}

/// <summary>Validated retention period for operational history cleanup.</summary>
public sealed record OperationalHistoryCleanupQuery
{
    [Range(1, 36_500)]
    public int OlderThanDays { get; init; }
}

/// <summary>Counts and cutoff returned after operational history cleanup.</summary>
public sealed record OperationalHistoryCleanupResponse(
    int OlderThanDays,
    DateTimeOffset Cutoff,
    int DeletedScanRuns,
    int DeletedParseLogs);

/// <summary>One parse-log entry returned from history.</summary>
public sealed record ParseLogResponse(
    long Id,
    long? ScanRunId,
    long? SourceId,
    string? SourceName,
    string? SourceItemKey,
    string RawTitle,
    string FeedName,
    bool ParsedSuccessfully,
    string? ParsedTitle,
    int? ParsedYear,
    string[] LookupTitles,
    string OmdbStatus,
    bool Ignored,
    string? IgnoreReason,
    string? ErrorMessage,
    string? Decision,
    DateTimeOffset ProcessedAt,
    string RetryState,
    int AttemptCount,
    DateTimeOffset? LastAttemptAt,
    string? FeedType,
    DateTimeOffset? SourcePublishedAt,
    DateTimeOffset? ObservedAt,
    string? EventKind)
{
    public bool CanRetryMatch { get; init; }
}

/// <summary>One ingestion scan summary returned from history.</summary>
public sealed record ScanRunResponse(
    long Id,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string Status,
    string Trigger,
    int FeedsProcessed,
    int EntriesSeen,
    int KnownEntriesSkipped,
    int TitlesCreated,
    int OccurrencesCreated,
    int CacheHits,
    int OmdbRequests,
    int IgnoredEntries,
    int ErrorCount,
    IReadOnlyList<string> ErrorSummary);