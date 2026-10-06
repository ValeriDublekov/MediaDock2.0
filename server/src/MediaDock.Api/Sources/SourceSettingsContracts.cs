using System.ComponentModel.DataAnnotations;

namespace MediaDock.Api.Sources;

/// <summary>One fixed system profile and its configured RSS URLs.</summary>
public sealed record SourceProfileResponse(
    string Id,
    string Name,
    IReadOnlyList<SourceUrlResponse> Urls);

/// <summary>One configured RSS URL. Profile identity is supplied by the route.</summary>
public sealed record SourceUrlResponse(long Id, string Url);

/// <summary>Validated payload for adding or replacing a profile URL.</summary>
public sealed record SourceUrlRequest
{
    [Required, Url, MaxLength(2048)]
    public string Url { get; init; } = string.Empty;
}

/// <summary>Application matching settings returned by the API.</summary>
public sealed record SettingsResponse(
    IReadOnlyList<string> ExcludedGenres,
    IReadOnlyList<string> ExcludedCountries,
    decimal MinMovieRating,
    decimal MinSeriesRating,
    long MinImdbVotes,
    DateTimeOffset? UpdatedAt);

/// <summary>Validated payload for replacing application matching settings.</summary>
public sealed record UpdateSettingsRequest
{
    [Required, MaxLength(100)]
    public string[]? ExcludedGenres { get; init; } = [];

    [Required, MaxLength(100)]
    public string[]? ExcludedCountries { get; init; } = [];

    [Range(0, 10)]
    public decimal MinMovieRating { get; init; }

    [Range(0, 10)]
    public decimal MinSeriesRating { get; init; }

    [Range(0L, 1_000_000_000L)]
    public long MinImdbVotes { get; init; }
}

/// <summary>Provider settings returned without exposing saved credentials.</summary>
public sealed record ProviderSettingsResponse(
    bool OmdbApiKeyConfigured,
    int OmdbDailyRequestLimit,
    DateTimeOffset? UpdatedAt);

/// <summary>Result of the fixed-ID OMDb diagnostics request.</summary>
public sealed record OmdbDiagnosticResponse(
    string ImdbId,
    string Status,
    bool IsSuccessful,
    string Message,
    string? Title,
    int? Year,
    int HttpAttempts);

/// <summary>One UTC day's OMDb request usage and most recent provider error.</summary>
public sealed record OmdbDailyUsageResponse(
    DateOnly UtcDate,
    int TotalRequests,
    int RssRequests,
    int OscarRequests,
    bool DailyRequestLimitReached,
    bool ProviderQuotaExceeded,
    string? LastErrorCode);

/// <summary>Validated payload for replacing OMDb provider settings.</summary>
public sealed record UpdateProviderSettingsRequest
{
    [MaxLength(512)]
    public string? OmdbApiKey { get; init; }

    public bool ClearOmdbApiKey { get; init; }

    [Range(0, int.MaxValue)]
    public int OmdbDailyRequestLimit { get; init; }
}