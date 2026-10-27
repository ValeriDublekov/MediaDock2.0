namespace MediaDock.Infrastructure.Persistence.Entities;

public sealed class RssFeedPreviewSnapshot
{
    public long Id { get; set; }
    public string ProfileId { get; set; } = string.Empty;
    public DateTimeOffset CachedAt { get; set; }
    public string FeedsJson { get; set; } = "[]";
}