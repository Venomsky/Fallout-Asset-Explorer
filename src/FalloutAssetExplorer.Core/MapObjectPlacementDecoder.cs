using System.Buffers.Binary;

namespace FalloutAssetExplorer.Core;

public static class MapObjectPlacementDecoder
{
    public const int HexGridWidth = 200;
    public const int HexGridHeight = 200;
    public const int HexGridTileCount = HexGridWidth * HexGridHeight;

    private const int ScriptListCount = 5;
    private const int ScriptExtentSize = 16;
    private const int ScriptRecordBaseIntCount = 16;
    private const int ScriptExtentTrailerIntCount = 2;
    private const int ObjectBaseIntCount = 18;
    private const int InventoryHeaderIntCount = 3;
    private const int MinimumNonCritterObjectIntCount = ObjectBaseIntCount + InventoryHeaderIntCount + 1;
    private const int MaximumTopLevelObjects = 100_000;

    public static (int X, int Y) GetHexCoordinates(int serializedIndex)
    {
        if ((uint)serializedIndex >= HexGridTileCount)
        {
            throw new ArgumentOutOfRangeException(nameof(serializedIndex));
        }

        return (HexGridWidth - 1 - serializedIndex % HexGridWidth, serializedIndex / HexGridWidth);
    }

    public static MapObjectPlacementData Decode(ReadOnlyMemory<byte> data, MapHeaderMetadata header)
    {
        ArgumentNullException.ThrowIfNull(header);

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

        var bytes = data.Span;
        var offset = checked((int)(validatedHeader.TileDataOffset + validatedHeader.TileDataLength));
        SkipScriptSection(bytes, ref offset);

        var objectSectionOffset = offset;
        var totalObjectCount = ReadInt32(bytes, ref offset, "total object count");
        if (totalObjectCount < 0 || totalObjectCount > MaximumTopLevelObjects)
        {
            throw new InvalidDataException(
                $"MAP total object count {totalObjectCount} is outside the supported range 0..{MaximumTopLevelObjects}.");
        }

        var objectCountsByElevation = new int[MapHeaderDecoder.ElevationCount];
        long countSum = 0;

        var remainingBytes = bytes.Length - offset;
        if ((long)totalObjectCount * MinimumNonCritterObjectIntCount * sizeof(int) > remainingBytes)
        {
            throw new InvalidDataException(
                $"MAP object records at byte {objectSectionOffset} cannot fit within the remaining entry data.");
        }

        var objects = new List<MapObjectPlacement>(totalObjectCount);
        string? unsupportedReason = null;
        var recordIndex = 0;
        for (var elevation = 0; elevation < objectCountsByElevation.Length; elevation++)
        {
            var count = ReadInt32(bytes, ref offset, $"elevation {elevation} object count");
            if (count < 0)
            {
                throw new InvalidDataException($"MAP elevation {elevation} has a negative object count.");
            }

            countSum += count;
            if (countSum > totalObjectCount)
            {
                throw new InvalidDataException(
                    $"MAP total object count {totalObjectCount} is smaller than the cumulative elevation counts ({countSum}) at byte {objectSectionOffset}.");
            }

            objectCountsByElevation[elevation] = count;
            for (var objectIndex = 0; objectIndex < count; objectIndex++)
            {
                var placement = ReadObject(bytes, ref offset, recordIndex, elevation);
                objects.Add(placement.Placement);
                recordIndex++;
                if (placement.UnsupportedReason is not null)
                {
                    unsupportedReason = placement.UnsupportedReason;
                    return new MapObjectPlacementData(
                        objectSectionOffset,
                        totalObjectCount,
                        objectCountsByElevation,
                        objects,
                        unsupportedReason,
                        bytes.Length - offset);
                }
            }
        }

        if (countSum != totalObjectCount)
        {
            throw new InvalidDataException(
                $"MAP total object count {totalObjectCount} does not match the elevation counts ({countSum}) at byte {objectSectionOffset}.");
        }

        return new MapObjectPlacementData(
            objectSectionOffset,
            totalObjectCount,
            objectCountsByElevation,
            objects,
            null,
            bytes.Length - offset);
    }

    private static void SkipScriptSection(ReadOnlySpan<byte> bytes, ref int offset)
    {
        for (var listIndex = 0; listIndex < ScriptListCount; listIndex++)
        {
            var scriptCount = ReadInt32(bytes, ref offset, $"script list {listIndex} count");
            if (scriptCount < 0)
            {
                throw new InvalidDataException($"MAP script list {listIndex} has a negative count.");
            }

            var extentCount = ((long)scriptCount + ScriptExtentSize - 1) / ScriptExtentSize;
            var minimumExtentSize = (ScriptExtentSize * ScriptRecordBaseIntCount + ScriptExtentTrailerIntCount)
                * sizeof(int);
            if (extentCount > (bytes.Length - offset) / minimumExtentSize)
            {
                throw new InvalidDataException(
                    $"MAP script list {listIndex} extents exceed the remaining entry data.");
            }

            for (long extent = 0; extent < extentCount; extent++)
            {
                for (var script = 0; script < ScriptExtentSize; script++)
                {
                    var scriptId = ReadInt32(bytes, ref offset, $"script list {listIndex} record id");
                    SkipBytes(bytes, ref offset, sizeof(int), $"script list {listIndex} record link");
                    var scriptType = scriptId >> 24;
                    var optionalFieldCount = scriptType switch
                    {
                        1 => 2,
                        2 => 1,
                        _ => 0
                    };
                    SkipBytes(
                        bytes,
                        ref offset,
                        (optionalFieldCount + ScriptRecordBaseIntCount - 2) * sizeof(int),
                        $"script list {listIndex} record fields");
                }

                SkipBytes(
                    bytes,
                    ref offset,
                    ScriptExtentTrailerIntCount * sizeof(int),
                    $"script list {listIndex} extent trailer");
            }
        }
    }

