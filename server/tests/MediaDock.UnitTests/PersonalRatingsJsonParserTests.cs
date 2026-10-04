using System.Text;
using MediaDock.Application.PersonalRatings;

namespace MediaDock.UnitTests;

public sealed class PersonalRatingsJsonParserTests
{
    [Fact]
    public void ParseNormalizesIdsAndDeduplicatesIdenticalRatings()
    {
        var json = """
            [
              { "id": " TT14452776 ", "rating": 8 },
              { "id": "tt1172049", "rating": 6 },
              { "id": "tt14452776", "rating": 8 }
            ]
            """;

        var result = PersonalRatingsJsonParser.Parse(Encoding.UTF8.GetBytes(json));

        Assert.Equal(2, result.Ratings.Count);
        Assert.Equal(8, result.Ratings["tt14452776"]);
        Assert.Equal(6, result.Ratings["tt1172049"]);
    }

    [Fact]
    public void ParseSkipsRowsWithoutRatingsAndReturnsTheirIds()
    {
        const string json = "[{\"id\":\"tt14452776\",\"rating\":8},{\"id\":\"tt1234567\"},{\"id\":\"tt7654321\",\"rating\":null}]";

        var result = PersonalRatingsJsonParser.Parse(Encoding.UTF8.GetBytes(json));

        Assert.Single(result.Ratings);
        Assert.Equal(8, result.Ratings["tt14452776"]);
        Assert.Equal(new[] { "tt1234567", "tt7654321" }, result.MissingRatingIds);
    }

    [Theory]
    [InlineData("[{\"id\":\"movie-1\",\"rating\":8}]")]
    [InlineData("[{\"id\":\"tt1234567\",\"rating\":0}]")]
    [InlineData("[{\"id\":\"tt1234567\",\"rating\":10.5}]")]
    [InlineData("[null]")]
    [InlineData("[]")]
    [InlineData("not json")]
    public void ParseRejectsInvalidFiles(string json)
    {
        Assert.Throws<PersonalRatingsImportException>(() =>
            PersonalRatingsJsonParser.Parse(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void ParseRejectsConflictingDuplicateIds()
    {
        const string json = "[{\"id\":\"tt1234567\",\"rating\":7},{\"id\":\"TT1234567\",\"rating\":8}]";

        Assert.Throws<PersonalRatingsImportException>(() =>
            PersonalRatingsJsonParser.Parse(Encoding.UTF8.GetBytes(json)));
    }
}