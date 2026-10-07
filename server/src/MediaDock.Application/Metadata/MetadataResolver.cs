using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MediaDock.Application.Metadata;

public sealed class MetadataResolver
{
    private static readonly TimeSpan FoundTtl = TimeSpan.FromDays(30);
    private static readonly TimeSpan NotFoundTtl = TimeSpan.FromDays(2);
    private const int MaximumSearchPages = 5;
    private const decimal MinimumCandidateMargin = 5m;

    private readonly IOmdbClient _client;
    private readonly IMetadataCacheStore _cache;

    public MetadataResolver(IOmdbClient client, IMetadataCacheStore cache)
    {
        _client = client;
        _cache = cache;
    }

    public async Task<MetadataResolution> ResolveAsync(
        string title,
        int? year,
        string sourceType,
        DateTimeOffset now,
        CancellationToken cancellationToken = default,
        OmdbRequestPurpose requestPurpose = OmdbRequestPurpose.RssIngestion,
        string? imdbId = null)
    {
        var normalizedTitle = NormalizeTitle(title);
        var normalizedSourceType = sourceType.Trim().ToLowerInvariant();
        var normalizedImdbId = ImdbIdNormalizer.Normalize(imdbId);
        if (normalizedTitle.Length == 0 || normalizedSourceType is not ("movie" or "series"))
        {
            return new MetadataResolution(
                MetadataLookupStatus.InvalidRequest,
                null,
                false,
                0,
                "invalid_lookup");
        }

        var yearSemantics = normalizedSourceType == "series" ? "series_title" : "movie_release_year";
        var lookupYear = normalizedSourceType == "series" ? null : year;
        var cacheKey = CreateCacheKey(normalizedTitle, lookupYear, normalizedSourceType, yearSemantics, normalizedImdbId);
        var cached = await _cache.GetAsync(cacheKey, cancellationToken);
        if (cached is not null && cached.ExpiresAt > now)
        {
            return new MetadataResolution(cached.Status, cached.Metadata, true, 0);
        }

        var result = await _client.LookupAsync(
            title.Trim(),
            lookupYear,
            normalizedSourceType,
            cancellationToken,
            requestPurpose,
            normalizedImdbId);
        if (result.Status == MetadataLookupStatus.Found
            && result.Metadata is not null
            && normalizedImdbId is not null
            && !ImdbIdNormalizer.IsCompatible(normalizedImdbId, result.Metadata.ImdbId))
        {
            result = result with
            {
                Status = MetadataLookupStatus.ProviderFailure,
                Metadata = null,
                ErrorCode = "imdb_id_mismatch"
            };
        }

        if (result.Status == MetadataLookupStatus.Found && result.Metadata is not null)
        {
            await StoreAsync(
                cacheKey,
                normalizedTitle,
                lookupYear,
                yearSemantics,
                normalizedSourceType,
                MetadataLookupStatus.Found,
                result.Metadata,
                now,
                FoundTtl,
                cancellationToken,
                normalizedImdbId);
        }
        else if (result.Status == MetadataLookupStatus.ConfirmedNotFound)
        {
            await StoreAsync(
                cacheKey,
                normalizedTitle,
                lookupYear,
                yearSemantics,
                normalizedSourceType,
                MetadataLookupStatus.ConfirmedNotFound,
                null,
                now,
                NotFoundTtl,
                cancellationToken,
                normalizedImdbId);
        }
        else if (result.Status == MetadataLookupStatus.Found)
        {
            result = result with
            {
                Status = MetadataLookupStatus.ProviderFailure,
                ErrorCode = "invalid_metadata"
            };
        }

        return new MetadataResolution(
            result.Status,
            result.Metadata,
            false,
            result.HttpAttempts,
            result.ErrorCode);
    }

