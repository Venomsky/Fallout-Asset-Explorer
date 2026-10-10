namespace FalloutAssetExplorer.Core;

public sealed record Dat1ArchiveEntry : ArchiveEntry
{
    public Dat1ArchiveEntry(
        string path,
        uint type,
        long offset,
        long uncompressedSize,
        long packedSize,
        string archivePath)
        : base(
            path,
            uncompressedSize,
            type == Dat1Archive.RawEntryType ? null : packedSize,
            archivePath)
    {
        Type = type;
        Offset = offset;
        PackedSize = packedSize;
        ArchivePath = archivePath;
    }

    public uint Type { get; }

    public long Offset { get; }

    public long PackedSize { get; }

    public string ArchivePath { get; }

    public long StoredDataSize => Type == Dat1Archive.RawEntryType ? UncompressedSize : PackedSize;

    public ArchiveEntry ToBrowserEntry() => this;
}
