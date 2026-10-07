using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using MediaDock.Application.Matching;
using MediaDock.Application.Metadata;

namespace MediaDock.Infrastructure.Metadata;

public sealed class OmdbClient : IOmdbClient
{
    private const string Endpoint = "https://www.omdbapi.com/";
    private const int DefaultMaximumResponseBytes = 1024 * 1024;

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly TimeSpan _timeout;
    private readonly int _maximumResponseBytes;
    private readonly IOmdbRequestBudget? _requestBudget;
    private readonly int _dailyRequestLimit;
    private readonly TimeProvider _timeProvider;

    public OmdbClient(
        HttpClient httpClient,
        string apiKey,
        TimeSpan? timeout = null,
        int maximumResponseBytes = DefaultMaximumResponseBytes,
        IOmdbRequestBudget? requestBudget = null,
        int dailyRequestLimit = int.MaxValue,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("OMDb API key is required on the server.");
        }

        if ((timeout ?? TimeSpan.FromSeconds(8)) <= TimeSpan.Zero || maximumResponseBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        if (requestBudget is not null && dailyRequestLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dailyRequestLimit));
        }

        _httpClient = httpClient;
        _apiKey = apiKey;
        _timeout = timeout ?? TimeSpan.FromSeconds(8);
        _maximumResponseBytes = maximumResponseBytes;
        _requestBudget = requestBudget;
        _dailyRequestLimit = dailyRequestLimit;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<MetadataLookupResult> LookupAsync(
        string title,
        int? year,
        string sourceType,
        CancellationToken cancellationToken = default,
        OmdbRequestPurpose requestPurpose = OmdbRequestPurpose.RssIngestion,
        string? imdbId = null)
    {
        if (string.IsNullOrWhiteSpace(title) || sourceType is not ("movie" or "series"))
        {
            return new MetadataLookupResult(MetadataLookupStatus.InvalidRequest, ErrorCode: "invalid_lookup");
        }

        var normalizedImdbId = ImdbIdNormalizer.Normalize(imdbId);
        var first = await RequestAsync(title.Trim(), year, sourceType, requestPurpose, cancellationToken, normalizedImdbId);
        if (normalizedImdbId is not null
            || first.Status != MetadataLookupStatus.ConfirmedNotFound
            || year is null
            || sourceType == "series")
        {
            return first;
        }

        var fallback = await RequestAsync(title.Trim(), null, sourceType, requestPurpose, cancellationToken, null);
        return fallback with { HttpAttempts = first.HttpAttempts + fallback.HttpAttempts };
    }

    private async Task<MetadataLookupResult> RequestAsync(
        string title,
        int? year,
        string sourceType,
        OmdbRequestPurpose requestPurpose,
        CancellationToken cancellationToken,
        string? imdbId)
    {
        var query = new StringBuilder()
            .Append("apikey=").Append(Uri.EscapeDataString(_apiKey));
        if (imdbId is not null)
        {
            query.Append("&i=").Append(Uri.EscapeDataString(imdbId));
        }
        else
        {
            query.Append("&t=").Append(Uri.EscapeDataString(title))
                .Append("&type=").Append(Uri.EscapeDataString(sourceType));
            if (year is not null && sourceType != "series")
            {
                query.Append("&y=").Append(year.Value.ToString(CultureInfo.InvariantCulture));
            }
        }

        return await SendRequestAsync(
            query.ToString(),
            requestPurpose,
            cancellationToken,
            ParseResponse,
            result => result,
            result => result);
    }

    public Task<MetadataSearchResult> SearchAsync(
        string title,
        string? sourceType,
        int page,
        CancellationToken cancellationToken = default,
        OmdbRequestPurpose requestPurpose = OmdbRequestPurpose.RssIngestion)
    {
        if (string.IsNullOrWhiteSpace(title)
            || sourceType is not (null or "movie" or "series")
            || page is < 1 or > 100)
        {
            return Task.FromResult(new MetadataSearchResult(
                MetadataLookupStatus.InvalidRequest,
                [],
                0,
                ErrorCode: "invalid_search"));
        }

        var query = new StringBuilder()
            .Append("apikey=").Append(Uri.EscapeDataString(_apiKey))
            .Append("&s=").Append(Uri.EscapeDataString(title.Trim()));
        if (sourceType is not null)
        {
            query.Append("&type=").Append(Uri.EscapeDataString(sourceType));
        }
        query.Append("&page=").Append(page.ToString(CultureInfo.InvariantCulture));

        return SendRequestAsync(
            query.ToString(),
            requestPurpose,
            cancellationToken,
            ParseSearchResponse,
            result => new MetadataSearchResult(
                result.Status,
                [],
                0,
                result.HttpAttempts,
                result.ErrorCode),
            result => new MetadataLookupResult(
                result.Status,
                HttpAttempts: result.HttpAttempts,
                ErrorCode: result.ErrorCode));
    }

    private async Task<T> SendRequestAsync<T>(
        string query,
        OmdbRequestPurpose requestPurpose,
        CancellationToken cancellationToken,
        Func<byte[], T> parseResponse,
        Func<MetadataLookupResult, T> convertError,
        Func<T, MetadataLookupResult> asLookupResult)
    {
        var utcDate = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        if (_requestBudget is not null)
        {
            var reserved = await _requestBudget.TryReserveAsync(
                utcDate,
                requestPurpose,
                _dailyRequestLimit,
                cancellationToken);
            if (!reserved)
            {
                return convertError(new MetadataLookupResult(
                    MetadataLookupStatus.RequestBudgetExhausted,
                    ErrorCode: "daily_budget_exhausted"));
            }
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint + "?" + query);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_timeout);

        try
        {
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutSource.Token);
            if ((int)response.StatusCode is < 200 or >= 300)
            {
                var errorBody = await ReadBoundedAsync(response.Content, timeoutSource.Token);
                var providerMessage = ExtractProviderMessage(errorBody);
                var fallbackStatus = response.StatusCode switch
                {
                    HttpStatusCode.TooManyRequests => MetadataLookupStatus.QuotaExceeded,
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => MetadataLookupStatus.AuthenticationFailure,
                    _ => MetadataLookupStatus.ProviderFailure
                };
                var status = providerMessage is null ? fallbackStatus : ClassifyError(providerMessage);
                if (status == MetadataLookupStatus.ProviderFailure && fallbackStatus != MetadataLookupStatus.ProviderFailure)
                {
                    status = fallbackStatus;
                }

                var errorResult = new MetadataLookupResult(
                    status,
                    HttpAttempts: 1,
                    ErrorCode: status == MetadataLookupStatus.ProviderFailure ? "http_error" : ErrorCode(status),
                    ProviderMessage: SanitizeProviderMessage(providerMessage)
                        ?? $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}".Trim());
                var recordedError = await RecordProviderErrorAsync(utcDate, errorResult, cancellationToken);
                return convertError(recordedError);
            }

            var body = await ReadBoundedAsync(response.Content, timeoutSource.Token);
            var result = parseResponse(body);
            await RecordProviderErrorAsync(utcDate, asLookupResult(result), cancellationToken);
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return convertError(await RecordProviderErrorAsync(
                utcDate,
                new MetadataLookupResult(MetadataLookupStatus.TransportFailure, HttpAttempts: 1, ErrorCode: "timeout"),
                cancellationToken));
        }
        catch (HttpRequestException)
        {
            return convertError(await RecordProviderErrorAsync(
                utcDate,
                new MetadataLookupResult(MetadataLookupStatus.TransportFailure, HttpAttempts: 1, ErrorCode: "transport_error"),
                cancellationToken));
        }
        catch (IOException)
        {
            return convertError(await RecordProviderErrorAsync(
                utcDate,
                new MetadataLookupResult(MetadataLookupStatus.TransportFailure, HttpAttempts: 1, ErrorCode: "transport_error"),
                cancellationToken));
        }
        catch (JsonException)
        {
            return convertError(await RecordProviderErrorAsync(
                utcDate,
                new MetadataLookupResult(MetadataLookupStatus.ProviderFailure, HttpAttempts: 1, ErrorCode: "invalid_response"),
                cancellationToken));
        }
        catch (InvalidDataException)
        {
            return convertError(await RecordProviderErrorAsync(
                utcDate,
                new MetadataLookupResult(MetadataLookupStatus.ProviderFailure, HttpAttempts: 1, ErrorCode: "response_too_large"),
                cancellationToken));
        }
    }

    private async Task<MetadataLookupResult> RecordProviderErrorAsync(
        DateOnly utcDate,
        MetadataLookupResult result,
        CancellationToken cancellationToken)
    {
        if (_requestBudget is not null
            && result.Status is not (MetadataLookupStatus.Found
                or MetadataLookupStatus.ConfirmedNotFound
                or MetadataLookupStatus.InvalidRequest
                or MetadataLookupStatus.RequestBudgetExhausted))
        {
            await _requestBudget.RecordProviderErrorAsync(
                utcDate,
                result.ErrorCode ?? "provider_error",
                result.Status == MetadataLookupStatus.QuotaExceeded,
                cancellationToken);
        }

        return result;
    }

    private async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > 0 and var contentLength && contentLength > _maximumResponseBytes)
        {
            throw new InvalidDataException("OMDb response exceeded the configured size limit.");
        }

        await using var input = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var bytesRead = await input.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            if (output.Length + bytesRead > _maximumResponseBytes)
            {
                throw new InvalidDataException("OMDb response exceeded the configured size limit.");
            }

            output.Write(buffer, 0, bytesRead);
        }

        return output.ToArray();
    }

    private MetadataLookupResult ParseResponse(byte[] body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new MetadataLookupResult(MetadataLookupStatus.ProviderFailure, HttpAttempts: 1, ErrorCode: "invalid_response");
        }

        var response = GetString(root, "Response");
        if (string.Equals(response, "False", StringComparison.OrdinalIgnoreCase))
        {
            var error = GetString(root, "Error") ?? string.Empty;
            var status = ClassifyError(error);
            return new MetadataLookupResult(
                status,
                HttpAttempts: 1,
                ErrorCode: ErrorCode(status),
                ProviderMessage: SanitizeProviderMessage(error));
        }

        if (!string.Equals(response, "True", StringComparison.OrdinalIgnoreCase))
        {
            return new MetadataLookupResult(MetadataLookupStatus.ProviderFailure, HttpAttempts: 1, ErrorCode: "invalid_response");
        }

        var title = GetString(root, "Title");
        if (string.IsNullOrWhiteSpace(title))
        {
            return new MetadataLookupResult(MetadataLookupStatus.ProviderFailure, HttpAttempts: 1, ErrorCode: "invalid_metadata");
        }

        var genres = SplitList(GetString(root, "Genre"));
        var countries = SplitList(GetString(root, "Country"));
        var classification = MatchPolicy.ClassifyMedia(GetString(root, "Type"), genres);
        var sourceType = classification.SourceType switch
        {
            SourceType.Movie => "movie",
            SourceType.Series => "series",
            _ => "unknown"
        };
        var contentKind = classification.ContentKind.ToString().ToLowerInvariant();
        var rawYear = GetString(root, "Year");
        var broadcastRange = sourceType == "series" ? BroadcastRange.Parse(rawYear) : null;
        var year = broadcastRange?.StartYear ?? ParseYear(rawYear);

        return new MetadataLookupResult(
            MetadataLookupStatus.Found,
            new MetadataDetails(
                title.Trim(),
                year,
                GetOptionalString(root, "imdbID"),
                classification.MediaType,
                sourceType,
                contentKind,
                broadcastRange,
                ParseDecimal(GetString(root, "imdbRating")),
                ParseLong(GetString(root, "imdbVotes")),
                ParseDecimal(GetString(root, "Metascore")),
                genres,
                countries,
                GetOptionalString(root, "Director"),
                GetOptionalString(root, "Plot"),
                GetOptionalString(root, "Poster"),
                GetOptionalString(root, "Runtime"),
                GetOptionalString(root, "Awards"),
                GetOptionalString(root, "BoxOffice")),
            1);
    }

    private static MetadataSearchResult ParseSearchResponse(byte[] body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new(MetadataLookupStatus.ProviderFailure, [], 0, 1, "invalid_search_response");
        }

        if (string.Equals(GetString(root, "Response"), "False", StringComparison.OrdinalIgnoreCase))
        {
            var status = ClassifyError(GetString(root, "Error") ?? string.Empty);
            return new(status, [], 0, 1, ErrorCode(status));
        }

        if (!string.Equals(GetString(root, "Response"), "True", StringComparison.OrdinalIgnoreCase)
            || !root.TryGetProperty("Search", out var search)
            || search.ValueKind != JsonValueKind.Array)
        {
            return new(MetadataLookupStatus.ProviderFailure, [], 0, 1, "invalid_search_response");
        }

        var candidates = new List<MetadataSearchCandidate>();
        foreach (var item in search.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var title = GetString(item, "Title")?.Trim();
            var imdbId = ImdbIdNormalizer.Normalize(GetString(item, "imdbID"));
            var rawType = GetString(item, "Type")?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(imdbId))
            {
                continue;
            }

            candidates.Add(new MetadataSearchCandidate(
                title,
                ParseYear(GetString(item, "Year")),
                imdbId,
                rawType is "movie" or "series" ? rawType : "unknown"));
        }

        var totalResults = int.TryParse(GetString(root, "totalResults"), NumberStyles.None, CultureInfo.InvariantCulture, out var total)
            ? Math.Max(total, candidates.Count)
            : candidates.Count;
        return new(MetadataLookupStatus.Found, candidates, totalResults, 1);
    }

    private static MetadataLookupStatus ClassifyError(string error)
    {
        var normalized = error.ToLowerInvariant();
        if (normalized.Contains("limit reached", StringComparison.Ordinal)
            || normalized.Contains("request limit", StringComparison.Ordinal))
        {
            return MetadataLookupStatus.QuotaExceeded;
        }

        if (normalized.Contains("invalid api key", StringComparison.Ordinal)
            || normalized.Contains("authentication", StringComparison.Ordinal)
            || normalized.Contains("unauthorized", StringComparison.Ordinal))
        {
            return MetadataLookupStatus.AuthenticationFailure;
        }

        if (normalized.Contains("not found", StringComparison.Ordinal)
            || normalized.Contains("not exist", StringComparison.Ordinal)
            || normalized.Contains("incorrect imdb id", StringComparison.Ordinal))
        {
            return MetadataLookupStatus.ConfirmedNotFound;
        }

        if (normalized.Contains("invalid request", StringComparison.Ordinal)
            || normalized.Contains("invalid parameter", StringComparison.Ordinal))
        {
            return MetadataLookupStatus.InvalidRequest;
        }

        return MetadataLookupStatus.ProviderFailure;
    }

    private static string ErrorCode(MetadataLookupStatus status) => status switch
    {
        MetadataLookupStatus.QuotaExceeded => "quota_exceeded",
        MetadataLookupStatus.AuthenticationFailure => "authentication_failed",
        MetadataLookupStatus.ConfirmedNotFound => "not_found",
        MetadataLookupStatus.InvalidRequest => "invalid_request",
        _ => "provider_error"
    };

    private static string? ExtractProviderMessage(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var providerMessage = GetString(document.RootElement, "Error")?.Trim();
            if (!string.IsNullOrWhiteSpace(providerMessage))
            {
                return providerMessage;
            }
        }
        catch (JsonException)
        {
        }

        var responseText = Encoding.UTF8.GetString(body).Trim();
        return string.IsNullOrWhiteSpace(responseText) ? null : responseText;
    }

    private string? SanitizeProviderMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        return message.Trim()
            .Replace(_apiKey, "[redacted]", StringComparison.OrdinalIgnoreCase)
            .Replace(Uri.EscapeDataString(_apiKey), "[redacted]", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        var value = GetString(element, propertyName)?.Trim();
        return string.IsNullOrEmpty(value) || string.Equals(value, "N/A", StringComparison.OrdinalIgnoreCase)
            ? null
            : value;
    }

    private static string[] SplitList(string? value) =>
        string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "N/A", StringComparison.OrdinalIgnoreCase)
            ? []
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static int? ParseYear(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length >= 4
        && int.TryParse(value.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            ? year
            : null;

    private static decimal? ParseDecimal(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !string.Equals(value, "N/A", StringComparison.OrdinalIgnoreCase)
        && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;

    private static long? ParseLong(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !string.Equals(value, "N/A", StringComparison.OrdinalIgnoreCase)
        && long.TryParse(value.Replace(",", string.Empty, StringComparison.Ordinal), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
}