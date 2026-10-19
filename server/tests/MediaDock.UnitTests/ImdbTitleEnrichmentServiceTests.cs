using MediaDock.Application.Metadata;
using MediaDock.Application.PersonalRatings;

namespace MediaDock.UnitTests;

public sealed class ImdbTitleEnrichmentServiceTests
{
    [Fact]
    public async Task BudgetDenialStopsWithoutChangingTheCandidate()
    {
        var now = new DateTimeOffset(2026, 10, 19, 10, 30, 0, TimeSpan.Zero);
        var repository = new RecordingRepository([
            new("tt1234567", 0),
            new("tt7654321", 0)
        ]);
        var client = new RecordingOmdbClient(_ => new MetadataLookupResult(
            MetadataLookupStatus.RequestBudgetExhausted,
            HttpAttempts: 0,
            ErrorCode: "daily_budget_exhausted"));
        var service = CreateService(repository, client, now);

        var result = await service.RunAsync(42);

        Assert.Equal("partial", result.Status);
        Assert.True(result.Summary.StoppedForQuota);
        Assert.Equal(2, result.Summary.EligibleTitles);
        Assert.Equal(0, result.Summary.AttemptedTitles);
        Assert.Empty(repository.Updates);
        Assert.Equal(["tt1234567"], client.RequestedImdbIds);
        Assert.Equal(0, result.Summary.HttpAttempts);
    }

