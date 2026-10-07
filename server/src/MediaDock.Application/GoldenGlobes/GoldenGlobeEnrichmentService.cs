using MediaDock.Application.Metadata;

namespace MediaDock.Application.GoldenGlobes;

public sealed record GoldenGlobeEnrichmentCandidate(string Title, int CeremonyYear, int AttemptCount, string SourceType = "movie");
public sealed record GoldenGlobeManualRefreshCandidate(string Title, int CeremonyYear, string SourceType, string ImdbId, int LinkVersion, int AttemptCount);
public sealed record GoldenGlobeEnrichmentUpdate(string Status, int AttemptCount, DateTimeOffset AttemptedAt, DateTimeOffset? NextAttemptAt, string? ErrorCode, string? ImdbId, string? ResolvedNomineeType = null);
public sealed record GoldenGlobeEnrichmentSummary(int EligibleTitles, int AttemptedTitles, int EnrichedTitles, int ProblemTitles, int NotFoundTitles, int TemporaryErrors, int CacheHits, int HttpAttempts, bool StoppedForQuota);
public sealed record GoldenGlobeEnrichmentResult(GoldenGlobeEnrichmentSummary Summary, string Status);
public sealed record GoldenGlobeManualRefreshResult(string Status, string? ErrorCode, string ImdbId, int HttpAttempts, bool CacheHit, bool Applied);

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
    Task<bool> SaveManualOutcomeAsync(GoldenGlobeManualRefreshCandidate candidate, GoldenGlobeEnrichmentUpdate update, CancellationToken cancellationToken = default);
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
            var resolution = await metadataResolver.ResolveGoldenGlobeAsync(
                candidate.Title,
                candidate.CeremonyYear,
                candidate.SourceType,
                attemptedAt,
                cancellationToken,
                OmdbRequestPurpose.GoldenGlobeEnrichment);
            http += resolution.HttpAttempts; cacheHits += resolution.CacheHit ? 1 : 0;
            if (resolution.Status == MetadataLookupStatus.RequestBudgetExhausted) { quota = true; break; }
            var attempt = candidate.AttemptCount + 1;
            var status = GoldenGlobeEnrichmentStatuses.TemporaryError;
            string? error = resolution.ErrorCode ?? resolution.Status.ToString().ToLowerInvariant();
            string? imdb = null;
            string? resolvedNomineeType = null;
            DateTimeOffset? next = attemptedAt.AddHours(Math.Min(24, Math.Pow(2, Math.Clamp(attempt - 1, 0, 5))));
            if (resolution.Status == MetadataLookupStatus.Found && resolution.Metadata is not null)
            {
                imdb = ImdbIdNormalizer.Normalize(resolution.Metadata.ImdbId);
                if (!ImdbIdNormalizer.IsValid(imdb))
                { status = GoldenGlobeEnrichmentStatuses.Problem; error = imdb is null ? "missing_imdb_id" : "invalid_imdb_id"; imdb = null; next = null; problems++; }
                else if (resolution.Metadata.SourceType is not ("movie" or "series"))
                { status = GoldenGlobeEnrichmentStatuses.Problem; error = "type_mismatch"; imdb = null; next = null; problems++; }
                else if (resolution.Metadata.SourceType == "movie" && resolution.Metadata.Year is not int)
                { status = GoldenGlobeEnrichmentStatuses.Problem; error = "missing_year"; next = null; problems++; }
                else if (resolution.Metadata.SourceType == "movie"
                    && resolution.Metadata.Year is int foundYear
                    && foundYear != candidate.CeremonyYear
                    && foundYear != candidate.CeremonyYear - 1)
                { status = GoldenGlobeEnrichmentStatuses.Problem; error = $"year_mismatch:{foundYear}"; next = null; problems++; }
                else { status = GoldenGlobeEnrichmentStatuses.Enriched; error = null; next = null; resolvedNomineeType = resolution.Metadata.SourceType; enriched++; }
            }
            else if (resolution.Status == MetadataLookupStatus.ConfirmedNotFound) { status = GoldenGlobeEnrichmentStatuses.NotFound; error = "not_found"; next = null; notFound++; }
            else if (resolution.Status == MetadataLookupStatus.ProviderFailure
                && resolution.ErrorCode is "ambiguous_match" or "no_confident_match" or "candidate_mismatch" or "no_golden_globe_match")
            { status = GoldenGlobeEnrichmentStatuses.Problem; error = resolution.ErrorCode; next = null; problems++; }
            else errors++;
            await repository.SaveOutcomeAsync(candidate.Title, candidate.CeremonyYear, candidate.SourceType, new(status, attempt, attemptedAt, next, error, imdb, resolvedNomineeType), cancellationToken);
            attempted++;
            if (resolution.Status == MetadataLookupStatus.QuotaExceeded) { quota = true; break; }
        }
        var summary = new GoldenGlobeEnrichmentSummary(candidates.Count, attempted, enriched, problems, notFound, errors, cacheHits, http, quota);
        return new(summary, quota || errors > 0 || problems > 0 ? "partial" : "succeeded");
    }

    public async Task<GoldenGlobeManualRefreshResult> RefreshManualAsync(
        GoldenGlobeManualRefreshCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        var attemptedAt = clock.GetUtcNow();
        var resolution = await metadataResolver.ResolveAsync(
            candidate.Title,
            candidate.CeremonyYear,
            candidate.SourceType,
            attemptedAt,
            cancellationToken,
            OmdbRequestPurpose.GoldenGlobeEnrichment,
            candidate.ImdbId);
        var attempt = candidate.AttemptCount + 1;
        DateTimeOffset? nextAttempt = attemptedAt.AddHours(Math.Min(24, Math.Pow(2, Math.Clamp(attempt - 1, 0, 5))));
        var status = GoldenGlobeEnrichmentStatuses.TemporaryError;
        var error = resolution.ErrorCode ?? resolution.Status.ToString().ToLowerInvariant();

        if (resolution.ErrorCode == "imdb_id_mismatch")
        {
            status = GoldenGlobeEnrichmentStatuses.Problem;
            error = "imdb_id_mismatch";
            nextAttempt = null;
        }
        else if (resolution.Status == MetadataLookupStatus.Found && resolution.Metadata is { } metadata)
        {
            var returnedId = ImdbIdNormalizer.Normalize(metadata.ImdbId);
            if (!string.Equals(returnedId, candidate.ImdbId, StringComparison.Ordinal))
            {
                status = GoldenGlobeEnrichmentStatuses.Problem;
                error = "imdb_id_mismatch";
                nextAttempt = null;
            }
            else if (!string.Equals(metadata.SourceType, candidate.SourceType, StringComparison.Ordinal))
            {
                status = GoldenGlobeEnrichmentStatuses.Problem;
                error = "type_mismatch";
                nextAttempt = null;
            }
            else
            {
                status = GoldenGlobeEnrichmentStatuses.Enriched;
                error = null;
                nextAttempt = null;
            }
        }
        else if (resolution.Status == MetadataLookupStatus.ConfirmedNotFound)
        {
            status = GoldenGlobeEnrichmentStatuses.NotFound;
            error = "not_found";
            nextAttempt = null;
        }

        var update = new GoldenGlobeEnrichmentUpdate(
            status,
            attempt,
            attemptedAt,
            nextAttempt,
            error,
            candidate.ImdbId);
        var applied = await repository.SaveManualOutcomeAsync(candidate, update, cancellationToken);
        return new(status, error, candidate.ImdbId, resolution.HttpAttempts, resolution.CacheHit, applied);
    }
}
