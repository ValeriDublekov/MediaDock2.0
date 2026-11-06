using System.Net;
using System.Net.Http.Json;
using MediaDock.Api.Operations;
using MediaDock.Api.Sources;
using MediaDock.Application.Metadata;
using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.IntegrationTests;

[Trait("Category", "Api")]
public sealed class SettingsApiTests
{
    [Fact]
    public async Task SettingsAndOmdbProviderSettingsCanBeReadAndUpdated()
    {
        await using var host = await CatalogApiTestHost.CreateAsync("mediadock_settings_api_test");
        var db = host.Db;
        var client = host.Client;

        using var settingsResponse = await client.GetAsync("/api/settings");
        var defaultSettings = await settingsResponse.Content.ReadFromJsonAsync<SettingsResponse>();
        Assert.NotNull(defaultSettings);
        Assert.Empty(defaultSettings.ExcludedGenres);
        Assert.Empty(defaultSettings.ExcludedCountries);
        Assert.Equal(0m, defaultSettings.MinMovieRating);
        Assert.Equal(0m, defaultSettings.MinSeriesRating);
        Assert.Equal(0, defaultSettings.MinImdbVotes);
        Assert.Null(defaultSettings.UpdatedAt);
        var initialSettingsRow = await db.Settings.AsNoTracking().SingleAsync();
        Assert.Null(initialSettingsRow.UpdatedAt);
        var initialRecommendationSettingsRow = await db.GeneralRecommendationSettings.AsNoTracking().SingleAsync();
        Assert.Equal(5, initialRecommendationSettingsRow.MedianPeriodYears);
        Assert.False(initialRecommendationSettingsRow.RequiresVoteRecalculation);

        using var updateSettingsResponse = await client.PutAsJsonAsync("/api/settings", new UpdateSettingsRequest
        {
            ExcludedGenres = ["Horror"],
            ExcludedCountries = ["USA"],
            MinMovieRating = 6.5m,
            MinSeriesRating = 7m,
            MinImdbVotes = 1000
        });
        Assert.Equal(HttpStatusCode.OK, updateSettingsResponse.StatusCode);
        Assert.Equal(6.5m, (await updateSettingsResponse.Content.ReadFromJsonAsync<SettingsResponse>())?.MinMovieRating);

        using var invalidSettingsResponse = await client.PutAsJsonAsync("/api/settings", new UpdateSettingsRequest
        {
            MinMovieRating = 11,
            MinSeriesRating = 0,
            MinImdbVotes = 0
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidSettingsResponse.StatusCode);
        Assert.NotNull(await invalidSettingsResponse.Content.ReadFromJsonAsync<ValidationProblemDetails>());

        using var defaultProviderSettingsResponse = await client.GetAsync("/api/settings/providers/omdb");
        var defaultProviderSettings = await defaultProviderSettingsResponse.Content
            .ReadFromJsonAsync<ProviderSettingsResponse>();
        Assert.NotNull(defaultProviderSettings);
        Assert.False(defaultProviderSettings.OmdbApiKeyConfigured);
        Assert.Equal(0, defaultProviderSettings.OmdbDailyRequestLimit);
        Assert.NotNull(defaultProviderSettings.UpdatedAt);

        var currentUtcDate = DateOnly.FromDateTime(DateTime.UtcNow);
        db.OmdbDailyUsage.AddRange(
            new OmdbDailyUsage
            {
                UtcDate = currentUtcDate,
                TotalRequests = 12,
                TorrentRequests = 3,
                OscarRequests = 3,
                DailyRequestLimitReached = true,
                ProviderQuotaExceeded = true,
                LastErrorCode = "quota_exceeded"
            },
            new OmdbDailyUsage
            {
                UtcDate = currentUtcDate.AddDays(-40),
                TotalRequests = 1
            });
        await db.SaveChangesAsync();

        using var omdbUsageResponse = await client.GetAsync("/api/settings/providers/omdb/usage");
        var omdbUsage = await omdbUsageResponse.Content.ReadFromJsonAsync<List<OmdbDailyUsageResponse>>();
        Assert.NotNull(omdbUsage);
        var currentUsage = Assert.Single(omdbUsage);
        Assert.Equal(currentUtcDate, currentUsage.UtcDate);
        Assert.Equal(12, currentUsage.TotalRequests);
        Assert.Equal(3, currentUsage.TorrentRequests);
        Assert.Equal(9, currentUsage.EnrichmentRequests);
        Assert.Equal(3, currentUsage.OscarRequests);
        Assert.True(currentUsage.DailyRequestLimitReached);
        Assert.False(currentUsage.EnrichmentRequestLimitReached);
        Assert.True(currentUsage.ProviderQuotaExceeded);
        Assert.Equal("quota_exceeded", currentUsage.LastErrorCode);

        using var invalidProviderSettingsResponse = await client.PutAsJsonAsync(
            "/api/settings/providers/omdb",
            new UpdateProviderSettingsRequest
            {
                OmdbApiKey = "test-omdb-key-value"
            });
        Assert.Equal(HttpStatusCode.BadRequest, invalidProviderSettingsResponse.StatusCode);
        Assert.NotNull(await invalidProviderSettingsResponse.Content.ReadFromJsonAsync<ValidationProblemDetails>());

        using var aboveMaximumProviderSettingsResponse = await client.PutAsJsonAsync(
            "/api/settings/providers/omdb",
            new UpdateProviderSettingsRequest
            {
                OmdbApiKey = "test-omdb-key-value",
                OmdbDailyRequestLimit = 1001
            });
        Assert.Equal(HttpStatusCode.BadRequest, aboveMaximumProviderSettingsResponse.StatusCode);

        const string testOmdbApiKey = "test-omdb-key-value";
        using var updateProviderSettingsResponse = await client.PutAsJsonAsync(
            "/api/settings/providers/omdb",
            new UpdateProviderSettingsRequest
            {
                OmdbApiKey = testOmdbApiKey,
                OmdbDailyRequestLimit = 20
            });
        Assert.Equal(HttpStatusCode.OK, updateProviderSettingsResponse.StatusCode);
        var providerSettingsJson = await updateProviderSettingsResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(testOmdbApiKey, providerSettingsJson);
        var savedProviderSettings = await updateProviderSettingsResponse.Content
            .ReadFromJsonAsync<ProviderSettingsResponse>();
        Assert.NotNull(savedProviderSettings);
        Assert.True(savedProviderSettings.OmdbApiKeyConfigured);
        Assert.Equal(20, savedProviderSettings.OmdbDailyRequestLimit);

        db.ChangeTracker.Clear();
        var providerSettingsInDatabase = await db.Settings.AsNoTracking().SingleAsync();
        Assert.Equal(1, providerSettingsInDatabase.Id);
        Assert.Equal(testOmdbApiKey, providerSettingsInDatabase.OmdbApiKey);

        using var clearProviderSettingsResponse = await client.PutAsJsonAsync(
            "/api/settings/providers/omdb",
            new UpdateProviderSettingsRequest
            {
                ClearOmdbApiKey = true,
                OmdbDailyRequestLimit = 20
            });
        Assert.Equal(HttpStatusCode.OK, clearProviderSettingsResponse.StatusCode);
        Assert.False((await clearProviderSettingsResponse.Content
            .ReadFromJsonAsync<ProviderSettingsResponse>())?.OmdbApiKeyConfigured);
    }

    [Fact]
    public async Task OmdbDiagnosticResetsDailyUsageOnlyAfterSuccessfulProviderResponse()
    {
        var omdbClient = new StubOmdbClient
        {
            Result = new MetadataLookupResult(
                MetadataLookupStatus.ProviderFailure,
                HttpAttempts: 1,
                ErrorCode: "provider_error")
        };
        await using var host = await CatalogApiTestHost.CreateAsync(
            "mediadock_omdb_diagnostic_api_test",
            omdbClientFactory: new StubOmdbClientFactory(omdbClient));

        using var settingsResponse = await host.Client.PutAsJsonAsync(
            "/api/settings/providers/omdb",
            new UpdateProviderSettingsRequest
            {
                OmdbApiKey = "test-omdb-key",
                OmdbDailyRequestLimit = 1
            });
        Assert.Equal(HttpStatusCode.OK, settingsResponse.StatusCode);

        var utcDate = DateOnly.FromDateTime(DateTime.UtcNow);
        host.Db.OmdbDailyUsage.Add(new OmdbDailyUsage
        {
            UtcDate = utcDate,
            TotalRequests = 12,
            TorrentRequests = 3,
            OscarRequests = 3,
            DailyRequestLimitReached = true,
            ProviderQuotaExceeded = true,
            LastErrorCode = "quota_exceeded"
        });
        await host.Db.SaveChangesAsync();

        using var failedResponse = await host.Client.PostAsync("/api/settings/providers/omdb/test", content: null);
        Assert.Equal(HttpStatusCode.OK, failedResponse.StatusCode);
        var failedDiagnostic = await failedResponse.Content.ReadFromJsonAsync<OmdbDiagnosticResponse>();
        Assert.NotNull(failedDiagnostic);
        Assert.False(failedDiagnostic.IsSuccessful);
        Assert.Equal(OmdbRequestPurpose.InteractiveDiagnostic, omdbClient.LastRequestPurpose);

        host.Db.ChangeTracker.Clear();
        var usageAfterFailure = await host.Db.OmdbDailyUsage.SingleAsync(row => row.UtcDate == utcDate);
        Assert.Equal(12, usageAfterFailure.TotalRequests);
        Assert.True(usageAfterFailure.DailyRequestLimitReached);
        Assert.True(usageAfterFailure.ProviderQuotaExceeded);
        Assert.Equal("quota_exceeded", usageAfterFailure.LastErrorCode);

        omdbClient.Result = new MetadataLookupResult(MetadataLookupStatus.ConfirmedNotFound, HttpAttempts: 1);
        using var successfulResponse = await host.Client.PostAsync("/api/settings/providers/omdb/test", content: null);
        Assert.Equal(HttpStatusCode.OK, successfulResponse.StatusCode);
        var successfulDiagnostic = await successfulResponse.Content.ReadFromJsonAsync<OmdbDiagnosticResponse>();
        Assert.NotNull(successfulDiagnostic);
        Assert.True(successfulDiagnostic.IsSuccessful);

        host.Db.ChangeTracker.Clear();
        var resetUsage = await host.Db.OmdbDailyUsage.SingleAsync(row => row.UtcDate == utcDate);
        Assert.Equal(0, resetUsage.TotalRequests);
        Assert.Equal(0, resetUsage.TorrentRequests);
        Assert.Equal(0, resetUsage.OscarRequests);
        Assert.False(resetUsage.DailyRequestLimitReached);
        Assert.False(resetUsage.ProviderQuotaExceeded);
        Assert.Null(resetUsage.LastErrorCode);
    }

    private sealed class StubOmdbClientFactory(StubOmdbClient client) : IOmdbClientFactory
    {
        public IOmdbClient Create(string apiKey, int dailyRequestLimit) => client;
    }

    private sealed class StubOmdbClient : IOmdbClient
    {
        public MetadataLookupResult Result { get; set; } = new(MetadataLookupStatus.ProviderFailure);
        public OmdbRequestPurpose? LastRequestPurpose { get; private set; }

        public Task<MetadataLookupResult> LookupAsync(
            string title,
            int? year,
            string sourceType,
            CancellationToken cancellationToken = default,
            OmdbRequestPurpose requestPurpose = OmdbRequestPurpose.RssIngestion,
            string? imdbId = null)
        {
            LastRequestPurpose = requestPurpose;
            return Task.FromResult(Result);
        }
    }
}