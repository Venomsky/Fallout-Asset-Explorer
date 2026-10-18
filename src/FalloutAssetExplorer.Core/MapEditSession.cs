namespace FalloutAssetExplorer.Core;

public readonly record struct MapEditObjectId(Guid SessionId, int RecordIndex);

public sealed record MapEditSessionObject(
    MapEditObjectId Id,
    MapObjectPlacement Placement);

public sealed class MapEditSession
{
    private sealed record SourceBoundData(
        MapSourceData SourceData,
        IReadOnlyList<MapObjectPlacement> Placements,
        MapDecodeProvenance Provenance,
        MapObjectPlacementData DecodedPlacements);

    private enum MapEditProperty
    {
        InventoryQuantity,
        HexTileIndex
    }

    private readonly record struct MapEditChange(
        MapEditObjectId ObjectId,
        MapEditProperty Property,
        int BeforeValue,
        int AfterValue);

    private readonly Dictionary<int, MapEditSessionObject> _objectsByRecordIndex;
    private readonly Dictionary<int, int> _objectIndexesByRecordIndex;
    private readonly Dictionary<int, int> _initialHexTileIndexesByRecordIndex;
    private readonly Dictionary<int, int?> _initialInventoryQuantitiesByRecordIndex;
    private readonly HashSet<int> _dirtyHexTileIndexes = [];
    private readonly HashSet<int> _dirtyInventoryQuantityRecordIndexes = [];
    private readonly List<MapEditSessionObject> _objects;
    private readonly IReadOnlyList<MapEditSessionObject> _readOnlyObjects;
    private readonly Stack<MapEditChange> _undoHistory = new();
    private readonly Stack<MapEditChange> _redoHistory = new();

    public MapEditSession(
        MapSourceData sourceData,
        IReadOnlyList<MapObjectPlacement> sourcePlacements)
        : this(sourceData, sourcePlacements, null, null)
    {
    }

    public MapEditSession(
        MapHeaderMetadata header,
        MapObjectPlacementData decodedPlacements)
        : this(GetSourceBoundData(header, decodedPlacements))
    {
    }

    private MapEditSession(SourceBoundData sourceBoundData)
        : this(
            sourceBoundData.SourceData,
            sourceBoundData.Placements,
            sourceBoundData.Provenance,
            sourceBoundData.DecodedPlacements)
    {
    }

    private MapEditSession(
        MapSourceData sourceData,
        IReadOnlyList<MapObjectPlacement> sourcePlacements,
        MapDecodeProvenance? provenance,
        MapObjectPlacementData? decodedPlacements)
    {
        ArgumentNullException.ThrowIfNull(sourceData);
        ArgumentNullException.ThrowIfNull(sourcePlacements);

        SourceData = sourceData;
        Provenance = provenance;
        DecodedObjectPlacements = decodedPlacements;
        SessionId = Guid.NewGuid();
        _objectsByRecordIndex = new Dictionary<int, MapEditSessionObject>(sourcePlacements.Count);
        _objectIndexesByRecordIndex = new Dictionary<int, int>(sourcePlacements.Count);
        _initialHexTileIndexesByRecordIndex = new Dictionary<int, int>(sourcePlacements.Count);
        _initialInventoryQuantitiesByRecordIndex = new Dictionary<int, int?>(sourcePlacements.Count);
        _objects = new List<MapEditSessionObject>(sourcePlacements.Count);

        foreach (var sourcePlacement in sourcePlacements)
        {
            ArgumentNullException.ThrowIfNull(sourcePlacement);
            ArgumentNullException.ThrowIfNull(sourcePlacement.RawPayloadWords);
            if (_objectsByRecordIndex.ContainsKey(sourcePlacement.RecordIndex))
            {
                throw new ArgumentException(
                    $"MAP record index {sourcePlacement.RecordIndex} occurs more than once.",
                    nameof(sourcePlacements));
            }

            var workingPlacement = sourcePlacement with
            {
                RawPayloadWords = Array.AsReadOnly(sourcePlacement.RawPayloadWords.ToArray())
            };
            var sessionObject = new MapEditSessionObject(
                new MapEditObjectId(SessionId, sourcePlacement.RecordIndex),
                workingPlacement);
            _objectsByRecordIndex.Add(sourcePlacement.RecordIndex, sessionObject);
            _objectIndexesByRecordIndex.Add(sourcePlacement.RecordIndex, _objects.Count);
            _initialHexTileIndexesByRecordIndex.Add(
                sourcePlacement.RecordIndex,
                workingPlacement.RawHexIndex);
            _initialInventoryQuantitiesByRecordIndex.Add(
                sourcePlacement.RecordIndex,
                workingPlacement.InventoryQuantity);
            _objects.Add(sessionObject);
        }

        _readOnlyObjects = _objects.AsReadOnly();
    }