    private static (MapObjectPlacement Placement, string? UnsupportedReason) ReadObject(
        ReadOnlySpan<byte> bytes,
        ref int offset,
        int recordIndex,
        int elevationGroup)
    {
        var recordOffset = offset;
        var rawHeader = new int[ObjectBaseIntCount];
        for (var index = 0; index < rawHeader.Length; index++)
        {
            rawHeader[index] = ReadInt32(bytes, ref offset, $"object record {recordIndex} base field {index}");
        }

        var rawInventoryLength = ReadInt32(bytes, ref offset, $"object record {recordIndex} inventory length");
        var rawInventoryCapacity = ReadInt32(bytes, ref offset, $"object record {recordIndex} inventory capacity");
        var rawInventoryPointer = ReadInt32(bytes, ref offset, $"object record {recordIndex} inventory pointer");
        if (rawInventoryLength < 0 || rawInventoryCapacity < 0)
        {
            throw new InvalidDataException($"MAP object record {recordIndex} has negative inventory metadata.");
        }

        var rawPid = rawHeader[11];
        var pidType = (int)((uint)rawPid >> 24);
        var payloadWords = new List<int>();
        var unsupportedReason = rawInventoryLength > 0
            ? $"Object record {recordIndex} has {rawInventoryLength} nested inventory entries; recursive inventory decoding is not supported."
            : null;

        if (unsupportedReason is null)
        {
            if (pidType == 1)
            {
                ReadInt32Words(bytes, ref offset, payloadWords, 11, $"object record {recordIndex} critter payload");
            }
            else
            {
                payloadWords.Add(ReadInt32(bytes, ref offset, $"object record {recordIndex} data flags"));
                if (pidType is 0 or 2)
                {
                    unsupportedReason = $"Object record {recordIndex} PID type {GetObjectTypeName(pidType)} requires PRO subtype data to determine its record layout.";
                }
                else if (pidType == 5 && rawPid is >= 0x05000010 and <= 0x05000017)
                {
                    ReadInt32Words(bytes, ref offset, payloadWords, 4, $"object record {recordIndex} exit-grid payload");
                }
                else if (pidType is < 0 or > 10)
                {
                    unsupportedReason = $"Object record {recordIndex} has unsupported PID type identifier {pidType}.";
                }
            }
        }

        var rawHexIndex = rawHeader[1];
        int? hexX = null;
        int? hexY = null;
        var warning = rawHexIndex == -1
            ? "No hex-grid tile is assigned to this record."
            : null;
        if ((uint)rawHexIndex < HexGridTileCount)
        {
            (hexX, hexY) = GetHexCoordinates(rawHexIndex);
        }
        else if (rawHexIndex != -1)
        {
            warning = $"Hex index {rawHexIndex} is outside the 0..{HexGridTileCount - 1} grid.";
        }

        var placement = new MapObjectPlacement(
            recordIndex,
            recordOffset,
            elevationGroup,
            rawHeader[0],
            rawHeader[1],
            hexX,
            hexY,
            rawHeader[2],
            rawHeader[3],
            rawHeader[4],
            rawHeader[5],
            rawHeader[6],
            rawHeader[7],
            rawHeader[8],
            rawHeader[9],
            rawHeader[10],
            rawPid,
            pidType,
            rawHeader[12],
            rawHeader[13],
            rawHeader[14],
            rawHeader[15],
            rawHeader[16],
            rawHeader[17],
            rawInventoryLength,
            rawInventoryCapacity,
            rawInventoryPointer,
            payloadWords,
            warning,
            unsupportedReason);
        return (placement, unsupportedReason);
    }

    private static void ReadInt32Words(
        ReadOnlySpan<byte> bytes,
        ref int offset,
        List<int> destination,
        int count,
        string description)
    {
        for (var index = 0; index < count; index++)
        {
            destination.Add(ReadInt32(bytes, ref offset, $"{description} field {index}"));
        }
    }

