namespace MediaDock.Infrastructure.Persistence.Entities;

public sealed class GeneralRecommendationSettings
{
    public long Id { get; set; }
    public int MedianPeriodYears { get; set; } = 5;
    public bool RequiresVoteRecalculation { get; set; }
    public DateTimeOffset? LastVoteRecalculationAt { get; set; }
    public int? LastVoteRecalculationPeriodYears { get; set; }
}