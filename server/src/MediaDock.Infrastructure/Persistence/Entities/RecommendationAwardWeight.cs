namespace MediaDock.Infrastructure.Persistence.Entities;

public sealed class RecommendationAwardWeight
{
    public long Id { get; set; }
    public string AwardSource { get; set; } = string.Empty;
    public string CategoryKey { get; set; } = string.Empty;
    public decimal WinPoints { get; set; }
    public decimal NominationPoints { get; set; }
    public bool IsOverride { get; set; }
}