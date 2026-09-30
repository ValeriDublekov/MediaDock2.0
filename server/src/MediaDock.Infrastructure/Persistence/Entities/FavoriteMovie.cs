namespace MediaDock.Infrastructure.Persistence.Entities;

public sealed class FavoriteMovie
{
    public long TitleId { get; set; }
    public Title Title { get; set; } = null!;
    public bool ToWatch { get; set; }
    public bool ToDownload { get; set; }
    public bool AddedFromOscar { get; set; }
    public bool AddedFromCatalog { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}