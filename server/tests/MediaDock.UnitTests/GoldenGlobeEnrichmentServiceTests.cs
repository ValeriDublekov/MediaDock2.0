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

        Assert.Equal(new GoldenGlobeEnrichmentSummary(4, 4, 1, 1, 1, 1, 0, 8, false), result.Summary);
        Assert.Equal("partial", result.Status);
        Assert.Equal("enriched", repository.SavedOutcomes[0].Update.Status);
        Assert.Equal("tt12345678", repository.SavedOutcomes[0].Update.ImdbId);
        Assert.Equal("problem", repository.SavedOutcomes[1].Update.Status);
        Assert.Equal("no_confident_match", repository.SavedOutcomes[1].Update.ErrorCode);
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
    public async Task RunAsyncLooksUpSeriesAndDoesNotComparePremiereYearToCeremonyYear()
    {
        var now = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        var repository = new FakeGoldenGlobeEnrichmentRepository(
        [new("The Crown", 2024, 0, "series")]);
        var client = new StubOmdbClient(
        [new(MetadataLookupStatus.Found, CreateMetadata("The Crown", 2016, "tt4786824") with { SourceType = "series" }, 1)]);

        var result = await CreateService(repository, client, now).RunAsync();

        Assert.Equal(1, result.Summary.EnrichedTitles);
        Assert.Equal(((int?)null, "series", null), Assert.Single(client.Requests));
        Assert.Equal("The Crown", Assert.Single(repository.SavedOutcomes).Title);
        Assert.Equal("enriched", Assert.Single(repository.SavedOutcomes).Update.Status);
    }

    [Fact]
    public async Task RunAsyncSearchesTitleVariantsAndFetchesCandidateDetailsByImdbId()
    {
        var now = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        var repository = new FakeGoldenGlobeEnrichmentRepository(
        [new("Film & Story", 2025, 0)]);
        var client = new StubOmdbClient(
        [
            new(MetadataLookupStatus.Found, CreateMetadata("Film and Story", 2020, "tt11111111"), 1),
            new(MetadataLookupStatus.Found, CreateMetadata("Film and Story", 2024, "tt22222222", "Nominated for 1 Golden Globe"), 1)
        ],
        [
            new(MetadataLookupStatus.ConfirmedNotFound, [], 0, 1, "not_found"),
            new(MetadataLookupStatus.Found,
                [new MetadataSearchCandidate("Film and Story", 2024, "tt22222222", "movie")], 1, 1),
            new(MetadataLookupStatus.ConfirmedNotFound, [], 0, 1, "not_found")
        ]);

        var result = await CreateService(repository, client, now).RunAsync();

        Assert.Equal(1, result.Summary.EnrichedTitles);
        Assert.Equal(5, result.Summary.HttpAttempts);
        Assert.Equal(["Film & Story", "Film and Story", "Film Story"], client.SearchRequests.Select(request => request.Title));
        Assert.Equal((null, "movie", null), client.Requests[0]);
        Assert.Equal((2025, "movie", "tt22222222"), client.Requests[1]);
        Assert.Equal("tt22222222", repository.SavedOutcomes[0].Update.ImdbId);
    }

    [Fact]
    public async Task RunAsyncCorrectsNomineeTypeAfterVerifiedCrossTypeMatch()
    {
        var now = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        var repository = new FakeGoldenGlobeEnrichmentRepository(
        [new("Too Big To Fail", 2012, 0, "series")]);
        var client = new StubOmdbClient(
        [
            new(MetadataLookupStatus.ConfirmedNotFound, HttpAttempts: 1),
            new(MetadataLookupStatus.Found, CreateMetadata("Too Big to Fail", 2011, "tt1742683", "Nominated for 1 Golden Globe"), 1)
        ],
        [new(MetadataLookupStatus.Found,
            [new MetadataSearchCandidate("Too Big to Fail", 2011, "tt1742683", "movie")], 1, 1)]);

        var result = await CreateService(repository, client, now).RunAsync();

        Assert.Equal(1, result.Summary.EnrichedTitles);
        Assert.Equal("movie", repository.SavedOutcomes[0].Update.ResolvedNomineeType);
        Assert.Equal("tt1742683", repository.SavedOutcomes[0].Update.ImdbId);
        Assert.Null(client.SearchRequests[0].SourceType);
        Assert.Equal((2012, "movie", "tt1742683"), client.Requests[1]);
    }

    [Fact]
    public async Task RunAsyncChecksDetailsForClosestCandidateBelowSearchScoreThreshold()
    {
        var now = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        var repository = new FakeGoldenGlobeEnrichmentRepository(
        [new("The Same Film", 2025, 0)]);
        var client = new StubOmdbClient(
        [
            new(MetadataLookupStatus.ConfirmedNotFound, HttpAttempts: 1),
            new(MetadataLookupStatus.Found, CreateMetadata("The Same Film", 2024, "tt44444444", "Nominated for 1 Golden Globe"), 1)
        ],
        [new(MetadataLookupStatus.Found,
            [new MetadataSearchCandidate("The Same Fim", 2024, "tt44444444", "movie")], 1, 1)]);

        var result = await CreateService(repository, client, now).RunAsync();

        Assert.Equal(1, result.Summary.EnrichedTitles);
        Assert.Equal("enriched", repository.SavedOutcomes[0].Update.Status);
        Assert.Equal("tt44444444", repository.SavedOutcomes[0].Update.ImdbId);
        Assert.Equal((2025, "movie", "tt44444444"), client.Requests[1]);
    }

    [Fact]
    public async Task RunAsyncSearchesSeriesWithoutFilteringPremiereYear()
    {
        var now = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        var repository = new FakeGoldenGlobeEnrichmentRepository(
        [new("Abbott Elementary", 2024, 0, "series")]);
        var client = new StubOmdbClient(
        [
            new(MetadataLookupStatus.ConfirmedNotFound, HttpAttempts: 1),
            new(MetadataLookupStatus.Found,
                CreateMetadata("Abbott Elementary", 2021, "tt14218830", "Nominated for 1 Golden Globe") with { SourceType = "series" }, 1)
        ],
        [new(MetadataLookupStatus.Found,
            [new MetadataSearchCandidate("Abbott Elementary", 2021, "tt14218830", "series")], 1, 1)]);

        var result = await CreateService(repository, client, now).RunAsync();

        Assert.Equal(1, result.Summary.EnrichedTitles);
        Assert.Equal((null, "series", null), client.Requests[0]);
        Assert.Equal((null, "series", "tt14218830"), client.Requests[1]);
        Assert.Equal("enriched", repository.SavedOutcomes[0].Update.Status);
    }

    [Fact]
    public async Task RunAsyncReadsLaterSearchPagesBeforeSelectingCandidate()
    {
        var now = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        var repository = new FakeGoldenGlobeEnrichmentRepository(
        [new("Pagination Film", 2025, 0)]);
        var client = new StubOmdbClient(
        [
            new(MetadataLookupStatus.ConfirmedNotFound, HttpAttempts: 1),
            new(MetadataLookupStatus.Found, CreateMetadata("Pagination Film", 2024, "tt33333333", "Nominated for 1 Golden Globe"), 1)
        ],
        [
            new(MetadataLookupStatus.Found,
                [new MetadataSearchCandidate("Pagination Film", 2020, "tt11111111", "movie")], 11, 1),
            new(MetadataLookupStatus.Found,
                [new MetadataSearchCandidate("Pagination Film", 2024, "tt33333333", "movie")], 11, 1)
        ]);

        var result = await CreateService(repository, client, now).RunAsync();

        Assert.Equal(1, result.Summary.EnrichedTitles);
        Assert.Equal(4, result.Summary.HttpAttempts);
        Assert.Equal([1, 2], client.SearchRequests.Select(request => request.Page));
        Assert.Equal("tt33333333", repository.SavedOutcomes[0].Update.ImdbId);
    }

    [Fact]
    public async Task RunAsyncUsesGoldenGlobeAwardsToResolveCloselyRankedCandidates()
    {
        var now = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        var repository = new FakeGoldenGlobeEnrichmentRepository(
        [new("Shared Title", 2025, 0)]);
        var client = new StubOmdbClient(
        [
            new(MetadataLookupStatus.ConfirmedNotFound, HttpAttempts: 1),
            new(MetadataLookupStatus.Found, CreateMetadata("Shared Title", 2025, "tt11111111", "2 wins & 3 nominations"), 1),
            new(MetadataLookupStatus.Found, CreateMetadata("Shared Title", 2025, "tt22222222", "Nominated for 1 Golden Globe"), 1)
        ],
        [new(MetadataLookupStatus.Found,
        [
            new MetadataSearchCandidate("Shared Title", 2025, "tt11111111", "movie"),
            new MetadataSearchCandidate("Shared Title", 2025, "tt22222222", "movie")
        ], 2, 1)]);

        var result = await CreateService(repository, client, now).RunAsync();

        Assert.Equal(1, result.Summary.EnrichedTitles);
        Assert.Equal("enriched", repository.SavedOutcomes[0].Update.Status);
        Assert.Equal("tt22222222", repository.SavedOutcomes[0].Update.ImdbId);
        Assert.Equal([null, "tt11111111", "tt22222222"], client.Requests.Select(request => request.ImdbId));
    }

    [Fact]
    public async Task RunAsyncKeepsMultipleGoldenGlobeCandidatesAmbiguous()
    {
        var now = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        var repository = new FakeGoldenGlobeEnrichmentRepository(
        [new("Shared Title", 2025, 0)]);
        var client = new StubOmdbClient(
        [
            new(MetadataLookupStatus.ConfirmedNotFound, HttpAttempts: 1),
            new(MetadataLookupStatus.Found, CreateMetadata("Shared Title", 2025, "tt11111111", "Nominated for 1 Golden Globe"), 1),
            new(MetadataLookupStatus.Found, CreateMetadata("Shared Title", 2025, "tt22222222", "Nominated for 2 Golden Globes"), 1)
        ],
        [new(MetadataLookupStatus.Found,
        [
            new MetadataSearchCandidate("Shared Title", 2025, "tt11111111", "movie"),
            new MetadataSearchCandidate("Shared Title", 2025, "tt22222222", "movie")
        ], 2, 1)]);

        var result = await CreateService(repository, client, now).RunAsync();

        Assert.Equal(1, result.Summary.ProblemTitles);
        Assert.Equal("problem", repository.SavedOutcomes[0].Update.Status);
        Assert.Equal("ambiguous_match", repository.SavedOutcomes[0].Update.ErrorCode);
        Assert.Null(repository.SavedOutcomes[0].Update.ImdbId);
    }

    [Fact]
    public async Task RunAsyncRejectsSearchCandidateWithoutGoldenGlobeAwardEvidence()
    {
        var now = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        var repository = new FakeGoldenGlobeEnrichmentRepository(
        [new("Candidate Film", 2025, 0)]);
        var client = new StubOmdbClient(
        [
            new(MetadataLookupStatus.ConfirmedNotFound, HttpAttempts: 1),
            new(MetadataLookupStatus.Found, CreateMetadata("Candidate Film", 2024, "tt11111111", "5 wins & 12 nominations"), 1)
        ],
        [new(MetadataLookupStatus.Found,
            [new MetadataSearchCandidate("Candidate Film", 2024, "tt11111111", "movie")], 1, 1)]);

        var result = await CreateService(repository, client, now).RunAsync();

        Assert.Equal(1, result.Summary.ProblemTitles);
        Assert.Equal("problem", repository.SavedOutcomes[0].Update.Status);
        Assert.Equal("no_golden_globe_match", repository.SavedOutcomes[0].Update.ErrorCode);
        Assert.Null(repository.SavedOutcomes[0].Update.ImdbId);
    }

    [Fact]
    public async Task RunAsyncMarksCandidateDetailIdMismatchAsProblem()
    {
        var now = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        var repository = new FakeGoldenGlobeEnrichmentRepository(
        [new("Candidate Film", 2025, 0)]);
        var client = new StubOmdbClient(
        [
            new(MetadataLookupStatus.ConfirmedNotFound, HttpAttempts: 1),
            new(MetadataLookupStatus.Found, CreateMetadata("Candidate Film", 2024, "tt22222222"), 1)
        ],
        [new(MetadataLookupStatus.Found,
            [new MetadataSearchCandidate("Candidate Film", 2024, "tt11111111", "movie")], 1, 1)]);

        var result = await CreateService(repository, client, now).RunAsync();

        Assert.Equal(1, result.Summary.ProblemTitles);
        Assert.Equal("problem", repository.SavedOutcomes[0].Update.Status);
        Assert.Equal("candidate_mismatch", repository.SavedOutcomes[0].Update.ErrorCode);
        Assert.Null(repository.SavedOutcomes[0].Update.ImdbId);
    }

    [Fact]
    public async Task RunAsyncRejectsInvalidImdbIdsAndIncompatibleTypes()
    {
        var now = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        var repository = new FakeGoldenGlobeEnrichmentRepository(
        [
            new("Invalid ID Film", 2025, 0),
            new("Wrong Type Series", 2025, 0, "series")
        ]);
        var client = new StubOmdbClient(
        [
            new(MetadataLookupStatus.Found, CreateMetadata("Invalid ID Film", 2024, "not-an-imdb-id"), 1),
            new(MetadataLookupStatus.Found, CreateMetadata("Wrong Type Series", 2020, "tt22222222"), 1)
        ]);

        var result = await CreateService(repository, client, now).RunAsync();

        Assert.Equal(2, result.Summary.ProblemTitles);
        Assert.Equal("no_confident_match", repository.SavedOutcomes[0].Update.ErrorCode);
        Assert.Null(repository.SavedOutcomes[0].Update.ImdbId);
        Assert.Equal("no_confident_match", repository.SavedOutcomes[1].Update.ErrorCode);
        Assert.Null(repository.SavedOutcomes[1].Update.ImdbId);
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

    [Fact]
    public async Task RefreshManualAsyncLooksUpExactIdAndPersistsSuccessfulMetadata()
    {
        var now = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        var repository = new FakeGoldenGlobeEnrichmentRepository([]);
        var client = new StubOmdbClient(
        [new(MetadataLookupStatus.Found, CreateMetadata("Other Display Title", 2018, "tt12345678"), 1)]);
        var candidate = new GoldenGlobeManualRefreshCandidate("Nomination Title", 2025, "movie", "tt12345678", 3, 0);

        var result = await CreateService(repository, client, now).RefreshManualAsync(candidate);

        Assert.Equal("enriched", result.Status);
        Assert.True(result.Applied);
        Assert.Equal(1, result.HttpAttempts);
        Assert.Equal((2025, "movie", "tt12345678"), Assert.Single(client.Requests));
        Assert.Equal(candidate, Assert.Single(repository.SavedManualOutcomes).Candidate);
        Assert.Equal("enriched", repository.SavedManualOutcomes[0].Update.Status);
        Assert.Equal("tt12345678", repository.SavedManualOutcomes[0].Update.ImdbId);
    }

    [Fact]
    public async Task RefreshManualAsyncRejectsAnotherIdButTrustsExactManualIdAcrossTypes()
    {
        var now = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        var repository = new FakeGoldenGlobeEnrichmentRepository([]);
        var mismatchedIdClient = new StubOmdbClient(
        [new(MetadataLookupStatus.Found, CreateMetadata("Film", 2024, "tt87654321"), 1)]);
        var candidate = new GoldenGlobeManualRefreshCandidate("Film", 2025, "movie", "tt12345678", 2, 0);

        var mismatchedId = await CreateService(repository, mismatchedIdClient, now).RefreshManualAsync(candidate);

        Assert.Equal("problem", mismatchedId.Status);
        Assert.Equal("imdb_id_mismatch", mismatchedId.ErrorCode);
        Assert.Equal("tt12345678", repository.SavedManualOutcomes[0].Update.ImdbId);

        var mismatchedTypeClient = new StubOmdbClient(
        [new(MetadataLookupStatus.Found, CreateMetadata("Film", 2024, "tt12345678"), 1)]);
        var seriesCandidate = candidate with { SourceType = "series" };
        var mismatchedType = await CreateService(repository, mismatchedTypeClient, now).RefreshManualAsync(seriesCandidate);

        Assert.Equal("enriched", mismatchedType.Status);
        Assert.Null(mismatchedType.ErrorCode);
        Assert.True(mismatchedType.Applied);
        Assert.Equal("enriched", repository.SavedManualOutcomes[1].Update.Status);
        Assert.Equal("tt12345678", repository.SavedManualOutcomes[1].Update.ImdbId);

        var unsupportedTypeClient = new StubOmdbClient(
        [new(MetadataLookupStatus.Found, CreateMetadata("Film", 2024, "tt12345678") with { SourceType = "episode" }, 1)]);
        var unsupportedType = await CreateService(repository, unsupportedTypeClient, now).RefreshManualAsync(seriesCandidate);

        Assert.Equal("problem", unsupportedType.Status);
        Assert.Equal("type_mismatch", unsupportedType.ErrorCode);
    }

    private static GoldenGlobeEnrichmentService CreateService(
        FakeGoldenGlobeEnrichmentRepository repository,
        StubOmdbClient client,
        DateTimeOffset now) => new(
            repository,
            new MetadataResolver(client, new EmptyMetadataCacheStore()),
            new FrozenTimeProvider(now));

    private static MetadataDetails CreateMetadata(string title, int? year, string imdbId, string? awards = null) => new(
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
        awards,
        null);

    private sealed class FakeGoldenGlobeEnrichmentRepository(
        IReadOnlyList<GoldenGlobeEnrichmentCandidate> candidates) : IGoldenGlobeEnrichmentRepository
    {
        public List<(string Title, int CeremonyYear, GoldenGlobeEnrichmentUpdate Update)> SavedOutcomes { get; } = [];
        public List<(GoldenGlobeManualRefreshCandidate Candidate, GoldenGlobeEnrichmentUpdate Update)> SavedManualOutcomes { get; } = [];

        public Task<IReadOnlyList<GoldenGlobeEnrichmentCandidate>> GetEligibleCandidatesAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken = default) => Task.FromResult(candidates);

        public Task SaveOutcomeAsync(
            string title,
            int ceremonyYear,
            string sourceType,
            GoldenGlobeEnrichmentUpdate update,
            CancellationToken cancellationToken = default)
        {
            SavedOutcomes.Add((title, ceremonyYear, update));
            return Task.CompletedTask;
        }

        public Task<bool> SaveManualOutcomeAsync(
            GoldenGlobeManualRefreshCandidate candidate,
            GoldenGlobeEnrichmentUpdate update,
            CancellationToken cancellationToken = default)
        {
            SavedManualOutcomes.Add((candidate, update));
            return Task.FromResult(true);
        }
    }

    private sealed class StubOmdbClient(
        IReadOnlyList<MetadataLookupResult> results,
        IReadOnlyList<MetadataSearchResult>? searchResults = null) : IOmdbClient
    {
        private readonly Queue<MetadataLookupResult> _results = new(results);
        private readonly Queue<MetadataSearchResult> _searchResults = new(searchResults ?? []);

        public int Calls { get; private set; }
        public List<OmdbRequestPurpose> RequestPurposes { get; } = [];
        public List<(int? Year, string SourceType, string? ImdbId)> Requests { get; } = [];
        public List<(string Title, string? SourceType, int Page)> SearchRequests { get; } = [];

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
            Requests.Add((year, sourceType, imdbId));
            return Task.FromResult(_results.Dequeue());
        }

        public Task<MetadataSearchResult> SearchAsync(
            string title,
            string? sourceType,
            int page,
            CancellationToken cancellationToken = default,
            OmdbRequestPurpose requestPurpose = OmdbRequestPurpose.RssIngestion)
        {
            SearchRequests.Add((title, sourceType, page));
            RequestPurposes.Add(requestPurpose);
            return Task.FromResult(_searchResults.Count > 0
                ? _searchResults.Dequeue()
                : new MetadataSearchResult(MetadataLookupStatus.ConfirmedNotFound, [], 0, 1, "not_found"));
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