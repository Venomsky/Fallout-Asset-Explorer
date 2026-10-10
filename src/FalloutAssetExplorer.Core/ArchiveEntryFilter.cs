namespace FalloutAssetExplorer.Core;

public static class ArchiveEntryFilter
{
    public const string AllExtensions = "All types";

    public static IReadOnlyList<ArchiveEntry> Apply(
        IEnumerable<ArchiveEntry> entries,
        string? searchText,
        string? extension)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var search = searchText?.Trim();
        var selectedExtension = extension?.Trim();

        return entries
            .Where(entry =>
                string.IsNullOrEmpty(search)
                || entry.Path.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Where(entry =>
                string.IsNullOrEmpty(selectedExtension)
                || string.Equals(selectedExtension, AllExtensions, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.Extension, selectedExtension, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}
