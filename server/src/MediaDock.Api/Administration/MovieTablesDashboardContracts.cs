namespace MediaDock.Api.Administration;

/// <summary>Summary counts for the system's movie-related tables.</summary>
public sealed record MovieTablesDashboardResponse(
    ImdbTitlesSummary Imdb,
    GoldenGlobeTitlesSummary GoldenGlobes,
    OscarMoviesSummary Oscar,
    DateTimeOffset GeneratedAtUtc);

/// <summary>Counts and status breakdown for canonical IMDb titles.</summary>
public sealed record ImdbTitlesSummary(
    int TotalCount,
    Dictionary<string, int> ByMediaType,
    Dictionary<string, int> ByMetadataStatus,
    int EnrichedCount,
    int PendingCount,
    int NotFoundCount,
    int TemporaryErrorCount)
{
    public int FailedCount => NotFoundCount + TemporaryErrorCount;
}

/// <summary>Counts and status breakdown for Golden Globes titles and nominations.</summary>
public sealed record GoldenGlobeTitlesSummary(
    int TotalCount,
    Dictionary<string, int> ByNomineeType,
    Dictionary<string, int> ByEnrichmentStatus,
    int WithImdbLinkCount,
    int WithoutImdbLinkCount,
    int TotalNominationsCount);

/// <summary>Counts and status breakdown for Oscar films and nominations.</summary>
public sealed record OscarMoviesSummary(
    int TotalCount,
    Dictionary<string, int> ByEnrichmentStatus,
    int WithImdbLinkCount,
    int WithoutImdbLinkCount,
    int TotalNominationsCount);