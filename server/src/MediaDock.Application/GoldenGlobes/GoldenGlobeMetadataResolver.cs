using System.Text;
using System.Text.RegularExpressions;
using MediaDock.Application.Metadata;

namespace MediaDock.Application.GoldenGlobes;

public sealed class GoldenGlobeMetadataResolver
{
    private const int MaximumSearchPages = 5;
    private const decimal MinimumCandidateMargin = 5m;

    private readonly IOmdbClient _client;
    private readonly MetadataResolver _metadataResolver;

    public GoldenGlobeMetadataResolver(IOmdbClient client, MetadataResolver metadataResolver)
    {
        _client = client;
        _metadataResolver = metadataResolver;
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

        var request = new ResolutionRequest(
            title,
            ceremonyYear,
            normalizedSourceType,
            now,
            cancellationToken,
            requestPurpose);
        var exact = await ResolveExactLookupAsync(request);
        return exact.NeedsSearch
            ? await SearchAndResolveAsync(request, exact.Resolution)
            : exact.Resolution;
    }

    private async Task<ExactLookupOutcome> ResolveExactLookupAsync(ResolutionRequest request)
    {
        var exact = await _metadataResolver.ResolveByTitleAsync(
            request.Title,
            request.SourceType,
            request.Now,
            request.CancellationToken,
            request.RequestPurpose);
        if (exact.Status is not (MetadataLookupStatus.Found or MetadataLookupStatus.ConfirmedNotFound))
        {
            return new(exact, false);
        }

        if (exact.Status == MetadataLookupStatus.Found
            && exact.Metadata is { } metadata
            && IsPlausibleGoldenGlobeMatch(request.Title, request.CeremonyYear, request.SourceType, metadata))
        {
            return new(exact, false);
        }

        return new(exact, true);
    }

    private async Task<MetadataResolution> SearchAndResolveAsync(
        ResolutionRequest request,
        MetadataResolution exact)
    {
        var search = await SearchCandidatesAsync(request);
        var attempts = exact.HttpAttempts + search.HttpAttempts;
        if (search.Failure is { } failure)
        {
            return new(failure.Status, null, exact.CacheHit, attempts, failure.ErrorCode);
        }

        if (search.Candidates.Count == 0)
        {
            return exact.Status == MetadataLookupStatus.Found || search.SearchReturnedCandidates
                ? new(MetadataLookupStatus.ProviderFailure, null, exact.CacheHit, attempts, "no_confident_match")
                : new(MetadataLookupStatus.ConfirmedNotFound, null, exact.CacheHit, attempts, "not_found");
        }

        var contenders = RankTopCandidates(request, search.Candidates.Values);
        if (search.IncompleteSearch || contenders.Count == 0)
        {
            return new(MetadataLookupStatus.ProviderFailure, null, exact.CacheHit, attempts, "no_confident_match");
        }

        return await ResolveContendersAsync(request, contenders, exact.CacheHit, attempts);
    }

    private async Task<CandidateSearchOutcome> SearchCandidatesAsync(ResolutionRequest request)
    {
        var candidates = new Dictionary<string, MetadataSearchCandidate>(StringComparer.Ordinal);
        var attempts = 0;
        var searchReturnedCandidates = false;
        var incompleteSearch = false;

        foreach (var variant in CreateSearchVariants(request.Title))
        {
            var variantSearch = await SearchVariantAsync(variant, request);
            attempts += variantSearch.HttpAttempts;
            searchReturnedCandidates |= variantSearch.SearchReturnedCandidates;
            incompleteSearch |= variantSearch.IncompleteSearch;
            foreach (var candidate in variantSearch.Candidates)
            {
                candidates.TryAdd(candidate.Key, candidate.Value);
            }

            if (variantSearch.Failure is not null)
            {
                return new(candidates, attempts, searchReturnedCandidates, incompleteSearch, variantSearch.Failure);
            }
        }

        return new(candidates, attempts, searchReturnedCandidates, incompleteSearch);
    }

    private async Task<CandidateSearchOutcome> SearchVariantAsync(string variant, ResolutionRequest request)
    {
        var candidates = new Dictionary<string, MetadataSearchCandidate>(StringComparer.Ordinal);
        var attempts = 0;
        var searchReturnedCandidates = false;
        var firstPage = await _client.SearchAsync(
            variant,
            null,
            1,
            request.CancellationToken,
            request.RequestPurpose);
        attempts += firstPage.HttpAttempts;

        if (firstPage.Status == MetadataLookupStatus.ConfirmedNotFound)
        {
            return new(candidates, attempts, searchReturnedCandidates, false);
        }

        if (firstPage.Status != MetadataLookupStatus.Found)
        {
            return new(candidates, attempts, searchReturnedCandidates, false, firstPage);
        }

        AddCandidates(firstPage.Candidates, candidates, ref searchReturnedCandidates);
        var pageCount = Math.Max(1, (firstPage.TotalResults + 9) / 10);
        var pagesToRead = Math.Min(pageCount, MaximumSearchPages);
        var incompleteSearch = pageCount > MaximumSearchPages;

        for (var page = 2; page <= pagesToRead; page++)
        {
            var nextPage = await _client.SearchAsync(
                variant,
                null,
                page,
                request.CancellationToken,
                request.RequestPurpose);
            attempts += nextPage.HttpAttempts;

            if (nextPage.Status == MetadataLookupStatus.ConfirmedNotFound)
            {
                break;
            }

            if (nextPage.Status != MetadataLookupStatus.Found)
            {
                return new(candidates, attempts, searchReturnedCandidates, incompleteSearch, nextPage);
            }

            AddCandidates(nextPage.Candidates, candidates, ref searchReturnedCandidates);
        }

        return new(candidates, attempts, searchReturnedCandidates, incompleteSearch);
    }

