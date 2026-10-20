using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace MediaDock.Application.Ingestion;

internal static class RssFeedParser
{
    private const int MaximumFeedSizeBytes = 4 * 1024 * 1024;

    public static IReadOnlyList<IngestionFeedItem> Parse(byte[] body)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumFeedSizeBytes,
            MaxCharactersFromEntities = 0,
            ConformanceLevel = ConformanceLevel.Document
        };

        using var stream = new MemoryStream(body, writable: false);
        using var reader = XmlReader.Create(stream, settings);
        var document = XDocument.Load(reader);
        var root = document.Root ?? throw new FormatException("Feed has no root element.");
        IEnumerable<XElement> elements = root.Name.LocalName switch
        {
            "rss" => root.Elements().FirstOrDefault(element => element.Name.LocalName == "channel")?
                .Elements().Where(element => element.Name.LocalName == "item")
                ?? throw new FormatException("RSS feed has no channel."),
            "feed" => root.Elements().Where(element => element.Name.LocalName == "entry"),
            _ => throw new FormatException("Feed root is not RSS or Atom.")
        };

        return elements.Select(ParseEntry).ToArray();
    }

    private static IngestionFeedItem ParseEntry(XElement element)
    {
        var title = ChildValue(element, "title");
        var id = ChildValue(element, "guid") ?? ChildValue(element, "id");
        var linkElement = element.Elements().FirstOrDefault(child => child.Name.LocalName == "link");
        var link = linkElement?.Attribute("href")?.Value ?? linkElement?.Value;
        var published = ChildValue(element, "pubDate")
            ?? ChildValue(element, "published")
            ?? ChildValue(element, "updated");
        DateTimeOffset? publishedAt = DateTimeOffset.TryParse(
            published,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsedDate)
            ? parsedDate
            : null;

        return new IngestionFeedItem(title, id, link?.Trim(), publishedAt);
    }

    private static string? ChildValue(XElement parent, string name) =>
        parent.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value.Trim();
}