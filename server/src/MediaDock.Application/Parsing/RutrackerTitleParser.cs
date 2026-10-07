using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MediaDock.Application.Parsing;

public sealed record ParsedRutrackerTitle(
    string Title,
    string NormalizedTitle,
    int? Year,
    bool IsSeries,
    IReadOnlyList<string> ReasonCodes)
{
    public IReadOnlyList<TitleCandidate> Candidates { get; init; } = [];
    public string YearSource { get; init; } = "missing";
    public bool YearAmbiguous { get; init; }
}

public sealed record TitleCandidate(string Title, string Origin);

public static class RutrackerTitleParser
{
    private const int MinimumYear = 1888;
    private static int MaximumYear => DateTime.UtcNow.Year + 10;
    private const RegexOptions IgnoreCase = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex LeadingCategory = new(@"^\[.*?\]\s*", RegexOptions.CultureInvariant);
    private static readonly Regex SeriesMarker = new(
        @"\b(?:s\d{1,2}(?:\s*-\s*s?\d{1,2})?(?:\s*e\d{1,2}(?:\s*-\s*e?\d{1,2})?)?|seasons?\s*\d+(?:\s*-\s*\d+)?|сезоны?:?\s*\d+(?:\s*-\s*\d+)?|сери[яи]:?\s*\d+(?:\s*-\s*\d+)?(?:\s*из\s*\d+)?|episodes?\s*\d+(?:\s*-\s*\d+)?(?:\s*of\s*\d+)?|т/с|телесериал|мини-сериал)\b",
        IgnoreCase);
    private static readonly Regex BracketYear = new(
        @"\[\s*(?<year>\d{4})(?:\s*[-–—]\s*\d{4})?(?!\s*[pPiI])\b",
        RegexOptions.CultureInvariant);
    private static readonly Regex ParenthesizedYear = new(
        @"\((?<year>\d{4})(?:\s*[-–—]\s*\d{4})?\)",
        RegexOptions.CultureInvariant);
    private static readonly Regex StandaloneYear = new(
        @"\b(?<year>\d{4})\b(?!\s*[pPiI])",
        RegexOptions.CultureInvariant);
    private static readonly Regex OutOfRangeYear = new(
        @"(?:\[|\()\s*(?<year>\d{4})(?!\s*[pPiI])",
        RegexOptions.CultureInvariant);
    private static readonly Regex BracketSection = new(@"\[[^\]]*\]", RegexOptions.CultureInvariant);
    private static readonly Regex TechnicalBracket = new(
        @"\b(?:\d{3,4}p|bdrip|brrip|webrip|web[- .]?dl|dvdrip|hdrip|uhd|x26[45]|h\.?26[45])\b",
        IgnoreCase);
    private static readonly Regex TrailingParenthesis = new(@"\s*(\([^)]*\))$", RegexOptions.CultureInvariant);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant);
    private static readonly Regex TrailingSeriesMarker = new(
        @"[\s,.-]+(?:s\d{1,2}(?:e\d{1,2})?|seasons?\s*\d+|episodes?\b|сери[яи]\b|сезон\b).*$",
        IgnoreCase);
    private static readonly Regex TrailingCompleteSeasonMarker = new(
        @"[\s,.-]+(?:(?:s\d{1,2}(?:\s*-\s*s?\d{1,2})?|seasons?\s*\d+(?:\s*-\s*\d+)?|сезоны?:?\s*\d+(?:\s*-\s*\d+)?)\b|episodes?\s*\d+(?:\s*-\s*\d+)?(?:\s+of\s+\d+)?|сери[яи]:?\s*\d+(?:\s*-\s*\d+)?(?:\s+из\s+\d+)?)(?:\s+.*)?$",
        IgnoreCase);
    private static readonly Regex TrailingOngoingEpisodeMarker = new(
        @"[\s,.-]+(?:(?:s\d{1,2}\s*e\d{1,2}(?:\s*-\s*e?\d{1,2})?)\b|episodes?\s*\d+(?:\s*-\s*\d+)?(?:\s+of\s+\d+)?|сери[яи]:?\s*\d+(?:\s*-\s*\d+)?(?:\s+из\s+\d+)?)(?:\s+.*)?$",
        IgnoreCase);
    private static readonly Regex StandaloneSeriesMarker = new(
        @"^(?:сезоны?:?\s*\d+|сери[яи]:?\s*\d+|seasons?\s*\d+|episodes?\s*\d+|s\d{1,2}(?:e\d{1,2})?)\b",
        IgnoreCase);
    private static readonly Regex LatinLetters = new("[a-zA-Z]", RegexOptions.CultureInvariant);
    private static readonly Regex CyrillicLetters = new("[а-яА-ЯёЁ]", RegexOptions.CultureInvariant);
    private static readonly Regex ExoticScripts = new("[\\u0590-\\u05FF\\u0600-\\u06FF\\u4E00-\\u9FFF\\u3040-\\u30FF\\uAC00-\\uD7AF]", RegexOptions.CultureInvariant);
    private static readonly Regex Digits = new(@"\d", RegexOptions.CultureInvariant);
    private static readonly Regex DirectorMetadata = new(@"\b(?:реж(?:исс?[её]р|\.|\b)|directed\s+by|dir\.)", IgnoreCase);
    private static readonly Regex PartMetadata = new(@"^(?:part|vol|chapter|edition|version)\s*\d*$", IgnoreCase);
    private static readonly Regex AudioMetadata = new(
        @"^(?:дубляж|перевод|dvo|mvo|avo|vo|sub|lostfilm|hdrezka|newstudio|кураж-бамбей|звук)\b",
        IgnoreCase);
    private static readonly Regex SeriesMetadata = new(@"^(?:сезон|серии|season|episodes?)\b", IgnoreCase);
    private static readonly HashSet<string> MeaningfulParentheticals = new(StringComparer.OrdinalIgnoreCase)
    {
        "death and rebirth",
        "the end of evangelion",
        "stand alone complex",
        "the movie",
        "extended cut",
        "extended edition",
        "director's cut",
        "directors cut",
        "special edition",
        "unrated",
        "remastered",
        "final cut"
    };

    public static ParsedRutrackerTitle Parse(string? rawTitle, string? feedType = null)
    {
        if (string.IsNullOrWhiteSpace(rawTitle))
        {
            return Empty("empty_raw_title");
        }

        var clean = LeadingCategory.Replace(rawTitle, string.Empty).Trim();
        if (clean.Length == 0)
        {
            return Empty("empty_raw_title");
        }

        var (year, yearReason) = ExtractYear(clean);
        var titleSection = ExtractTitleSection(clean);
        var parts = SplitTitleParts(titleSection)
            .Select(part => part.Trim(' ', '-'))
            .Where(part => part.Length > 0)
            .ToArray();
        var normalizedFeedType = feedType?.Trim();
        var candidates = parts
            .Where(part => !StandaloneSeriesMarker.IsMatch(part))
            .Select((part, index) => new TitleCandidate(
                CleanupTitlePart(part, removeSeriesMarkers: true, feedType: normalizedFeedType),
                index == 0 ? "leading" : "alternate"))
            .Where(candidate => candidate.Title.Length is > 0 and <= 160)
            .DistinctBy(candidate => candidate.Title, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();

        ParsedRutrackerTitle Result(string selected, bool series, IReadOnlyList<string> reasons) =>
            CreateResult(selected, year, series, reasons) with
            {
                Candidates = candidates,
                YearSource = yearReason == "valid_year_extracted"
                    ? ParenthesizedYear.IsMatch(clean) ? "title_parenthesis" : BracketYear.IsMatch(clean) ? "bracket" : "standalone"
                    : yearReason,
                YearAmbiguous = Regex.Matches(clean, @"(?<!\d)(?:18|19|20)\d{2}(?!\d)")
                    .Select(match => match.Value).Distinct().Skip(1).Any()
            };

        string title;
        bool isSeries;
        string? typeReason;
        if (string.Equals(normalizedFeedType, "movie", StringComparison.OrdinalIgnoreCase))
        {
            (title, var titleReason) = SelectTitle(parts, removeSeriesMarkers: true, feedType: normalizedFeedType);
            isSeries = false;
            typeReason = "feed_type_authoritative_movie";
            var reasons = CreateReasons(yearReason, titleReason, typeReason, title);
            return Result(title, isSeries, reasons);
        }

        if (normalizedFeedType is not null &&
            (string.Equals(normalizedFeedType, "series_complete", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(normalizedFeedType, "series_ongoing", StringComparison.OrdinalIgnoreCase)))
        {
            (title, var titleReason) = SelectTitle(parts, removeSeriesMarkers: true, feedType: normalizedFeedType);
            isSeries = true;
            typeReason = "feed_type_authoritative_series";
            var reasons = CreateReasons(yearReason, titleReason, typeReason, title);
            return Result(title, isSeries, reasons);
        }

        isSeries = SeriesMarker.IsMatch(clean);
        (title, var inferredTitleReason) = SelectTitle(
            parts,
            removeSeriesMarkers: isSeries,
            feedType: normalizedFeedType);
        typeReason = isSeries ? "series_inferred_from_markers" : null;
        return Result(title, isSeries, CreateReasons(yearReason, inferredTitleReason, typeReason, title));
    }

    private static ParsedRutrackerTitle Empty(string reason) =>
        new(string.Empty, string.Empty, null, false, Array.AsReadOnly(new[] { reason }));

    private static ParsedRutrackerTitle CreateResult(
        string title,
        int? year,
        bool isSeries,
        IReadOnlyList<string> reasons) =>
        new(title, title.ToLowerInvariant(), year, isSeries, reasons);

    private static IReadOnlyList<string> CreateReasons(
        string yearReason,
        string? titleReason,
        string? typeReason,
        string title)
    {
        var reasons = new List<string> { yearReason };
        if (titleReason is not null)
        {
            reasons.Add(titleReason);
        }

        if (typeReason is not null)
        {
            reasons.Add(typeReason);
        }

        if (title.Contains('/', StringComparison.Ordinal))
        {
            reasons.Add("embedded_slash_preserved");
        }

        return Array.AsReadOnly(reasons.ToArray());
    }

    private static (int? Year, string Reason) ExtractYear(string title)
    {
        var match = ParenthesizedYear.Match(title);
        if (match.Success)
        {
            return ValidateYear(match.Groups["year"].Value);
        }

        match = BracketYear.Match(title);
        if (match.Success)
        {
            return ValidateYear(match.Groups["year"].Value);
        }

        match = StandaloneYear.Match(title);
        if (match.Success)
        {
            return ValidateYear(match.Groups["year"].Value);
        }

        match = OutOfRangeYear.Match(title);
        return match.Success ? (null, "invalid_year_range") : (null, "year_missing");
    }

    private static (int? Year, string Reason) ValidateYear(string value)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var year) ||
            year < MinimumYear || year > MaximumYear)
        {
            return (null, "invalid_year_range");
        }

        return (year, "valid_year_extracted");
    }

    private static string ExtractTitleSection(string title)
    {
        var releaseBracket = BracketSection.Matches(title)
            .Cast<Match>()
            .FirstOrDefault(match =>
                (TechnicalBracket.IsMatch(match.Value) ||
                 Regex.IsMatch(match.Value, @"^\[\s*\d{4}(?:\s*[-–—]\s*\d{4})?(?:\s*[,\]])", RegexOptions.CultureInvariant)) &&
                !Regex.IsMatch(title[(match.Index + match.Length)..], @"^\s+[/|]\s*", RegexOptions.CultureInvariant));
        var titleSection = releaseBracket is null
            ? BracketSection.Replace(title, match =>
                TechnicalBracket.IsMatch(match.Value) ||
                Regex.IsMatch(match.Value, @"^\[\s*\d{4}(?:\s*[-–—]\s*\d{4})?(?:\s*[,\]])", RegexOptions.CultureInvariant) ||
                Regex.IsMatch(title[(match.Index + match.Length)..], @"^\s+[/|]\s*", RegexOptions.CultureInvariant)
                    ? " "
                    : match.Value).Trim()
            : title[..releaseBracket.Index].Trim();
        titleSection = Whitespace.Replace(titleSection, " ");
        var trailingParenthesis = TrailingParenthesis.Match(titleSection);
        if (trailingParenthesis.Success && IsMetadataParenthesis(trailingParenthesis.Groups[1].Value))
        {
            titleSection = titleSection[..trailingParenthesis.Index].Trim();
        }

        return titleSection;
    }

    private static string[] SplitTitleParts(string title)
    {
        var parts = new List<string>();
        var start = 0;
        var parenthesesDepth = 0;
        for (var index = 0; index < title.Length; index++)
        {
            if (title[index] == '(')
            {
                parenthesesDepth++;
                continue;
            }

            if (title[index] == ')')
            {
                parenthesesDepth = Math.Max(0, parenthesesDepth - 1);
                continue;
            }

            if (parenthesesDepth != 0 || title[index] is not ('/' or '|'))
            {
                continue;
            }

            var hasAdjacentWhitespace =
                (index > 0 && char.IsWhiteSpace(title[index - 1])) ||
                (index + 1 < title.Length && char.IsWhiteSpace(title[index + 1]));
            if (!hasAdjacentWhitespace)
            {
                continue;
            }

            parts.Add(title[start..index]);
            start = index + 1;
        }

        parts.Add(title[start..]);
        return parts.ToArray();
    }

    private static bool IsMetadataParenthesis(string value)
    {
        var text = value.Trim('(', ')', ' ');
        if (text.Length == 0 || Regex.IsMatch(text, @"^\d{4}(?:[-–—]\d{4})?$", RegexOptions.CultureInvariant))
        {
            return true;
        }

        if (MeaningfulParentheticals.Contains(text) || PartMetadata.IsMatch(text))
        {
            return false;
        }

        return DirectorMetadata.IsMatch(text) ||
               CyrillicLetters.IsMatch(text) ||
               text.Contains('/', StringComparison.Ordinal) ||
               AudioMetadata.IsMatch(text) ||
               SeriesMetadata.IsMatch(text);
    }

    private static (string Title, string? Reason) SelectTitle(
        string[] parts,
        bool removeSeriesMarkers,
        string? feedType)
    {
        var candidates = parts
            .Where(part => !StandaloneSeriesMarker.IsMatch(part))
            .Select(part => CleanupTitlePart(part, removeSeriesMarkers, feedType))
            .Where(part => part.Length > 0)
            .ToArray();

        var latinCandidate = candidates.FirstOrDefault(IsLatinCandidate);
        if (latinCandidate is not null)
        {
            return (latinCandidate, "latin_candidate_selected");
        }

        var numericCandidate = candidates.FirstOrDefault(IsNumericCandidate);
        if (numericCandidate is not null)
        {
            return (numericCandidate, "numeric_candidate_selected");
        }

        if (candidates.Length == 0)
        {
            return (string.Empty, "no_title_candidate");
        }

        var first = candidates[0];
        if (CyrillicLetters.IsMatch(first))
        {
            return (first, "cyrillic_candidate_selected");
        }

        return LatinLetters.IsMatch(first)
            ? (first, null)
            : (first, "numeric_candidate_selected");
    }

    private static string CleanupTitlePart(string part, bool removeSeriesMarkers, string? feedType)
    {
        var cleaned = Whitespace.Replace(part, " ").Trim(' ', '-');
        cleaned = ParenthesizedYear.Replace(cleaned, string.Empty).Trim(' ', '-');
        cleaned = RemoveMetadataParentheticals(cleaned);
        cleaned = Whitespace.Replace(cleaned, " ").Trim(' ', '-');

        if (removeSeriesMarkers)
        {
            var releaseMarker = feedType switch
            {
                "series_complete" => TrailingCompleteSeasonMarker,
                "series_ongoing" => TrailingOngoingEpisodeMarker,
                _ => TrailingSeriesMarker
            };
            cleaned = releaseMarker.Replace(cleaned, string.Empty).Trim(' ', '-', ',', '.');
        }

        return cleaned;
    }

    private static string RemoveMetadataParentheticals(string value)
    {
        var result = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length;)
        {
            if (value[index] != '(')
            {
                result.Append(value[index++]);
                continue;
            }

            var end = FindParenthesisEnd(value, index);
            if (end < 0)
            {
                result.Append(value[index++]);
                continue;
            }

            var parenthetical = value[index..(end + 1)];
            if (IsMetadataParenthesis(parenthetical))
            {
                result.Append(' ');
            }
            else
            {
                result.Append('(');
                result.Append(RemoveMetadataParentheticals(value[(index + 1)..end]));
                result.Append(')');
            }

            index = end + 1;
        }

        return result.ToString();
    }

    private static int FindParenthesisEnd(string value, int start)
    {
        var depth = 0;
        for (var index = start; index < value.Length; index++)
        {
            if (value[index] == '(')
            {
                depth++;
            }
            else if (value[index] == ')' && --depth == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsLatinCandidate(string value)
    {
        if (!LatinLetters.IsMatch(value) || ExoticScripts.IsMatch(value))
        {
            return false;
        }

        var latinCount = Regex.Matches(value, "[a-zA-Z]", RegexOptions.CultureInvariant).Count;
        var cyrillicCount = CyrillicLetters.Matches(value).Count;
        return cyrillicCount < 3 && latinCount >= cyrillicCount;
    }

    private static bool IsNumericCandidate(string value) =>
        Digits.IsMatch(value) && !CyrillicLetters.IsMatch(value) && !ExoticScripts.IsMatch(value);
}