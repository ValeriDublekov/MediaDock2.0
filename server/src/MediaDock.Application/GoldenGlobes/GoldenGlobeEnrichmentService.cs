using MediaDock.Application.Metadata;

namespace MediaDock.Application.GoldenGlobes;

public sealed record GoldenGlobeEnrichmentCandidate(string Title, int CeremonyYear, int AttemptCount, string SourceType = "movie");
public sealed record GoldenGlobeEnrichmentUpdate(string Status, int AttemptCount, DateTimeOffset AttemptedAt, DateTimeOffset? NextAttemptAt, string? ErrorCode, string? ImdbId);
public sealed record GoldenGlobeEnrichmentSummary(int EligibleTitles, int AttemptedTitles, int EnrichedTitles, int ProblemTitles, int NotFoundTitles, int TemporaryErrors, int CacheHits, int HttpAttempts, bool StoppedForQuota);
public sealed record GoldenGlobeEnrichmentResult(GoldenGlobeEnrichmentSummary Summary, string Status);

public static class GoldenGlobeEnrichmentStatuses
{
    public const string Pending = "pending";
    public const string Enriched = "enriched";
    public const string Problem = "problem";
    public const string NotFound = "not_found";
    public const string TemporaryError = "temporary_error";
}

public interface IGoldenGlobeEnrichmentRepository
{
    Task<IReadOnlyList<GoldenGlobeEnrichmentCandidate>> GetEligibleCandidatesAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
    Task SaveOutcomeAsync(string title, int ceremonyYear, string sourceType, GoldenGlobeEnrichmentUpdate update, CancellationToken cancellationToken = default);
}

public sealed class GoldenGlobeEnrichmentService(
    IGoldenGlobeEnrichmentRepository repository,
    MetadataResolver metadataResolver,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<GoldenGlobeEnrichmentResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var candidates = await repository.GetEligibleCandidatesAsync(clock.GetUtcNow(), cancellationToken);
        var attempted = 0; var enriched = 0; var problems = 0; var notFound = 0; var errors = 0; var cacheHits = 0; var http = 0; var quota = false;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attemptedAt = clock.GetUtcNow();
            var resolution = await metadataResolver.ResolveByTitleAsync(candidate.Title, candidate.SourceType, attemptedAt, cancellationToken, OmdbRequestPurpose.GoldenGlobeEnrichment);
            http += resolution.HttpAttempts; cacheHits += resolution.CacheHit ? 1 : 0;
            if (resolution.Status == MetadataLookupStatus.RequestBudgetExhausted) { quota = true; break; }
            var attempt = candidate.AttemptCount + 1;
            var status = GoldenGlobeEnrichmentStatuses.TemporaryError;
            string? error = resolution.ErrorCode ?? resolution.Status.ToString().ToLowerInvariant();
            string? imdb = null;
            DateTimeOffset? next = attemptedAt.AddHours(Math.Min(24, Math.Pow(2, Math.Clamp(attempt - 1, 0, 5))));
            if (resolution.Status == MetadataLookupStatus.Found && resolution.Metadata is not null)
            {
                imdb = ImdbIdNormalizer.Normalize(resolution.Metadata.ImdbId);
                if (imdb is null)
                { status = GoldenGlobeEnrichmentStatuses.Problem; error = "missing_imdb_id"; next = null; problems++; }
                else if (candidate.SourceType == "movie" && resolution.Metadata.Year is not int)
                { status = GoldenGlobeEnrichmentStatuses.Problem; error = "missing_year"; next = null; problems++; }
                else if (candidate.SourceType == "movie"
                    && resolution.Metadata.Year is int foundYear
                    && foundYear != candidate.CeremonyYear
                    && foundYear != candidate.CeremonyYear - 1)
                { status = GoldenGlobeEnrichmentStatuses.Problem; error = $"year_mismatch:{foundYear}"; next = null; problems++; }
                else { status = GoldenGlobeEnrichmentStatuses.Enriched; error = null; next = null; enriched++; }
            }
            else if (resolution.Status == MetadataLookupStatus.ConfirmedNotFound) { status = GoldenGlobeEnrichmentStatuses.NotFound; error = "not_found"; next = null; notFound++; }
            else errors++;
            await repository.SaveOutcomeAsync(candidate.Title, candidate.CeremonyYear, candidate.SourceType, new(status, attempt, attemptedAt, next, error, imdb), cancellationToken);
            attempted++;
            if (resolution.Status == MetadataLookupStatus.QuotaExceeded) { quota = true; break; }
        }
        var summary = new GoldenGlobeEnrichmentSummary(candidates.Count, attempted, enriched, problems, notFound, errors, cacheHits, http, quota);
        return new(summary, quota || errors > 0 || problems > 0 ? "partial" : "succeeded");
    }
}
