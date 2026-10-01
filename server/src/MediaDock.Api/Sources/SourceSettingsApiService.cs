using MediaDock.Api.Middleware;
using MediaDock.Application.Ingestion;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Persistence.Entities;
using MediaDock.Infrastructure.Rss;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediaDock.Api.Sources;

internal interface ISourceSettingsApiService
{
    Task<IReadOnlyList<SourceProfileResponse>> GetSourceProfilesAsync(CancellationToken cancellationToken);

    Task<SourceUrlResponse> AddSourceUrlAsync(
        string profileId,
        SourceUrlRequest request,
        CancellationToken cancellationToken);

    Task<SourceUrlResponse> ReplaceSourceUrlAsync(
        string profileId,
        long id,
        SourceUrlRequest request,
        CancellationToken cancellationToken);

    Task RemoveSourceUrlAsync(string profileId, long id, CancellationToken cancellationToken);

    Task<SettingsResponse> GetSettingsAsync(CancellationToken cancellationToken);

    Task<SettingsResponse> UpdateSettingsAsync(
        UpdateSettingsRequest request,
        CancellationToken cancellationToken);

    Task<ProviderSettingsResponse> GetProviderSettingsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<OmdbDailyUsageResponse>> GetOmdbDailyUsageAsync(CancellationToken cancellationToken);

    Task<ProviderSettingsResponse> UpdateProviderSettingsAsync(
        UpdateProviderSettingsRequest request,
        CancellationToken cancellationToken);
}

internal sealed class SourceSettingsApiService(MediaDockDbContext dbContext) : ISourceSettingsApiService
{
    public async Task<IReadOnlyList<SourceProfileResponse>> GetSourceProfilesAsync(CancellationToken cancellationToken)
    {
        var sources = await dbContext.Sources
            .AsNoTracking()
            .Where(source => source.IsEnabled)
            .OrderBy(source => source.Id)
            .Select(source => new SourceProfileUrl(source.Id, source.FeedType, source.Url))
            .ToListAsync(cancellationToken);

        return RssFeedTypes.Profiles
            .Select(profile => new SourceProfileResponse(
                profile.Id,
                profile.Name,
                sources.Where(source => source.FeedType == profile.Id)
                    .Select(source => new SourceUrlResponse(source.Id, source.Url))
                    .ToArray()))
            .ToArray();
    }

    public async Task<SourceUrlResponse> AddSourceUrlAsync(
        string profileId,
        SourceUrlRequest request,
        CancellationToken cancellationToken)
    {
        var profile = GetProfile(profileId);
        var url = ValidateFeedUrl(request.Url);
        var existing = await dbContext.Sources.FirstOrDefaultAsync(source => source.Url == url, cancellationToken);
        if (existing is not null)
        {
            if (existing.IsEnabled)
            {
                if (existing.FeedType != profile.Id)
                {
                    throw new ApiConflictException("This URL is already assigned to another profile.");
                }

                throw new ApiConflictException("This URL is already configured in the profile.");
            }

            // A removed URL is retained for occurrence/parse-log history. Reusing it
            // from another profile is the supported way to repair a misclassified feed.
            existing.FeedType = profile.Id;
            existing.Name = profile.Name;
            existing.IsEnabled = true;
            await dbContext.SaveChangesAsync(cancellationToken);
            return new SourceUrlResponse(existing.Id, existing.Url);
        }

        var source = new Source
        {
            StableKey = $"{profile.Id}-{Guid.NewGuid():N}",
            Name = profile.Name,
            FeedType = profile.Id,
            Url = url,
            IsEnabled = true
        };
        dbContext.Sources.Add(source);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new SourceUrlResponse(source.Id, source.Url);
    }

    public async Task<SourceUrlResponse> ReplaceSourceUrlAsync(
        string profileId,
        long id,
        SourceUrlRequest request,
        CancellationToken cancellationToken)
    {
        var profile = GetProfile(profileId);
        var url = ValidateFeedUrl(request.Url);
        var source = await dbContext.Sources.FirstOrDefaultAsync(
            entity => entity.Id == id && entity.FeedType == profile.Id && entity.IsEnabled,
            cancellationToken)
            ?? throw new ApiNotFoundException("RSS URL not found in this profile.");
        if (await dbContext.Sources.AnyAsync(
                entity => entity.Id != id && entity.Url == url && entity.IsEnabled,
                cancellationToken))
        {
            throw new ApiConflictException("This URL is already configured.");
        }

        source.Url = url;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new SourceUrlResponse(source.Id, source.Url);
    }

