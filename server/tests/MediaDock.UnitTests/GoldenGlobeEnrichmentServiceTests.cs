using MediaDock.Application.GoldenGlobes;
using MediaDock.Application.Metadata;

namespace MediaDock.UnitTests;

[Trait("Category", "GoldenGlobes")]
public sealed class GoldenGlobeEnrichmentServiceTests
{
    [Fact]
    public async Task RunAsyncValidatesYearsAndPersistsTerminalAndTemporaryOutcomes()
    {
        var now = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        var repository = new FakeGoldenGlobeEnrichmentRepository(
        [
            new("Accepted Film", 2025, 2),
            new("Wrong Year Film", 2025, 0),
            new("Missing Film", 2025, 0),
            new("Unknown Film", 2025, 1)
        ]);
        var client = new StubOmdbClient(
        [
            new(MetadataLookupStatus.Found, CreateMetadata("Accepted Film", 2024, " TT12345678 "), 2),
            new(MetadataLookupStatus.Found, CreateMetadata("Wrong Year Film", 2023, "tt22222222"), 1),
            new(MetadataLookupStatus.ConfirmedNotFound, HttpAttempts: 1),
            new(MetadataLookupStatus.TransportFailure, HttpAttempts: 2, ErrorCode: "timeout")
        ]);
        var service = CreateService(repository, client, now);

        var result = await service.RunAsync();

        Assert.Equal(new GoldenGlobeEnrichmentSummary(4, 4, 1, 1, 1, 1, 0, 6, false), result.Summary);
        Assert.Equal("partial", result.Status);
        Assert.Equal("enriched", repository.SavedOutcomes[0].Update.Status);
        Assert.Equal("tt12345678", repository.SavedOutcomes[0].Update.ImdbId);
        Assert.Equal("problem", repository.SavedOutcomes[1].Update.Status);
        Assert.Equal("year_mismatch:2023", repository.SavedOutcomes[1].Update.ErrorCode);
        Assert.Null(repository.SavedOutcomes[1].Update.NextAttemptAt);
        Assert.Equal("not_found", repository.SavedOutcomes[2].Update.Status);
        Assert.Equal("temporary_error", repository.SavedOutcomes[3].Update.Status);
        Assert.Equal(2, repository.SavedOutcomes[3].Update.AttemptCount);
        Assert.Equal(now.AddHours(2), repository.SavedOutcomes[3].Update.NextAttemptAt);
        Assert.All(client.RequestPurposes, purpose => Assert.Equal(OmdbRequestPurpose.GoldenGlobeEnrichment, purpose));
        Assert.All(client.Requests, request => Assert.Equal((null, "movie"), (request.Year, request.SourceType)));
    }

    [Fact]
    public async Task RunAsyncDefersProviderQuotaAndStopsBeforeLaterCandidates()
    {
        var now = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        var repository = new FakeGoldenGlobeEnrichmentRepository(
        [
            new("Retry Film", 2025, 1),
            new("Quota Film", 2025, 0),
            new("Unprocessed Film", 2024, 0)
        ]);
        var client = new StubOmdbClient(
        [
            new(MetadataLookupStatus.TransportFailure, HttpAttempts: 1, ErrorCode: "timeout"),
            new(MetadataLookupStatus.QuotaExceeded, HttpAttempts: 1, ErrorCode: "quota_exceeded")
        ]);

        var result = await CreateService(repository, client, now).RunAsync();

        Assert.Equal(3, result.Summary.EligibleTitles);
        Assert.Equal(2, result.Summary.AttemptedTitles);
        Assert.Equal(2, result.Summary.TemporaryErrors);
        Assert.Equal(2, result.Summary.HttpAttempts);
        Assert.True(result.Summary.StoppedForQuota);
        Assert.Equal(2, client.Calls);
        Assert.Equal("timeout", repository.SavedOutcomes[0].Update.ErrorCode);
        Assert.Equal(now.AddHours(2), repository.SavedOutcomes[0].Update.NextAttemptAt);
        Assert.Equal("quota_exceeded", repository.SavedOutcomes[1].Update.ErrorCode);
        Assert.Equal(now.AddHours(1), repository.SavedOutcomes[1].Update.NextAttemptAt);
    }

    [Fact]
    public async Task RunAsyncLeavesCandidatesPendingWhenDailyBudgetDeniesRequest()
    {
        var now = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        var repository = new FakeGoldenGlobeEnrichmentRepository([new("Pending Film", 2025, 0)]);
        var client = new StubOmdbClient(
        [new(MetadataLookupStatus.RequestBudgetExhausted, ErrorCode: "daily_budget_exhausted")]);

        var result = await CreateService(repository, client, now).RunAsync();

        Assert.Equal("partial", result.Status);
        Assert.True(result.Summary.StoppedForQuota);
        Assert.Equal(0, result.Summary.AttemptedTitles);
        Assert.Empty(repository.SavedOutcomes);
        Assert.Equal(1, client.Calls);
    }

    private static GoldenGlobeEnrichmentService CreateService(
        FakeGoldenGlobeEnrichmentRepository repository,
        StubOmdbClient client,
        DateTimeOffset now) => new(
            repository,
            new MetadataResolver(client, new EmptyMetadataCacheStore()),
            new FrozenTimeProvider(now));

    private static MetadataDetails CreateMetadata(string title, int? year, string imdbId) => new(
        title,
        year,
        imdbId,
        "movie",
        "movie",
        "standard",
        null,
        null,
        null,
        null,
        [],
        [],
        null,
        null,
        null,
        null,
        null,
        null);

    private sealed class FakeGoldenGlobeEnrichmentRepository(
        IReadOnlyList<GoldenGlobeEnrichmentCandidate> candidates) : IGoldenGlobeEnrichmentRepository
    {
        public List<(string Title, int CeremonyYear, GoldenGlobeEnrichmentUpdate Update)> SavedOutcomes { get; } = [];

        public Task<IReadOnlyList<GoldenGlobeEnrichmentCandidate>> GetEligibleCandidatesAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken = default) => Task.FromResult(candidates);

        public Task SaveOutcomeAsync(
            string title,
            int ceremonyYear,
            GoldenGlobeEnrichmentUpdate update,
            CancellationToken cancellationToken = default)
        {
            SavedOutcomes.Add((title, ceremonyYear, update));
            return Task.CompletedTask;
        }
    }

    private sealed class StubOmdbClient(IReadOnlyList<MetadataLookupResult> results) : IOmdbClient
    {
        private readonly Queue<MetadataLookupResult> _results = new(results);

        public int Calls { get; private set; }
        public List<OmdbRequestPurpose> RequestPurposes { get; } = [];
        public List<(int? Year, string SourceType)> Requests { get; } = [];

        public Task<MetadataLookupResult> LookupAsync(
            string title,
            int? year,
            string sourceType,
            CancellationToken cancellationToken = default,
            OmdbRequestPurpose requestPurpose = OmdbRequestPurpose.RssIngestion,
            string? imdbId = null)
        {
            Calls++;
            RequestPurposes.Add(requestPurpose);
            Requests.Add((year, sourceType));
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class EmptyMetadataCacheStore : IMetadataCacheStore
    {
        public Task<MetadataCacheValue?> GetAsync(string cacheKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<MetadataCacheValue?>(null);

        public Task StoreAsync(string cacheKey, MetadataCacheValue value, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FrozenTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}