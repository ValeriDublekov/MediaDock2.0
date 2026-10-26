using MediaDock.Application.Metadata;

namespace MediaDock.Application.PersonalRatings;

public enum ImdbTitleEnrichmentStatus
{
    Pending,
    Enriched,
    NotFound,
    TemporaryError
}

public static class ImdbTitleEnrichmentStatusValues
{
    public const string Pending = "pending";
    public const string Enriched = "enriched";
    public const string NotFound = "not_found";
    public const string TemporaryError = "temporary_error";

    public static string ToDatabaseValue(this ImdbTitleEnrichmentStatus status) => status switch
    {
        ImdbTitleEnrichmentStatus.Pending => Pending,
        ImdbTitleEnrichmentStatus.Enriched => Enriched,
        ImdbTitleEnrichmentStatus.NotFound => NotFound,
        ImdbTitleEnrichmentStatus.TemporaryError => TemporaryError,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "The IMDb title enrichment status is invalid.")
    };
}

public sealed record ImdbTitleEnrichmentCandidate(string ImdbId, int AttemptCount);

public sealed record ImdbTitleEnrichmentUpdate(
    ImdbTitleEnrichmentStatus Status,
    int AttemptCount,
    DateTimeOffset AttemptedAt,
    DateTimeOffset? NextAttemptAt,
    string? ErrorCode,
    MetadataDetails? Metadata);

public sealed record ImdbTitleEnrichmentSummary(
    int EligibleTitles,
    int AttemptedTitles,
    int EnrichedTitles,
    int NotFoundTitles,
    int TemporaryErrors,
    int CacheHits,
    int HttpAttempts,
    bool StoppedForQuota);

public sealed record ImdbTitleEnrichmentResult(
    string Status,
    ImdbTitleEnrichmentSummary Summary);

public interface IImdbTitleEnrichmentRepository
{
    Task<IReadOnlyList<ImdbTitleEnrichmentCandidate>> GetEligibleCandidatesAsync(
        long ownerUserId,
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImdbTitleEnrichmentCandidate>> GetEligibleCatalogCandidatesAsync(
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken = default);

    Task SaveOutcomeAsync(
        long ownerUserId,
        string imdbId,
        ImdbTitleEnrichmentUpdate update,
        CancellationToken cancellationToken = default);

    Task SaveCatalogOutcomeAsync(
        string imdbId,
        ImdbTitleEnrichmentUpdate update,
        CancellationToken cancellationToken = default);
}