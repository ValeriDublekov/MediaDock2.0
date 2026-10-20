using System.Net;
using System.Net.Http.Json;
using MediaDock.Api.Operations;
using MediaDock.Api.Sources;
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
        Assert.Null(defaultSettings.UpdatedAt);

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

        var currentUtcDate = DateOnly.FromDateTime(DateTime.UtcNow);
        db.OmdbDailyUsage.AddRange(
            new OmdbDailyUsage
            {
                UtcDate = currentUtcDate,
                TotalRequests = 12,
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
        Assert.Equal(9, currentUsage.RssRequests);
        Assert.Equal(3, currentUsage.OscarRequests);
        Assert.True(currentUsage.DailyRequestLimitReached);
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
}