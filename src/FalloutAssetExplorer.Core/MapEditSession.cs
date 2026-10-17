namespace FalloutAssetExplorer.Core;

public readonly record struct MapEditObjectId(Guid SessionId, int RecordIndex);

public sealed record MapEditSessionObject(
    MapEditObjectId Id,
    MapObjectPlacement Placement);

public sealed class MapEditSession
{
    private readonly Dictionary<int, MapEditSessionObject> _objectsByRecordIndex;
    private readonly IReadOnlyList<MapEditSessionObject> _objects;

    public MapEditSession(
        MapSourceData sourceData,
        IReadOnlyList<MapObjectPlacement> sourcePlacements)
    {
        ArgumentNullException.ThrowIfNull(sourceData);
        ArgumentNullException.ThrowIfNull(sourcePlacements);

        SourceData = sourceData;
        SessionId = Guid.NewGuid();
        _objectsByRecordIndex = new Dictionary<int, MapEditSessionObject>(sourcePlacements.Count);
        var objects = new List<MapEditSessionObject>(sourcePlacements.Count);

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
            objects.Add(sessionObject);
        }

        _objects = Array.AsReadOnly(objects.ToArray());
    }

    public Guid SessionId { get; }

    public MapSourceData SourceData { get; }

    public IReadOnlyList<MapEditSessionObject> Objects => _objects;

    public MapEditSessionObject? GetObject(MapEditObjectId id) =>
        id.SessionId == SessionId ? GetObject(id.RecordIndex) : null;

    public MapEditSessionObject? GetObject(int recordIndex) =>
        _objectsByRecordIndex.TryGetValue(recordIndex, out var sessionObject)
            ? sessionObject
            : null;
}
