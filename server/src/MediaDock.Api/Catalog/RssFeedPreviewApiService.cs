using MediaDock.Api.Middleware;
using MediaDock.Application.Ingestion;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace MediaDock.Api.Catalog;

public sealed record RssFeedPreviewResponse(
    string ProfileId,
    string ProfileName,
    DateTimeOffset? CachedAt,
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
    Task<RssFeedPreviewResponse> RefreshPreviewAsync(string profileId, CancellationToken cancellationToken);
}

internal sealed class RssFeedPreviewApiService(
    MediaDockDbContext dbContext,
    IRssFeedTransport feedTransport,
    TimeProvider timeProvider) : IRssFeedPreviewApiService
{
    private const int MaxCachedSnapshotsPerProfile = 5;

    public async Task<RssFeedPreviewResponse> GetPreviewAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        var profile = GetProfile(profileId);
        var snapshot = await dbContext.RssFeedPreviewSnapshots
            .AsNoTracking()
            .Where(candidate => candidate.ProfileId == profile.Id)
            .OrderByDescending(candidate => candidate.CachedAt)
            .ThenByDescending(candidate => candidate.Id)
            .Select(candidate => new { candidate.CachedAt, candidate.FeedsJson })
            .FirstOrDefaultAsync(cancellationToken);

        var feeds = snapshot is null
            ? []
            : JsonSerializer.Deserialize<RssFeedPreviewSourceResponse[]>(snapshot.FeedsJson) ?? [];
        return new RssFeedPreviewResponse(profile.Id, profile.Name, snapshot?.CachedAt, feeds);
    }

    public async Task<RssFeedPreviewResponse> RefreshPreviewAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        var profile = GetProfile(profileId);
        var sources = await GetEnabledSourcesAsync(profile.Id, cancellationToken);
        var feeds = await FetchFeedsAsync(sources, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var cachedAt = new DateTimeOffset(
            now.UtcDateTime.Ticks - now.UtcDateTime.Ticks % 10,
            TimeSpan.Zero);
        var snapshot = new RssFeedPreviewSnapshot
        {
            ProfileId = profile.Id,
            CachedAt = cachedAt,
            FeedsJson = JsonSerializer.Serialize(feeds)
        };

        dbContext.RssFeedPreviewSnapshots.Add(snapshot);
        await dbContext.SaveChangesAsync(cancellationToken);

        var expiredSnapshots = await dbContext.RssFeedPreviewSnapshots
            .Where(candidate => candidate.ProfileId == profile.Id && candidate.Id != snapshot.Id)
            .OrderByDescending(candidate => candidate.CachedAt)
            .ThenByDescending(candidate => candidate.Id)
            .Skip(MaxCachedSnapshotsPerProfile - 1)
            .ToListAsync(cancellationToken);
        dbContext.RssFeedPreviewSnapshots.RemoveRange(expiredSnapshots);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RssFeedPreviewResponse(profile.Id, profile.Name, cachedAt, feeds);
    }

    private static RssFeedProfile GetProfile(string profileId) =>
        RssFeedTypes.Profiles.FirstOrDefault(candidate => candidate.Id == profileId)
        ?? throw new ApiValidationException(new Dictionary<string, string[]>
        {
            ["profileId"] = ["Unknown RSS profile."]
        });

    private async Task<SourceInfo[]> GetEnabledSourcesAsync(string profileId, CancellationToken cancellationToken) =>
        await dbContext.Sources
            .AsNoTracking()
            .Where(source => source.IsEnabled && source.FeedType == profileId)
            .OrderBy(source => source.Id)
            .Select(source => new SourceInfo(source.Id, source.Url))
            .ToArrayAsync(cancellationToken);

    private async Task<RssFeedPreviewSourceResponse[]> FetchFeedsAsync(
        IReadOnlyList<SourceInfo> sources,
        CancellationToken cancellationToken)
    {
        var feeds = new RssFeedPreviewSourceResponse[sources.Count];
        for (var index = 0; index < sources.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = sources[index];
            var fetchStartedAt = Stopwatch.GetTimestamp();
            var body = await feedTransport.FetchAsync(source.Url, cancellationToken);
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
        }

        return feeds;
    }

    private sealed record SourceInfo(long Id, string Url);
}