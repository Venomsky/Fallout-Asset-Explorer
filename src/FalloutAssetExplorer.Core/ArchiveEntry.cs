namespace FalloutAssetExplorer.Core;

public record ArchiveEntry(
    string Path,
    long UncompressedSize,
    long? CompressedSize,
    string ArchiveLocation)
{
    public string Extension => System.IO.Path.GetExtension(Path).ToUpperInvariant();
}