    public Guid SessionId { get; }

    public MapSourceData SourceData { get; }

    public MapDecodeProvenance? Provenance { get; }

    internal MapObjectPlacementData? DecodedObjectPlacements { get; }

    public event EventHandler? ObjectsChanged;

    public IReadOnlyList<MapEditSessionObject> Objects => _readOnlyObjects;

    public bool CanUndo => _undoHistory.Count > 0;

    public bool CanRedo => _redoHistory.Count > 0;

    public bool IsDirty =>
        _dirtyInventoryQuantityRecordIndexes.Count > 0
        || _dirtyHexTileIndexes.Count > 0;

    public MapEditSessionObject? GetObject(MapEditObjectId id) =>
        id.SessionId == SessionId ? GetObject(id.RecordIndex) : null;

    public MapEditSessionObject? GetObject(int recordIndex) =>
        _objectsByRecordIndex.TryGetValue(recordIndex, out var sessionObject)
            ? sessionObject
            : null;

    public bool TrySetInventoryQuantity(
        MapEditObjectId id,
        int quantity,
        out string? error)
    {
        if (quantity < 1)
        {
            error = "Inventory quantity must be at least 1.";
            return false;
        }

        if (GetObject(id) is not { } sessionObject)
        {
            error = "The object does not belong to this MAP edit session.";
            return false;
        }

        if (sessionObject.Placement.ParentRecordIndex is null
            || sessionObject.Placement.InventoryQuantity is not int currentQuantity)
        {
            error = "The object has no decoded inventory quantity.";
            return false;
        }

        if (currentQuantity == quantity)
        {
            error = null;
            return true;
        }

        var change = new MapEditChange(
            id,
            MapEditProperty.InventoryQuantity,
            currentQuantity,
            quantity);
        ApplyInventoryQuantity(id, sessionObject, quantity);
        _undoHistory.Push(change);
        _redoHistory.Clear();
        ObjectsChanged?.Invoke(this, EventArgs.Empty);
        error = null;
        return true;
    }

    public bool TrySetHexTileIndex(
        MapEditObjectId id,
        int hexTileIndex,
        out string? error)
    {
        if ((uint)hexTileIndex >= MapObjectPlacementDecoder.HexGridTileCount)
        {
            error =
                $"Hex tile index must be in the range 0..{MapObjectPlacementDecoder.HexGridTileCount - 1}.";
            return false;
        }

        if (GetObject(id) is not { } sessionObject)
        {
            error = "The object does not belong to this MAP edit session.";
            return false;
        }

        if (sessionObject.Placement.ParentRecordIndex is not null
            || !HasConsistentHexTileIndex(sessionObject.Placement))
        {
            error = "Only top-level objects with a valid, consistent source hex tile can be moved.";
            return false;
        }

        var currentIndex = sessionObject.Placement.RawHexIndex;
        if (currentIndex == hexTileIndex)
        {
            error = null;
            return true;
        }

        var change = new MapEditChange(
            id,
            MapEditProperty.HexTileIndex,
            currentIndex,
            hexTileIndex);
        ApplyHexTileIndex(id, sessionObject, hexTileIndex);
        _undoHistory.Push(change);
        _redoHistory.Clear();
        ObjectsChanged?.Invoke(this, EventArgs.Empty);
        error = null;
        return true;
    }

