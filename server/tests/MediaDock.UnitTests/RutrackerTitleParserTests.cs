using MediaDock.Application.Parsing;

namespace MediaDock.UnitTests;

[Trait("Category", "Parsing")]
public sealed class RutrackerTitleParserTests
{
    [Fact]
    public void SelectsLatinTitleAndPreservesEmbeddedSlash()
    {
        var parsed = RutrackerTitleParser.Parse(
            "Без лица / Face/Off (Джон Ву) [1997, США, BDRip 1080p]",
            "movie");

        Assert.Equal("Face/Off", parsed.Title);
        Assert.Equal("face/off", parsed.NormalizedTitle);
        Assert.Equal(1997, parsed.Year);
        Assert.False(parsed.IsSeries);
        Assert.Contains("embedded_slash_preserved", parsed.ReasonCodes);
    }

    [Fact]
    public void KeepsOriginalTitleAfterBracketLabel()
    {
        var parsed = RutrackerTitleParser.Parse("Локално име [етикет] / Original Title (1999) [2024, BDRip]", "movie");

        Assert.Equal("Original Title", parsed.Title);
        Assert.Equal(1999, parsed.Year);
        Assert.Equal("title_parenthesis", parsed.YearSource);
        Assert.True(parsed.YearAmbiguous);
        Assert.Contains(parsed.Candidates, candidate => candidate.Title == "Original Title" && candidate.Origin == "alternate");
    }

    [Fact]
    public void ConfiguredFeedTypeOverridesSeriesMarkers()
    {
        const string rawTitle = "Example / Example Title / Сезон: 1 / Серии: 1-8 из 8 [2026]";

        var movie = RutrackerTitleParser.Parse(rawTitle, "movie");
        var series = RutrackerTitleParser.Parse(rawTitle, "series_ongoing");

        Assert.Equal("Example", movie.Title);
        Assert.False(movie.IsSeries);
        Assert.Contains("feed_type_authoritative_movie", movie.ReasonCodes);
        Assert.Equal("Example", series.Title);
        Assert.True(series.IsSeries);
        Assert.Contains("feed_type_authoritative_series", series.ReasonCodes);
    }

    [Theory]
    [InlineData("series_complete", "Silo / Season 7 / Episodes 7 of 9 [2026]")]
    [InlineData("series_ongoing", "Silo S07E07 [2026]")]
    public void SeriesSeasonMarkersDoNotBecomePartOfTheShowTitle(string feedType, string rawTitle)
    {
        var parsed = RutrackerTitleParser.Parse(rawTitle, feedType);

        Assert.Equal("Silo", parsed.Title);
        Assert.Equal(2026, parsed.Year);
        Assert.True(parsed.IsSeries);
    }

    [Fact]
    public void SeriesProfilesUseDifferentSeasonAndEpisodeMarkerRules()
    {
        var completeSeason = RutrackerTitleParser.Parse("Silo Season 7 [2026]", "series_complete");
        var completeEpisode = RutrackerTitleParser.Parse("Silo S07E07 [2026]", "series_complete");
        var ongoingSeason = RutrackerTitleParser.Parse("Silo Season 7 [2026]", "series_ongoing");
        var ongoingEpisode = RutrackerTitleParser.Parse("Silo S07E07 [2026]", "series_ongoing");

        Assert.Equal("Silo", completeSeason.Title);
        Assert.NotEqual("Silo", completeEpisode.Title);
        Assert.NotEqual("Silo", ongoingSeason.Title);
        Assert.Equal("Silo", ongoingEpisode.Title);
    }

    [Fact]
    public void UnknownFeedInfersSeriesFromExplicitMarkerWithoutSubstringFalsePositives()
    {
        var series = RutrackerTitleParser.Parse("Monarch: Legacy of Monsters / Season 2 [2024]");
        var movie = RutrackerTitleParser.Parse("Season of the Witch [2011]");

        Assert.Equal("Monarch: Legacy of Monsters", series.Title);
        Assert.True(series.IsSeries);
        Assert.Contains("series_inferred_from_markers", series.ReasonCodes);
        Assert.Equal("Season of the Witch", movie.Title);
        Assert.False(movie.IsSeries);
    }

