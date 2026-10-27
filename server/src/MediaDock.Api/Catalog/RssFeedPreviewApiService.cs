using MediaDock.Api.Middleware;
using MediaDock.Application.Ingestion;
using MediaDock.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Text;

namespace MediaDock.Api.Catalog;

public sealed record RssFeedPreviewResponse(
    string ProfileId,
    string ProfileName,
    IReadOnlyList<RssFeedPreviewSourceResponse> Feeds);

public sealed record RssFeedPreviewSourceResponse(
    long SourceId,
    string SourceUrl,
    string OriginalContent,
    double FetchDurationMs,
    double ParseDurationMs,
    IReadOnlyList<RssFeedPreviewItemResponse> Items);

public sealed record RssFeedPreviewItemResponse(
    string? Title,
    string? FeedEntryId,
    string? TorrentUrl,
    DateTimeOffset? PublishedAt);

internal interface IRssFeedPreviewApiService
{
    Task<RssFeedPreviewResponse> GetPreviewAsync(string profileId, CancellationToken cancellationToken);
}

internal sealed class RssFeedPreviewApiService(
    MediaDockDbContext dbContext,
    IRssFeedTransport feedTransport) : IRssFeedPreviewApiService
{
    private const int MaxConcurrentFeedRequests = 4;

    public async Task<RssFeedPreviewResponse> GetPreviewAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        var profile = RssFeedTypes.Profiles.FirstOrDefault(candidate => candidate.Id == profileId)
            ?? throw new ApiValidationException(new Dictionary<string, string[]>
            {
                ["profileId"] = ["Unknown RSS profile."]
            });

        var sources = await dbContext.Sources
            .AsNoTracking()
            .Where(source => source.IsEnabled && source.FeedType == profile.Id)
            .OrderBy(source => source.Id)
            .Select(source => new { source.Id, source.Url })
            .ToListAsync(cancellationToken);

        var feeds = new RssFeedPreviewSourceResponse[sources.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, sources.Count),
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = MaxConcurrentFeedRequests
            },
            async (index, token) =>
            {
                var source = sources[index];
                var fetchStartedAt = Stopwatch.GetTimestamp();
                var body = await feedTransport.FetchAsync(source.Url, token);
                var fetchDurationMs = Stopwatch.GetElapsedTime(fetchStartedAt).TotalMilliseconds;
                var parseStartedAt = Stopwatch.GetTimestamp();
                var parsedItems = RssFeedParser.Parse(body);
                var parseDurationMs = Stopwatch.GetElapsedTime(parseStartedAt).TotalMilliseconds;
                var items = parsedItems
                    .Select(item => new RssFeedPreviewItemResponse(
                        item.Title,
                        item.FeedEntryId,
                        item.TorrentUrl,
                        item.PublishedAt))
                    .ToArray();
                feeds[index] = new RssFeedPreviewSourceResponse(
                    source.Id,
                    source.Url,
                    Encoding.UTF8.GetString(body),
                    fetchDurationMs,
                    parseDurationMs,
                    items);
            });

        return new RssFeedPreviewResponse(profile.Id, profile.Name, feeds);
    }
}