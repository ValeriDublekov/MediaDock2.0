namespace MediaDock.Infrastructure.Persistence.Entities;

public sealed class PersonalRating
{
    public long UserId { get; set; }
    public User User { get; set; } = null!;
    public string ImdbId { get; set; } = string.Empty;
    public int Rating { get; set; }
    public DateOnly DateRated { get; set; }
    public bool IsManuallyAdded { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ImdbTitle ImdbTitle { get; set; } = null!;
}