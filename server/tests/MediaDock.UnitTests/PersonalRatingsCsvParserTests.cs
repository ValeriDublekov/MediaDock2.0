using System.Text;
using MediaDock.Application.PersonalRatings;

namespace MediaDock.UnitTests;

public sealed class PersonalRatingsCsvParserTests
{
    [Fact]
    public void ParseReadsImdbCsvExportAndConvertsTimestampsToUtcDates()
    {
        const string csv = "\"Title ID\",Rating,\"Last Modified Date\"\r\n"
            + "tt14452776,8,2026-08-30T21:09:30.554Z\r\n"
            + "tt1172049,6,2026-08-13T16:26:49.541Z\r\n"
            + "tt1535109,8,2026-08-09T21:21:23.099Z";

        var result = Parse(csv);

        Assert.Equal(3, result.Ratings.Count);
        Assert.Equal(new PersonalRatingImportEntry(8, new DateOnly(2026, 8, 30)), result.Ratings["tt14452776"]);
        Assert.Equal(new PersonalRatingImportEntry(6, new DateOnly(2026, 8, 13)), result.Ratings["tt1172049"]);
        Assert.Equal(new PersonalRatingImportEntry(8, new DateOnly(2026, 8, 9)), result.Ratings["tt1535109"]);
    }

    [Fact]
    public void ParseHandlesBomReorderedColumnsAndIdenticalDuplicates()
    {
        const string csv = "\uFEFFRating,\"Last Modified Date\",\"Title ID\"\n"
            + "8,2026-08-30T21:09:30.554Z, TT14452776 \n"
            + "8,2026-08-30T21:09:30.554Z,tt14452776";

        var result = Parse(csv);

        Assert.Single(result.Ratings);
        Assert.Equal(8, result.Ratings["tt14452776"].Rating);
    }

    [Fact]
    public void ParseSkipsRowsWithoutRatingsAndReturnsTheirIds()
    {
        const string csv = "\"Title ID\",Rating,\"Last Modified Date\"\n"
            + "tt14452776,8,2026-08-30T21:09:30.554Z\n"
            + "tt1234567,,\n"
            + "tt7654321,0,";

        var result = Parse(csv);

        Assert.Single(result.Ratings);
        Assert.Equal(new[] { "tt1234567", "tt7654321" }, result.MissingRatingIds);
    }

    [Fact]
    public void ParseUsesCurrentDateWhenLastModifiedDateIsMissing()
    {
        const string csv = "Title ID,Rating,Last Modified Date\ntt1234567,8,";

        var result = Parse(csv);

        Assert.Equal(new DateOnly(2026, 10, 29), result.Ratings["tt1234567"].DateRated);
    }

    [Theory]
    [InlineData("Title,Rating,Date\ntt1234567,8,2026-08-30T21:09:30Z")]
    [InlineData("Title ID,Rating,Last Modified Date\ntt1234567,11,2026-08-30T21:09:30Z")]
    [InlineData("Title ID,Rating,Last Modified Date\ntt1234567,8,not-a-date")]
    [InlineData("Title ID,Rating,Last Modified Date\nmovie-1,8,2026-08-30T21:09:30Z")]
    [InlineData("Title ID,Rating,Last Modified Date\ntt1234567,8")]
    [InlineData("Title ID,Rating,Last Modified Date\n")]
    [InlineData("Title ID,Rating,Last Modified Date\n\"tt1234567,8,2026-08-30T21:09:30Z")]
    public void ParseRejectsInvalidFiles(string csv)
    {
        Assert.Throws<PersonalRatingsImportException>(() =>
            Parse(csv));
    }

    [Fact]
    public void ParseRejectsConflictingDuplicateIds()
    {
        const string csv = "Title ID,Rating,Last Modified Date\n"
            + "tt1234567,7,2026-08-30T21:09:30Z\n"
            + "TT1234567,8,2026-08-30T21:09:30Z";

        Assert.Throws<PersonalRatingsImportException>(() =>
            Parse(csv));
    }

    private static PersonalRatingsParseResult Parse(string csv) =>
        PersonalRatingsCsvParser.Parse(Encoding.UTF8.GetBytes(csv), new DateOnly(2026, 10, 29));
}