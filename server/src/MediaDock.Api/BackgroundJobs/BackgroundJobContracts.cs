using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace MediaDock.Api.BackgroundJobs;

public sealed record BackgroundJobResponse(
    long Id,
    string JobType,
    string Trigger,
    string Status,
    DateTimeOffset EnqueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? CurrentStage,
    string? CurrentSource,
    DateTimeOffset? ProgressUpdatedAt,
    string? ErrorCode,
    JsonElement? ResultSummary,
    long? ScanRunId,
    string? InputFileName);

public sealed record BackgroundJobAcceptedResponse(long Id, string Status, string StatusUrl);

public sealed record BackgroundJobEventResponse(
    long Id,
    DateTimeOffset OccurredAt,
    string Level,
    string EventCode,
    string Message,
    JsonElement? Data);

public sealed record BackgroundJobEventsResponse(
    IReadOnlyList<BackgroundJobEventResponse> Items,
    long NextAfterId);

public sealed record BackgroundJobEventQuery
{
    [Range(0, long.MaxValue)]
    public long AfterId { get; init; }

    [Range(1, 100)]
    public int PageSize { get; init; } = 50;
}

public sealed class OscarImportForm
{
    public IFormFile? File { get; init; }

    [Range(0, 9998)]
    public int YearAfter { get; init; } = 1980;
}

internal sealed record EnqueuedBackgroundJob(BackgroundJobResponse Job, bool Accepted);