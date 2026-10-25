using System.Buffers.Binary;

namespace FalloutAssetExplorer.Core;

public sealed record MapInventoryQuantityPatchEligibility(
    bool IsEligible,
    string Message);

public sealed record MapInventoryQuantityPatchResult(
    bool IsSuccess,
    string Message,
    byte[]? OutputBytes);

public static class MapInventoryQuantityPatchBuilder
{
    private const int SerializedQuantitySize = sizeof(int);

    public static MapInventoryQuantityPatchEligibility Validate(
        MapSourceData source,
        FalloutGame game,
        MapHeaderMetadata? header,
        MapTileGridData? tileGrids,
        MapObjectPlacementData? objectPlacements,
        MapEditSession? editSession)
    {
        ArgumentNullException.ThrowIfNull(source);

        return TryGetEdits(
            source,
            game,
            header,
            tileGrids,
            objectPlacements,
            editSession,
            out _,
            out var message)
                ? new MapInventoryQuantityPatchEligibility(true, message)
                : new MapInventoryQuantityPatchEligibility(false, message);
    }

    public static MapInventoryQuantityPatchResult Create(
        MapSourceData source,
        FalloutGame game,
        MapHeaderMetadata? header,
        MapTileGridData? tileGrids,
        MapObjectPlacementData? objectPlacements,
        MapEditSession? editSession)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!TryGetEdits(
                source,
                game,
                header,
                tileGrids,
                objectPlacements,
                editSession,
                out var edits,
                out var message))
        {
            return new MapInventoryQuantityPatchResult(false, message, null);
        }

        var output = source.GetRange(0, source.Length).ToArray();
        foreach (var edit in edits)
        {
            BinaryPrimitives.WriteInt32BigEndian(
                output.AsSpan(edit.ByteOffset!.Value, SerializedQuantitySize),
                edit.Quantity);
        }

        return new MapInventoryQuantityPatchResult(
            true,
            "Created a MAP copy with only supported inventory-quantity fields patched.",
            output);
    }

    private static bool TryGetEdits(
        MapSourceData source,
        FalloutGame game,
        MapHeaderMetadata? header,
        MapTileGridData? tileGrids,
        MapObjectPlacementData? objectPlacements,
        MapEditSession? editSession,
        out IReadOnlyList<MapInventoryQuantityEdit> edits,
        out string message)
    {
        edits = [];
        if (editSession is null)
        {
            message = "No source-bound MAP edit session is available.";
            return false;
        }

        var serializationEligibility = MapSerializationEligibility.Validate(
            source,
            game,
            header,
            tileGrids,
            objectPlacements,
            editSession);
        if (!serializationEligibility.IsEligible)
        {
            message = serializationEligibility.Message;
            return false;
        }

        if (editSession.HasDirtyHexTileIndexEdits)
        {
            message = "MAP Save As currently supports inventory-quantity edits only.";
            return false;
        }

        var quantityEdits = editSession.GetDirtyInventoryQuantityEdits();
        if (editSession.IsDirty && quantityEdits.Count == 0)
        {
            message = "The MAP edit session contains an unsupported dirty edit.";
            return false;
        }

        var sourcePlacementsByRecordIndex = new Dictionary<int, MapObjectPlacement>();
        if (quantityEdits.Count > 0)
        {
            if (objectPlacements?.SourceDecodedObjects is not { } sourceDecodedObjects)
            {
                message = "The MAP object records do not retain their source-decoded metadata.";
                return false;
            }

            sourcePlacementsByRecordIndex = sourceDecodedObjects
                .ToDictionary(placement => placement.RecordIndex);
        }

        var usedOffsets = new HashSet<int>();
        foreach (var edit in quantityEdits)
        {
            if (edit.Quantity < 1)
            {
                message = $"MAP record {edit.RecordIndex} has an inventory quantity outside the supported positive Int32 range.";
                return false;
            }

            if (edit.ByteOffset is not int offset
                || offset < 0
                || offset > source.Length - SerializedQuantitySize)
            {
                message = $"MAP record {edit.RecordIndex} has an invalid inventory-quantity byte offset.";
                return false;
            }

            if (editSession.GetObject(edit.RecordIndex)?.Placement is not
                {
                    ParentRecordIndex: not null
                } placement
                || offset != placement.RecordOffset - SerializedQuantitySize)
            {
                message = $"MAP record {edit.RecordIndex} inventory-quantity offset does not precede its decoded child record.";
                return false;
            }

            if (!sourcePlacementsByRecordIndex.TryGetValue(edit.RecordIndex, out var sourcePlacement)
                || placement.RecordOffset != sourcePlacement.RecordOffset
                || placement.ParentRecordIndex != sourcePlacement.ParentRecordIndex
                || placement.InventoryQuantityOffset != sourcePlacement.InventoryQuantityOffset
                || edit.ByteOffset != sourcePlacement.InventoryQuantityOffset
                || edit.OriginalQuantity != sourcePlacement.InventoryQuantity)
            {
                message = $"MAP record {edit.RecordIndex} source bytes or inventory-quantity metadata do not match the source decode.";
                return false;
            }

            if (!usedOffsets.Add(offset))
            {
                message = "Multiple MAP inventory edits refer to the same source byte range.";
                return false;
            }

            var sourceQuantity = BinaryPrimitives.ReadInt32BigEndian(
                source.GetRange(offset, SerializedQuantitySize).ToArray());
            if (sourceQuantity != edit.OriginalQuantity)
            {
                message = $"MAP record {edit.RecordIndex} source bytes do not match the decoded original inventory quantity.";
                return false;
            }
        }

        edits = quantityEdits;
        message = "The complete source-bound MAP is eligible for inventory-quantity Save As.";
        return true;
    }
}
