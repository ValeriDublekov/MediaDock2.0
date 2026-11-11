using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.Infrastructure.Metadata;

public sealed record ImdbRatingRefreshSummary(
    int TargetYear,
    long SelectedTitles,
    long RowsScanned,
    long MatchedRows,
    long UpdatedRows,
    long UnchangedRows,
    long AbsentFromSnapshot,
    long ConcurrentConflicts,
    long CompressedBytes,
    long DecompressedBytes,
    long DatabaseReadMilliseconds,
    long ProcessingMilliseconds,
    long DatabaseApplyMilliseconds,
    long TotalMilliseconds,
    string Message);

public sealed class ImdbRatingRefreshService(
    MediaDockDbContext dbContext,
    HttpClient httpClient,
    ImdbRatingSnapshotParser parser,
    TimeProvider timeProvider)
{
    private const int BatchSize = 500;
    private const int MaximumDownloadAttempts = 3;
    private static readonly Uri DatasetUri = new("https://datasets.imdbws.com/title.ratings.tsv.gz");
    private static readonly TimeZoneInfo SofiaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Sofia");

    public async Task<ImdbRatingRefreshSummary> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var totalTimer = Stopwatch.StartNew();
        var processingTimer = Stopwatch.StartNew();
        var databaseReadTimer = new Stopwatch();
        var targetYear = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), SofiaTimeZone).Year;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));

        try
        {
            for (var attempt = 1; attempt <= MaximumDownloadAttempts; attempt++)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, DatasetUri);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/gzip"));
                    using var response = await httpClient.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        timeout.Token);

                    if (IsTransientStatusCode(response.StatusCode) && attempt < MaximumDownloadAttempts)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(attempt), timeout.Token);
                        continue;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException(
                            $"IMDb ratings download failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).",
                            null,
                            response.StatusCode);
                    }

                    await using var contentStream = await response.Content.ReadAsStreamAsync(timeout.Token);
                    using var transportStream = new TransportReadStream(contentStream);
                    return await RefreshFromSnapshotAsync(
                        transportStream, targetYear, totalTimer, processingTimer, databaseReadTimer, timeout.Token);
                }
                catch (HttpRequestException exception) when (
                    attempt < MaximumDownloadAttempts && IsRetryableHttpRequest(exception))
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt), timeout.Token);
                }
                catch (HttpRequestException exception) when (attempt == MaximumDownloadAttempts)
                {
                    throw new HttpRequestException(
                        $"IMDb ratings download failed after {MaximumDownloadAttempts} attempts: {exception.Message}",
                        exception,
                        exception.StatusCode);
                }
            }

            throw new HttpRequestException(
                $"IMDb ratings download failed after {MaximumDownloadAttempts} attempts.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("IMDb ratings refresh exceeded its 10-minute time limit.");
        }
    }

    public async Task<ImdbRatingRefreshSummary> RefreshFromSnapshotAsync(
        Stream compressedSnapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(compressedSnapshot);
        var totalTimer = Stopwatch.StartNew();
        var processingTimer = Stopwatch.StartNew();
        var targetYear = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), SofiaTimeZone).Year;
        return await RefreshFromSnapshotAsync(
            compressedSnapshot, targetYear, totalTimer, processingTimer, new Stopwatch(), cancellationToken);
    }

    private async Task<ImdbRatingRefreshSummary> RefreshFromSnapshotAsync(
        Stream compressedSnapshot,
        int targetYear,
        Stopwatch totalTimer,
        Stopwatch processingTimer,
        Stopwatch databaseReadTimer,
        CancellationToken cancellationToken)
    {
        var firstYear = targetYear - 1;

        processingTimer.Stop();
        databaseReadTimer.Start();
        SelectedTitle[] selectedTitles;
        try
        {
            selectedTitles = await SelectExistingTitlesByYear(dbContext.ImdbTitles.AsNoTracking(), targetYear)
                .Select(title => new SelectedTitle(
                    title.ImdbId,
                    title.Year!.Value,
                    title.ImdbRating,
                    title.ImdbVotes))
                .ToArrayAsync(cancellationToken);
        }
        finally
        {
            databaseReadTimer.Stop();
            processingTimer.Start();
        }
        var selectedById = selectedTitles.ToDictionary(title => title.ImdbId, StringComparer.Ordinal);

        var parsed = await parser.ParseAsync(
            compressedSnapshot,
            selectedById.Keys.ToHashSet(StringComparer.Ordinal),
            cancellationToken);
        var updates = new List<RatingUpdate>();
        var unchanged = new List<SelectedTitle>();
        foreach (var (imdbId, value) in parsed.Matches)
        {
            var selected = selectedById[imdbId];
            if (selected.Rating == value.Rating && selected.Votes == value.Votes)
            {
                unchanged.Add(selected);
                continue;
            }

            updates.Add(new RatingUpdate(selected, value));
        }

        var absentFromSnapshot = selectedTitles.LongLength - parsed.Matches.Count;
        processingTimer.Stop();

        var applyTimer = Stopwatch.StartNew();
        long updatedRows = 0;
        long unchangedRows = 0;
        long concurrentConflicts = 0;
        foreach (var batch in unchanged.Chunk(BatchSize))
        {
            var ids = batch.Select(title => title.ImdbId).ToArray();
            var current = await dbContext.ImdbTitles.AsNoTracking()
                .Where(title => ids.Contains(title.ImdbId))
                .Select(title => new SelectedTitle(title.ImdbId, title.Year ?? 0, title.ImdbRating, title.ImdbVotes))
                .ToDictionaryAsync(title => title.ImdbId, cancellationToken);
            foreach (var selected in batch)
            {
                if (current.TryGetValue(selected.ImdbId, out var actual) && actual == selected)
                {
                    unchangedRows++;
                }
                else
                {
                    concurrentConflicts++;
                }
            }
        }
        if (updates.Count > 0)
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var updatedAt = timeProvider.GetUtcNow();
            foreach (var batch in updates.Chunk(BatchSize))
            {
                var ids = batch.Select(update => update.Selected.ImdbId).ToArray();
                var years = batch.Select(update => update.Selected.Year).ToArray();
                var expectedRatings = batch.Select(update => update.Selected.Rating).ToArray();
                var expectedVotes = batch.Select(update => update.Selected.Votes).ToArray();
                var ratings = batch.Select(update => update.Value.Rating).ToArray();
                var votes = batch.Select(update => update.Value.Votes).ToArray();

                var batchUpdated = await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE imdb_titles AS existing
                    SET imdb_rating = incoming.rating,
                        imdb_votes = incoming.votes,
                        updated_at = {updatedAt}
                    FROM unnest(
                        {ids}::text[],
                        {years}::integer[],
                        {expectedRatings}::numeric[],
                        {expectedVotes}::bigint[],
                        {ratings}::numeric[],
                        {votes}::bigint[]
                    ) AS incoming(imdb_id, expected_year, expected_rating, expected_votes, rating, votes)
                    WHERE existing.imdb_id = incoming.imdb_id
                      AND existing.year = incoming.expected_year
                      AND existing.year BETWEEN {firstYear} AND {targetYear}
                      AND existing.imdb_rating IS NOT DISTINCT FROM incoming.expected_rating
                      AND existing.imdb_votes IS NOT DISTINCT FROM incoming.expected_votes
                      AND (existing.imdb_rating IS DISTINCT FROM incoming.rating
                           OR existing.imdb_votes IS DISTINCT FROM incoming.votes)
                    """,
                    cancellationToken);
                updatedRows += batchUpdated;
                concurrentConflicts += batch.Length - batchUpdated;
            }

            await transaction.CommitAsync(cancellationToken);
        }

        applyTimer.Stop();
        totalTimer.Stop();
        var message =
            $"IMDb ratings refresh for {firstYear}-{targetYear} completed: "
            + $"{FormatCount(updatedRows, "title", "titles")} updated, "
            + $"{FormatCount(unchangedRows, "title", "titles")} unchanged, "
            + $"{FormatCount(absentFromSnapshot, "selected title", "selected titles")} absent from the snapshot, "
            + $"{FormatCount(concurrentConflicts, "concurrent change", "concurrent changes")} skipped.";

        return new ImdbRatingRefreshSummary(
            targetYear,
            selectedTitles.LongLength,
            parsed.RowsScanned,
            parsed.Matches.Count,
            updatedRows,
            unchangedRows,
            absentFromSnapshot,
            concurrentConflicts,
            parsed.CompressedBytesRead,
            parsed.DecompressedBytesRead,
            databaseReadTimer.ElapsedMilliseconds,
            processingTimer.ElapsedMilliseconds,
            applyTimer.ElapsedMilliseconds,
            totalTimer.ElapsedMilliseconds,
            message);
    }

    private static bool IsTransientStatusCode(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
        || (int)statusCode >= 500;

    private static bool IsRetryableHttpRequest(HttpRequestException exception) =>
        exception.StatusCode is null || IsTransientStatusCode(exception.StatusCode.Value);

    private static IQueryable<ImdbTitle> SelectExistingTitlesByYear(IQueryable<ImdbTitle> titles, int targetYear) =>
        titles.Where(title => title.Year.HasValue && title.Year >= targetYear - 1 && title.Year <= targetYear);

    private static string FormatCount(long count, string singular, string plural) =>
        $"{count} {(count == 1 ? singular : plural)}";

    private sealed record SelectedTitle(string ImdbId, int Year, decimal? Rating, long? Votes);

    private sealed record RatingUpdate(SelectedTitle Selected, ImdbRatingDatasetValue Value);

    private sealed class TransportReadStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            try { return inner.Read(buffer, offset, count); }
            catch (IOException exception) { throw new HttpRequestException("IMDb ratings download was interrupted.", exception); }
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            try { return await inner.ReadAsync(buffer, cancellationToken); }
            catch (IOException exception) { throw new HttpRequestException("IMDb ratings download was interrupted.", exception); }
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}