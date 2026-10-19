using MediaDock.Application.Metadata;

namespace MediaDock.Application.PersonalRatings;

public static class ImdbTitleEnrichmentStatuses
{
    public const string Pending = "pending";
    public const string Enriched = "enriched";
    public const string NotFound = "not_found";
    public const string TemporaryError = "temporary_error";
}

public sealed record ImdbTitleEnrichmentCandidate(string ImdbId, int AttemptCount);

public sealed record ImdbTitleEnrichmentUpdate(
    string Status,
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

    Task SaveOutcomeAsync(
        long ownerUserId,
        string imdbId,
        ImdbTitleEnrichmentUpdate update,
        CancellationToken cancellationToken = default);
}