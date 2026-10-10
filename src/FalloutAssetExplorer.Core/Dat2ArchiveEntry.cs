namespace FalloutAssetExplorer.Core;

public sealed record Dat2ArchiveEntry : ArchiveEntry
{
    public Dat2ArchiveEntry(
        string storedPath,
        string normalizedPath,
        bool isCompressed,
        long uncompressedSize,
        long storedSize,
        long offset,
        string archivePath)
        : base(
            normalizedPath,
            uncompressedSize,
            isCompressed ? storedSize : null,
            archivePath)
    {
        StoredPath = storedPath;
        IsCompressed = isCompressed;
        StoredSize = storedSize;
        Offset = offset;
        ArchivePath = archivePath;
    }

    public string StoredPath { get; }

    public bool IsCompressed { get; }

    public long StoredSize { get; }

    public long Offset { get; }

    public string ArchivePath { get; }
}