    [Fact]
    public async Task ProviderQuotaDefersCurrentTitleAndStopsTheBatch()
    {
        var now = new DateTimeOffset(2026, 10, 19, 23, 30, 0, TimeSpan.Zero);
        var repository = new RecordingRepository([
            new("tt1234567", 0),
            new("tt7654321", 0)
        ]);
        var client = new RecordingOmdbClient(_ => new MetadataLookupResult(
            MetadataLookupStatus.QuotaExceeded,
            HttpAttempts: 1,
            ErrorCode: "quota_exceeded"));
        var service = CreateService(repository, client, now);

        var result = await service.RunAsync(42);

        var update = Assert.Single(repository.Updates);
        Assert.Equal("partial", result.Status);
        Assert.Equal("temporary_error", update.Update.Status);
        Assert.Equal("quota_exceeded", update.Update.ErrorCode);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero), update.Update.NextAttemptAt);
        Assert.Equal(42, update.OwnerUserId);
        Assert.Equal(["tt1234567"], client.RequestedImdbIds);
        Assert.True(result.Summary.StoppedForQuota);
    }

    [Fact]
    public async Task ConfirmedNotFoundUsesNotFoundStateAndDeferredRetry()
    {
        var now = new DateTimeOffset(2026, 10, 19, 10, 30, 0, TimeSpan.Zero);
        var repository = new RecordingRepository([new("tt1234567", 0)]);
        var client = new RecordingOmdbClient(_ => new MetadataLookupResult(
            MetadataLookupStatus.ConfirmedNotFound,
            HttpAttempts: 1,
            ErrorCode: "not_found"));
        var service = CreateService(repository, client, now);

        var result = await service.RunAsync(42);

        var update = Assert.Single(repository.Updates).Update;
        Assert.Equal("succeeded", result.Status);
        Assert.Equal("not_found", update.Status);
        Assert.Equal(1, update.AttemptCount);
        Assert.Equal("not_found", update.ErrorCode);
        Assert.Equal(now.AddDays(2), update.NextAttemptAt);
        Assert.Null(update.Metadata);
    }

    [Fact]
    public async Task TransientFailureUsesCappedExponentialRetryState()
    {
        var now = new DateTimeOffset(2026, 10, 19, 10, 30, 0, TimeSpan.Zero);
        var repository = new RecordingRepository([new("tt1234567", 2)]);
        var client = new RecordingOmdbClient(_ => new MetadataLookupResult(
            MetadataLookupStatus.TransportFailure,
            HttpAttempts: 1,
            ErrorCode: "timeout"));
        var service = CreateService(repository, client, now);

        var result = await service.RunAsync(42);

        var update = Assert.Single(repository.Updates).Update;
        Assert.Equal("partial", result.Status);
        Assert.Equal("temporary_error", update.Status);
        Assert.Equal(3, update.AttemptCount);
        Assert.Equal("timeout", update.ErrorCode);
        Assert.Equal(now.AddHours(4), update.NextAttemptAt);
    }

    [Fact]
    public async Task RunHonorsTheHardBatchLimit()
    {
        var now = new DateTimeOffset(2026, 10, 19, 10, 30, 0, TimeSpan.Zero);
        var candidates = Enumerable.Range(0, ImdbTitleEnrichmentService.MaximumBatchSize + 1)
            .Select(index => new ImdbTitleEnrichmentCandidate($"tt{index:D7}", 0))
            .ToArray();
        var repository = new RecordingRepository(candidates);
        var client = new RecordingOmdbClient(imdbId => CreateFoundResult(imdbId));
        var service = CreateService(repository, client, now);

        var result = await service.RunAsync(42);

        Assert.Equal(ImdbTitleEnrichmentService.MaximumBatchSize, repository.RequestedLimit);
        Assert.Equal(ImdbTitleEnrichmentService.MaximumBatchSize, result.Summary.EligibleTitles);
        Assert.Equal(ImdbTitleEnrichmentService.MaximumBatchSize, result.Summary.AttemptedTitles);
        Assert.Equal(ImdbTitleEnrichmentService.MaximumBatchSize, client.RequestedImdbIds.Count);
        Assert.DoesNotContain("tt0000100", client.RequestedImdbIds);
    }

    private static ImdbTitleEnrichmentService CreateService(
        RecordingRepository repository,
        RecordingOmdbClient client,
        DateTimeOffset now) =>
        new(repository, new MetadataResolver(client, new InMemoryMetadataCacheStore()), new FixedTimeProvider(now));

    private static MetadataLookupResult CreateFoundResult(string imdbId) => new(
        MetadataLookupStatus.Found,
        new MetadataDetails(
            "Resolved title",
            2020,
            imdbId,
            "movie",
            "movie",
            "standard",
            null,
            8.1m,
            1000,
            80m,
            [],
            [],
            null,
            null,
            null,
            null,
            null,
            null),
        1);

    private sealed class RecordingOmdbClient(Func<string, MetadataLookupResult> resolve) : IOmdbClient
    {
        public List<string> RequestedImdbIds { get; } = [];

        public Task<MetadataLookupResult> LookupAsync(
            string title,
            int? year,
            string sourceType,
            CancellationToken cancellationToken = default,
            OmdbRequestPurpose requestPurpose = OmdbRequestPurpose.RssIngestion,
            string? imdbId = null)
        {
            RequestedImdbIds.Add(imdbId ?? string.Empty);
            return Task.FromResult(resolve(imdbId ?? string.Empty));
        }
    }

    private sealed class RecordingRepository(IReadOnlyList<ImdbTitleEnrichmentCandidate> candidates)
        : IImdbTitleEnrichmentRepository
    {
        public int RequestedLimit { get; private set; }
        public List<(long OwnerUserId, string ImdbId, ImdbTitleEnrichmentUpdate Update)> Updates { get; } = [];

        public Task<IReadOnlyList<ImdbTitleEnrichmentCandidate>> GetEligibleCandidatesAsync(
            long ownerUserId,
            DateTimeOffset now,
            int limit,
            CancellationToken cancellationToken = default)
        {
            RequestedLimit = limit;
            return Task.FromResult<IReadOnlyList<ImdbTitleEnrichmentCandidate>>(candidates.Take(limit).ToArray());
        }

        public Task SaveOutcomeAsync(
            long ownerUserId,
            string imdbId,
            ImdbTitleEnrichmentUpdate update,
            CancellationToken cancellationToken = default)
        {
            Updates.Add((ownerUserId, imdbId, update));
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryMetadataCacheStore : IMetadataCacheStore
    {
        private readonly Dictionary<string, MetadataCacheValue> _values = new(StringComparer.Ordinal);

        public Task<MetadataCacheValue?> GetAsync(string cacheKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.GetValueOrDefault(cacheKey));

        public Task StoreAsync(string cacheKey, MetadataCacheValue value, CancellationToken cancellationToken = default)
        {
            _values[cacheKey] = value;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}