    private static IReadOnlyList<ScoredSearchCandidate> RankTopCandidates(
        ResolutionRequest request,
        IEnumerable<MetadataSearchCandidate> candidates)
    {
        var ranked = candidates
            .Select(candidate => ScoreCandidate(request.Title, request.CeremonyYear, candidate))
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .OrderByDescending(candidate => candidate.Score)
            .ToArray();
        if (ranked.Length == 0)
        {
            return [];
        }

        var topScore = ranked[0].Score;
        return ranked
            .Where(candidate => topScore - candidate.Score < MinimumCandidateMargin)
            .ToArray();
    }

    private async Task<MetadataResolution> ResolveContendersAsync(
        ResolutionRequest request,
        IReadOnlyList<ScoredSearchCandidate> contenders,
        bool cacheHit,
        int attempts)
    {
        var matchingCandidates = new List<MetadataResolution>();
        var candidateMismatch = false;

        foreach (var contender in contenders)
        {
            var resolution = await ResolveContenderAsync(request, contender.Candidate);
            var details = resolution.Resolution;
            attempts += details.HttpAttempts;
            cacheHit |= details.CacheHit;

            switch (resolution.Disposition)
            {
                case CandidateDisposition.Stop:
                    return details with { CacheHit = cacheHit, HttpAttempts = attempts };
                case CandidateDisposition.Mismatch:
                    candidateMismatch = true;
                    break;
                case CandidateDisposition.Match:
                    matchingCandidates.Add(details);
                    break;
            }
        }

        if (matchingCandidates.Count > 1)
        {
            return new(MetadataLookupStatus.ProviderFailure, null, cacheHit, attempts, "ambiguous_match");
        }

        if (matchingCandidates.Count == 0)
        {
            var errorCode = contenders.Count == 1 && candidateMismatch
                ? "candidate_mismatch"
                : "no_golden_globe_match";
            return new(MetadataLookupStatus.ProviderFailure, null, cacheHit, attempts, errorCode);
        }

        return matchingCandidates[0] with { CacheHit = cacheHit, HttpAttempts = attempts };
    }

    private async Task<CandidateResolution> ResolveContenderAsync(
        ResolutionRequest request,
        MetadataSearchCandidate candidate)
    {
        var candidateType = candidate.SourceType.Trim().ToLowerInvariant();
        var details = await _metadataResolver.ResolveAsync(
            request.Title,
            candidateType == "series" ? null : request.CeremonyYear,
            candidateType,
            request.Now,
            request.CancellationToken,
            request.RequestPurpose,
            candidate.ImdbId);

        return new(details, ClassifyCandidate(details, request, candidate, candidateType));
    }

    private static CandidateDisposition ClassifyCandidate(
        MetadataResolution details,
        ResolutionRequest request,
        MetadataSearchCandidate candidate,
        string candidateType)
    {
        if (details.Status is MetadataLookupStatus.RequestBudgetExhausted or MetadataLookupStatus.QuotaExceeded)
        {
            return CandidateDisposition.Stop;
        }

        if (details.Status == MetadataLookupStatus.ConfirmedNotFound
            || details.ErrorCode == "imdb_id_mismatch")
        {
            return CandidateDisposition.Mismatch;
        }

        if (details.Status != MetadataLookupStatus.Found || details.Metadata is not { } metadata)
        {
            return CandidateDisposition.Stop;
        }

        var returnedId = ImdbIdNormalizer.Normalize(metadata.ImdbId);
        if (!string.Equals(returnedId, candidate.ImdbId, StringComparison.Ordinal)
            || !IsPlausibleGoldenGlobeMatch(
                request.Title,
                request.CeremonyYear,
                candidateType,
                metadata))
        {
            return CandidateDisposition.Mismatch;
        }

        return HasGoldenGlobeAward(metadata)
            ? CandidateDisposition.Match
            : CandidateDisposition.NoAward;
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
        MetadataSearchCandidate candidate)
    {
        var sourceType = candidate.SourceType.Trim().ToLowerInvariant();
        if (sourceType is not ("movie" or "series"))
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

    private sealed record ResolutionRequest(
        string Title,
        int CeremonyYear,
        string SourceType,
        DateTimeOffset Now,
        CancellationToken CancellationToken,
        OmdbRequestPurpose RequestPurpose);

    private sealed record ExactLookupOutcome(MetadataResolution Resolution, bool NeedsSearch);

    private sealed record CandidateSearchOutcome(
        Dictionary<string, MetadataSearchCandidate> Candidates,
        int HttpAttempts,
        bool SearchReturnedCandidates,
        bool IncompleteSearch,
        MetadataSearchResult? Failure = null);

    private sealed record CandidateResolution(MetadataResolution Resolution, CandidateDisposition Disposition);

    private enum CandidateDisposition
    {
        Match,
        Mismatch,
        NoAward,
        Stop
    }
}