using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using FalloutAssetExplorer.Core;

namespace FalloutAssetExplorer.App.ViewModels;

public sealed class MapInspectorViewModel : INotifyPropertyChanged
{
    private const string DefaultMapObjectPrototypeDiagnosticStatus =
        "Select an Item or Scenery object to inspect its Fallout 2 prototype.";

    public const int SquareTilesLayerIndex = 0;
    public const int ObjectHexesLayerIndex = 1;
    public const int FloorTilesLayerIndex = 2;
    public const int IsometricFloorTilesLayerIndex = 3;
    public const int IsometricObjectMarkersLayerIndex = 4;

    private static readonly MapObjectTypeFilterOption AllMapObjectTypes = new("All types", null);
    private static readonly MapObjectListSortOption DefaultMapObjectListSort =
        new("Record index (asc)", MapObjectListSortMode.RecordIndexAscending);

    private IReadOnlyList<MapObjectPlacement> _sourceMapObjectPlacements = [];
    private IReadOnlyList<MapObjectPlacement> _mapObjectPlacements = [];
    private bool _isMapInspector;
    private int? _selectedMapElevationIndex;
    private int _selectedMapDiagnosticLayerIndex;
    private string _mapObjectSearchText = string.Empty;
    private MapObjectTypeFilterOption _selectedMapObjectTypeFilter = AllMapObjectTypes;
    private MapObjectListSortOption _selectedMapObjectListSort = DefaultMapObjectListSort;
    private IReadOnlyList<MapObjectPlacement> _filteredMapObjectPlacements = [];
    private bool _isReorderingSortedMapObjectPlacements;
    private bool _sortedListReportedTransientSelectionClear;
    private MapObjectPlacement? _selectedMapObject;
    private MapEditObjectId? _selectedMapEditObjectId;
    private MapEditSession? _mapEditSession;
    private FalloutGame? _mapSaveGame;
    private MapHeaderMetadata? _mapSaveHeader;
    private MapTileGridData? _mapSaveTileGrids;
    private MapObjectPlacementData? _mapSaveObjectPlacements;
    private string? _mapSaveSourceArchivePath;
    private long _mapEditRevision;
    private Guid? _savedMapSessionId;
    private long? _savedMapEditRevision;
    private string? _savedMapOutputPath;
    private string? _mapSaveFailureMessage;
    private long? _mapSaveFailureRevision;
    private string _inventoryQuantityInput = string.Empty;
    private string? _inventoryQuantityValidationError;
    private string _mapObjectDetailsDisplay = "Select a parsed object to inspect its raw fields.";
    private string _mapObjectPrototypeDiagnosticStatus = DefaultMapObjectPrototypeDiagnosticStatus;
    private string _mapObjectPrototypeDiagnosticDetails = string.Empty;
    private string _mapObjectPrototypeSceneryFieldsDisplay = string.Empty;
    private string _mapObjectPrototypeItemFieldsDisplay = string.Empty;
    private IReadOnlyList<string> _mapObjectPrototypeRawByteLines = [];

    public sealed record MapObjectTypeFilterOption(string DisplayName, int? PidTypeIdentifier);

    public enum MapObjectListSortMode
    {
        RecordIndexAscending,
        RecordIndexDescending,
        PidAscending,
        PidDescending,
        ObjectTypeAscending,
        HexCoordinatesAscending,
        HexCoordinatesDescending,
        ByteOffsetAscending,
        ByteOffsetDescending
    }

    public sealed record MapObjectListSortOption(string DisplayName, MapObjectListSortMode SortMode);

    public enum MapObjectSelectionSource
    {
        List,
        FloorMarker,
        IsometricMarker,
        Programmatic
    }

