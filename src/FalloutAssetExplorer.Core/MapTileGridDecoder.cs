using System.Buffers.Binary;

namespace FalloutAssetExplorer.Core;

public static class MapTileGridDecoder
{
    public const int GridWidth = 100;
    public const int GridHeight = 100;
    public const int ValuesPerGrid = GridWidth * GridHeight;
    public const int BytesPerGrid = ValuesPerGrid * sizeof(uint);

    public static (int X, int Y) GetSquareGridCoordinates(int serializedIndex)
    {
        if ((uint)serializedIndex >= ValuesPerGrid)
        {
            throw new ArgumentOutOfRangeException(nameof(serializedIndex));
        }

        return (GridWidth - 1 - serializedIndex % GridWidth, serializedIndex / GridWidth);
    }

    public static int GetSerializedIndex(int x, int y)
    {
        if ((uint)x >= GridWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        if ((uint)y >= GridHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }

        return y * GridWidth + GridWidth - 1 - x;
    }

    public static MapTileGridData Decode(MapSourceData source, MapHeaderMetadata header)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(header);
        if (!ReferenceEquals(header.SourceData, source)
            || header.Provenance is not { } provenance
            || provenance.SourceSnapshotId != source.SnapshotId)
        {
            throw new InvalidDataException("The MAP header metadata is not bound to this source data.");
        }

        var decoded = Decode(source.Memory, header);
        return decoded with
        {
            Header = decoded.Header with
            {
                SourceData = source,
                Provenance = provenance
            },
            Provenance = provenance
        };
    }

    public static MapTileGridData Decode(ReadOnlyMemory<byte> data, MapHeaderMetadata header)
    {
        ArgumentNullException.ThrowIfNull(header);
        if (header.Provenance is { } provenance
            && (provenance.Game != header.Game || provenance.Version != header.Version))
        {
            throw new InvalidDataException("The MAP header provenance does not match its game and version.");
        }

        var validatedHeader = MapHeaderDecoder.Decode(data, header.Game);
        if (header.Version != validatedHeader.Version
            || header.EntryLength != validatedHeader.EntryLength
            || header.Flags != validatedHeader.Flags
            || header.GlobalVariableCount != validatedHeader.GlobalVariableCount
            || header.LocalVariableCount != validatedHeader.LocalVariableCount
            || header.GlobalVariablesOffset != validatedHeader.GlobalVariablesOffset
            || header.GlobalVariablesLength != validatedHeader.GlobalVariablesLength
            || header.LocalVariablesOffset != validatedHeader.LocalVariablesOffset
            || header.LocalVariablesLength != validatedHeader.LocalVariablesLength
            || header.TileDataOffset != validatedHeader.TileDataOffset
            || header.TileDataLength != validatedHeader.TileDataLength
            || header.PresentElevationCount != validatedHeader.PresentElevationCount)
        {
            throw new InvalidDataException("The MAP header metadata does not match the entry data.");
        }

        var grids = new List<MapTileGrid>(validatedHeader.PresentElevationCount);
        var offset = validatedHeader.TileDataOffset;
        for (var elevation = 0; elevation < MapHeaderDecoder.ElevationCount; elevation++)
        {
            if ((validatedHeader.Flags & (1 << (elevation + 1))) != 0)
            {
                continue;
            }

            var gridEnd = checked(offset + BytesPerGrid);
            if (offset < 0 || gridEnd > data.Length)
            {
                throw new InvalidDataException(
                    $"MAP elevation {elevation} tile grid extends beyond the {data.Length}-byte entry.");
            }

            var values = new uint[ValuesPerGrid];
            var gridBytes = data.Span.Slice(checked((int)offset), BytesPerGrid);
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = BinaryPrimitives.ReadUInt32BigEndian(
                    gridBytes.Slice(index * sizeof(uint), sizeof(uint)));
            }

            grids.Add(new MapTileGrid(elevation, values));
            offset = gridEnd;
        }

        if (offset != validatedHeader.TileDataOffset + validatedHeader.TileDataLength)
        {
            throw new InvalidDataException("The MAP tile-grid data does not match the header section length.");
        }

        return new MapTileGridData(validatedHeader, grids);
    }
}

public sealed record MapTileGrid(int ElevationIndex, uint[] RawValues);

public sealed record MapTileGridData(
    MapHeaderMetadata Header,
    IReadOnlyList<MapTileGrid> Grids)
{
    public MapDecodeProvenance? Provenance { get; internal init; }

    public MapTileGrid? GetElevation(int elevationIndex) =>
        Grids.FirstOrDefault(grid => grid.ElevationIndex == elevationIndex);
}
