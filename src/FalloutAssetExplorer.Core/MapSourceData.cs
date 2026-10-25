namespace FalloutAssetExplorer.Core;

public sealed class MapSourceData
{
    private readonly byte[] _bytes;

    public MapSourceData(ReadOnlyMemory<byte> source)
    {
        _bytes = source.ToArray();
        SnapshotId = Guid.NewGuid();
    }

    public Guid SnapshotId { get; }

    public int Length => _bytes.Length;

    public MapSourceRange GetRange(long offset, long length)
    {
        if (offset < 0 || offset > _bytes.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        if (length < 0 || length > _bytes.Length - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        return new MapSourceRange(this, offset, length);
    }

    internal ReadOnlyMemory<byte> Memory => _bytes;

    internal byte[] CopyRange(int offset, int length) =>
        _bytes.AsSpan(offset, length).ToArray();
}

public sealed class MapDecodeProvenance
{
    internal MapDecodeProvenance(MapSourceData source, FalloutGame game, int version)
    {
        SourceSnapshotId = source.SnapshotId;
        DecodeId = Guid.NewGuid();
        Game = game;
        Version = version;
    }

    public Guid SourceSnapshotId { get; }

    public Guid DecodeId { get; }

    public FalloutGame Game { get; }

    public int Version { get; }
}

public sealed class MapSourceRange
{
    private readonly MapSourceData _source;

    internal MapSourceRange(MapSourceData source, long offset, long length)
    {
        _source = source;
        Offset = offset;
        Length = length;
    }

    public long Offset { get; }

    public long Length { get; }

    public byte[] ToArray() =>
        _source.CopyRange(checked((int)Offset), checked((int)Length));
}
