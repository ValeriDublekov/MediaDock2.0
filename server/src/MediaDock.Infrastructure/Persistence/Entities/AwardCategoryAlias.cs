namespace MediaDock.Infrastructure.Persistence.Entities;

public sealed class AwardCategoryAlias
{
    public long Id { get; set; }
    public string AwardSource { get; set; } = string.Empty;
    public string RawName { get; set; } = string.Empty;
    public string CategoryKey { get; set; } = string.Empty;
}