using MediaDock.Api.Middleware;
using MediaDock.Application.Ingestion;
using MediaDock.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.Api.Catalog;

public sealed record RssFeedPreviewResponse(
    string ProfileId,
    string ProfileName,
    IReadOnlyList<RssFeedPreviewSourceResponse> Feeds);

public sealed record RssFeedPreviewSourceResponse(
    long SourceId,
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

        var feeds = new List<RssFeedPreviewSourceResponse>(sources.Count);
        foreach (var source in sources)
        {
            var body = await feedTransport.FetchAsync(source.Url, cancellationToken);
            var items = RssFeedParser.Parse(body)
                .Select(item => new RssFeedPreviewItemResponse(
                    item.Title,
                    item.FeedEntryId,
                    item.TorrentUrl,
                    item.PublishedAt))
                .ToArray();
            feeds.Add(new RssFeedPreviewSourceResponse(source.Id, items));
        }

        return new RssFeedPreviewResponse(profile.Id, profile.Name, feeds);
    }
}