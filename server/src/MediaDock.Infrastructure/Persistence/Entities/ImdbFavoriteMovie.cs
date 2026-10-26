namespace MediaDock.Infrastructure.Persistence.Entities;

public sealed class ImdbFavoriteMovie
{
    public long UserId { get; set; }
    public string ImdbId { get; set; } = string.Empty;
    public bool ToWatch { get; set; }
    public bool ToDownload { get; set; }
    public bool AddedFromOscar { get; set; }
    public bool AddedFromCatalog { get; set; }
    public bool AddedFromRecommendations { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public User User { get; set; } = null!;
    public ImdbTitle ImdbTitle { get; set; } = null!;
}