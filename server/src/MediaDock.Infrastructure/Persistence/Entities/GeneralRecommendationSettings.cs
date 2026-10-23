namespace MediaDock.Infrastructure.Persistence.Entities;

public sealed class GeneralRecommendationSettings
{
    public long Id { get; set; }
    public int MedianPeriodYears { get; set; } = 5;
    public decimal BayesianDefaultC { get; set; } = 5.5m;
    public decimal BayesianMRatio { get; set; } = 0.25m;
    public decimal OscarWinPoints { get; set; } = 3m;
    public decimal OscarNomPoints { get; set; } = 1m;
    public decimal GgWinPoints { get; set; } = 2m;
    public decimal GgNomPoints { get; set; } = 0.5m;
    public bool RequiresVoteRecalculation { get; set; }
    public DateTimeOffset? LastVoteRecalculationAt { get; set; }
    public int? LastVoteRecalculationPeriodYears { get; set; }
}