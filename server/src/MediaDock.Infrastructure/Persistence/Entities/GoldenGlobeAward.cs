namespace MediaDock.Infrastructure.Persistence.Entities;

public sealed class GoldenGlobeAward
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CategoryKey { get; set; } = "award_other";

    public ICollection<GoldenGlobeNomination> Nominations { get; set; } = new List<GoldenGlobeNomination>();
}
