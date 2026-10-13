using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using FalloutAssetExplorer.Core;

namespace FalloutAssetExplorer.App.ViewModels;

public sealed class MapInspectorViewModel : INotifyPropertyChanged
{
    private static readonly MapObjectTypeFilterOption AllMapObjectTypes = new("All types", null);
    private static readonly MapObjectListSortOption DefaultMapObjectListSort =
        new("Record index (asc)", MapObjectListSortMode.RecordIndexAscending);

    private IReadOnlyList<MapObjectPlacement> _mapObjectPlacements = [];
    private string _mapObjectSearchText = string.Empty;
    private MapObjectTypeFilterOption _selectedMapObjectTypeFilter = AllMapObjectTypes;
    private MapObjectListSortOption _selectedMapObjectListSort = DefaultMapObjectListSort;
    private IReadOnlyList<MapObjectPlacement> _filteredMapObjectPlacements = [];
    private bool _isReorderingSortedMapObjectPlacements;
    private bool _sortedListReportedTransientSelectionClear;
    private MapObjectPlacement? _selectedMapObject;
    private string _mapObjectDetailsDisplay = "Select a parsed object to inspect its raw fields.";

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

    public string MapObjectDetailsDisplay
    {
        get => _mapObjectDetailsDisplay;
        private set => SetProperty(ref _mapObjectDetailsDisplay, value);
    }

    public void SetMapObjectPlacements(IReadOnlyList<MapObjectPlacement> placements)
    {
        ArgumentNullException.ThrowIfNull(placements);
        _mapObjectPlacements = placements;

        if (_selectedMapObject is not null
            && !_mapObjectPlacements.Any(
                placement => placement.RecordIndex == _selectedMapObject.RecordIndex))
        {
            SetSelectedMapObject(null, MapObjectSelectionSource.Programmatic);
        }
        else if (_selectedMapObject is not null
                 && !MatchesMapObjectFilters(_selectedMapObject))
        {
            SetSelectedMapObject(null, MapObjectSelectionSource.Programmatic);
        }

        RefreshFilteredMapObjectPlacements();
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
                    SetSelectedMapObject(retainedSelection, MapObjectSelectionSource.Programmatic);
                }
                else if (_sortedListReportedTransientSelectionClear)
                {
                    OnPropertyChanged(nameof(SelectedMapObject));
                }
            }
        }

        _sortedListReportedTransientSelectionClear = false;
    }

    private void SetSelectedMapObject(
        MapObjectPlacement? placement,
        MapObjectSelectionSource source)
    {
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

        if (SetProperty(ref _selectedMapObject, placement, nameof(SelectedMapObject)))
        {
            OnPropertyChanged(nameof(SelectedMapObjectRecordIndex));
            MapObjectDetailsDisplay = placement?.Details
                ?? "Select a parsed object to inspect its raw fields.";
            SelectedMapObjectChanged?.Invoke(
                this,
                new MapObjectSelectionChangedEventArgs(placement, source));
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
