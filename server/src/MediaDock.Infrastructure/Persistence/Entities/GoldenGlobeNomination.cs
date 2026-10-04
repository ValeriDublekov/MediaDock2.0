namespace MediaDock.Infrastructure.Persistence.Entities;

public sealed class GoldenGlobeNomination
{
    public long Id { get; set; }
    public string ImportKey { get; set; } = string.Empty;
    public int Year { get; set; }
    public bool Winner { get; set; }
    public long AwardId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? ImdbId { get; set; }

    public GoldenGlobeAward Award { get; set; } = null!;
}
