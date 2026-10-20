using System.Text.RegularExpressions;

namespace MediaDock.Application.Ingestion;

internal sealed class IngestionRunContext(long runId)
{
    internal const int MaximumTitleLength = 2048;
    internal const int MaximumFeedUrlLength = 2048;

    private const int MaximumParseLogsPerRun = 1000;
    private const int MaximumErrorSummaries = 50;

    private static readonly Regex SensitiveValue = new(
        @"\b(api_?key|key)(\s*[=:]\s*)[^\s&]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly List<string> _errorSummary = [];

    public long RunId { get; } = runId;
    public int MaximumParseLogs { get; set; } = MaximumParseLogsPerRun;
    public string? CurrentSource { get; set; }
    public int FeedsProcessed { get; set; }
    public int EntriesSeen { get; set; }
    public int KnownEntriesSkipped { get; set; }
    public int TitlesCreated { get; set; }
    public int OccurrencesCreated { get; set; }
    public int CacheHits { get; set; }
    public int OmdbRequests { get; set; }
    public bool OmdbBudgetExhausted { get; set; }
    public int IgnoredEntries { get; set; }
    public int ErrorCount { get; private set; }
    public int ParseLogsWritten { get; private set; }
    public List<IngestionParseLog> PendingLogs { get; } = [];
    public IReadOnlyList<string> ErrorSummary => _errorSummary;

    public void RecordError(string message)
    {
        ErrorCount++;
        if (_errorSummary.Count < MaximumErrorSummaries)
        {
            _errorSummary.Add(BoundText(message, 300));
        }
    }

    public void AddLog(IngestionParseLog log)
    {
        if (ParseLogsWritten + PendingLogs.Count >= MaximumParseLogs)
        {
            return;
        }

        PendingLogs.Add(log with
        {
            RawTitle = BoundText(log.RawTitle, MaximumTitleLength + 1),
            FeedName = BoundText(log.FeedName, 200),
            ParsedTitle = BoundText(log.ParsedTitle, 500),
            OmdbStatus = BoundText(log.OmdbStatus, 32),
            IgnoreReason = BoundText(log.IgnoreReason, 64),
            ErrorMessage = BoundText(log.ErrorMessage, 500),
            Decision = BoundText(log.Decision, 64),
            LookupTitles = log.LookupTitles.Take(2).Select(title => BoundText(title, 160)).ToArray(),
            FeedEntryId = BoundText(log.FeedEntryId, MaximumFeedUrlLength + 1),
            TorrentUrl = BoundText(log.TorrentUrl, MaximumFeedUrlLength + 1)
        });
    }

    public void MarkLogsWritten()
    {
        ParseLogsWritten += PendingLogs.Count;
        PendingLogs.Clear();
    }

    public async Task FlushLogsAsync(
        IRssIngestionRepository repository,
        CancellationToken cancellationToken)
    {
        if (PendingLogs.Count == 0)
        {
            return;
        }

        await repository.AddParseLogsAsync(RunId, PendingLogs, cancellationToken);
        MarkLogsWritten();
    }

    public async Task ReportProgressAsync(
        string stage,
        Func<IngestionProgressUpdate, CancellationToken, Task>? reportProgress,
        CancellationToken cancellationToken)
    {
        if (reportProgress is not null)
        {
            await reportProgress(ToProgressUpdate(stage), cancellationToken);
        }
    }

    public IngestionProgressUpdate ToProgressUpdate(string stage) =>
        new(
            RunId,
            stage,
            CurrentSource,
            FeedsProcessed,
            EntriesSeen,
            KnownEntriesSkipped,
            TitlesCreated,
            OccurrencesCreated,
            CacheHits,
            OmdbRequests,
            IgnoredEntries,
            ErrorCount);

    public IngestionRunSummary ToSummary(DateTimeOffset finishedAt, string status) =>
        new(
            status,
            FeedsProcessed,
            EntriesSeen,
            KnownEntriesSkipped,
            TitlesCreated,
            OccurrencesCreated,
            CacheHits,
            OmdbRequests,
            IgnoredEntries,
            ErrorCount,
            ErrorSummary,
            finishedAt);

    internal static string BoundText(string? value, int maximumLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var sanitized = new string(value.Select(character => char.IsControl(character) ? ' ' : character).ToArray());
        sanitized = SensitiveValue.Replace(sanitized, "$1$2[REDACTED]");
        sanitized = Regex.Replace(sanitized, @"\s+", " ", RegexOptions.CultureInvariant);
        return sanitized.Length <= maximumLength ? sanitized : sanitized[..maximumLength];
    }
}