    public async Task<MetadataResolution> ResolveByTitleAsync(
        string title,
        string sourceType,
        DateTimeOffset now,
        CancellationToken cancellationToken = default,
        OmdbRequestPurpose requestPurpose = OmdbRequestPurpose.RssIngestion)
    {
        var normalizedTitle = NormalizeTitle(title);
        var normalizedSourceType = sourceType.Trim().ToLowerInvariant();
        if (normalizedTitle.Length == 0 || normalizedSourceType is not ("movie" or "series"))
            return new(MetadataLookupStatus.InvalidRequest, null, false, 0, "invalid_lookup");

        var cached = await _cache.GetByTitleAsync(normalizedTitle, normalizedSourceType, cancellationToken);
        if (cached is not null && cached.ExpiresAt > now)
            return new(cached.Status, cached.Metadata, true, 0);

        var result = await _client.LookupAsync(title.Trim(), null, normalizedSourceType, cancellationToken, requestPurpose);
        if (result.Status is MetadataLookupStatus.Found or MetadataLookupStatus.ConfirmedNotFound)
        {
            await StoreAsync(
                CreateCacheKey(normalizedTitle, null, normalizedSourceType, "title", null),
                normalizedTitle, null, "title", normalizedSourceType, result.Status, result.Metadata,
                now, result.Status == MetadataLookupStatus.Found ? FoundTtl : NotFoundTtl, cancellationToken);
        }
        return new(result.Status, result.Metadata, false, result.HttpAttempts, result.ErrorCode);
    }

    public async Task<MetadataResolution> ResolveGoldenGlobeAsync(
        string title,
        int ceremonyYear,
        string sourceType,
        DateTimeOffset now,
        CancellationToken cancellationToken = default,
        OmdbRequestPurpose requestPurpose = OmdbRequestPurpose.GoldenGlobeEnrichment)
    {
        var normalizedSourceType = sourceType.Trim().ToLowerInvariant();
        if (normalizedSourceType is not ("movie" or "series"))
        {
            return new(MetadataLookupStatus.InvalidRequest, null, false, 0, "invalid_lookup");
        }

        var exact = await ResolveByTitleAsync(title, normalizedSourceType, now, cancellationToken, requestPurpose);
        if (exact.Status == MetadataLookupStatus.Found
            && exact.Metadata is { } exactMetadata
            && IsPlausibleGoldenGlobeMatch(title, ceremonyYear, normalizedSourceType, exactMetadata))
        {
            return exact;
        }

        if (exact.Status is not (MetadataLookupStatus.Found or MetadataLookupStatus.ConfirmedNotFound))
        {
            return exact;
        }

        var exactResultWasImplausible = exact.Status == MetadataLookupStatus.Found;
        var candidates = new Dictionary<string, MetadataSearchCandidate>(StringComparer.Ordinal);
        var attempts = exact.HttpAttempts;
        var searchReturnedCandidates = false;
        var incompleteSearch = false;

        foreach (var variant in CreateSearchVariants(title))
        {
            var firstPage = await _client.SearchAsync(
                variant,
                normalizedSourceType,
                1,
                cancellationToken,
                requestPurpose);
            attempts += firstPage.HttpAttempts;

            if (firstPage.Status is MetadataLookupStatus.RequestBudgetExhausted or MetadataLookupStatus.QuotaExceeded)
            {
                return new(firstPage.Status, null, exact.CacheHit, attempts, firstPage.ErrorCode);
            }

            if (firstPage.Status == MetadataLookupStatus.ConfirmedNotFound)
            {
                continue;
            }

            if (firstPage.Status != MetadataLookupStatus.Found)
            {
                return new(firstPage.Status, null, exact.CacheHit, attempts, firstPage.ErrorCode);
            }

            AddCandidates(firstPage.Candidates, candidates, ref searchReturnedCandidates);
            var pageCount = Math.Max(1, (firstPage.TotalResults + 9) / 10);
            var pagesToRead = Math.Min(pageCount, MaximumSearchPages);
            incompleteSearch |= pageCount > MaximumSearchPages;

            for (var page = 2; page <= pagesToRead; page++)
            {
                var nextPage = await _client.SearchAsync(
                    variant,
                    normalizedSourceType,
                    page,
                    cancellationToken,
                    requestPurpose);
                attempts += nextPage.HttpAttempts;

                if (nextPage.Status is MetadataLookupStatus.RequestBudgetExhausted or MetadataLookupStatus.QuotaExceeded)
                {
                    return new(nextPage.Status, null, exact.CacheHit, attempts, nextPage.ErrorCode);
                }

                if (nextPage.Status == MetadataLookupStatus.ConfirmedNotFound)
                {
                    break;
                }

                if (nextPage.Status != MetadataLookupStatus.Found)
                {
                    return new(nextPage.Status, null, exact.CacheHit, attempts, nextPage.ErrorCode);
                }

                AddCandidates(nextPage.Candidates, candidates, ref searchReturnedCandidates);
            }
        }

        if (candidates.Count == 0)
        {
            return exactResultWasImplausible || searchReturnedCandidates
                ? new(MetadataLookupStatus.ProviderFailure, null, exact.CacheHit, attempts, "no_confident_match")
                : new(MetadataLookupStatus.ConfirmedNotFound, null, exact.CacheHit, attempts, "not_found");
        }

        var ranked = candidates.Values
            .Select(candidate => ScoreCandidate(title, ceremonyYear, normalizedSourceType, candidate))
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .OrderByDescending(candidate => candidate.Score)
            .ToArray();

        if (incompleteSearch || ranked.Length == 0)
        {
            return new(MetadataLookupStatus.ProviderFailure, null, exact.CacheHit, attempts, "no_confident_match");
        }

        var topScore = ranked[0].Score;
        var contenders = ranked
            .Where(candidate => topScore - candidate.Score < MinimumCandidateMargin)
            .ToArray();
        var matchingCandidates = new List<MetadataResolution>();
        var cacheHit = exact.CacheHit;
        var candidateMismatch = false;

        foreach (var contender in contenders)
        {
            var candidate = contender.Candidate;
            var details = await ResolveAsync(
                title,
                ceremonyYear,
                normalizedSourceType,
                now,
                cancellationToken,
                requestPurpose,
                candidate.ImdbId);
            attempts += details.HttpAttempts;
            cacheHit |= details.CacheHit;

            if (details.Status is MetadataLookupStatus.RequestBudgetExhausted or MetadataLookupStatus.QuotaExceeded)
            {
                return details with { CacheHit = cacheHit, HttpAttempts = attempts };
            }

            if (details.Status == MetadataLookupStatus.ConfirmedNotFound
                || details.ErrorCode == "imdb_id_mismatch")
            {
                candidateMismatch = true;
                continue;
            }

            if (details.Status != MetadataLookupStatus.Found || details.Metadata is not { } metadata)
            {
                return details with { CacheHit = cacheHit, HttpAttempts = attempts };
            }

            var returnedId = ImdbIdNormalizer.Normalize(metadata.ImdbId);
            if (!string.Equals(returnedId, candidate.ImdbId, StringComparison.Ordinal)
                || !IsPlausibleGoldenGlobeMatch(title, ceremonyYear, normalizedSourceType, metadata))
            {
                candidateMismatch = true;
                continue;
            }

            if (HasGoldenGlobeAward(metadata))
            {
                matchingCandidates.Add(details);
            }
        }

        if (matchingCandidates.Count > 1)
        {
            return new(MetadataLookupStatus.ProviderFailure, null, cacheHit, attempts, "ambiguous_match");
        }

        if (matchingCandidates.Count == 0)
        {
            var errorCode = contenders.Length == 1 && candidateMismatch
                ? "candidate_mismatch"
                : "no_golden_globe_match";
            return new(MetadataLookupStatus.ProviderFailure, null, cacheHit, attempts, errorCode);
        }

        return matchingCandidates[0] with { CacheHit = cacheHit, HttpAttempts = attempts };
    }

