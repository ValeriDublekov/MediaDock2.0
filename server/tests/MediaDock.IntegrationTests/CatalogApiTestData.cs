using MediaDock.Infrastructure.Persistence.Entities;

namespace MediaDock.IntegrationTests;

internal static class CatalogApiTestData
{
    internal static TorrentMovie CreateTitle(string title, string normalizedTitle, int? year, string mediaType)
    {
        var seenAt = year is { } knownYear
            ? new DateTimeOffset(knownYear, 1, 1, 0, 0, 0, TimeSpan.Zero)
            : new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var imdbId = year is { } imdbYear ? $"tt{imdbYear}" : null;
        return new TorrentMovie
        {
            TitleText = title,
            NormalizedTitle = normalizedTitle,
            Year = year,
            MediaType = mediaType,
            SourceType = mediaType,
            ContentKind = "standard",
            ImdbId = imdbId,
            FirstSeenAt = seenAt,
            LastSeenAt = seenAt,
            UpdatedAt = seenAt
        };
    }

    internal static TorrentRelease CreateRelease(
        long torrentMovieId,
        long sourceId,
        string sourceItemKey,
        string rawTitle,
        DateTimeOffset lastSeenAt,
        string feedType = "movie",
        string sourceFeedName = "Movies",
        string? quality = null,
        string? ripType = null,
        DateTimeOffset? sourcePublishedAt = null) => new()
    {
        TorrentMovieId = torrentMovieId,
        SourceId = sourceId,
        SourceItemKey = sourceItemKey,
        TorrentUrl = $"https://feed.example/release/{Uri.EscapeDataString(sourceItemKey)}",
        RawTitle = rawTitle,
        SourceFeedName = sourceFeedName,
        FeedType = feedType,
        SourcePublishedAt = sourcePublishedAt,
        Quality = quality,
        RipType = ripType,
        FirstSeenAt = lastSeenAt,
        LastSeenAt = lastSeenAt
    };

    internal static RssItemProcessingState CreateItemState(long sourceId, string sourceItemKey, DateTimeOffset updatedAt) => new()
    {
        SourceId = sourceId,
        SourceItemKey = sourceItemKey,
        Fingerprint = $"fingerprint-{sourceItemKey}",
        Disposition = "resolved",
        UpdatedAt = updatedAt
    };
}
