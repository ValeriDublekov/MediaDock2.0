using MediaDock.Application.Ingestion;

namespace MediaDock.UnitTests;

[Trait("Category", "Parsing")]
public sealed class TorrentUrlIdentityTests
{
    [Theory]
    [InlineData("http://www.rutracker.org/forum/viewtopic.php?start=20&t=6916519", "https://rutracker.org/forum/viewtopic.php?t=6916519")]
    [InlineData("https://rutracker.cc/forum/viewtopic.php?t=6916519&view=previous", "https://rutracker.org/forum/viewtopic.php?t=6916519")]
    [InlineData("https://rutracker.org/forum/viewtopic.php?t=0006916519", "https://rutracker.org/forum/viewtopic.php?t=6916519")]
    public void NormalizeUsesTheRutrackerTopicId(string url, string expected)
    {
        Assert.Equal(expected, TorrentUrlIdentity.Normalize(url));
    }

    [Fact]
    public void UrlBasedSourceItemKeysUseTheCanonicalTopicUrl()
    {
        var first = SourceItemIdentity.From(null, "https://rutracker.org/forum/viewtopic.php?t=6916519");
        var second = SourceItemIdentity.From(null, "https://www.rutracker.org/forum/viewtopic.php?start=20&t=6916519");

        Assert.Equal(first, second);
    }

    [Fact]
    public void NormalizeLeavesOtherTrackerUrlsUnchanged()
    {
        const string url = "https://tracker.example/topic/123?source=rss";

        Assert.Equal(url, TorrentUrlIdentity.Normalize(url));
    }
}