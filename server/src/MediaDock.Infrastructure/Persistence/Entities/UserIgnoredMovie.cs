namespace MediaDock.Infrastructure.Persistence.Entities;

public sealed class UserIgnoredMovie
{
    public long UserId { get; set; }
    public User User { get; set; } = null!;
    public string ImdbId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public ImdbTitle ImdbTitle { get; set; } = null!;
}