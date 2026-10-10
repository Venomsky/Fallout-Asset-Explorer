namespace FalloutAssetExplorer.Core;

public enum FalloutGame
{
    Fallout1,
    Fallout2
}

public enum ArchiveFormat
{
    Dat1,
    Dat2
}

public interface IGameArchive
{
    string Path { get; }

    FalloutGame Game { get; }

    ArchiveFormat Format { get; }

    IReadOnlyList<ArchiveEntry> BrowserEntries { get; }

    Task CopyEntryToAsync(
        ArchiveEntry entry,
        Stream destination,
        CancellationToken cancellationToken = default);
}