    public sealed class MapObjectSelectionChangedEventArgs(
        MapObjectPlacement? placement,
        MapObjectSelectionSource source) : EventArgs
    {
        public MapObjectPlacement? Placement { get; } = placement;

        public MapObjectSelectionSource Source { get; } = source;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<MapObjectSelectionChangedEventArgs>? SelectedMapObjectChanged;

    public ObservableCollection<int> MapElevationOptions { get; } = [];

    public bool IsMapInspector => _isMapInspector;

    public IReadOnlyList<string> MapDiagnosticLayerOptions { get; } =
    [
        "Square tiles 100 x 100",
        "Object hexes 200 x 200",
        "Floor tiles 100 x 100",
        "Floor tiles isometric (experimental)",
        "Object markers isometric"
    ];

    public int? SelectedMapElevationIndex
    {
        get => _selectedMapElevationIndex;
        set
        {
            if (value is not null && !MapElevationOptions.Contains(value.Value))
            {
                return;
            }

            if (SetProperty(ref _selectedMapElevationIndex, value))
            {
                OnPropertyChanged(nameof(IsMapHexGridEmpty));
                if (_selectedMapObject is not null
                    && _selectedMapObject.ElevationGroup != value)
                {
                    ClearSelectedMapObject();
                }
            }
        }
    }

    public int SelectedMapDiagnosticLayerIndex
    {
        get => _selectedMapDiagnosticLayerIndex;
        set
        {
            if ((uint)value >= (uint)MapDiagnosticLayerOptions.Count)
            {
                return;
            }

            if (SetProperty(ref _selectedMapDiagnosticLayerIndex, value))
            {
                OnPropertyChanged(nameof(IsMapHexGridEmpty));
            }
        }
    }

    public IReadOnlyList<MapObjectTypeFilterOption> MapObjectTypeFilterOptions { get; } =
    [
        AllMapObjectTypes,
        new("Item", 0),
        new("Critter", 1),
        new("Scenery", 2),
        new("Wall", 3),
        new("Tile", 4),
        new("Miscellaneous", 5),
        new("Interface", 6),
        new("Inventory", 7),
        new("Head", 8),
        new("Background", 9),
        new("Skilldex", 10)
    ];

    public IReadOnlyList<MapObjectListSortOption> MapObjectListSortOptions { get; } =
    [
        DefaultMapObjectListSort,
        new("Record index (desc)", MapObjectListSortMode.RecordIndexDescending),
        new("PID (asc)", MapObjectListSortMode.PidAscending),
        new("PID (desc)", MapObjectListSortMode.PidDescending),
        new("Object type (A-Z)", MapObjectListSortMode.ObjectTypeAscending),
        new("Hex coordinates (Y, X, asc)", MapObjectListSortMode.HexCoordinatesAscending),
        new("Hex coordinates (Y, X, desc)", MapObjectListSortMode.HexCoordinatesDescending),
        new("Byte offset (asc)", MapObjectListSortMode.ByteOffsetAscending),
        new("Byte offset (desc)", MapObjectListSortMode.ByteOffsetDescending)
    ];

    public IReadOnlyList<MapObjectPlacement> FilteredMapObjectPlacements =>
        !HasActiveMapObjectFilters
            ? _mapObjectPlacements
            : _filteredMapObjectPlacements;

    public IReadOnlyList<MapObjectPlacement> MapObjectPlacements => _mapObjectPlacements;

    public ObservableCollection<MapObjectPlacement> SortedMapObjectPlacements { get; } = [];

    public string MapObjectSearchResultCountDisplay =>
        string.Format(
            CultureInfo.CurrentCulture,
            "{0} / {1} objects",
            FilteredMapObjectPlacements.Count,
            _mapObjectPlacements.Count);

    public bool HasNoMatchingMapObjects =>
        HasActiveMapObjectFilters
        && FilteredMapObjectPlacements.Count == 0;

    public bool IsMapHexGridEmpty =>
        IsMapInspector
        && SelectedMapDiagnosticLayerIndex == ObjectHexesLayerIndex
        && !_mapObjectPlacements.Any(placement =>
            placement.ElevationGroup == SelectedMapElevationIndex
            && placement.HexX is int x
            && (uint)x < MapObjectPlacementDecoder.HexGridWidth
            && placement.HexY is int y
            && (uint)y < MapObjectPlacementDecoder.HexGridHeight);

    public MapObjectTypeFilterOption SelectedMapObjectTypeFilter
    {
        get => _selectedMapObjectTypeFilter;
        set
        {
            var selectedOption = value is not null && MapObjectTypeFilterOptions.Contains(value)
                ? value
                : AllMapObjectTypes;
            if (!SetProperty(ref _selectedMapObjectTypeFilter, selectedOption))
            {
                return;
            }

            if (_selectedMapObject is not null && !MatchesMapObjectFilters(_selectedMapObject))
            {
                SetSelectedMapObject(null, MapObjectSelectionSource.Programmatic);
            }

            RefreshFilteredMapObjectPlacements();
        }
    }

    public MapObjectListSortOption SelectedMapObjectListSort
    {
        get => _selectedMapObjectListSort;
        set
        {
            var selectedOption = value is not null && MapObjectListSortOptions.Contains(value)
                ? value
                : DefaultMapObjectListSort;
            if (SetProperty(ref _selectedMapObjectListSort, selectedOption))
            {
                RefreshSortedMapObjectPlacements();
            }
        }
    }

    public string MapObjectSearchText
    {
        get => _mapObjectSearchText;
        set
        {
            if (!SetProperty(ref _mapObjectSearchText, value ?? string.Empty))
            {
                return;
            }

            if (_selectedMapObject is not null && !MatchesMapObjectFilters(_selectedMapObject))
            {
                SetSelectedMapObject(null, MapObjectSelectionSource.Programmatic);
            }

            RefreshFilteredMapObjectPlacements();
        }
    }

    public MapObjectPlacement? SelectedMapObject
    {
        get => _selectedMapObject;
        set => SetSelectedMapObject(value, MapObjectSelectionSource.List);
    }

    public int? SelectedMapObjectRecordIndex => _selectedMapObject?.RecordIndex;

    public MapEditSession? EditSession => _mapEditSession;

    public bool HasEditSession => _mapEditSession is not null;

    public string InventoryQuantityInput
    {
        get => _inventoryQuantityInput;
        set
        {
            if (SetProperty(ref _inventoryQuantityInput, value ?? string.Empty))
            {
                SetInventoryQuantityValidationError(null);
            }
        }
    }

    public string? InventoryQuantityValidationError => _inventoryQuantityValidationError;

    public bool HasInventoryQuantityValidationError =>
        !string.IsNullOrWhiteSpace(_inventoryQuantityValidationError);

    public bool CanEditSelectedInventoryQuantity =>
        SelectedEditObject?.Placement is
        {
            ParentRecordIndex: not null,
            InventoryQuantity: not null
        };

    public bool IsMapEditSessionDirty => _mapEditSession?.IsDirty ?? false;

    public bool CanSaveMapAs =>
        !string.IsNullOrWhiteSpace(_mapSaveSourceArchivePath)
        && IsMapEditSessionDirty
        && GetMapSaveEligibility().IsEligible;

    public long MapEditRevision => _mapEditRevision;

    public string MapSaveStatusDisplay
    {
        get
        {
            var eligibility = GetMapSaveEligibility();
            if (!eligibility.IsEligible)
            {
                return $"MAP Save As is unavailable: {eligibility.Message}";
            }

            if (string.IsNullOrWhiteSpace(_mapSaveSourceArchivePath))
            {
                return "MAP Save As is unavailable because the source archive path is not retained.";
            }

            if (!IsMapEditSessionDirty)
            {
                return "MAP Save As is disabled because there are no changed inventory quantities.";
            }

            if (_mapSaveFailureRevision == _mapEditRevision
                && !string.IsNullOrWhiteSpace(_mapSaveFailureMessage))
            {
                return $"MAP Save As failed: {_mapSaveFailureMessage} The in-memory edit is still available.";
            }

            if (IsCurrentMapEditSaved)
            {
                return $"Saved MAP copy: {_savedMapOutputPath}. The original archive is unchanged.";
            }

            var previousSave = _savedMapOutputPath is null
                ? string.Empty
                : $" Last saved copy: {_savedMapOutputPath}.";
            return eligibility.Message + previousSave;
        }
    }

    public bool CanUndoMapEdit => _mapEditSession?.CanUndo ?? false;

    public bool CanRedoMapEdit => _mapEditSession?.CanRedo ?? false;

    public string EditSessionDirtyDisplay =>
        !IsMapEditSessionDirty
            ? "No unsaved in-memory MAP changes"
            : IsCurrentMapEditSaved
                ? "Current changes are saved to a separate MAP copy"
                : "Unsaved in-memory MAP changes";

    public MapEditSessionObject? SelectedEditObject =>
        _mapEditSession is { } session && _selectedMapEditObjectId is { } id
            ? session.GetObject(id)
            : null;

    public string EditSessionStatusDisplay =>
        _mapEditSession is { } session
            ? string.Format(
                CultureInfo.CurrentCulture,
                "MAP edit session: {0:N0} parsed objects; {1:N0} source bytes retained. {2}.",
                session.Objects.Count,
                session.SourceData.Length,
                IsMapEditSessionDirty
                    ? IsCurrentMapEditSaved
                        ? $"Changes saved to {_savedMapOutputPath}; the original archive is unchanged"
                        : "There are unsaved changes"
                    : "There are no unsaved changes")
            : string.Empty;

    public bool ApplyInventoryQuantity()
    {
        if (_mapEditSession is not { } session
            || SelectedEditObject is not { } selectedObject
            || selectedObject.Id.SessionId != session.SessionId
            || selectedObject.Placement.ParentRecordIndex is null
            || selectedObject.Placement.InventoryQuantity is null)
        {
            SetInventoryQuantityValidationError(
                "Select a nested object with a decoded inventory quantity in the active MAP session.");
            return false;
        }

        if (!int.TryParse(
                _inventoryQuantityInput,
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out var quantity))
        {
            SetInventoryQuantityValidationError("Enter a whole-number inventory quantity.");
            return false;
        }

        if (!session.TrySetInventoryQuantity(selectedObject.Id, quantity, out var error))
        {
            SetInventoryQuantityValidationError(error);
            return false;
        }

        SetInventoryQuantityValidationError(null);
        return true;
    }

    public bool UndoMapEdit()
    {
        if (_mapEditSession is not { } session)
        {
            return false;
        }

        var succeeded = session.Undo(out var error);
        SetInventoryQuantityValidationError(error);
        return succeeded;
    }

    public bool RedoMapEdit()
    {
        if (_mapEditSession is not { } session)
        {
            return false;
        }

        var succeeded = session.Redo(out var error);
        SetInventoryQuantityValidationError(error);
        return succeeded;
    }

    public void SetMapSerializationContext(
        FalloutGame? game,
        MapHeaderMetadata? header,
        MapTileGridData? tileGrids,
        MapObjectPlacementData? objectPlacements,
        string? sourceArchivePath)
    {
        _mapSaveGame = game;
        _mapSaveHeader = header;
        _mapSaveTileGrids = tileGrids;
        _mapSaveObjectPlacements = objectPlacements;
        _mapSaveSourceArchivePath = sourceArchivePath;
        NotifyMapSaveStateChanged();
    }

    public MapInventoryQuantityPatchResult CreateMapSaveOutput()
    {
        if (_mapEditSession is null
            || _mapSaveGame is not FalloutGame game
            || _mapSaveHeader is null
            || _mapSaveTileGrids is null
            || _mapSaveObjectPlacements is null)
        {
            return new MapInventoryQuantityPatchResult(
                false,
                "The complete source-bound MAP decode is not available.",
                null);
        }

        return MapInventoryQuantityPatchBuilder.Create(
            _mapEditSession.SourceData,
            game,
            _mapSaveHeader,
            _mapSaveTileGrids,
            _mapSaveObjectPlacements,
            _mapEditSession);
    }

    public bool IsCurrentMapSave(Guid sessionId, long revision) =>
        _mapEditSession?.SessionId == sessionId
        && _mapEditRevision == revision;

    public void MarkMapSaveAsSucceeded(
        Guid sessionId,
        long revision,
        string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (!IsCurrentMapSave(sessionId, revision))
        {
            return;
        }

        _savedMapSessionId = sessionId;
        _savedMapEditRevision = revision;
        _savedMapOutputPath = destinationPath;
        _mapSaveFailureMessage = null;
        _mapSaveFailureRevision = null;
        NotifyMapSaveStateChanged();
        OnPropertyChanged(nameof(EditSessionDirtyDisplay));
        OnPropertyChanged(nameof(EditSessionStatusDisplay));
    }

    public void ReportMapSaveAsFailure(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        _mapSaveFailureMessage = message;
        _mapSaveFailureRevision = _mapEditRevision;
        NotifyMapSaveStateChanged();
    }

    public string MapObjectDetailsDisplay
    {
        get => _mapObjectDetailsDisplay;
        private set => SetProperty(ref _mapObjectDetailsDisplay, value);
    }

    public string MapObjectPrototypeDiagnosticStatus
    {
        get => _mapObjectPrototypeDiagnosticStatus;
        private set => SetProperty(ref _mapObjectPrototypeDiagnosticStatus, value);
    }

    public string MapObjectPrototypeDiagnosticDetails
    {
        get => _mapObjectPrototypeDiagnosticDetails;
        private set => SetProperty(ref _mapObjectPrototypeDiagnosticDetails, value);
    }

    public string MapObjectPrototypeSceneryFieldsDisplay
    {
        get => _mapObjectPrototypeSceneryFieldsDisplay;
        private set
        {
            if (SetProperty(ref _mapObjectPrototypeSceneryFieldsDisplay, value))
            {
                OnPropertyChanged(nameof(HasMapObjectPrototypeSceneryFields));
            }
        }
    }

    public bool HasMapObjectPrototypeSceneryFields =>
        !string.IsNullOrEmpty(MapObjectPrototypeSceneryFieldsDisplay);

    public string MapObjectPrototypeItemFieldsDisplay
    {
        get => _mapObjectPrototypeItemFieldsDisplay;
        private set
        {
            if (SetProperty(ref _mapObjectPrototypeItemFieldsDisplay, value))
            {
                OnPropertyChanged(nameof(HasMapObjectPrototypeItemFields));
            }
        }
    }

    public bool HasMapObjectPrototypeItemFields =>
        !string.IsNullOrEmpty(MapObjectPrototypeItemFieldsDisplay);

    public IReadOnlyList<string> MapObjectPrototypeRawByteLines
    {
        get => _mapObjectPrototypeRawByteLines;
        private set
        {
            if (SetProperty(ref _mapObjectPrototypeRawByteLines, value))
            {
                OnPropertyChanged(nameof(HasMapObjectPrototypeRawBytes));
            }
        }
    }

    public bool HasMapObjectPrototypeRawBytes => MapObjectPrototypeRawByteLines.Count > 0;

    public void SetMapObjectPrototypeDiagnostic(
        string status,
        string details,
        string sceneryFields,
        string itemFields,
        IReadOnlyList<string> rawByteLines)
    {
        ArgumentNullException.ThrowIfNull(rawByteLines);
        MapObjectPrototypeDiagnosticStatus = status;
        MapObjectPrototypeDiagnosticDetails = details;
        MapObjectPrototypeSceneryFieldsDisplay = sceneryFields;
        MapObjectPrototypeItemFieldsDisplay = itemFields;
        MapObjectPrototypeRawByteLines = rawByteLines;
    }

    public void ClearMapObjectPrototypeDiagnostic() =>
        SetMapObjectPrototypeDiagnostic(
            DefaultMapObjectPrototypeDiagnosticStatus,
            string.Empty,
            string.Empty,
            string.Empty,
            []);

    public void SetAvailableElevations(
        IEnumerable<int> elevations,
        int? preferredElevationIndex)
    {
        ArgumentNullException.ThrowIfNull(elevations);
        var availableElevations = elevations.Distinct().Order().ToArray();

        SelectedMapElevationIndex = null;
        MapElevationOptions.Clear();
        foreach (var elevation in availableElevations)
        {
            MapElevationOptions.Add(elevation);
        }

        int? initialElevationIndex = preferredElevationIndex is int preferred
            && MapElevationOptions.Contains(preferred)
                ? preferred
                : MapElevationOptions.Count > 0
                    ? MapElevationOptions[0]
                    : null;
        SelectedMapElevationIndex = initialElevationIndex;
    }

    public void ResetMapState()
    {
        SetMapEditSession(null);
        ClearSelectedMapObject();
        SelectedMapDiagnosticLayerIndex = SquareTilesLayerIndex;
        SetAvailableElevations([], preferredElevationIndex: null);
        ClearMapObjectPrototypeDiagnostic();
    }

    public void SetMapEditSession(MapEditSession? session)
    {
        if (ReferenceEquals(_mapEditSession, session))
        {
            return;
        }

        if (_selectedMapObject is not null)
        {
            ClearSelectedMapObject();
        }

        if (_mapEditSession is { } oldSession)
        {
            oldSession.ObjectsChanged -= MapEditSession_ObjectsChanged;
        }

        _mapEditSession = session;
        _mapEditRevision++;
        _savedMapSessionId = null;
        _savedMapEditRevision = null;
        _savedMapOutputPath = null;
        _mapSaveFailureMessage = null;
        _mapSaveFailureRevision = null;
        if (_mapEditSession is { } newSession)
        {
            newSession.ObjectsChanged += MapEditSession_ObjectsChanged;
        }
        else
        {
            SetMapSerializationContext(null, null, null, null, null);
        }

        OnPropertyChanged(nameof(EditSession));
        OnPropertyChanged(nameof(HasEditSession));
        OnPropertyChanged(nameof(MapEditRevision));
        OnPropertyChanged(nameof(SelectedEditObject));
        OnPropertyChanged(nameof(EditSessionStatusDisplay));
        NotifyEditSessionStateChanged();
        NotifyMapSaveStateChanged();
        RefreshInventoryQuantityEditor();
        MapObjectDetailsDisplay = FormatMapObjectDetailsDisplay(null);
        RefreshDisplayedMapObjectPlacements();
    }

    public void SetMapInspectorActive(bool isActive)
    {
        if (SetProperty(ref _isMapInspector, isActive, nameof(IsMapInspector)))
        {
            OnPropertyChanged(nameof(IsMapHexGridEmpty));
        }
    }

    public void SetMapObjectPlacements(IReadOnlyList<MapObjectPlacement> placements)
    {
        ArgumentNullException.ThrowIfNull(placements);
        _sourceMapObjectPlacements = placements;
        RefreshDisplayedMapObjectPlacements();
    }

    private void RefreshDisplayedMapObjectPlacements()
    {
        var placements = _mapEditSession is { } session
            ? session.Objects.Select(sessionObject => sessionObject.Placement).ToArray()
            : _sourceMapObjectPlacements;
        SetDisplayedMapObjectPlacements(placements);
    }

    private void SetDisplayedMapObjectPlacements(IReadOnlyList<MapObjectPlacement> placements)
    {
        _mapObjectPlacements = placements;
        OnPropertyChanged(nameof(MapObjectPlacements));
        OnPropertyChanged(nameof(IsMapHexGridEmpty));

        if (_selectedMapObject is { } selectedPlacement)
        {
            var selectionIsCurrent = _mapEditSession is { } session
                ? _selectedMapEditObjectId is { } selectedId
                    && selectedId.SessionId == session.SessionId
                    && selectedId.RecordIndex == selectedPlacement.RecordIndex
                    && session.GetObject(selectedId) is not null
                    && placements.Any(placement => placement.RecordIndex == selectedId.RecordIndex)
                : placements.Any(placement => placement.RecordIndex == selectedPlacement.RecordIndex);

            if (!selectionIsCurrent || !MatchesMapObjectFilters(selectedPlacement))
            {
                SetSelectedMapObject(null, MapObjectSelectionSource.Programmatic);
            }
        }

        RefreshFilteredMapObjectPlacements();
    }

    private void MapEditSession_ObjectsChanged(object? sender, EventArgs e)
    {
        if (sender is MapEditSession session && ReferenceEquals(_mapEditSession, session))
        {
            _mapEditRevision++;
            _mapSaveFailureMessage = null;
            _mapSaveFailureRevision = null;
            OnPropertyChanged(nameof(MapEditRevision));
            RefreshDisplayedMapObjectPlacements();
            NotifyEditSessionStateChanged();
            NotifyMapSaveStateChanged();
            RefreshInventoryQuantityEditor();
        }
    }

    public void SelectMapObjectFromFloorMarker(MapObjectPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        SetSelectedMapObject(placement, MapObjectSelectionSource.FloorMarker);
    }

    public void SelectMapObjectFromIsometricMarker(int? recordIndex, int? elevationIndex)
    {
        var placement = recordIndex is int selectedRecordIndex
            ? _mapObjectPlacements.FirstOrDefault(candidate =>
                candidate.RecordIndex == selectedRecordIndex
                && candidate.ElevationGroup == elevationIndex
                && candidate.ParentRecordIndex is null)
            : null;
        SetSelectedMapObject(placement, MapObjectSelectionSource.IsometricMarker);
    }

    public void ClearSelectedMapObject() =>
        SetSelectedMapObject(null, MapObjectSelectionSource.Programmatic);

    private bool HasActiveMapObjectFilters =>
        !string.IsNullOrWhiteSpace(_mapObjectSearchText)
        || _selectedMapObjectTypeFilter.PidTypeIdentifier.HasValue;

    private bool MatchesMapObjectFilters(MapObjectPlacement placement) =>
        (!_selectedMapObjectTypeFilter.PidTypeIdentifier.HasValue
            || placement.PidTypeIdentifier == _selectedMapObjectTypeFilter.PidTypeIdentifier.Value)
        && MatchesMapObjectSearch(placement, _mapObjectSearchText.Trim());

    private void RefreshFilteredMapObjectPlacements()
    {
        _filteredMapObjectPlacements = !HasActiveMapObjectFilters
            ? []
            : _mapObjectPlacements
                .Where(MatchesMapObjectFilters)
                .ToArray();
        OnPropertyChanged(nameof(FilteredMapObjectPlacements));
        RefreshSortedMapObjectPlacements();
        NotifyMapObjectSearchFeedbackChanged();
    }

    private void RefreshSortedMapObjectPlacements()
    {
        var placements = FilteredMapObjectPlacements;
        var selectedRecordIndex = _selectedMapObject?.RecordIndex;
        _sortedListReportedTransientSelectionClear = false;
        var sortedPlacements = _selectedMapObjectListSort.SortMode switch
        {
            MapObjectListSortMode.RecordIndexAscending =>
                placements.OrderBy(placement => placement.RecordIndex).ToArray(),
            MapObjectListSortMode.RecordIndexDescending =>
                placements.OrderByDescending(placement => placement.RecordIndex)
                    .ThenBy(placement => placement.RecordIndex)
                    .ToArray(),
            MapObjectListSortMode.PidAscending =>
                placements.OrderBy(placement => unchecked((uint)placement.RawPid))
                    .ThenBy(placement => placement.RecordIndex)
                    .ToArray(),
            MapObjectListSortMode.PidDescending =>
                placements.OrderByDescending(placement => unchecked((uint)placement.RawPid))
                    .ThenBy(placement => placement.RecordIndex)
                    .ToArray(),
            MapObjectListSortMode.ObjectTypeAscending =>
                placements.OrderBy(placement => placement.ObjectTypeDisplay, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(placement => placement.PidTypeIdentifier)
                    .ThenBy(placement => placement.RecordIndex)
                    .ToArray(),
            MapObjectListSortMode.HexCoordinatesAscending =>
                placements.OrderBy(placement => HasCoordinates(placement) ? 0 : 1)
                    .ThenBy(placement => placement.HexY)
                    .ThenBy(placement => placement.HexX)
                    .ThenBy(placement => placement.RecordIndex)
                    .ToArray(),
            MapObjectListSortMode.HexCoordinatesDescending =>
                placements.OrderBy(placement => HasCoordinates(placement) ? 0 : 1)
                    .ThenByDescending(placement => placement.HexY)
                    .ThenByDescending(placement => placement.HexX)
                    .ThenBy(placement => placement.RecordIndex)
                    .ToArray(),
            MapObjectListSortMode.ByteOffsetAscending =>
                placements.OrderBy(placement => placement.RecordOffset)
                    .ThenBy(placement => placement.RecordIndex)
                    .ToArray(),
            MapObjectListSortMode.ByteOffsetDescending =>
                placements.OrderByDescending(placement => placement.RecordOffset)
                    .ThenBy(placement => placement.RecordIndex)
                    .ToArray(),
            _ => placements.OrderBy(placement => placement.RecordIndex).ToArray()
        };

        _isReorderingSortedMapObjectPlacements = true;
        try
        {
            for (var targetIndex = 0; targetIndex < sortedPlacements.Length; targetIndex++)
            {
                var targetPlacement = sortedPlacements[targetIndex];
                if (targetIndex < SortedMapObjectPlacements.Count
                    && ReferenceEquals(SortedMapObjectPlacements[targetIndex], targetPlacement))
                {
                    continue;
                }

                var currentIndex = -1;
                for (var index = targetIndex + 1; index < SortedMapObjectPlacements.Count; index++)
                {
                    if (ReferenceEquals(SortedMapObjectPlacements[index], targetPlacement))
                    {
                        currentIndex = index;
                        break;
                    }
                }

                if (currentIndex >= 0)
                {
                    SortedMapObjectPlacements.Move(currentIndex, targetIndex);
                }
                else
                {
                    SortedMapObjectPlacements.Insert(targetIndex, targetPlacement);
                }
            }

            while (SortedMapObjectPlacements.Count > sortedPlacements.Length)
            {
                SortedMapObjectPlacements.RemoveAt(SortedMapObjectPlacements.Count - 1);
            }
        }
        finally
        {
            _isReorderingSortedMapObjectPlacements = false;
        }

        if (selectedRecordIndex is int recordIndex)
        {
            var retainedSelection = SortedMapObjectPlacements.FirstOrDefault(
                placement => placement.RecordIndex == recordIndex);
            if (retainedSelection is not null)
            {
                if (!ReferenceEquals(_selectedMapObject, retainedSelection))
                {
                    if (_mapEditSession is { } session
                        && _selectedMapEditObjectId is { } selectedId
                        && selectedId == new MapEditObjectId(session.SessionId, recordIndex)
                        && session.GetObject(selectedId) is not null)
                    {
                        RebindSelectedMapObject(retainedSelection);
                    }
                    else
                    {
                        SetSelectedMapObject(retainedSelection, MapObjectSelectionSource.Programmatic);
                    }
                }
                else if (_sortedListReportedTransientSelectionClear)
                {
                    OnPropertyChanged(nameof(SelectedMapObject));
                }
            }
        }

        _sortedListReportedTransientSelectionClear = false;
    }

    private void RebindSelectedMapObject(MapObjectPlacement placement)
    {
        if (SetProperty(ref _selectedMapObject, placement, nameof(SelectedMapObject)))
        {
            OnPropertyChanged(nameof(SelectedMapObjectRecordIndex));
            OnPropertyChanged(nameof(SelectedEditObject));
            MapObjectDetailsDisplay = FormatMapObjectDetailsDisplay(placement);
            RefreshInventoryQuantityEditor();
        }
    }

    private void SetSelectedMapObject(
        MapObjectPlacement? placement,
        MapObjectSelectionSource source)
    {
        MapEditObjectId? selectedId = null;
        if (placement is not null && _mapEditSession is { } session)
        {
            var id = new MapEditObjectId(session.SessionId, placement.RecordIndex);
            var belongsToCurrentMap = _mapObjectPlacements.Any(candidate =>
                candidate.RecordIndex == placement.RecordIndex
                && ReferenceEquals(candidate, placement));
            if (!belongsToCurrentMap
                || session.GetObject(id) is null)
            {
                return;
            }

            selectedId = id;
        }

        if (placement is not null && !MatchesMapObjectFilters(placement))
        {
            placement = null;
        }

        if (placement is null
            && _isReorderingSortedMapObjectPlacements
            && _selectedMapObject is not null
            && FilteredMapObjectPlacements.Any(
                candidate => candidate.RecordIndex == _selectedMapObject.RecordIndex))
        {
            _sortedListReportedTransientSelectionClear = true;
            return;
        }

        _selectedMapEditObjectId = placement is null ? null : selectedId;
        if (SetProperty(ref _selectedMapObject, placement, nameof(SelectedMapObject)))
        {
            OnPropertyChanged(nameof(SelectedMapObjectRecordIndex));
            OnPropertyChanged(nameof(SelectedEditObject));
            MapObjectDetailsDisplay = FormatMapObjectDetailsDisplay(placement);
            RefreshInventoryQuantityEditor();
            SelectedMapObjectChanged?.Invoke(
                this,
                new MapObjectSelectionChangedEventArgs(placement, source));
        }
    }

    private string FormatMapObjectDetailsDisplay(MapObjectPlacement? placement)
    {
        if (_mapEditSession is not { } session)
        {
            return placement?.Details ?? "Select a parsed object to inspect its raw fields.";
        }

        if (placement is null)
        {
            return "Select a parsed object to inspect its confirmed placement fields.";
        }

        var sessionObject = _selectedMapEditObjectId is { } selectedId
            && selectedId.SessionId == session.SessionId
            && selectedId.RecordIndex == placement.RecordIndex
                ? session.GetObject(selectedId)
                : null;
        if (sessionObject is null)
        {
            return "The selected object is not available in the current MAP edit session.";
        }

        var workingPlacement = sessionObject.Placement;
        var coordinates = workingPlacement.HexX is int x && workingPlacement.HexY is int y
            ? $"({x}, {y})"
            : "unavailable";
        return $"Session-local object ID: {sessionObject.Id.RecordIndex}"
            + Environment.NewLine
            + $"Elevation group: {workingPlacement.ElevationGroup}"
            + Environment.NewLine
            + $"Hex-grid coordinates: {coordinates}"
            + (workingPlacement.ParentRecordIndex is not null
                && workingPlacement.InventoryQuantity is int quantity
                    ? Environment.NewLine + $"Inventory quantity: {quantity}"
                    : string.Empty);
    }

    private void RefreshInventoryQuantityEditor()
    {
        var selectedPlacement = SelectedEditObject?.Placement;
        var quantityText = selectedPlacement is
        {
            ParentRecordIndex: not null,
            InventoryQuantity: int quantity
        }
            ? quantity.ToString(CultureInfo.CurrentCulture)
            : string.Empty;

        SetProperty(
            ref _inventoryQuantityInput,
            quantityText,
            nameof(InventoryQuantityInput));
        OnPropertyChanged(nameof(CanEditSelectedInventoryQuantity));
        SetInventoryQuantityValidationError(null);
    }

    private void NotifyEditSessionStateChanged()
    {
        OnPropertyChanged(nameof(IsMapEditSessionDirty));
        OnPropertyChanged(nameof(EditSessionDirtyDisplay));
        OnPropertyChanged(nameof(CanUndoMapEdit));
        OnPropertyChanged(nameof(CanRedoMapEdit));
        OnPropertyChanged(nameof(EditSessionStatusDisplay));
    }

    private bool IsCurrentMapEditSaved =>
        _mapEditSession is { } session
        && _savedMapSessionId == session.SessionId
        && _savedMapEditRevision == _mapEditRevision;

    private MapInventoryQuantityPatchEligibility GetMapSaveEligibility()
    {
        if (_mapEditSession is null)
        {
            return new(false, "no MAP edit session is active.");
        }

        if (_mapSaveGame is not FalloutGame game
            || _mapSaveHeader is null
            || _mapSaveTileGrids is null
            || _mapSaveObjectPlacements is null)
        {
            return new(false, "the complete source-bound MAP decode is not available.");
        }

        return MapInventoryQuantityPatchBuilder.Validate(
            _mapEditSession.SourceData,
            game,
            _mapSaveHeader,
            _mapSaveTileGrids,
            _mapSaveObjectPlacements,
            _mapEditSession);
    }

    private void NotifyMapSaveStateChanged()
    {
        OnPropertyChanged(nameof(CanSaveMapAs));
        OnPropertyChanged(nameof(MapSaveStatusDisplay));
    }

    private void SetInventoryQuantityValidationError(string? error)
    {
        if (SetProperty(
                ref _inventoryQuantityValidationError,
                error,
                nameof(InventoryQuantityValidationError)))
        {
            OnPropertyChanged(nameof(HasInventoryQuantityValidationError));
        }
    }

    private void NotifyMapObjectSearchFeedbackChanged()
    {
        OnPropertyChanged(nameof(MapObjectSearchResultCountDisplay));
        OnPropertyChanged(nameof(HasNoMatchingMapObjects));
    }

    private static bool HasCoordinates(MapObjectPlacement placement) =>
        placement.HexY.HasValue && placement.HexX.HasValue;

    private static bool MatchesMapObjectSearch(MapObjectPlacement placement, string query)
    {
        return query.Length == 0
            || ContainsSearchText(placement.PrototypeFileName, query)
            || ContainsSearchText(placement.ObjectTypeDisplay, query)
            || ContainsNumber(placement.PidTypeIdentifier, query)
            || ContainsSearchText(
                string.Concat("PID type ", FormatNumber(placement.PidTypeIdentifier)),
                query)
            || ContainsNumber(placement.RecordIndex, query)
            || ContainsSearchText(
                string.Concat("record #", FormatNumber(placement.RecordIndex)),
                query)
            || ContainsHexNumber(placement.RawPid, query)
            || ContainsHexNumber(placement.RawFid, query)
            || ContainsNumber(placement.RawHexIndex, query)
            || ContainsSearchText(
                string.Concat("hex index ", FormatNumber(placement.RawHexIndex)),
                query)
            || ContainsNumber(placement.HexX, query)
            || ContainsNumber(placement.HexY, query)
            || ContainsCoordinates(placement.HexX, placement.HexY, query)
            || ContainsNumber(placement.ElevationGroup, query)
            || ContainsSearchText(
                string.Concat("elevation ", FormatNumber(placement.ElevationGroup)),
                query)
            || ContainsSearchText(
                string.Concat("elevation group ", FormatNumber(placement.ElevationGroup)),
                query)
            || ContainsNumber(placement.StoredElevation, query)
            || ContainsSearchText(
                string.Concat("stored elevation ", FormatNumber(placement.StoredElevation)),
                query)
            || ContainsNumber(placement.RecordOffset, query)
            || ContainsSearchText(
                string.Concat("byte offset ", FormatNumber(placement.RecordOffset)),
                query);
    }

    private static bool ContainsCoordinates(int? hexX, int? hexY, string query)
    {
        if (hexX is not int x || hexY is not int y)
        {
            return false;
        }

        var xText = FormatNumber(x);
        var yText = FormatNumber(y);
        return ContainsSearchText(string.Concat(xText, ", ", yText), query)
            || ContainsSearchText(string.Concat(xText, ",", yText), query);
    }

    private static bool ContainsNumber(int value, string query) =>
        ContainsSearchText(FormatNumber(value), query);

    private static bool ContainsNumber(int? value, string query) =>
        value is int number && ContainsNumber(number, query);

    private static bool ContainsHexNumber(int value, string query)
    {
        var hexadecimal = unchecked((uint)value).ToString("X8", CultureInfo.InvariantCulture);
        return ContainsSearchText(string.Concat("0x", hexadecimal), query)
            || ContainsSearchText(hexadecimal, query);
    }

    private static string FormatNumber(int value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static bool ContainsSearchText(string? value, string query) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
