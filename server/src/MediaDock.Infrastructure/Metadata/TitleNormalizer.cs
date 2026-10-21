namespace MediaDock.Infrastructure.Metadata;

internal static class TitleNormalizer
{
    public static string NormalizeTitle(string title) =>
        string.Join(' ', title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
}