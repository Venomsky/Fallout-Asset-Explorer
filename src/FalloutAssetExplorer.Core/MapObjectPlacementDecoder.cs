using System.Buffers.Binary;
using System.Text;

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
    private const int MaximumDecodedObjects = 100_000;
    private const int MaxInventoryDepth = 8;
    private const int MaxInventoryLengthPerObject = 1_000;
    private const int MaximumPrototypeListSize = 16 * 1024 * 1024;
    private const int MaximumPrototypeSize = 1024 * 1024;
    private const int MinimumPrototypeSize = 36;

    public static (int X, int Y) GetHexCoordinates(int serializedIndex)
    {
        if ((uint)serializedIndex >= HexGridTileCount)
        {
            throw new ArgumentOutOfRangeException(nameof(serializedIndex));
        }

        return (HexGridWidth - 1 - serializedIndex % HexGridWidth, serializedIndex / HexGridWidth);
    }

    public static MapObjectPlacementData Decode(ReadOnlyMemory<byte> data, MapHeaderMetadata header) =>
        DecodeAsync(data, header).GetAwaiter().GetResult();

    public static Task<MapObjectPlacementData> DecodeAsync(
        ReadOnlyMemory<byte> data,
        MapHeaderMetadata header,
        IGameArchive? archive = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(header);

        return DecodeCoreAsync(data, header, archive, cancellationToken);
    }

    private static async Task<MapObjectPlacementData> DecodeCoreAsync(
        ReadOnlyMemory<byte> data,
        MapHeaderMetadata header,
        IGameArchive? archive,
        CancellationToken cancellationToken)
    {
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

        var offset = checked((int)(validatedHeader.TileDataOffset + validatedHeader.TileDataLength));
        SkipScriptSection(data.Span, ref offset);

        var objectSectionOffset = offset;
        var totalObjectCount = ReadInt32(data.Span, ref offset, "total object count");
        if (totalObjectCount < 0 || totalObjectCount > MaximumTopLevelObjects)
        {
            throw new InvalidDataException(
                $"MAP total object count {totalObjectCount} is outside the supported range 0..{MaximumTopLevelObjects}.");
        }

        var objectCountsByElevation = new int[MapHeaderDecoder.ElevationCount];
        long countSum = 0;

        var remainingBytes = data.Length - offset;
        if ((long)totalObjectCount * MinimumNonCritterObjectIntCount * sizeof(int) > remainingBytes)
        {
            throw new InvalidDataException(
                $"MAP object records at byte {objectSectionOffset} cannot fit within the remaining entry data.");
        }

        var objects = new List<MapObjectPlacement>(totalObjectCount);
        var sceneryResolver = new PrototypeTypeResolver(archive, "scenery", "scenery.lst", 2, 5, "scenery");
        var itemResolver = new PrototypeTypeResolver(archive, "items", "items.lst", 0, 6, "item");
        string? unsupportedReason = null;
        for (var elevation = 0; elevation < objectCountsByElevation.Length; elevation++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = ReadInt32(data.Span, ref offset, $"elevation {elevation} object count");
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
                cancellationToken.ThrowIfCancellationRequested();
                if (objects.Count >= MaximumDecodedObjects)
                {
                    unsupportedReason =
                        $"MAP decoded object limit {MaximumDecodedObjects} was reached before top-level record {objects.Count}.";
                    return new MapObjectPlacementData(
                        objectSectionOffset,
                        totalObjectCount,
                        objectCountsByElevation,
                        objects,
                        unsupportedReason,
                        data.Length - offset);
                }

                var objectResult = await ReadObjectTreeAsync(
                    data,
                    elevation,
                    validatedHeader,
                    sceneryResolver,
                    itemResolver,
                    objects,
                    0,
                    null,
                    null,
                    new MapReadCursor(offset),
                    cancellationToken);
                offset = objectResult.Offset;
                if (objectResult.UnsupportedReason is not null)
                {
                    unsupportedReason = objectResult.UnsupportedReason;
                    return new MapObjectPlacementData(
                        objectSectionOffset,
                        totalObjectCount,
                        objectCountsByElevation,
                        objects,
                        unsupportedReason,
                        data.Length - offset);
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
            data.Length - offset);
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

    private static async Task<(
        string? UnsupportedReason,
        int Offset)> ReadObjectTreeAsync(
        ReadOnlyMemory<byte> data,
        int elevationGroup,
        MapHeaderMetadata header,
        PrototypeTypeResolver sceneryResolver,
        PrototypeTypeResolver itemResolver,
        List<MapObjectPlacement> objects,
        int inventoryDepth,
        int? parentRecordIndex,
        int? inventoryQuantity,
        MapReadCursor cursor,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var recordIndex = objects.Count;
        var recordOffset = cursor.Offset;
        var rawHeader = new int[ObjectBaseIntCount];
        for (var index = 0; index < rawHeader.Length; index++)
        {
            rawHeader[index] = ReadInt32(data.Span, cursor, $"object record {recordIndex} base field {index}");
        }

        var rawInventoryLength = ReadInt32(data.Span, cursor, $"object record {recordIndex} inventory length");
        var rawInventoryCapacity = ReadInt32(data.Span, cursor, $"object record {recordIndex} inventory capacity");
        var rawInventoryPointer = ReadInt32(data.Span, cursor, $"object record {recordIndex} inventory pointer");

        var rawPid = rawHeader[11];
        var pidType = (int)((uint)rawPid >> 24);
        var payloadWords = new List<int>();
        string? unsupportedReason = null;

        if (unsupportedReason is null)
        {
            if (pidType == 1)
            {
                ReadInt32Words(data.Span, cursor, payloadWords, 11, $"object record {recordIndex} critter payload");
            }
            else
            {
                payloadWords.Add(ReadInt32(data.Span, cursor, $"object record {recordIndex} data flags"));
                if (pidType == 0)
                {
                    if (header.Game == FalloutGame.Fallout1)
                    {
                        unsupportedReason = $"Object record {recordIndex} PID type Item requires Fallout 2 PRO subtype data to determine its record layout.";
                    }
                    else
                    {
                        var itemType = await itemResolver.ResolveAsync(rawPid, cancellationToken);
                        if (itemType.Type is int resolvedType)
                        {
                            var payloadWordCount = resolvedType switch
                            {
                                0 or 1 or 2 => 0,
                                3 => 2,
                                4 or 5 or 6 => 1,
                                _ => 0
                            };
                            ReadInt32Words(
                                data.Span,
                                cursor,
                                payloadWords,
                                payloadWordCount,
                                $"object record {recordIndex} item subtype {resolvedType} payload");
                        }
                        else
                        {
                            unsupportedReason = $"Object record {recordIndex} item PRO metadata is unavailable: {itemType.Error}";
                        }
                    }
                }
                else if (pidType == 2 && header.Game == FalloutGame.Fallout1)
                {
                    unsupportedReason = $"Object record {recordIndex} PID type Scenery requires PRO subtype data to determine its record layout.";
                }
                else if (pidType == 2)
                {
                    var sceneryType = await sceneryResolver.ResolveAsync(rawPid, cancellationToken);

                    if (sceneryType.Type == 0)
                    {
                        ReadInt32Words(
                            data.Span,
                            cursor,
                            payloadWords,
                            1,
                            $"object record {recordIndex} scenery subtype 0 payload");
                    }
                    else if (sceneryType.Type == 1)
                    {
                        ReadInt32Words(
                            data.Span,
                            cursor,
                            payloadWords,
                            2,
                            $"object record {recordIndex} scenery subtype 1 payload");
                    }
                    else if (sceneryType.Type == 2)
                    {
                        ReadInt32Words(
                            data.Span,
                            cursor,
                            payloadWords,
                            2,
                            $"object record {recordIndex} scenery subtype 2 payload");
                    }
                    else if (sceneryType.Type is 3 or 4)
                    {
                        var payloadWordCount = header.Version == 19 ? 1 : 2;
                        ReadInt32Words(
                            data.Span,
                            cursor,
                            payloadWords,
                            payloadWordCount,
                            $"object record {recordIndex} scenery subtype {sceneryType.Type} payload");
                    }
                    else if (sceneryType.Type == 5)
                    {
                        // GENERIC scenery has no subtype-specific MAP payload.
                    }
                    else if (sceneryType.Type is not null)
                    {
                        unsupportedReason = $"Object record {recordIndex} scenery subtype {sceneryType.Type} is not supported.";
                    }
                    else
                    {
                        unsupportedReason = $"Object record {recordIndex} scenery PRO metadata is unavailable: {sceneryType.Error}";
                    }
                }
                else if (pidType == 5 && rawPid is >= 0x05000010 and <= 0x05000017)
                {
                    ReadInt32Words(data.Span, cursor, payloadWords, 4, $"object record {recordIndex} exit-grid payload");
                }
                else if (pidType is < 0 or > 10)
                {
                    unsupportedReason = $"Object record {recordIndex} has unsupported PID type identifier {pidType}.";
                }
            }
        }

        if (unsupportedReason is null)
        {
            if (rawInventoryLength < 0 || rawInventoryCapacity < 0)
            {
                unsupportedReason = $"Object record {recordIndex} has negative inventory metadata.";
            }
            else if (rawInventoryLength > rawInventoryCapacity)
            {
                unsupportedReason =
                    $"Object record {recordIndex} inventory length {rawInventoryLength} exceeds capacity {rawInventoryCapacity}.";
            }
            else if (rawInventoryLength > MaxInventoryLengthPerObject)
            {
                unsupportedReason =
                    $"Object record {recordIndex} inventory length {rawInventoryLength} exceeds the supported limit {MaxInventoryLengthPerObject}.";
            }
            else if (rawInventoryLength > 0 && inventoryDepth >= MaxInventoryDepth)
            {
                unsupportedReason =
                    $"Object record {recordIndex} inventory exceeds the maximum supported depth {MaxInventoryDepth}.";
            }
            else if (rawInventoryLength > 0 && objects.Count >= MaximumDecodedObjects - 1)
            {
                unsupportedReason =
                    $"MAP decoded object limit {MaximumDecodedObjects} would be exceeded by inventory in record {recordIndex}.";
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
            unsupportedReason)
        {
            ParentRecordIndex = parentRecordIndex,
            InventoryDepth = inventoryDepth,
            InventoryQuantity = inventoryQuantity
        };
        objects.Add(placement);

        if (unsupportedReason is not null || rawInventoryLength == 0)
        {
            return (unsupportedReason, cursor.Offset);
        }

        for (var inventoryIndex = 0; inventoryIndex < rawInventoryLength; inventoryIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (objects.Count >= MaximumDecodedObjects)
            {
                unsupportedReason =
                    $"MAP decoded object limit {MaximumDecodedObjects} was reached in inventory of record {recordIndex}.";
                break;
            }

            int quantity;
            try
            {
                quantity = ReadInt32(
                    data.Span,
                    cursor,
                    $"object record {recordIndex} inventory child {inventoryIndex} quantity");
            }
            catch (InvalidDataException exception)
            {
                unsupportedReason =
                    $"Object record {recordIndex} inventory child {inventoryIndex} quantity could not be read: {exception.Message}";
                break;
            }

            var childOffset = cursor.Offset;
            (string? UnsupportedReason, int Offset) childResult;
            try
            {
                childResult = await ReadObjectTreeAsync(
                    data,
                    elevationGroup,
                    header,
                    sceneryResolver,
                    itemResolver,
                    objects,
                    inventoryDepth + 1,
                    recordIndex,
                    quantity,
                    cursor,
                    cancellationToken);
            }
            catch (InvalidDataException exception)
            {
                unsupportedReason =
                    $"Object record {recordIndex} inventory child {inventoryIndex} at byte {childOffset} could not be decoded: {exception.Message}";
                break;
            }

            if (childResult.UnsupportedReason is not null)
            {
                unsupportedReason = childResult.UnsupportedReason;
                break;
            }
        }

        return (unsupportedReason, cursor.Offset);
    }

    private sealed class PrototypeTypeResolver(
        IGameArchive? archive,
        string prototypeDirectory,
        string listFileName,
        int expectedPidType,
        int maximumSubtype,
        string prototypeCategory)
    {
        private Task<(byte[]? Bytes, string? Error)>? _listTask;
        private readonly Dictionary<int, Task<(int? Type, string? Error)>> _results = [];

        public Task<(int? Type, string? Error)> ResolveAsync(int pid, CancellationToken cancellationToken)
        {
            if (_results.TryGetValue(pid, out var result))
            {
                return result;
            }

            result = ResolveCoreAsync(pid, cancellationToken);
            _results.Add(pid, result);
            return result;
        }

        private async Task<(int? Type, string? Error)> ResolveCoreAsync(
            int pid,
            CancellationToken cancellationToken)
        {
            if (archive is null
                || archive.Game != FalloutGame.Fallout2
                || archive.Format != ArchiveFormat.Dat2)
            {
                return (null, "the active Fallout 2 DAT2 archive is unavailable");
            }

            _listTask ??= ReadPrototypeListAsync(
                archive,
                prototypeDirectory,
                listFileName,
                prototypeCategory,
                cancellationToken);
            var listResult = await _listTask;
            if (listResult.Bytes is null)
            {
                return (null, listResult.Error);
            }

            var listIndex = pid & 0x00ffffff;
            if (listIndex == 0)
            {
                return (null, $"the {prototypeCategory} PID has an invalid zero list index");
            }

            var lineIndex = 0;
            string? prototypeName = null;
            using (var reader = new StringReader(Encoding.ASCII.GetString(listResult.Bytes)))
            {
                while (reader.ReadLine() is { } line)
                {
                    lineIndex++;
                    if (lineIndex == listIndex)
                    {
                        prototypeName = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(prototypeName))
            {
                return (null, $"{prototypeCategory} PID list index {listIndex} is outside the prototype list");
            }

            if (prototypeName.IndexOfAny(['/', '\\', ':']) >= 0
                || prototypeName is "." or ".."
                || !string.Equals(Path.GetExtension(prototypeName), ".PRO", StringComparison.OrdinalIgnoreCase))
            {
                return (null, $"the {prototypeCategory} prototype list contains an unsafe or invalid filename");
            }

            var prototypeEntry = FindArchiveEntry(archive, $"proto/{prototypeDirectory}/{prototypeName}");
            if (prototypeEntry is null)
            {
                return (null, $"the {prototypeCategory} prototype '{prototypeName}' is missing");
            }

            if (!TryReadEntrySize(prototypeEntry, MaximumPrototypeSize, out var prototypeSize, out var sizeError))
            {
                return (null, $"the {prototypeCategory} prototype {sizeError}");
            }

            if (prototypeSize < MinimumPrototypeSize)
            {
                return (null, $"the {prototypeCategory} prototype is shorter than {MinimumPrototypeSize} bytes");
            }

            byte[] prototypeBytes;
            try
            {
                prototypeBytes = await ReadArchiveEntryAsync(archive, prototypeEntry, prototypeSize, cancellationToken);
            }
            catch (InvalidDataException exception)
            {
                System.Diagnostics.Debug.WriteLine($"MAP {prototypeCategory} prototype read failed: {exception}");
                return (null, $"the {prototypeCategory} prototype could not be read");
            }
            catch (IOException exception)
            {
                System.Diagnostics.Debug.WriteLine($"MAP {prototypeCategory} prototype read failed: {exception}");
                return (null, $"the {prototypeCategory} prototype could not be read");
            }

            var prototypePid = BinaryPrimitives.ReadInt32BigEndian(prototypeBytes.AsSpan(0, sizeof(int)));
            if (prototypePid != pid || (uint)prototypePid >> 24 != expectedPidType)
            {
                return (null, $"the {prototypeCategory} prototype PID 0x{prototypePid:X8} does not match MAP PID 0x{pid:X8}");
            }

            var prototypeType = BinaryPrimitives.ReadInt32BigEndian(prototypeBytes.AsSpan(32, sizeof(int)));
            return prototypeType is >= 0 && prototypeType <= maximumSubtype
                ? (prototypeType, null)
                : (null, $"the {prototypeCategory} prototype has unsupported subtype {prototypeType}");
        }

        private static async Task<(byte[]? Bytes, string? Error)> ReadPrototypeListAsync(
            IGameArchive archive,
            string prototypeDirectory,
            string listFileName,
            string prototypeCategory,
            CancellationToken cancellationToken)
        {
            var listPath = $"proto/{prototypeDirectory}/{listFileName}";
            var listEntry = FindArchiveEntry(archive, listPath);
            if (listEntry is null)
            {
                return (null, $"{listPath} is missing");
            }

            if (!TryReadEntrySize(listEntry, MaximumPrototypeListSize, out var listSize, out var sizeError))
            {
                return (null, $"the {prototypeCategory} prototype list {sizeError}");
            }

            byte[] listBytes;
            try
            {
                listBytes = await ReadArchiveEntryAsync(archive, listEntry, listSize, cancellationToken);
            }
            catch (InvalidDataException exception)
            {
                System.Diagnostics.Debug.WriteLine($"MAP {prototypeCategory} prototype list read failed: {exception}");
                return (null, $"the {prototypeCategory} prototype list could not be read");
            }
            catch (IOException exception)
            {
                System.Diagnostics.Debug.WriteLine($"MAP {prototypeCategory} prototype list read failed: {exception}");
                return (null, $"the {prototypeCategory} prototype list could not be read");
            }

            return listBytes.Any(value => value > 0x7f)
                ? (null, $"the {prototypeCategory} prototype list contains non-ASCII data")
                : (listBytes, null);
        }
    }

    private static ArchiveEntry? FindArchiveEntry(IGameArchive archive, string expectedPath)
    {
        ArchiveEntry? match = null;
        foreach (var entry in archive.BrowserEntries)
        {
            if (!string.Equals(
                    entry.Path.Replace('\\', '/'),
                    expectedPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (match is not null)
            {
                return null;
            }

            match = entry;
        }

        return match;
    }

    private static bool TryReadEntrySize(
        ArchiveEntry entry,
        int maximumSize,
        out int size,
        out string error)
    {
        if (entry.UncompressedSize < 0 || entry.UncompressedSize > maximumSize)
        {
            size = 0;
            error = $"has an invalid size of {entry.UncompressedSize} bytes";
            return false;
        }

        size = (int)entry.UncompressedSize;
        error = string.Empty;
        return true;
    }

    private static async Task<byte[]> ReadArchiveEntryAsync(
        IGameArchive archive,
        ArchiveEntry entry,
        int expectedSize,
        CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream(expectedSize);
        await archive.CopyEntryToAsync(entry, stream, cancellationToken);
        if (stream.Length != expectedSize)
        {
            throw new InvalidDataException(
                $"Archive entry '{entry.Path}' declared {expectedSize} bytes but yielded {stream.Length}.");
        }

        return stream.ToArray();
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

    private static void ReadInt32Words(
        ReadOnlySpan<byte> bytes,
        MapReadCursor cursor,
        List<int> destination,
        int count,
        string description)
    {
        for (var index = 0; index < count; index++)
        {
            destination.Add(ReadInt32(bytes, cursor, $"{description} field {index}"));
        }
    }

    private static int ReadInt32(ReadOnlySpan<byte> bytes, MapReadCursor cursor, string description)
    {
        var offset = cursor.Offset;
        var value = ReadInt32(bytes, ref offset, description);
        cursor.Offset = offset;
        return value;
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

    private sealed class MapReadCursor(int offset)
    {
        public int Offset { get; set; } = offset;
    }

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
    public int? ParentRecordIndex { get; init; }

    public int InventoryDepth { get; init; }

    public int? InventoryQuantity { get; init; }

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
        + $"Inventory relation: {(ParentRecordIndex is int parent ? $"child of record #{parent}" : "top-level")} | Depth: {InventoryDepth}"
            + (InventoryQuantity is int quantity ? $" | Quantity: {quantity}" : string.Empty)
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
    public int DecodedTopLevelObjectCount => Objects.Count(placement => placement.ParentRecordIndex is null);

    public bool IsComplete =>
        UnsupportedReason is null && DecodedTopLevelObjectCount == TotalObjectCount;

    public string StatusDisplay =>
        UnsupportedReason is not null
            ? $"Decoded {Objects.Count} records, including {DecodedTopLevelObjectCount} of {TotalObjectCount} top-level objects. {UnsupportedReason}"
                + (UnparsedByteCount == 0 ? string.Empty : $" {UnparsedByteCount} bytes after the parsed subset remain uninterpreted.")
            : $"Decoded {Objects.Count} records, including {DecodedTopLevelObjectCount} top-level objects."
                + (UnparsedByteCount == 0 ? string.Empty : $" {UnparsedByteCount} trailing bytes remain uninterpreted.");
}
