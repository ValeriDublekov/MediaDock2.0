namespace MediaDock.Infrastructure.Persistence.Entities;

public sealed class TorrentMovie
{
    public long Id { get; set; }
    public string TitleText { get; set; } = string.Empty;
    public string NormalizedTitle { get; set; } = string.Empty;
    public int? Year { get; set; }
    public string MediaType { get; set; } = string.Empty;
    public string? SourceType { get; set; }
    public string? ContentKind { get; set; }
    public int? BroadcastRangeStartYear { get; set; }
    public int? BroadcastRangeEndYear { get; set; }
    public string? BroadcastRangeRaw { get; set; }
    public string? ImdbId { get; set; }
    public DateTimeOffset? FirstSeenAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ImdbTitle? ImdbTitle { get; set; }
    public ICollection<TorrentRelease> Releases { get; set; } = new List<TorrentRelease>();
}