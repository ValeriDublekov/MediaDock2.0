namespace MediaDock.Infrastructure.Persistence.Entities;

public sealed class GoldenGlobeNomination
{
    public long Id { get; set; }
    public string ImportKey { get; set; } = string.Empty;
    public bool Winner { get; set; }
    public long AwardId { get; set; }
    public long? GoldenGlobeTitleId { get; set; }

    public GoldenGlobeAward Award { get; set; } = null!;
    public GoldenGlobeTitle? GoldenGlobeTitle { get; set; }
}
