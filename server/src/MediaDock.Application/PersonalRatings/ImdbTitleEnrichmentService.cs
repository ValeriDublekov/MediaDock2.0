using MediaDock.Application.Metadata;

namespace MediaDock.Application.PersonalRatings;

public sealed class ImdbTitleEnrichmentService
{
    public const int MaximumBatchSize = 100;

    private static readonly TimeSpan NotFoundRetryDelay = TimeSpan.FromDays(2);

    private readonly IImdbTitleEnrichmentRepository _repository;
    private readonly MetadataResolver _metadataResolver;
    private readonly TimeProvider _timeProvider;

    public ImdbTitleEnrichmentService(
        IImdbTitleEnrichmentRepository repository,
        MetadataResolver metadataResolver,
        TimeProvider? timeProvider = null)
    {
        _repository = repository;
        _metadataResolver = metadataResolver;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<ImdbTitleEnrichmentResult> RunAsync(
        long ownerUserId,
        CancellationToken cancellationToken = default)
    {
        if (ownerUserId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ownerUserId));
        }

        return RunCoreAsync(
            (now, token) => _repository.GetEligibleCandidatesAsync(
                ownerUserId,
                now,
                MaximumBatchSize,
                token),
            (imdbId, update, token) => _repository.SaveOutcomeAsync(
                ownerUserId,
                imdbId,
                update,
                token),
            continueUntilEmpty: false,
            cancellationToken);
    }

    public Task<ImdbTitleEnrichmentResult> RunCatalogAsync(CancellationToken cancellationToken = default) =>
        RunCoreAsync(
            (now, token) => _repository.GetEligibleCatalogCandidatesAsync(
                now,
                MaximumBatchSize,
                token),
            (imdbId, update, token) => _repository.SaveCatalogOutcomeAsync(
                imdbId,
                update,
                token),
            continueUntilEmpty: true,
            cancellationToken);

    private async Task<ImdbTitleEnrichmentResult> RunCoreAsync(
        Func<DateTimeOffset, CancellationToken, Task<IReadOnlyList<ImdbTitleEnrichmentCandidate>>> getEligibleCandidatesAsync,
        Func<string, ImdbTitleEnrichmentUpdate, CancellationToken, Task> saveOutcomeAsync,
        bool continueUntilEmpty,
        CancellationToken cancellationToken)
    {
        var eligibleCount = 0;
        var attemptedTitles = 0;
        var enrichedTitles = 0;
        var notFoundTitles = 0;
        var temporaryErrors = 0;
        var cacheHits = 0;
        var httpAttempts = 0;
        var stoppedForQuota = false;

        while (true)
        {
            var eligible = await getEligibleCandidatesAsync(_timeProvider.GetUtcNow(), cancellationToken);
            eligibleCount += eligible.Count;
            foreach (var candidate in eligible)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attemptedAt = _timeProvider.GetUtcNow();
                var resolution = await _metadataResolver.ResolveByImdbIdAsync(
                    candidate.ImdbId,
                    attemptedAt,
                    cancellationToken,
                    OmdbRequestPurpose.ImdbTitleEnrichment);
                httpAttempts += resolution.HttpAttempts;
                cacheHits += resolution.CacheHit ? 1 : 0;

                if (resolution.Status == MetadataLookupStatus.RequestBudgetExhausted)
                {
                    stoppedForQuota = true;
                    break;
                }

                var update = CreateUpdate(resolution, candidate, attemptedAt);
                await saveOutcomeAsync(candidate.ImdbId, update, cancellationToken);
                attemptedTitles++;

                switch (update.Status)
                {
                    case ImdbTitleEnrichmentStatus.Enriched:
                        enrichedTitles++;
                        break;
                    case ImdbTitleEnrichmentStatus.NotFound:
                        notFoundTitles++;
                        break;
                    default:
                        temporaryErrors++;
                        break;
                }

                if (resolution.Status == MetadataLookupStatus.QuotaExceeded)
                {
                    stoppedForQuota = true;
                    break;
                }
            }

            if (stoppedForQuota || !continueUntilEmpty || eligible.Count < MaximumBatchSize)
            {
                break;
            }
        }

        var summary = new ImdbTitleEnrichmentSummary(
            eligibleCount,
            attemptedTitles,
            enrichedTitles,
            notFoundTitles,
            temporaryErrors,
            cacheHits,
            httpAttempts,
            stoppedForQuota);
        var status = stoppedForQuota || temporaryErrors > 0 ? "partial" : "succeeded";
        return new ImdbTitleEnrichmentResult(status, summary);
    }

    private static ImdbTitleEnrichmentUpdate CreateUpdate(
        MetadataResolution resolution,
        ImdbTitleEnrichmentCandidate candidate,
        DateTimeOffset attemptedAt)
    {
        var attemptCount = candidate.AttemptCount + 1;
        if (resolution.Status == MetadataLookupStatus.Found && resolution.Metadata is { } metadata)
        {
            var returnedImdbId = ImdbIdNormalizer.Normalize(metadata.ImdbId);
            if (!string.Equals(returnedImdbId, candidate.ImdbId, StringComparison.Ordinal))
            {
                return TemporaryError(attemptCount, attemptedAt, "imdb_id_mismatch");
            }

            return new(
                ImdbTitleEnrichmentStatus.Enriched,
                attemptCount,
                attemptedAt,
                null,
                null,
                metadata);
        }

        if (resolution.Status == MetadataLookupStatus.ConfirmedNotFound)
        {
            return new(
                ImdbTitleEnrichmentStatus.NotFound,
                attemptCount,
                attemptedAt,
                attemptedAt.Add(NotFoundRetryDelay),
                resolution.ErrorCode ?? "not_found",
                null);
        }

        var nextAttemptAt = resolution.Status == MetadataLookupStatus.QuotaExceeded
            ? NextUtcDay(attemptedAt)
            : attemptedAt.Add(GetRetryDelay(attemptCount));
        return new(
            ImdbTitleEnrichmentStatus.TemporaryError,
            attemptCount,
            attemptedAt,
            nextAttemptAt,
            resolution.ErrorCode ?? resolution.Status.ToString().ToLowerInvariant(),
            null);
    }

    private static ImdbTitleEnrichmentUpdate TemporaryError(
        int attemptCount,
        DateTimeOffset attemptedAt,
        string errorCode) =>
        new(
            ImdbTitleEnrichmentStatus.TemporaryError,
            attemptCount,
            attemptedAt,
            attemptedAt.Add(GetRetryDelay(attemptCount)),
            errorCode,
            null);

    private static TimeSpan GetRetryDelay(int attemptCount)
    {
        var exponent = Math.Clamp(attemptCount - 1, 0, 5);
        return TimeSpan.FromHours(Math.Min(24, Math.Pow(2, exponent)));
    }

    private static DateTimeOffset NextUtcDay(DateTimeOffset value) =>
        new(value.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
}