    private static int ReadInt32(ReadOnlySpan<byte> bytes, ref int offset, string description)
    {
        SkipBytes(bytes, ref offset, sizeof(int), description);
        var valueOffset = offset - sizeof(int);
        return BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(valueOffset, sizeof(int)));
    }

    private static void SkipBytes(ReadOnlySpan<byte> bytes, ref int offset, int count, string description)
    {
        if (count < 0 || offset < 0 || (long)offset + count > bytes.Length)
        {
            throw new InvalidDataException(
                $"MAP {description} at byte {offset} extends beyond the {bytes.Length}-byte entry.");
        }

        offset += count;
    }

    private static string GetObjectTypeName(int type) =>
        type switch
        {
            0 => "Item",
            1 => "Critter",
            2 => "Scenery",
            3 => "Wall",
            4 => "Tile",
            5 => "Misc",
            6 => "Interface",
            7 => "Inventory",
            8 => "Head",
            9 => "Background",
            10 => "Skilldex",
            _ => $"Type {type}"
        };
}

public sealed record MapObjectPlacement(
    int RecordIndex,
    int RecordOffset,
    int ElevationGroup,
    int RawObjectId,
    int RawHexIndex,
    int? HexX,
    int? HexY,
    int RawOffsetX,
    int RawOffsetY,
    int RawScreenX,
    int RawScreenY,
    int RawFrame,
    int RawRotation,
    int RawFid,
    int RawObjectFlags,
    int StoredElevation,
    int RawPid,
    int PidTypeIdentifier,
    int RawCid,
    int RawLightDistance,
    int RawLightIntensity,
    int RawField74,
    int RawScriptId,
    int RawScriptField,
    int RawInventoryLength,
    int RawInventoryCapacity,
    int RawInventoryPointer,
    IReadOnlyList<int> RawPayloadWords,
    string? Warning,
    string? UnsupportedReason)
{
    public string ObjectTypeDisplay => PidTypeIdentifier switch
    {
        >= 0 and <= 10 => PidTypeIdentifier switch
        {
            0 => "Item",
            1 => "Critter",
            2 => "Scenery",
            3 => "Wall",
            4 => "Tile",
            5 => "Misc",
            6 => "Interface",
            7 => "Inventory",
            8 => "Head",
            9 => "Background",
            10 => "Skilldex",
            _ => $"Type {PidTypeIdentifier}"
        },
        _ => $"PID type {PidTypeIdentifier}"
    };

    public string DisplayText =>
        $"#{RecordIndex} | Elev {ElevationGroup} | {ObjectTypeDisplay} | PID 0x{RawPid:X8} | Hex {FormatHexPosition()}";

    public string FormatHexPosition() =>
        HexX is int x && HexY is int y
            ? $"({x}, {y}) [{RawHexIndex}]"
            : $"{RawHexIndex} (unplaced/invalid)";

    public string Details =>
        $"Record #{RecordIndex} at byte {RecordOffset}"
        + Environment.NewLine
        + $"Type: {ObjectTypeDisplay} (PID type {PidTypeIdentifier}) | PID: 0x{RawPid:X8} | FID: 0x{RawFid:X8}"
        + Environment.NewLine
        + $"Hex index: {RawHexIndex} | Coordinates: {(HexX is int x && HexY is int y ? $"({x}, {y})" : "unavailable")} | Elevation group: {ElevationGroup} | Stored elevation: {StoredElevation}"
        + Environment.NewLine
        + $"Object id: {RawObjectId} | Flags: 0x{RawObjectFlags:X8} | CID: {RawCid} | Rotation: {RawRotation} | Frame: {RawFrame}"
        + Environment.NewLine
        + $"Offsets: ({RawOffsetX}, {RawOffsetY}) | Screen fields: ({RawScreenX}, {RawScreenY})"
        + Environment.NewLine
        + $"Light distance/intensity: {RawLightDistance}/{RawLightIntensity} | Field 0x74: {RawField74} | Script id/field: {RawScriptId}/{RawScriptField}"
        + Environment.NewLine
        + $"Inventory length/capacity/pointer: {RawInventoryLength}/{RawInventoryCapacity}/0x{RawInventoryPointer:X8}"
        + Environment.NewLine
        + $"Additional raw words: {(RawPayloadWords.Count == 0 ? "none" : string.Join(", ", RawPayloadWords.Select(value => $"0x{value:X8}")))}"
        + (Warning is null ? string.Empty : Environment.NewLine + $"Warning: {Warning}")
        + (UnsupportedReason is null ? string.Empty : Environment.NewLine + $"Unsupported: {UnsupportedReason}");
}

public sealed record MapObjectPlacementData(
    int ObjectSectionOffset,
    int TotalObjectCount,
    IReadOnlyList<int> ObjectCountsByElevation,
    IReadOnlyList<MapObjectPlacement> Objects,
    string? UnsupportedReason,
    int UnparsedByteCount)
{
    public bool IsComplete => UnsupportedReason is null && Objects.Count == TotalObjectCount;

    public string StatusDisplay =>
        UnsupportedReason is not null
            ? $"Parsed {Objects.Count} of {TotalObjectCount} top-level objects. {UnsupportedReason}"
                + (UnparsedByteCount == 0 ? string.Empty : $" {UnparsedByteCount} bytes after the parsed subset remain uninterpreted.")
            : $"Parsed {Objects.Count} of {TotalObjectCount} top-level objects."
                + (UnparsedByteCount == 0 ? string.Empty : $" {UnparsedByteCount} trailing bytes remain uninterpreted.");
}
