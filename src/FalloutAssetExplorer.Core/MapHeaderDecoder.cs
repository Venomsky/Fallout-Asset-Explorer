using System.Buffers.Binary;

namespace FalloutAssetExplorer.Core;

public static class MapHeaderDecoder
{
    public const int HeaderSize = 0xEC;
    public const int ElevationCount = 3;
    public const int SquareGridTileCount = 100 * 100;
    public const int BytesPerElevation = SquareGridTileCount * sizeof(uint);
    public const int ReservedDataSize = HeaderSize - 0x3C;

    public static MapHeaderMetadata Decode(MapSourceData source, FalloutGame game)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Decode(source.Memory, game) with { SourceData = source };
    }

    public static MapHeaderMetadata Decode(ReadOnlyMemory<byte> data, FalloutGame game)
    {
        if (data.Length < HeaderSize)
        {
            throw new InvalidDataException(
                $"The MAP file is shorter than its 0x{HeaderSize:X}-byte header.");
        }

        var bytes = data.Span;
        var version = ReadInt32BigEndian(bytes, 0);
        if (!IsSupportedVersion(game, version))
        {
            throw new InvalidDataException(
                $"Unsupported Fallout {GetGameNumber(game)} MAP version {version}.");
        }

        var localVariableCount = ReadInt32BigEndian(bytes, 0x20);
        var flags = ReadInt32BigEndian(bytes, 0x28);
        var globalVariableCount = ReadInt32BigEndian(bytes, 0x30);
        ValidateVariableCount("global", globalVariableCount);
        ValidateVariableCount("local", localVariableCount);

        var globalVariablesOffset = (long)HeaderSize;
        var globalVariablesLength = (long)globalVariableCount * sizeof(int);
        var localVariablesOffset = globalVariablesOffset + globalVariablesLength;
        var localVariablesLength = (long)localVariableCount * sizeof(int);
        var tileDataOffset = localVariablesOffset + localVariablesLength;
        var presentElevationCount = CountPresentElevations(flags);
        var tileDataLength = (long)presentElevationCount * BytesPerElevation;
        var tileDataEnd = tileDataOffset + tileDataLength;
        if (tileDataEnd > data.Length)
        {
            throw new InvalidDataException(
                $"The MAP variable and tile sections end at byte {tileDataEnd}, beyond the {data.Length}-byte file.");
        }

        return new MapHeaderMetadata(
            game,
            version,
            data.Length,
            bytes.Slice(0x04, 16).ToArray(),
            ReadInt32BigEndian(bytes, 0x14),
            ReadInt32BigEndian(bytes, 0x18),
            ReadInt32BigEndian(bytes, 0x1C),
            localVariableCount,
            ReadInt32BigEndian(bytes, 0x24),
            flags,
            ReadInt32BigEndian(bytes, 0x2C),
            globalVariableCount,
            ReadInt32BigEndian(bytes, 0x34),
            BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(0x38, sizeof(uint))),
            bytes.Slice(0x3C, ReservedDataSize).ToArray(),
            globalVariablesOffset,
            globalVariablesLength,
            localVariablesOffset,
            localVariablesLength,
            tileDataOffset,
            tileDataLength,
            presentElevationCount);
    }

    private static int CountPresentElevations(int flags)
    {
        var presentCount = ElevationCount;
        for (var elevation = 0; elevation < ElevationCount; elevation++)
        {
            if ((flags & (1 << (elevation + 1))) != 0)
            {
                presentCount--;
            }
        }

        return presentCount;
    }

    private static void ValidateVariableCount(string name, int count)
    {
        if (count < 0)
        {
            throw new InvalidDataException(
                $"The MAP {name} variable count {count} is negative.");
        }
    }

    private static bool IsSupportedVersion(FalloutGame game, int version) =>
        game switch
        {
            FalloutGame.Fallout1 => version == 19,
            FalloutGame.Fallout2 => version is 19 or 20,
            _ => throw new ArgumentOutOfRangeException(nameof(game), game, "Unknown Fallout game.")
        };

    private static int GetGameNumber(FalloutGame game) =>
        game switch
        {
            FalloutGame.Fallout1 => 1,
            FalloutGame.Fallout2 => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(game), game, "Unknown Fallout game.")
        };

    private static int ReadInt32BigEndian(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(offset, sizeof(int)));
}

public sealed record MapHeaderMetadata(
    FalloutGame Game,
    int Version,
    int EntryLength,
    byte[] RawName,
    int EnteringTile,
    int EnteringElevation,
    int EnteringRotation,
    int LocalVariableCount,
    int ScriptIndex,
    int Flags,
    int Darkness,
    int GlobalVariableCount,
    int MapIndex,
    uint LastVisitTimeRaw,
    byte[] ReservedHeaderData,
    long GlobalVariablesOffset,
    long GlobalVariablesLength,
    long LocalVariablesOffset,
    long LocalVariablesLength,
    long TileDataOffset,
    long TileDataLength,
    int PresentElevationCount)
{
    public MapSourceData? SourceData { get; init; }
}
