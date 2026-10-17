namespace FalloutAssetExplorer.Core;

public readonly record struct MapEditObjectId(Guid SessionId, int RecordIndex);

public sealed record MapEditSessionObject(
    MapEditObjectId Id,
    MapObjectPlacement Placement);

public sealed class MapEditSession
{
    private readonly Dictionary<int, MapEditSessionObject> _objectsByRecordIndex;
    private readonly Dictionary<int, int> _objectIndexesByRecordIndex;
    private readonly List<MapEditSessionObject> _objects;
    private readonly IReadOnlyList<MapEditSessionObject> _readOnlyObjects;

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
            _objects.Add(sessionObject);
        }

        _readOnlyObjects = _objects.AsReadOnly();
    }

    public Guid SessionId { get; }

    public MapSourceData SourceData { get; }

    public IReadOnlyList<MapEditSessionObject> Objects => _readOnlyObjects;

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
            || sessionObject.Placement.InventoryQuantity is null)
        {
            error = "The object has no decoded inventory quantity.";
            return false;
        }

        var updatedObject = sessionObject with
        {
            Placement = sessionObject.Placement with { InventoryQuantity = quantity }
        };
        _objectsByRecordIndex[id.RecordIndex] = updatedObject;
        _objects[_objectIndexesByRecordIndex[id.RecordIndex]] = updatedObject;
        error = null;
        return true;
    }
}
