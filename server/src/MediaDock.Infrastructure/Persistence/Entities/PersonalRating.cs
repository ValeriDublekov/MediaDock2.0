namespace MediaDock.Infrastructure.Persistence.Entities;

public sealed class PersonalRating
{
    public string ImdbId { get; set; } = string.Empty;
    public int Rating { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}