    [Fact]
    public void ExtractsRealisticYearsAndReportsMissingOrInvalidYears()
    {
        var lowerBoundary = RutrackerTitleParser.Parse("Old Film [1888, BDRip]");
        var upperBoundary = RutrackerTitleParser.Parse("Future Film [2035, BDRip]");
        var missing = RutrackerTitleParser.Parse("Film [1080p, BDRip]");
        var invalid = RutrackerTitleParser.Parse("Film [2150, BDRip]");

        Assert.Equal(1888, lowerBoundary.Year);
        Assert.Equal(2035, upperBoundary.Year);
        Assert.Null(missing.Year);
        Assert.Contains("year_missing", missing.ReasonCodes);
        Assert.Null(invalid.Year);
        Assert.Contains("invalid_year_range", invalid.ReasonCodes);
    }

    [Fact]
    public void ExtractsMovieTitleFromLocalizedTitleWithDirectorAndReleaseDetails()
    {
        const string rawTitle = "Человек-паук: Новый день / Spider-Man: Brand New Day (Дестин Дэниел Креттон / Destin Daniel Cretton) [2026, США, Канада, Великобритания, Германия, фэнтези, комедия, приключения, семейный, WEB-DL 1080p] Dub (MovieDalen) + Original Eng + Sub (Rus, Eng) [9.38 GB]";

        var parsed = RutrackerTitleParser.Parse(rawTitle, "movie");

        Assert.Equal("Spider-Man: Brand New Day", parsed.Title);
        Assert.Equal(2026, parsed.Year);
        Assert.False(parsed.IsSeries);
        Assert.Equal(
            new[] { "Человек-паук: Новый день", "Spider-Man: Brand New Day" },
            parsed.Candidates.Select(candidate => candidate.Title));
    }

    [Fact]
    public void RemovesNestedDirectorMetadataWithoutLeavingAnExtraClosingParenthesis()
    {
        var parsed = RutrackerTitleParser.Parse(
            "Локальное название / Local Title (Кирилл Режиссер / Kirill Director (Alias)) [1978, страна, HDTVRip 1080p] + Sub Rus, Eng",
            "movie");

        Assert.Equal("Local Title", parsed.Title);
        Assert.Equal(1978, parsed.Year);
        Assert.Equal(new[] { "Локальное название", "Local Title" }, parsed.Candidates.Select(candidate => candidate.Title));
    }

    [Fact]
    public void PreservesMeaningfulParenthesesAndHandlesEmptyInput()
    {
        var titled = RutrackerTitleParser.Parse(
            "The Lord of the Rings: The Two Towers (Extended Edition) [2002]");
        var empty = RutrackerTitleParser.Parse(null);

        Assert.Equal("The Lord of the Rings: The Two Towers (Extended Edition)", titled.Title);
        Assert.Equal("the lord of the rings: the two towers (extended edition)", titled.NormalizedTitle);
        Assert.Equal(string.Empty, empty.Title);
        Assert.Null(empty.Year);
        Assert.Contains("empty_raw_title", empty.ReasonCodes);
    }

    [Theory]
    [InlineData("Без лица / Face/Off (1997) [1080p]", "movie", "Face/Off", 1997, false)]
    [InlineData("Локално [етикет] / Original Title (1999) [2024, BDRip]", "movie", "Original Title", 1999, false)]
    [InlineData("Локално | Original Title [1999, BDRip]", "movie", "Original Title", 1999, false)]
    [InlineData("2046 [2004, BDRip]", "movie", "2046", 2004, false)]
    [InlineData("Season of the Witch [2011]", null, "Season of the Witch", 2011, false)]
    [InlineData("Monarch / Monarch: Legacy of Monsters / S01E02 [2024-2026]", "series_ongoing", "Monarch", 2024, true)]
    [InlineData("未来 [2024, BDRip]", "movie", "未来", 2024, false)]
    [InlineData("Film [Part 2] (2020) / Another Film [2024, BDRip]", "movie", "Film [Part 2]", 2020, false)]
    [InlineData("Film [Part 2, The Return] (2020) [BDRip]", "movie", "Film [Part 2, The Return]", 2020, false)]
    public void OfflineTitleCorpus(string raw, string? feedType, string title, int year, bool isSeries)
    {
        var parsed = RutrackerTitleParser.Parse(raw, feedType);

        Assert.Equal(title, parsed.Title);
        Assert.Equal(year, parsed.Year);
        Assert.Equal(isSeries, parsed.IsSeries);
        Assert.InRange(parsed.Candidates.Count, 1, 3);
    }

    [Fact]
    public void FutureYearBoundaryMovesWithCurrentYear()
    {
        var maximum = DateTime.UtcNow.Year + 10;

        Assert.Equal(maximum, RutrackerTitleParser.Parse($"Future Film [{maximum}]", "movie").Year);
        Assert.Null(RutrackerTitleParser.Parse($"Future Film [{maximum + 1}]", "movie").Year);
    }
}