    private static void AddCandidates(
        IReadOnlyList<MetadataSearchCandidate> found,
        IDictionary<string, MetadataSearchCandidate> candidates,
        ref bool searchReturnedCandidates)
    {
        foreach (var candidate in found)
        {
            searchReturnedCandidates = true;
            var imdbId = ImdbIdNormalizer.Normalize(candidate.ImdbId);
            if (ImdbIdNormalizer.IsValid(imdbId))
            {
                candidates.TryAdd(imdbId!, candidate with { ImdbId = imdbId });
            }
        }
    }

    private static IReadOnlyList<string> CreateSearchVariants(string title)
    {
        var variants = new List<string>();

        void Add(string value)
        {
            var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (normalized.Length > 0 && !variants.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                variants.Add(normalized);
            }
        }

        Add(title);
        Add(title.Replace("&", " and ", StringComparison.Ordinal));
        Add(Regex.Replace(title, @"\band\b", "&", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

        var punctuationNormalized = new string(title
            .Normalize(NormalizationForm.FormKC)
            .Select(character => char.IsLetterOrDigit(character) || char.IsWhiteSpace(character) ? character : ' ')
            .ToArray());
        Add(punctuationNormalized);
        return variants;
    }

    private static ScoredSearchCandidate? ScoreCandidate(
        string title,
        int ceremonyYear,
        string sourceType,
        MetadataSearchCandidate candidate)
    {
        if (!string.Equals(candidate.SourceType, sourceType, StringComparison.Ordinal))
        {
            return null;
        }

        decimal score;
        if (sourceType == "movie")
        {
            if (candidate.Year is not int candidateYear
                || candidateYear != ceremonyYear && candidateYear != ceremonyYear - 1)
            {
                return null;
            }

            score = TitleSimilarity(title, candidate.Title) * 80m
                + 10m
                + (candidateYear == ceremonyYear ? 10m : 5m);
        }
        else
        {
            score = TitleSimilarity(title, candidate.Title) * 90m + 10m;
        }

        return new(candidate, score);
    }

    private static bool IsPlausibleGoldenGlobeMatch(
        string title,
        int ceremonyYear,
        string sourceType,
        MetadataDetails metadata)
    {
        if (!ImdbIdNormalizer.IsValid(metadata.ImdbId)
            || !string.Equals(metadata.SourceType, sourceType, StringComparison.Ordinal)
            || TitleSimilarity(title, metadata.Title) < 0.9m)
        {
            return false;
        }

        return sourceType == "series"
            || metadata.Year is int year && (year == ceremonyYear || year == ceremonyYear - 1);
    }

    private static bool HasGoldenGlobeAward(MetadataDetails metadata) =>
        metadata.Awards?.Contains("Golden Globe", StringComparison.OrdinalIgnoreCase) == true;

    private static decimal TitleSimilarity(string first, string second)
    {
        var normalizedFirst = NormalizeComparisonTitle(first);
        var normalizedSecond = NormalizeComparisonTitle(second);
        if (normalizedFirst.Length == 0 || normalizedSecond.Length == 0)
        {
            return 0m;
        }

        if (string.Equals(normalizedFirst, normalizedSecond, StringComparison.Ordinal))
        {
            return 1m;
        }

        var maximumLength = Math.Max(normalizedFirst.Length, normalizedSecond.Length);
        if (maximumLength > 512)
        {
            return 0m;
        }

        var previous = new int[normalizedSecond.Length + 1];
        var current = new int[normalizedSecond.Length + 1];
        for (var column = 0; column <= normalizedSecond.Length; column++)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= normalizedFirst.Length; row++)
        {
            current[0] = row;
            for (var column = 1; column <= normalizedSecond.Length; column++)
            {
                var substitutionCost = normalizedFirst[row - 1] == normalizedSecond[column - 1] ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + substitutionCost);
            }

            (previous, current) = (current, previous);
        }

        return (maximumLength - previous[normalizedSecond.Length]) / (decimal)maximumLength;
    }