    public bool Undo(out string? error)
    {
        if (!_undoHistory.TryPeek(out var change))
        {
            error = null;
            return false;
        }

        if (!TryApplyHistoryChange(change, undo: true, out error))
        {
            return false;
        }

        _undoHistory.Pop();
        _redoHistory.Push(change);
        ObjectsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Redo(out string? error)
    {
        if (!_redoHistory.TryPeek(out var change))
        {
            error = null;
            return false;
        }

        if (!TryApplyHistoryChange(change, undo: false, out error))
        {
            return false;
        }

        _redoHistory.Pop();
        _undoHistory.Push(change);
        ObjectsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private bool TryApplyHistoryChange(
        MapEditChange change,
        bool undo,
        out string? error)
    {
        if (GetObject(change.ObjectId) is not { } sessionObject
            || GetHistoryValue(change.Property, sessionObject.Placement) is not int currentValue)
        {
            error = "The history entry does not refer to an editable object in this MAP edit session.";
            return false;
        }

        var expectedValue = undo ? change.AfterValue : change.BeforeValue;
        if (currentValue != expectedValue)
        {
            error = "The current property value does not match the history entry.";
            return false;
        }

        var targetValue = undo ? change.BeforeValue : change.AfterValue;
        switch (change.Property)
        {
            case MapEditProperty.InventoryQuantity:
                ApplyInventoryQuantity(change.ObjectId, sessionObject, targetValue);
                break;
            case MapEditProperty.HexTileIndex:
                ApplyHexTileIndex(change.ObjectId, sessionObject, targetValue);
                break;
            default:
                error = "The history entry refers to an unsupported property.";
                return false;
        }

        error = null;
        return true;
    }

    private static int? GetHistoryValue(
        MapEditProperty property,
        MapObjectPlacement placement) =>
        property switch
        {
            MapEditProperty.InventoryQuantity when placement.ParentRecordIndex is not null =>
                placement.InventoryQuantity,
            MapEditProperty.HexTileIndex
                when placement.ParentRecordIndex is null && HasConsistentHexTileIndex(placement) =>
                placement.RawHexIndex,
            _ => null
        };

    private static bool HasConsistentHexTileIndex(MapObjectPlacement placement)
    {
        if ((uint)placement.RawHexIndex >= MapObjectPlacementDecoder.HexGridTileCount
            || placement.HexX is not int hexX
            || placement.HexY is not int hexY)
        {
            return false;
        }

        return MapObjectPlacementDecoder.GetHexCoordinates(placement.RawHexIndex) == (hexX, hexY);
    }

    private void ApplyInventoryQuantity(
        MapEditObjectId id,
        MapEditSessionObject sessionObject,
        int quantity)
    {
        ReplaceObject(
            id,
            sessionObject,
            sessionObject.Placement with { InventoryQuantity = quantity });

        if (_initialInventoryQuantitiesByRecordIndex[id.RecordIndex] == quantity)
        {
            _dirtyInventoryQuantityRecordIndexes.Remove(id.RecordIndex);
        }
        else
        {
            _dirtyInventoryQuantityRecordIndexes.Add(id.RecordIndex);
        }
    }

    private void ApplyHexTileIndex(
        MapEditObjectId id,
        MapEditSessionObject sessionObject,
        int hexTileIndex)
    {
        var (hexX, hexY) = MapObjectPlacementDecoder.GetHexCoordinates(hexTileIndex);
        ReplaceObject(
            id,
            sessionObject,
            sessionObject.Placement with
            {
                RawHexIndex = hexTileIndex,
                HexX = hexX,
                HexY = hexY
            });

        if (_initialHexTileIndexesByRecordIndex[id.RecordIndex] == hexTileIndex)
        {
            _dirtyHexTileIndexes.Remove(id.RecordIndex);
        }
        else
        {
            _dirtyHexTileIndexes.Add(id.RecordIndex);
        }
    }

    private void ReplaceObject(
        MapEditObjectId id,
        MapEditSessionObject sessionObject,
        MapObjectPlacement placement)
    {
        var updatedObject = sessionObject with
        {
            Placement = placement
        };
        _objectsByRecordIndex[id.RecordIndex] = updatedObject;
        _objects[_objectIndexesByRecordIndex[id.RecordIndex]] = updatedObject;
    }

    private static SourceBoundData GetSourceBoundData(
        MapHeaderMetadata header,
        MapObjectPlacementData decodedPlacements)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(decodedPlacements);

        if (header.SourceData is not { } sourceData
            || !ReferenceEquals(decodedPlacements.SourceData, sourceData)
            || header.Provenance is not { } provenance
            || !ReferenceEquals(decodedPlacements.Provenance, provenance)
            || provenance.SourceSnapshotId != sourceData.SnapshotId
            || provenance.Game != header.Game
            || provenance.Version != header.Version)
        {
            throw new ArgumentException(
                "The MAP header and object placements must share source-bound decode provenance.",
                nameof(decodedPlacements));
        }

        return new SourceBoundData(
            sourceData,
            decodedPlacements.Objects,
            provenance,
            decodedPlacements);
    }
}
