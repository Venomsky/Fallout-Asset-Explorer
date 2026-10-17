namespace FalloutAssetExplorer.Core;

public readonly record struct MapEditObjectId(Guid SessionId, int RecordIndex);

public sealed record MapEditSessionObject(
    MapEditObjectId Id,
    MapObjectPlacement Placement);

public sealed class MapEditSession
{
    private enum MapEditProperty
    {
        InventoryQuantity
    }

    private readonly record struct MapEditChange(
        MapEditObjectId ObjectId,
        MapEditProperty Property,
        int BeforeValue,
        int AfterValue);

    private readonly Dictionary<int, MapEditSessionObject> _objectsByRecordIndex;
    private readonly Dictionary<int, int> _objectIndexesByRecordIndex;
    private readonly Dictionary<int, int?> _initialInventoryQuantitiesByRecordIndex;
    private readonly HashSet<int> _dirtyInventoryQuantityRecordIndexes = [];
    private readonly List<MapEditSessionObject> _objects;
    private readonly IReadOnlyList<MapEditSessionObject> _readOnlyObjects;
    private readonly Stack<MapEditChange> _undoHistory = new();
    private readonly Stack<MapEditChange> _redoHistory = new();

    public MapEditSession(
        MapSourceData sourceData,
        IReadOnlyList<MapObjectPlacement> sourcePlacements)
    {
        ArgumentNullException.ThrowIfNull(sourceData);
        ArgumentNullException.ThrowIfNull(sourcePlacements);

        SourceData = sourceData;
        SessionId = Guid.NewGuid();
        _objectsByRecordIndex = new Dictionary<int, MapEditSessionObject>(sourcePlacements.Count);
        _objectIndexesByRecordIndex = new Dictionary<int, int>(sourcePlacements.Count);
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
            _initialInventoryQuantitiesByRecordIndex.Add(
                sourcePlacement.RecordIndex,
                workingPlacement.InventoryQuantity);
            _objects.Add(sessionObject);
        }

        _readOnlyObjects = _objects.AsReadOnly();
    }

    public Guid SessionId { get; }

    public MapSourceData SourceData { get; }

    public IReadOnlyList<MapEditSessionObject> Objects => _readOnlyObjects;

    public bool CanUndo => _undoHistory.Count > 0;

    public bool CanRedo => _redoHistory.Count > 0;

    public bool IsDirty => _dirtyInventoryQuantityRecordIndexes.Count > 0;

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
        return true;
    }

    private bool TryApplyHistoryChange(
        MapEditChange change,
        bool undo,
        out string? error)
    {
        if (change.Property != MapEditProperty.InventoryQuantity
            || GetObject(change.ObjectId) is not { } sessionObject
            || sessionObject.Placement.ParentRecordIndex is null
            || sessionObject.Placement.InventoryQuantity is not int currentQuantity)
        {
            error = "The history entry does not refer to an editable object in this MAP edit session.";
            return false;
        }

        var expectedValue = undo ? change.AfterValue : change.BeforeValue;
        if (currentQuantity != expectedValue)
        {
            error = "The current inventory quantity does not match the history entry.";
            return false;
        }

        var targetValue = undo ? change.BeforeValue : change.AfterValue;
        ApplyInventoryQuantity(change.ObjectId, sessionObject, targetValue);
        error = null;
        return true;
    }

    private void ApplyInventoryQuantity(
        MapEditObjectId id,
        MapEditSessionObject sessionObject,
        int quantity)
    {
        var updatedObject = sessionObject with
        {
            Placement = sessionObject.Placement with { InventoryQuantity = quantity }
        };
        _objectsByRecordIndex[id.RecordIndex] = updatedObject;
        _objects[_objectIndexesByRecordIndex[id.RecordIndex]] = updatedObject;

        if (_initialInventoryQuantitiesByRecordIndex[id.RecordIndex] == quantity)
        {
            _dirtyInventoryQuantityRecordIndexes.Remove(id.RecordIndex);
        }
        else
        {
            _dirtyInventoryQuantityRecordIndexes.Add(id.RecordIndex);
        }
    }
}