    private static string NormalizeComparisonTitle(string title)
    {
        var normalized = title.Normalize(NormalizationForm.FormKC).ToLowerInvariant().Replace("&", " and ", StringComparison.Ordinal);
        var characters = normalized
            .Select(character => char.IsLetterOrDigit(character) || char.IsWhiteSpace(character) ? character : ' ')
            .ToArray();
        return string.Join(' ', new string(characters).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed record ScoredSearchCandidate(MetadataSearchCandidate Candidate, decimal Score);

    private async Task StoreAsync(
        string cacheKey,
        string normalizedTitle,
        int? year,
        string yearSemantics,
        string sourceType,
        MetadataLookupStatus status,
        MetadataDetails? metadata,
        DateTimeOffset now,
        TimeSpan ttl,
        CancellationToken cancellationToken,
        string? lookupIdentity = null)
    {
        await _cache.StoreAsync(
            cacheKey,
            new MetadataCacheValue(
                normalizedTitle,
                year,
                yearSemantics,
                sourceType,
                status,
                metadata,
                now,
                now.Add(ttl),
                lookupIdentity),
            cancellationToken);
    }

    private static string CreateCacheKey(
        string title,
        int? year,
        string sourceType,
        string yearSemantics,
        string? imdbId)
    {
        var identity = imdbId is null
            ? $"v2:{title}:{year?.ToString() ?? ""}:{sourceType}:{yearSemantics}"
            : $"v3:{sourceType}:imdb:{imdbId}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }

    private static string NormalizeTitle(string? title) =>
        string.Join(' ', (title ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();
}