    public async Task RemoveSourceUrlAsync(string profileId, long id, CancellationToken cancellationToken)
    {
        var profile = GetProfile(profileId);
        var source = await dbContext.Sources.FirstOrDefaultAsync(
            entity => entity.Id == id && entity.FeedType == profile.Id && entity.IsEnabled,
            cancellationToken)
            ?? throw new ApiNotFoundException("RSS URL not found in this profile.");

        source.IsEnabled = false;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<SettingsResponse> GetSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await dbContext.Settings
            .AsNoTracking()
            .Where(setting => setting.Id == 1)
            .Select(setting => new SettingsResponse(
                setting.ExcludedGenres,
                setting.ExcludedCountries,
                setting.MinMovieRating,
                setting.MinSeriesRating,
                setting.MinImdbVotes,
                setting.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);

        return settings ?? new SettingsResponse([], [], 0m, 0m, 0, null);
    }

    public async Task<SettingsResponse> UpdateSettingsAsync(
        UpdateSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var excludedGenres = NormalizeFilterValues(request.ExcludedGenres, nameof(request.ExcludedGenres), errors);
        var excludedCountries = NormalizeFilterValues(request.ExcludedCountries, nameof(request.ExcludedCountries), errors);
        if (errors.Count > 0)
        {
            throw new ApiValidationException(errors);
        }

        var settings = await dbContext.Settings.SingleOrDefaultAsync(value => value.Id == 1, cancellationToken)
            ?? await GetOrCreateSettingsAsync(cancellationToken);

        settings.ExcludedGenres = excludedGenres;
        settings.ExcludedCountries = excludedCountries;
        settings.MinMovieRating = request.MinMovieRating;
        settings.MinSeriesRating = request.MinSeriesRating;
        settings.MinImdbVotes = request.MinImdbVotes;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new SettingsResponse(
            settings.ExcludedGenres,
            settings.ExcludedCountries,
            settings.MinMovieRating,
            settings.MinSeriesRating,
            settings.MinImdbVotes,
            settings.UpdatedAt);
    }

    public async Task<ProviderSettingsResponse> GetProviderSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await dbContext.Settings
            .AsNoTracking()
            .Where(setting => setting.Id == 1)
            .Select(setting => new ProviderSettingsResponse(
                !string.IsNullOrWhiteSpace(setting.OmdbApiKey),
                setting.OmdbDailyRequestLimit,
                setting.OscarEnrichmentMaxFilmsPerRun,
                setting.OscarEnrichmentMaxRequestsPerDay,
                setting.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);

        return settings ?? new ProviderSettingsResponse(false, 0, 0, 0, null);
    }

    public async Task<IReadOnlyList<OmdbDailyUsageResponse>> GetOmdbDailyUsageAsync(
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var firstDay = today.AddDays(-29);
        return await dbContext.OmdbDailyUsage
            .AsNoTracking()
            .Where(usage => usage.UtcDate >= firstDay && usage.UtcDate <= today)
            .OrderByDescending(usage => usage.UtcDate)
            .Select(usage => new OmdbDailyUsageResponse(
                usage.UtcDate,
                usage.TotalRequests,
                usage.TotalRequests - usage.OscarRequests,
                usage.OscarRequests,
                usage.DailyRequestLimitReached,
                usage.ProviderQuotaExceeded,
                usage.LastErrorCode))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<ProviderSettingsResponse> UpdateProviderSettingsAsync(
        UpdateProviderSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var settings = await dbContext.Settings.SingleOrDefaultAsync(value => value.Id == 1, cancellationToken);
        var errors = new Dictionary<string, string[]>();
        if (request.ClearOmdbApiKey && !string.IsNullOrWhiteSpace(request.OmdbApiKey))
        {
            errors[nameof(request.OmdbApiKey)] = ["Provide a key or clear the saved key, not both."];
        }

        var hasOmdbApiKey = !request.ClearOmdbApiKey
            && (!string.IsNullOrWhiteSpace(request.OmdbApiKey)
                || !string.IsNullOrWhiteSpace(settings?.OmdbApiKey));
        if (hasOmdbApiKey && request.OmdbDailyRequestLimit <= 0)
        {
            errors[nameof(request.OmdbDailyRequestLimit)] =
                ["A positive shared daily request limit is required when an API key is configured."];
        }

        if (request.OscarEnrichmentMaxFilmsPerRun > 0 && request.OscarEnrichmentMaxRequestsPerDay == 0)
        {
            errors[nameof(request.OscarEnrichmentMaxRequestsPerDay)] =
                ["A positive Oscar request limit is required when enrichment is enabled."];
        }

        if (errors.Count > 0)
        {
            throw new ApiValidationException(errors);
        }

        if (settings is null)
        {
            settings = await GetOrCreateSettingsAsync(cancellationToken);
        }

        if (request.ClearOmdbApiKey)
        {
            settings.OmdbApiKey = null;
        }
        else if (!string.IsNullOrWhiteSpace(request.OmdbApiKey))
        {
            settings.OmdbApiKey = request.OmdbApiKey.Trim();
        }

        settings.OmdbDailyRequestLimit = request.OmdbDailyRequestLimit;
        settings.OscarEnrichmentMaxFilmsPerRun = request.OscarEnrichmentMaxFilmsPerRun;
        settings.OscarEnrichmentMaxRequestsPerDay = request.OscarEnrichmentMaxRequestsPerDay;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToProviderSettingsResponse(settings);
    }

    private static RssFeedProfile GetProfile(string profileId) =>
        RssFeedTypes.Profiles.FirstOrDefault(profile => profile.Id == profileId)
        ?? throw new ApiValidationException(new Dictionary<string, string[]>
        {
            ["profileId"] = ["Unknown RSS profile."]
        });

    private static string ValidateFeedUrl(string url)
    {
        var trimmedUrl = url.Trim();
        if (trimmedUrl.Length <= 2048
            && !trimmedUrl.Any(char.IsWhiteSpace)
            && Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var uri)
            && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.Equals(
                uri.IdnHost.TrimEnd('.'),
                RssFeedTransport.AllowedFeedHost,
                StringComparison.OrdinalIgnoreCase))
        {
            return trimmedUrl;
        }

        throw new ApiValidationException(new Dictionary<string, string[]>
        {
            [nameof(SourceUrlRequest.Url)] = ["Feed URLs must use HTTPS on the configured feed host."]
        });
    }

    private static string[] NormalizeFilterValues(
        string[]? values,
        string propertyName,
        IDictionary<string, string[]> errors)
    {
        if (values is null || values.Any(value =>
                string.IsNullOrWhiteSpace(value) || value.Trim().Length > 100))
        {
            errors[propertyName] = ["Values must be non-empty and at most 100 characters."];
            return [];
        }

        return values
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private sealed record SourceProfileUrl(long Id, string FeedType, string Url);

    private static ProviderSettingsResponse ToProviderSettingsResponse(AppSetting settings) => new(
        !string.IsNullOrWhiteSpace(settings.OmdbApiKey),
        settings.OmdbDailyRequestLimit,
        settings.OscarEnrichmentMaxFilmsPerRun,
        settings.OscarEnrichmentMaxRequestsPerDay,
        settings.UpdatedAt);

    private async Task<AppSetting> GetOrCreateSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await dbContext.Settings.SingleOrDefaultAsync(value => value.Id == 1, cancellationToken);
        if (settings is not null)
        {
            return settings;
        }

        settings = new AppSetting { Id = 1, UpdatedAt = DateTimeOffset.UtcNow };
        dbContext.Settings.Add(settings);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return settings;
        }
        catch (DbUpdateException exception) when (IsSettingsSingletonViolation(exception))
        {
            dbContext.ChangeTracker.Clear();
            return await dbContext.Settings.SingleAsync(value => value.Id == 1, cancellationToken);
        }
    }

    private static bool IsSettingsSingletonViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "pk_settings"
        };
}
