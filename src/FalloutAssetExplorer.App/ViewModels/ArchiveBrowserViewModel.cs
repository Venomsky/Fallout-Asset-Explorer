using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FalloutAssetExplorer.App.Views;
using FalloutAssetExplorer.Core;

namespace FalloutAssetExplorer.App.ViewModels;

public sealed class ArchiveBrowserViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly MapObjectTypeFilterOption AllMapObjectTypes = new("All types", null);
    private static readonly MapObjectListSortOption DefaultMapObjectListSort =
        new("Record index (asc)", MapObjectListSortMode.RecordIndexAscending);
    private static readonly string[] SupportedExtensions = [".FRM", ".MAP", ".PRO", ".LST", ".INT"];
    private static readonly string[] DirectionNames = ["NE", "E", "SE", "SW", "W", "NW"];
    private const int MapFloorTilesDiagnosticLayerIndex = 2;
    private const int MapIsometricFloorTilesDiagnosticLayerIndex = 3;
    private const int MapIsometricObjectMarkersDiagnosticLayerIndex = 4;

    private string _searchText = string.Empty;
    private string _mapObjectSearchText = string.Empty;
    private MapObjectTypeFilterOption _selectedMapObjectTypeFilter = AllMapObjectTypes;
    private MapObjectListSortOption _selectedMapObjectListSort = DefaultMapObjectListSort;
    private IReadOnlyList<MapObjectPlacement> _filteredMapObjectPlacements = [];
    private bool _isReplacingMapObjectPlacements;
    private bool _isReorderingSortedMapObjectPlacements;
    private bool _sortedListReportedTransientSelectionClear;
    private string _selectedExtension = ArchiveEntryFilter.AllExtensions;
    private string? _archivePath;
    private string _statusMessage = "Open a Fallout DAT archive to begin.";
    private int _selectedEntryCount;
    private ArchiveEntry? _selectedEntry;
    private bool _isLoading;
    private bool _isExtracting;
    private IGameArchive? _archive;
    private readonly FalloutPaletteResolver _paletteResolver = new();
    private readonly FrmPlaybackController _playback = new();
    private readonly DispatcherTimer _playbackTimer;
    private CancellationTokenSource? _previewCancellation;
    private CancellationTokenSource? _mapFloorTileCancellation;
    private CancellationTokenSource? _mapObjectPrototypeDiagnosticCancellation;
    private FrmDocument? _previewDocument;
    private Bitmap? _previewBitmap;
    private FalloutPalette? _falloutPalette;
    private string? _gameDataDirectory;
    private byte[] _renderBuffer = [];
    private MapTileGridData? _mapTileGridData;
    private MapScopedAssetCache<Bitmap> _mapFloorBitmapCache =
        new(MapScopedAssetCache<Bitmap>.DefaultByteLimit, bitmap => bitmap.Dispose());
    private readonly Dictionary<uint, Bitmap> _mapFloorTileBitmaps = [];
    private readonly HashSet<uint> _unavailableMapFloorIds = [];
    private MapObjectPlacement? _selectedMapObject;
    private Fallout2PrototypeResolver? _mapPrototypeResolver;
    private int? _selectedMapElevationIndex;
    private int _selectedMapDiagnosticLayerIndex;
    private bool _isMapInspector;
    private bool _isMapGridDataLoaded;
    private bool _mapFloorTilesLoaded;
    private int _mapFloorTilesRevision;
    private int? _selectedMapGridIndex;
    private string _mapGridCellDisplay = "Click a grid cell to keep its square-grid coordinates and raw value selected.";
    private string _mapGridHoverDisplay = "Move the pointer over a grid cell to inspect its square-grid coordinates and raw value.";
    private string _mapObjectStatusDisplay = "MAP objects have not been read.";
    private string _mapObjectDetailsDisplay = "Select a parsed object to inspect its raw fields.";
    private string _mapObjectPrototypeDiagnosticStatus = "Select an Item or Scenery object to inspect its Fallout 2 prototype.";
    private string _mapObjectPrototypeDiagnosticDetails = string.Empty;
    private IReadOnlyList<string> _mapObjectPrototypeRawByteLines = [];
    private string _mapFloorTilesStatusDisplay = "Select the floor-tile diagnostic layer to load this map's tiles.";
    private string _previewMessage = "Select a .FRM or .MAP entry to inspect.";
    private string _previewDetails = "Select a .FRM or .MAP entry to inspect.";
    private string _previewTitle = "Resource Inspector";
    private string _paletteStatus = "Palette discovery uses the open archive or a configured game-data folder.";
    private int _selectedDirectionIndex;
    private int _selectedFrameIndex;
    private bool _isCheckerboardBackground;
    private bool _isDisposed;
    private bool _isExportingPng;

    public ArchiveBrowserViewModel()
    {
        ExtensionOptions = new ObservableCollection<string>(
            new[] { ArchiveEntryFilter.AllExtensions }.Concat(SupportedExtensions));
        DirectionOptions = new ObservableCollection<string>(
            DirectionNames.Select((name, index) => $"{name} ({index})"));
        _playbackTimer = new DispatcherTimer
        {
            Interval = GetPlaybackInterval()
        };
        _playbackTimer.Tick += PlaybackTimer_Tick;
        MapObjectPlacements.CollectionChanged += MapObjectPlacements_CollectionChanged;
    }

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

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ArchiveEntry> Entries { get; } = [];

    public ObservableCollection<ArchiveEntry> VisibleEntries { get; } = [];

    public ObservableCollection<string> ExtensionOptions { get; }

    public ObservableCollection<string> DirectionOptions { get; }

    public ObservableCollection<string> OverwritePolicyOptions { get; } =
        ["Skip existing", "Overwrite existing", "Cancel operation"];

    public ObservableCollection<int> FrameOptions { get; } = [];

    public ObservableCollection<int> MapElevationOptions { get; } = [];

    public ObservableCollection<string> MapDiagnosticLayerOptions { get; } =
        [
            "Square tiles 100 x 100",
            "Object hexes 200 x 200",
            "Floor tiles 100 x 100",
            "Floor tiles isometric (experimental)",
            "Object markers isometric"
        ];

    public ObservableCollection<MapObjectPlacement> MapObjectPlacements { get; } = [];

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
            ? MapObjectPlacements
            : _filteredMapObjectPlacements;

    public ObservableCollection<MapObjectPlacement> SortedMapObjectPlacements { get; } = [];

    public string MapObjectSearchResultCountDisplay =>
        string.Format(
            CultureInfo.CurrentCulture,
            "{0} / {1} objects",
            FilteredMapObjectPlacements.Count,
            MapObjectPlacements.Count);

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
                SetSelectedMapObject(null);
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

            var query = _mapObjectSearchText.Trim();
            if (_selectedMapObject is not null
                && !MatchesMapObjectFilters(_selectedMapObject))
            {
                SetSelectedMapObject(null);
            }

            RefreshFilteredMapObjectPlacements();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                RefreshVisibleEntries();
            }
        }
    }

    public string SelectedExtension
    {
        get => _selectedExtension;
        set
        {
            if (SetProperty(ref _selectedExtension, value))
            {
                RefreshVisibleEntries();
            }
        }
    }

    public string ArchivePathDisplay => _archivePath ?? "No archive selected";

    public string ArchiveFormatDisplay => _archive is null ? "Not detected" : GetArchiveFormatDisplay(_archive);

    public string EntryCountDisplay => _archivePath is null ? "Not loaded" : $"{Entries.Count:N0} entries";

    public string SelectionDisplay =>
        _selectedEntryCount == 0 ? "No selection" : $"{_selectedEntryCount:N0} selected";

    public string GameDataDirectoryDisplay => _gameDataDirectory ?? "Not configured";

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool HasSelection => _selectedEntryCount > 0;

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(CanOpenArchive));
                NotifyExtractionAvailability();
            }
        }
    }

    public bool CanOpenArchive => !IsLoading;

    public bool IsExtracting
    {
        get => _isExtracting;
        private set
        {
            if (SetProperty(ref _isExtracting, value))
            {
                OnPropertyChanged(nameof(CanExtractSelected));
                OnPropertyChanged(nameof(CanExtractVisible));
                OnPropertyChanged(nameof(CanExtractAll));
            }
        }
    }

    public bool CanExtractSelected => !IsLoading && !IsExtracting && _archive is not null && _selectedEntryCount > 0;

    public bool CanExtractVisible => !IsLoading && !IsExtracting && _archive is not null && VisibleEntries.Count > 0;

    public bool CanExtractAll => !IsLoading && !IsExtracting && _archive is not null && Entries.Count > 0;

    public int SelectedOverwritePolicyIndex { get; set; } = (int)OverwritePolicy.SkipExisting;

    public OverwritePolicy SelectedOverwritePolicy => (OverwritePolicy)SelectedOverwritePolicyIndex;

    public ArchiveEntry? SelectedEntry
    {
        get => _selectedEntry;
        set => SetProperty(ref _selectedEntry, value);
    }

    public Bitmap? PreviewBitmap
    {
        get => _previewBitmap;
        private set
        {
            if (SetProperty(ref _previewBitmap, value))
            {
                OnPropertyChanged(nameof(HasPreview));
            }
        }
    }

    public bool HasPreview => PreviewBitmap is not null;

    public bool CanPlay => _previewDocument is not null && _playback.CanPlay;

    public bool CanExportPng =>
        !_isDisposed
        && !_isExportingPng
        && _previewDocument is not null
        && _falloutPalette is not null
        && (uint)_selectedDirectionIndex < (uint)_previewDocument.Directions.Count
        && (uint)_selectedFrameIndex
            < (uint)_previewDocument.Directions[_selectedDirectionIndex].Frames.Count;

    public string SuggestedPngFileName
    {
        get
        {
            var fileName = SelectedEntry is null
                ? "FRM"
                : Path.GetFileNameWithoutExtension(SelectedEntry.Path);
            var directionName = _previewDocument is not null
                && (uint)_selectedDirectionIndex < (uint)_previewDocument.Directions.Count
                ? _previewDocument.Directions[_selectedDirectionIndex].Name
                : DirectionNames[0];
            var unsanitized = $"{fileName}_{directionName}_Frame{_selectedFrameIndex + 1:D2}";
            var invalidCharacters = Path.GetInvalidFileNameChars()
                .Concat("<>:\"/\\|?*")
                .ToHashSet();
            var sanitized = new string(unsanitized
                .Select(character => invalidCharacters.Contains(character) ? '_' : character)
                .ToArray())
                .TrimEnd('.', ' ');
            return $"{(string.IsNullOrWhiteSpace(sanitized) ? "FRM" : sanitized)}.png";
        }
    }

    public bool IsPlaying => _playback.IsPlaying;

    public string PlayPauseButtonText => IsPlaying ? "Pause" : "Play";

    public string CurrentFrameDisplay => _previewDocument is null
        ? "Frame —"
        : $"Frame {_playback.CurrentFrameIndex + 1} / {_playback.CurrentDirectionFrameCount}";

    public bool IsLoopEnabled
    {
        get => _playback.IsLoopEnabled;
        set
        {
            if (_playback.IsLoopEnabled == value)
            {
                return;
            }

            _playback.IsLoopEnabled = value;
            OnPropertyChanged();
        }
    }

    public double PlaybackSpeed
    {
        get => _playback.FramesPerSecond;
        set
        {
            if (value is < FrmPlaybackController.MinimumFramesPerSecond
                or > FrmPlaybackController.MaximumFramesPerSecond)
            {
                return;
            }

            var framesPerSecond = (int)Math.Round(value);
            if (_playback.FramesPerSecond == framesPerSecond)
            {
                return;
            }

            _playback.FramesPerSecond = framesPerSecond;
            _playbackTimer.Interval = GetPlaybackInterval();
            OnPropertyChanged();
            OnPropertyChanged(nameof(PlaybackSpeedDisplay));
        }
    }

    public string PlaybackSpeedDisplay => $"{_playback.FramesPerSecond} FPS";

    public bool IsCheckerboardBackground
    {
        get => _isCheckerboardBackground;
        set => SetProperty(ref _isCheckerboardBackground, value);
    }

    public string PreviewMessage
    {
        get => _previewMessage;
        private set => SetProperty(ref _previewMessage, value);
    }

    public string PreviewDetails
    {
        get => _previewDetails;
        private set => SetProperty(ref _previewDetails, value);
    }

    public string PreviewTitle
    {
        get => _previewTitle;
        private set => SetProperty(ref _previewTitle, value);
    }

    public string PaletteStatus
    {
        get => _paletteStatus;
        private set => SetProperty(ref _paletteStatus, value);
    }

    public bool IsMapInspector => _isMapInspector;

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
                if (_selectedMapObject is not null
                    && _selectedMapObject.ElevationGroup != value)
                {
                    SetSelectedMapObject(null);
                }

                OnPropertyChanged(nameof(SelectedMapGridValues));
                OnPropertyChanged(nameof(HasMapGrid));
                OnPropertyChanged(nameof(IsMapGridEmpty));
                OnPropertyChanged(nameof(IsMapHexGridEmpty));
                OnPropertyChanged(nameof(IsMapFloorTileLayerVisible));
                OnPropertyChanged(nameof(IsMapFloorTileLayerEmpty));
                OnPropertyChanged(nameof(IsMapIsometricFloorLayerVisible));
                OnPropertyChanged(nameof(IsMapIsometricFloorLayerEmpty));
                OnPropertyChanged(nameof(IsMapIsometricObjectMarkerLayerVisible));
                OnPropertyChanged(nameof(IsMapIsometricViewVisible));
                UpdateSelectedMapGridDisplay();
                MapGridHoverDisplay = "Move the pointer over a grid cell to inspect its square-grid coordinates and raw value.";
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
                OnPropertyChanged(nameof(IsMapGridEmpty));
                OnPropertyChanged(nameof(IsMapSquareGridVisible));
                OnPropertyChanged(nameof(IsMapHexGridVisible));
                OnPropertyChanged(nameof(IsMapHexGridEmpty));
                OnPropertyChanged(nameof(IsMapFloorTileLayerVisible));
                OnPropertyChanged(nameof(IsMapFloorTileLayerEmpty));
                OnPropertyChanged(nameof(IsMapIsometricFloorLayerVisible));
                OnPropertyChanged(nameof(IsMapIsometricFloorLayerEmpty));
                OnPropertyChanged(nameof(IsMapIsometricObjectMarkerLayerVisible));
                OnPropertyChanged(nameof(IsMapIsometricViewVisible));
            }
        }
    }

    public int? SelectedMapGridIndex
    {
        get => _selectedMapGridIndex;
        set
        {
            if (value is int index && (uint)index >= MapTileGridDecoder.ValuesPerGrid)
            {
                return;
            }

            if (SetProperty(ref _selectedMapGridIndex, value))
            {
                UpdateSelectedMapGridDisplay();
            }
        }
    }

    public uint[]? SelectedMapGridValues =>
        _selectedMapElevationIndex is int elevationIndex
            ? _mapTileGridData?.GetElevation(elevationIndex)?.RawValues
            : null;

    public bool HasMapGrid => SelectedMapGridValues is not null;

    public bool IsMapGridEmpty =>
        _isMapInspector
        && _selectedMapDiagnosticLayerIndex == 0
        && _isMapGridDataLoaded
        && !HasMapGrid;

    public bool IsMapSquareGridVisible =>
        _isMapInspector && _selectedMapDiagnosticLayerIndex == 0 && HasMapGrid;

    public bool IsMapHexGridVisible =>
        _isMapInspector && _selectedMapDiagnosticLayerIndex == 1;

    public bool IsMapFloorTileLayerVisible =>
        _isMapInspector && _selectedMapDiagnosticLayerIndex == MapFloorTilesDiagnosticLayerIndex && HasMapGrid;

    public bool IsMapFloorTileLayerEmpty =>
        _isMapInspector
        && _selectedMapDiagnosticLayerIndex == MapFloorTilesDiagnosticLayerIndex
        && _isMapGridDataLoaded
        && !HasMapGrid;

    public bool IsMapIsometricFloorLayerVisible =>
        _isMapInspector
        && _selectedMapDiagnosticLayerIndex == MapIsometricFloorTilesDiagnosticLayerIndex
        && HasMapGrid;

    public bool IsMapIsometricFloorLayerEmpty =>
        _isMapInspector
        && _selectedMapDiagnosticLayerIndex == MapIsometricFloorTilesDiagnosticLayerIndex
        && _isMapGridDataLoaded
        && !HasMapGrid;

    public bool IsMapIsometricObjectMarkerLayerVisible =>
        _isMapInspector
        && _selectedMapDiagnosticLayerIndex == MapIsometricObjectMarkersDiagnosticLayerIndex;

    public bool IsMapIsometricViewVisible =>
        IsMapIsometricFloorLayerVisible || IsMapIsometricObjectMarkerLayerVisible;

    public IReadOnlyDictionary<uint, Bitmap> MapFloorTileBitmaps => _mapFloorTileBitmaps;

    public int MapFloorTilesRevision => _mapFloorTilesRevision;

    public IReadOnlySet<uint> UnavailableMapFloorIds => _unavailableMapFloorIds;

    public string MapFloorTilesStatusDisplay
    {
        get => _mapFloorTilesStatusDisplay;
        private set => SetProperty(ref _mapFloorTilesStatusDisplay, value);
    }

    public bool IsMapHexGridEmpty =>
        IsMapHexGridVisible
        && !MapObjectPlacements.Any(placement =>
            placement.ElevationGroup == _selectedMapElevationIndex
            && placement.HexX is int x
            && (uint)x < MapObjectPlacementDecoder.HexGridWidth
            && placement.HexY is int y
            && (uint)y < MapObjectPlacementDecoder.HexGridHeight);

    public string MapGridCellDisplay
    {
        get => _mapGridCellDisplay;
        private set => SetProperty(ref _mapGridCellDisplay, value);
    }

    public string MapGridHoverDisplay
    {
        get => _mapGridHoverDisplay;
        private set => SetProperty(ref _mapGridHoverDisplay, value);
    }

    public string MapObjectStatusDisplay
    {
        get => _mapObjectStatusDisplay;
        private set => SetProperty(ref _mapObjectStatusDisplay, value);
    }

    public MapObjectPlacement? SelectedMapObject
    {
        get => _selectedMapObject;
        set
        {
            if (value is null
                && _isReorderingSortedMapObjectPlacements
                && _selectedMapObject is not null
                && FilteredMapObjectPlacements.Any(
                    placement => placement.RecordIndex == _selectedMapObject.RecordIndex))
            {
                _sortedListReportedTransientSelectionClear = true;
                return;
            }

            if (value is not null
                && !MatchesMapObjectFilters(value))
            {
                SetSelectedMapObject(null);
                return;
            }

            if (value is not null)
            {
                if (MapElevationOptions.Contains(value.ElevationGroup))
                {
                    SelectedMapElevationIndex = value.ElevationGroup;
                }

                SelectedMapDiagnosticLayerIndex = 1;
            }

            SetSelectedMapObject(value);
        }
    }

    public int? SelectedMapObjectRecordIndex => _selectedMapObject?.RecordIndex;

    public void SelectMapObjectFromFloorMarker(MapObjectPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        SetSelectedMapObject(placement);
    }

    public void SelectMapObjectFromIsometricMarker(int? recordIndex)
    {
        var placement = recordIndex is int selectedRecordIndex
            ? MapObjectPlacements.FirstOrDefault(candidate =>
                candidate.RecordIndex == selectedRecordIndex
                && candidate.ElevationGroup == SelectedMapElevationIndex
                && candidate.ParentRecordIndex is null)
            : null;
        SetSelectedMapObject(placement);
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

    public int SelectedDirectionIndex
    {
        get => _selectedDirectionIndex;
        set
        {
            if (_previewDocument is null
                || value < 0
                || value >= _previewDocument.Directions.Count
                || value == _selectedDirectionIndex)
            {
                return;
            }

            _playback.SelectDirection(value);
            _playbackTimer.Stop();
            SetProperty(ref _selectedDirectionIndex, value);
            SetProperty(ref _selectedFrameIndex, _playback.CurrentFrameIndex, nameof(SelectedFrameIndex));
            RefreshPreviewFrame();
            NotifyPlaybackState();
            OnPropertyChanged(nameof(SuggestedPngFileName));
        }
    }

    public int SelectedFrameIndex
    {
        get => _selectedFrameIndex;
        set
        {
            if (_previewDocument is null
                || value < 0
                || value >= _playback.CurrentDirectionFrameCount
                || value == _selectedFrameIndex)
            {
                return;
            }

            _playback.SelectFrame(value);
            _playbackTimer.Stop();
            SetProperty(ref _selectedFrameIndex, value);
            RefreshPreviewFrame();
            NotifyPlaybackState();
            OnPropertyChanged(nameof(SuggestedPngFileName));
        }
    }

    public void TogglePlayback()
    {
        if (_isDisposed)
        {
            return;
        }

        if (_playback.IsPlaying)
        {
            _playback.Pause();
            _playbackTimer.Stop();
        }
        else if (_playback.Play())
        {
            _playbackTimer.Start();
        }

        NotifyPlaybackState();
    }

    public async Task ExportSelectedFramePngAsync(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!CanExportPng || _previewDocument is null || _falloutPalette is null)
        {
            throw new InvalidOperationException(
                "PNG export requires a selected, valid FRM frame and a resolved Fallout COLOR.PAL.");
        }

        var document = _previewDocument;
        var palette = _falloutPalette;
        var directionIndex = _selectedDirectionIndex;
        var frameIndex = _selectedFrameIndex;
        _isExportingPng = true;
        OnPropertyChanged(nameof(CanExportPng));

        try
        {
            await Task.Run(
                () => FrmPngExporter.Write(document, directionIndex, frameIndex, palette, destination));
        }
        finally
        {
            _isExportingPng = false;
            OnPropertyChanged(nameof(CanExportPng));
        }
    }

    public async Task ExtractSelectedEntriesAsync(
        string destinationDirectory,
        IEnumerable<ArchiveEntry>? selectedEntries = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new ArgumentException("A destination directory is required before extracting entries.", nameof(destinationDirectory));
        }

        var entries = (selectedEntries ?? Entries).ToList();
        if (entries.Count == 0)
        {
            StatusMessage = "No selected entries are available to extract.";
            return;
        }

        await ExtractEntriesAsync(entries, destinationDirectory, "selected entries", cancellationToken);
    }

    public async Task ExtractVisibleEntriesAsync(string destinationDirectory, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new ArgumentException("A destination directory is required before extracting visible entries.", nameof(destinationDirectory));
        }

        if (VisibleEntries.Count == 0)
        {
            StatusMessage = "No visible entries are available to extract.";
            return;
        }

        await ExtractEntriesAsync(VisibleEntries.ToList(), destinationDirectory, "visible entries", cancellationToken);
    }

    public async Task ExtractAllEntriesAsync(string destinationDirectory, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new ArgumentException("A destination directory is required before extracting all entries.", nameof(destinationDirectory));
        }

        if (Entries.Count == 0)
        {
            StatusMessage = "This archive contains no entries to extract.";
            return;
        }

        await ExtractEntriesAsync(Entries.ToList(), destinationDirectory, "all archive entries", cancellationToken);
    }

    public async Task ExtractEntriesAsync(
        IReadOnlyList<ArchiveEntry> entries,
        string destinationDirectory,
        string scopeLabel,
        CancellationToken cancellationToken = default)
    {
        if (_archive is null)
        {
            throw new InvalidOperationException("No archive is loaded, so no entries can be extracted.");
        }

        if (entries.Count == 0)
        {
            StatusMessage = $"No {scopeLabel} are available to extract.";
            return;
        }

        var service = new ArchiveExtractionService();
        IsExtracting = true;
        StatusMessage = $"Preparing to extract {scopeLabel} to {destinationDirectory}...";

        try
        {
            var summary = await service.ExtractAsync(
                _archive,
                entries,
                destinationDirectory,
                new ExtractionOptions(SelectedOverwritePolicy),
                new Progress<ExtractionProgress>(progress =>
                {
                    var current = progress.CurrentEntryPath is null
                        ? $"{progress.CompletedEntries}/{progress.TotalEntries}"
                        : $"{progress.CurrentEntryPath} ({progress.CompletedEntries}/{progress.TotalEntries})";

                    StatusMessage = progress.Message is null
                        ? $"Extracting {scopeLabel}: {current}"
                        : $"Extracting {scopeLabel}: {progress.Message} ({current})";
                }),
                cancellationToken);

            var finalStatus = summary.WasCancelled ? "Cancelled" : summary.HasFailures ? "Completed with warnings" : "Completed";
            StatusMessage = $"{finalStatus}: {summary.Successful} extracted, {summary.Skipped} skipped, {summary.Failed} failed, {summary.Cancelled} cancelled.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = $"Extraction of {scopeLabel} was cancelled.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Extraction of {scopeLabel} failed: {exception.Message}";
        }
        finally
        {
            IsExtracting = false;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _playbackTimer.Stop();
        _playbackTimer.Tick -= PlaybackTimer_Tick;
        _previewCancellation?.Cancel();
        _previewCancellation = null;
        _mapFloorTileCancellation?.Cancel();
        ClearPreview();
    }

    public async Task LoadArchiveAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _previewCancellation?.Cancel();
        ClearPreview();
        _archive = null;
        _archivePath = null;
        SetEntries([]);
        OnPropertyChanged(nameof(ArchivePathDisplay));
        OnPropertyChanged(nameof(ArchiveFormatDisplay));
        IsLoading = true;
        StatusMessage = "Detecting archive format and reading its directory...";
        try
        {
            var archive = await Task.Run(
                () => FalloutArchive.Open(path, cancellationToken),
                cancellationToken);

            _archive = archive;
            _archivePath = archive.Path;
            SetEntries(archive.BrowserEntries);
            StatusMessage = $"Loaded {Entries.Count:N0} {GetArchiveFormatDisplay(archive)} entries.";
            OnPropertyChanged(nameof(ArchivePathDisplay));
            OnPropertyChanged(nameof(ArchiveFormatDisplay));
        }
        catch (Exception exception) when (
            exception is InvalidDataException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            StatusMessage = $"Could not load Fallout archive: {exception.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task PreviewSelectedEntryAsync()
    {
        _previewCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _previewCancellation = cancellation;
        ClearPreview();

        try
        {
            var selectedEntry = SelectedEntry;
            if (selectedEntry is null)
            {
                PreviewMessage = "Select a .FRM or .MAP entry to inspect.";
                return;
            }

            var archive = _archive;
            if (archive is null)
            {
                PreviewMessage = "The selected entry is not available in the loaded archive.";
                return;
            }

            if (string.Equals(selectedEntry.Extension, ".MAP", StringComparison.OrdinalIgnoreCase))
            {
                _isMapInspector = true;
                OnPropertyChanged(nameof(IsMapInspector));
                PreviewTitle = "MAP Inspector";
                PreviewMessage = "Reading MAP header and tile-grid metadata...";
                using var mapBuffer = new MemoryStream();
                await archive.CopyEntryToAsync(selectedEntry, mapBuffer, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (mapBuffer.Length != selectedEntry.UncompressedSize)
                {
                    throw new InvalidDataException(
                        $"MAP entry declares {selectedEntry.UncompressedSize} bytes but {mapBuffer.Length} bytes were read.");
                }

                var mapData = mapBuffer.GetBuffer().AsMemory(0, checked((int)mapBuffer.Length));
                var metadata = MapHeaderDecoder.Decode(
                    mapData,
                    archive.Game);
                var tileGrids = MapTileGridDecoder.Decode(mapData, metadata);
                MapObjectPlacementData? objectPlacements = null;
                string? objectPlacementError = null;
                var prototypeResolver = new Fallout2PrototypeResolver(archive);
                _mapPrototypeResolver = prototypeResolver;
                try
                {
                    objectPlacements = await MapObjectPlacementDecoder.DecodeAsync(
                        mapData,
                        metadata,
                        archive,
                        cancellation.Token,
                        prototypeResolver);
                }
                catch (InvalidDataException exception)
                {
                    System.Diagnostics.Debug.WriteLine($"MAP object decoding failed: {exception}");
                    objectPlacementError = "MAP object data is malformed or truncated; header and grid inspection remain available.";
                }

                _mapTileGridData = tileGrids;
                MapElevationOptions.Clear();
                var availableElevations = tileGrids.Grids
                    .Select(grid => grid.ElevationIndex)
                    .Concat(objectPlacements?.Objects.Select(placement => placement.ElevationGroup) ?? [])
                    .Distinct()
                    .Order();
                foreach (var elevation in availableElevations)
                {
                    MapElevationOptions.Add(elevation);
                }

                _selectedMapElevationIndex = tileGrids.Grids.Count > 0
                    ? tileGrids.Grids[0].ElevationIndex
                    : MapElevationOptions.Count > 0
                        ? MapElevationOptions[0]
                        : null;
                _selectedMapDiagnosticLayerIndex = 0;

                _isMapGridDataLoaded = true;
                _isReplacingMapObjectPlacements = true;
                try
                {
                    MapObjectPlacements.Clear();
                    if (objectPlacements is not null)
                    {
                        foreach (var placement in objectPlacements.Objects)
                        {
                            MapObjectPlacements.Add(placement);
                        }
                    }
                }
                finally
                {
                    _isReplacingMapObjectPlacements = false;
                }

                RefreshFilteredMapObjectPlacements();

                SelectedMapObject = null;
                MapObjectStatusDisplay = objectPlacementError
                    ?? objectPlacements?.StatusDisplay
                    ?? "MAP object data is unavailable.";
                OnPropertyChanged(nameof(SelectedMapElevationIndex));
                OnPropertyChanged(nameof(SelectedMapGridValues));
                OnPropertyChanged(nameof(HasMapGrid));
                OnPropertyChanged(nameof(IsMapGridEmpty));
                OnPropertyChanged(nameof(SelectedMapDiagnosticLayerIndex));
                OnPropertyChanged(nameof(IsMapSquareGridVisible));
                OnPropertyChanged(nameof(IsMapHexGridVisible));
                OnPropertyChanged(nameof(IsMapHexGridEmpty));
                OnPropertyChanged(nameof(IsMapFloorTileLayerVisible));
                OnPropertyChanged(nameof(IsMapFloorTileLayerEmpty));
                OnPropertyChanged(nameof(IsMapIsometricFloorLayerVisible));
                OnPropertyChanged(nameof(IsMapIsometricFloorLayerEmpty));
                OnPropertyChanged(nameof(IsMapIsometricObjectMarkerLayerVisible));
                OnPropertyChanged(nameof(IsMapIsometricViewVisible));
                UpdateSelectedMapGridDisplay();
                MapGridHoverDisplay = "Move the pointer over a grid cell to inspect its square-grid coordinates and raw value.";
                PreviewMessage = selectedEntry.Path;
                PreviewDetails = BuildMapHeaderDetails(metadata, tileGrids);
                PaletteStatus = "Neutral diagnostic cells; values are not encoded as terrain. Square-grid cells are separate from object hex coordinates. Object layouts requiring PRO subtype data and nested inventories may be shown as unsupported.";
                return;
            }

            if (!string.Equals(selectedEntry.Extension, ".FRM", StringComparison.OrdinalIgnoreCase))
            {
                PreviewMessage = "Select a .FRM or .MAP entry to inspect.";
                return;
            }

            PreviewTitle = "FRM Preview";
            if (selectedEntry.UncompressedSize > FrmDecoder.MaximumFileSize)
            {
                PreviewMessage = $"FRM preview is limited to {FrmDecoder.MaximumFileSize / (1024 * 1024)} MiB per file.";
                return;
            }

            PreviewMessage = "Reading and decoding FRM...";
            using var buffer = new MemoryStream(checked((int)selectedEntry.UncompressedSize));
            await archive.CopyEntryToAsync(selectedEntry, buffer, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();

            var document = await Task.Run(
                () => FrmDecoder.Decode(buffer.GetBuffer().AsMemory(0, checked((int)buffer.Length))),
                cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();

            var paletteResolution = await ResolvePaletteAsync(archive, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            _previewDocument = document;
            _falloutPalette = paletteResolution.Palette;
            _playback.SetFrameCounts(document.Directions.Select(direction => direction.Frames.Count).ToArray());
            _playbackTimer.Stop();
            _selectedDirectionIndex = _playback.DirectionIndex;
            _selectedFrameIndex = _playback.CurrentFrameIndex;
            OnPropertyChanged(nameof(SelectedDirectionIndex));
            OnPropertyChanged(nameof(SelectedFrameIndex));
            FrameOptions.Clear();
            for (var index = 0; index < document.FramesPerDirection; index++)
            {
                FrameOptions.Add(index);
            }

            PreviewDetails = BuildPreviewDetails(document, 0, 0);
            PreviewMessage = selectedEntry.Path;
            PaletteStatus = paletteResolution.Palette is null
                ? paletteResolution.Diagnostic ?? "No valid Fallout palette is available; showing diagnostic grayscale."
                : archive.Game == FalloutGame.Fallout2
                    ? "Fallout 2 COLOR.PAL applied; visual color match is not verified."
                    : $"Full-color Fallout 1 palette: {paletteResolution.Source}";
            NotifyPlaybackState();
            OnPropertyChanged(nameof(CanExportPng));
            OnPropertyChanged(nameof(SuggestedPngFileName));
            RefreshPreviewFrame();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (
            exception is InvalidDataException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            if (!cancellation.IsCancellationRequested)
            {
                PreviewMessage = string.Equals(SelectedEntry?.Extension, ".MAP", StringComparison.OrdinalIgnoreCase)
                    ? $"Could not inspect MAP: {exception.Message}"
                    : $"Could not preview FRM: {exception.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(_previewCancellation, cancellation))
            {
                _previewCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    public async Task SetGameDataDirectoryAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Game data directory '{fullPath}' does not exist.");
        }

        _gameDataDirectory = fullPath;
        OnPropertyChanged(nameof(GameDataDirectoryDisplay));

        if (SelectedEntry is not null
            && string.Equals(SelectedEntry.Extension, ".FRM", StringComparison.OrdinalIgnoreCase))
        {
            await PreviewSelectedEntryAsync();
        }
        else
        {
            PaletteStatus = $"Configured game-data folder: {fullPath}";
        }
    }

    public void ReportError(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        StatusMessage = message;
    }

    public void InspectMapGridCell(int? x, int? y, int? serializedIndex, uint? rawValue)
    {
        MapGridHoverDisplay = rawValue is uint value
            ? $"Elevation slot {_selectedMapElevationIndex} | Square-grid ({x}, {y}) | Serialized index {serializedIndex} | Raw value 0x{value:X8}"
            : "Move the pointer over a grid cell to inspect its square-grid coordinates and raw value.";
    }

    public void SelectMapGridCell(int? serializedIndex)
    {
        SelectedMapGridIndex = serializedIndex;
    }

    public async Task PrepareMapFloorTilesAsync()
    {
        if (_isDisposed
            || !IsMapInspector
            || _selectedMapDiagnosticLayerIndex is not (
                MapFloorTilesDiagnosticLayerIndex
                or MapIsometricFloorTilesDiagnosticLayerIndex
                or MapIsometricObjectMarkersDiagnosticLayerIndex)
            || _mapFloorTilesLoaded
            || _mapFloorTileCancellation is not null)
        {
            return;
        }

        var archive = _archive;
        var mapTileGridData = _mapTileGridData;
        if (archive is null || mapTileGridData is null)
        {
            MapFloorTilesStatusDisplay = "No decoded MAP tile grids are available.";
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _mapFloorTileCancellation = cancellation;
        MapFloorTilesStatusDisplay = "Resolving floor tile IDs and loading unique FRM assets...";
        var cancellationToken = cancellation.Token;

        try
        {
            if (archive.Game != FalloutGame.Fallout2 || archive.Format != ArchiveFormat.Dat2)
            {
                _mapFloorTilesLoaded = true;
                MapFloorTilesStatusDisplay = "Floor tile asset resolution requires a Fallout 2 DAT2 archive.";
                return;
            }

            var floorIds = MapFloorTileDiagnosticGeometry.GetDrawableFloorIds(
                mapTileGridData.Grids.SelectMany(grid => grid.RawValues));
            if (floorIds.Count == 0)
            {
                _mapFloorTilesLoaded = true;
                MapFloorTilesStatusDisplay = "This map has no drawable floor IDs (render-suppressed cells and CE empty-grid IDs are excluded).";
                return;
            }

            var resolutions = await Fallout2TileAssetResolver.ResolveFloorsAsync(
                archive,
                floorIds,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var paletteResolution = await ResolvePaletteAsync(archive, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var palette = paletteResolution.Palette;
            if (palette is null)
            {
                foreach (var floorId in floorIds)
                {
                    _unavailableMapFloorIds.Add(checked((uint)floorId));
                }

                _mapFloorTilesLoaded = true;
                PaletteStatus = paletteResolution.Diagnostic
                    ?? "No Fallout palette is available; floor tiles cannot be color-decoded.";
                MapFloorTilesStatusDisplay =
                    $"Could not render {floorIds.Count} distinct floor IDs because no Fallout palette is available.";
                return;
            }

            _falloutPalette = palette;
            PaletteStatus = archive.Game == FalloutGame.Fallout2
                ? $"Fallout 2 palette applied from {paletteResolution.Source ?? "the available game data"}; visual color match is not verified."
                : $"Fallout palette applied from {paletteResolution.Source ?? "the available game data"}.";

            var failures = new List<string>();
            foreach (var floorId in floorIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var floorIdKey = checked((uint)floorId);
                if (!resolutions.TryGetValue(floorId, out var resolution) || !resolution.Success)
                {
                    _unavailableMapFloorIds.Add(floorIdKey);
                    failures.Add($"ID {floorId}: {resolution?.Error ?? "no resolver result"}");
                    continue;
                }

                if (_mapFloorBitmapCache.TryGet(resolution.ResourcePath!, out var cachedBitmap)
                    && cachedBitmap is not null)
                {
                    _mapFloorTileBitmaps[floorIdKey] = cachedBitmap;
                    continue;
                }

                if (resolution.Entry!.UncompressedSize is <= 0 or > FrmDecoder.MaximumFileSize)
                {
                    _unavailableMapFloorIds.Add(floorIdKey);
                    failures.Add($"ID {floorId}: FRM size is outside the supported preview limit.");
                    continue;
                }

                try
                {
                    using var buffer = new MemoryStream(checked((int)resolution.Entry.UncompressedSize));
                    await archive.CopyEntryToAsync(resolution.Entry, buffer, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (buffer.Length != resolution.Entry.UncompressedSize)
                    {
                        throw new InvalidDataException(
                            $"FRM entry declares {resolution.Entry.UncompressedSize} bytes but {buffer.Length} bytes were read.");
                    }

                    var document = await Task.Run(
                        () => FrmDecoder.Decode(buffer.GetBuffer().AsMemory(0, checked((int)buffer.Length))),
                        cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (document.Directions.Count == 0 || document.Directions[0].Frames.Count == 0)
                    {
                        throw new InvalidDataException("FRM has no frame in its first direction.");
                    }

                    var frame = document.Directions[0].Frames[0];
                    var bitmap = CreateTileBitmap(frame, palette);
                    var bitmapSize = checked((long)frame.Width * frame.Height * 4);
                    if (!_mapFloorBitmapCache.TryAdd(
                            resolution.ResourcePath!,
                            bitmap,
                            bitmapSize,
                            out var cacheError))
                    {
                        bitmap.Dispose();
                        _unavailableMapFloorIds.Add(floorIdKey);
                        failures.Add($"ID {floorId}: {cacheError}");
                        continue;
                    }

                    _mapFloorTileBitmaps[floorIdKey] = bitmap;
                }
                catch (Exception exception) when (
                    exception is InvalidDataException
                    or IOException
                    or UnauthorizedAccessException
                    or ArgumentException
                    or NotSupportedException
                    or OverflowException
                    or InvalidOperationException)
                {
                    _unavailableMapFloorIds.Add(floorIdKey);
                    failures.Add($"ID {floorId}: {exception.Message}");
                }
            }

            _mapFloorTilesLoaded = true;
            MapFloorTilesStatusDisplay = failures.Count == 0
                ? $"Loaded {_mapFloorTileBitmaps.Count:N0} of {floorIds.Count:N0} distinct drawable floor IDs; bitmap cache uses {_mapFloorBitmapCache.CachedBytes / (1024 * 1024):N0} MiB. Visual match is not verified."
                : $"Loaded {_mapFloorTileBitmaps.Count:N0} of {floorIds.Count:N0} distinct drawable floor IDs; {_unavailableMapFloorIds.Count:N0} unavailable. {string.Join(" | ", failures.Take(3))}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is InvalidDataException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or InvalidOperationException)
        {
            _mapFloorTilesLoaded = true;
            if (_mapTileGridData is not null)
            {
                foreach (var floorId in MapFloorTileDiagnosticGeometry.GetDrawableFloorIds(
                             _mapTileGridData.Grids.SelectMany(grid => grid.RawValues)))
                {
                    _unavailableMapFloorIds.Add(checked((uint)floorId));
                }
            }

            MapFloorTilesStatusDisplay = $"Could not load floor tiles: {exception.Message}";
        }
        finally
        {
            if (ReferenceEquals(_mapFloorTileCancellation, cancellation))
            {
                _mapFloorTileCancellation = null;
            }

            NotifyMapFloorTileAssetsChanged();
        }
    }

    public void UpdateSelectionCount(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        if (_selectedEntryCount == count)
        {
            return;
        }

        _selectedEntryCount = count;
        OnPropertyChanged(nameof(SelectionDisplay));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanExtractSelected));
    }

    public void SetEntries(IEnumerable<ArchiveEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        Entries.Clear();
        foreach (var entry in entries)
        {
            Entries.Add(entry);
        }

        SelectedEntry = null;
        _selectedEntryCount = 0;
        RefreshVisibleEntries();
        OnPropertyChanged(nameof(EntryCountDisplay));
        OnPropertyChanged(nameof(SelectionDisplay));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanExtractAll));
        OnPropertyChanged(nameof(CanExtractSelected));
    }

    private void RefreshVisibleEntries()
    {
        var filteredEntries = ArchiveEntryFilter.Apply(Entries, SearchText, SelectedExtension);
        VisibleEntries.Clear();
        foreach (var entry in filteredEntries)
        {
            VisibleEntries.Add(entry);
        }

        OnPropertyChanged(nameof(CanExtractVisible));
    }

    private void NotifyExtractionAvailability()
    {
        OnPropertyChanged(nameof(CanExtractSelected));
        OnPropertyChanged(nameof(CanExtractVisible));
        OnPropertyChanged(nameof(CanExtractAll));
    }

    private void RefreshPreviewFrame()
    {
        if (_previewDocument is null)
        {
            return;
        }

        var direction = _previewDocument.Directions[SelectedDirectionIndex];
        var frame = direction.Frames[SelectedFrameIndex];
        var requiredLength = checked(frame.Pixels.Length * 4);
        if (_renderBuffer.Length < requiredLength)
        {
            Array.Resize(ref _renderBuffer, requiredLength);
        }

        var rgba = _renderBuffer.AsSpan(0, requiredLength);
        if (_falloutPalette is not null)
        {
            _falloutPalette.CopyRgba(frame, rgba);
        }
        else
        {
            for (var index = 0; index < frame.Pixels.Length; index++)
            {
                var pixel = frame.Pixels[index];
                var outputIndex = index * 4;
                rgba[outputIndex] = pixel;
                rgba[outputIndex + 1] = pixel;
                rgba[outputIndex + 2] = pixel;
                rgba[outputIndex + 3] = pixel == 0 ? (byte)0 : byte.MaxValue;
            }
        }

        var bitmap = new WriteableBitmap(
            new PixelSize(frame.Width, frame.Height),
            new Vector(96, 96),
            PixelFormat.Rgba8888,
            AlphaFormat.Premul);
        using (var framebuffer = bitmap.Lock())
        {
            if (framebuffer.RowBytes == frame.Width * 4)
            {
                System.Runtime.InteropServices.Marshal.Copy(_renderBuffer, 0, framebuffer.Address, requiredLength);
            }
            else
            {
                for (var row = 0; row < frame.Height; row++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(
                        _renderBuffer,
                        row * frame.Width * 4,
                        framebuffer.Address + row * framebuffer.RowBytes,
                        frame.Width * 4);
                }
            }
        }

        var previousBitmap = PreviewBitmap;
        PreviewBitmap = bitmap;
        previousBitmap?.Dispose();
        PreviewDetails = BuildPreviewDetails(_previewDocument, SelectedDirectionIndex, SelectedFrameIndex);
        OnPropertyChanged(nameof(CurrentFrameDisplay));
        OnPropertyChanged(nameof(CanExportPng));
        OnPropertyChanged(nameof(SuggestedPngFileName));
    }

    private static Bitmap CreateTileBitmap(FrmFrame frame, FalloutPalette palette)
    {
        var byteCount = checked(frame.Pixels.Length * 4);
        var rgba = new byte[byteCount];
        palette.CopyRgba(frame, rgba);
        var bitmap = new WriteableBitmap(
            new PixelSize(frame.Width, frame.Height),
            new Vector(96, 96),
            PixelFormat.Rgba8888,
            AlphaFormat.Premul);

        try
        {
            using var framebuffer = bitmap.Lock();
            for (var row = 0; row < frame.Height; row++)
            {
                System.Runtime.InteropServices.Marshal.Copy(
                    rgba,
                    row * frame.Width * 4,
                    framebuffer.Address + row * framebuffer.RowBytes,
                    frame.Width * 4);
            }

            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private void PlaybackTimer_Tick(object? sender, EventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        if (_playback.Advance())
        {
            SetProperty(
                ref _selectedFrameIndex,
                _playback.CurrentFrameIndex,
                nameof(SelectedFrameIndex));
            RefreshPreviewFrame();
            if (!_playback.IsPlaying)
            {
                _playbackTimer.Stop();
                NotifyPlaybackState();
            }
        }
        else if (!_playback.IsPlaying)
        {
            _playbackTimer.Stop();
            NotifyPlaybackState();
        }
    }

    private void NotifyPlaybackState()
    {
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(PlayPauseButtonText));
        OnPropertyChanged(nameof(CurrentFrameDisplay));
    }

    private TimeSpan GetPlaybackInterval() =>
        TimeSpan.FromMilliseconds(1000d / _playback.FramesPerSecond);

    private async Task<FalloutPaletteResolution> ResolvePaletteAsync(
        IGameArchive archive,
        CancellationToken cancellationToken) =>
        await _paletteResolver.ResolveAsync(archive, _gameDataDirectory, cancellationToken);

    private static string BuildPreviewDetails(FrmDocument document, int directionIndex, int frameIndex)
    {
        var direction = document.Directions[directionIndex];
        var frame = direction.Frames[frameIndex];
        return $"Version {document.Version} | FRM rate metadata {document.FramesPerSecond} FPS | Action frame {document.ActionFrame}"
            + Environment.NewLine
            + $"Direction {direction.Name} | {frameIndex + 1}/{document.FramesPerDirection}"
            + Environment.NewLine
            + $"Frame {frame.Width} x {frame.Height} | Frame offset ({frame.OffsetX}, {frame.OffsetY})"
            + Environment.NewLine
            + $"Direction shift ({direction.ShiftX}, {direction.ShiftY})";
    }

    private static string BuildMapHeaderDetails(
        MapHeaderMetadata metadata,
        MapTileGridData tileGrids)
    {
        var name = string.Concat(metadata.RawName.Select(value =>
            value is >= 0x20 and <= 0x7e ? (char)value : '.'));
        var elevationSlots = tileGrids.Grids.Count == 0
            ? "none"
            : string.Join(", ", tileGrids.Grids.Select(grid => grid.ElevationIndex));
        return $"Fallout {(metadata.Game == FalloutGame.Fallout1 ? 1 : 2)} | MAP version {metadata.Version} | Header {MapHeaderDecoder.HeaderSize} bytes"
            + Environment.NewLine
            + $"Name bytes: {name} | {Convert.ToHexString(metadata.RawName)}"
            + Environment.NewLine
            + $"Entry length: {metadata.EntryLength} bytes"
            + Environment.NewLine
            + $"Entering tile/elevation/rotation: {metadata.EnteringTile} / {metadata.EnteringElevation} / {metadata.EnteringRotation}"
            + Environment.NewLine
            + $"Flags: 0x{metadata.Flags:X8} | Present elevations: {metadata.PresentElevationCount}/3"
            + Environment.NewLine
            + $"Global variables: {metadata.GlobalVariableCount} at {metadata.GlobalVariablesOffset} (length {metadata.GlobalVariablesLength})"
            + Environment.NewLine
            + $"Local variables: {metadata.LocalVariableCount} at {metadata.LocalVariablesOffset} (length {metadata.LocalVariablesLength})"
            + Environment.NewLine
            + $"Tile section: {metadata.TileDataOffset} (length {metadata.TileDataLength})"
            + Environment.NewLine
            + $"Decoded grids: {tileGrids.Grids.Count} | Grid dimensions: {MapTileGridDecoder.GridWidth} x {MapTileGridDecoder.GridHeight} serialized values"
            + Environment.NewLine
            + $"Values decoded: {tileGrids.Grids.Sum(grid => grid.RawValues.Length)} | Present elevation slots: {elevationSlots}"
            + Environment.NewLine
            + $"Script index: {metadata.ScriptIndex} | Darkness: {metadata.Darkness} | Map index: {metadata.MapIndex} | Last-visit field (raw): {metadata.LastVisitTimeRaw}"
            + Environment.NewLine
            + $"Reserved header bytes preserved: {metadata.ReservedHeaderData.Length}";
    }

    private static string GetArchiveFormatDisplay(IGameArchive archive) =>
        (archive.Game, archive.Format) switch
        {
            (FalloutGame.Fallout1, ArchiveFormat.Dat1) => "Fallout 1 DAT1",
            (FalloutGame.Fallout2, ArchiveFormat.Dat2) => "Fallout 2 DAT2",
            _ => $"{archive.Game} {archive.Format}"
        };

    private void ClearPreview()
    {
        _mapFloorTileCancellation?.Cancel();
        _mapFloorTileCancellation = null;
        _mapObjectPrototypeDiagnosticCancellation?.Cancel();
        _mapObjectPrototypeDiagnosticCancellation = null;
        _mapPrototypeResolver = null;
        _mapFloorBitmapCache.Dispose();
        if (!_isDisposed)
        {
            _mapFloorBitmapCache = new MapScopedAssetCache<Bitmap>(
                MapScopedAssetCache<Bitmap>.DefaultByteLimit,
                bitmap => bitmap.Dispose());
        }

        _mapFloorTileBitmaps.Clear();
        _unavailableMapFloorIds.Clear();
        _mapFloorTilesLoaded = false;
        MapFloorTilesStatusDisplay = "Select the floor-tile diagnostic layer to load this map's tiles.";
        NotifyMapFloorTileAssetsChanged();
        _mapTileGridData = null;
        MapObjectPlacements.Clear();
        SelectedMapObject = null;
        _selectedMapElevationIndex = null;
        _selectedMapGridIndex = null;
        _selectedMapDiagnosticLayerIndex = 0;
        _isMapInspector = false;
        _isMapGridDataLoaded = false;
        MapElevationOptions.Clear();
        OnPropertyChanged(nameof(IsMapInspector));
        OnPropertyChanged(nameof(SelectedMapElevationIndex));
        OnPropertyChanged(nameof(SelectedMapGridIndex));
        OnPropertyChanged(nameof(SelectedMapDiagnosticLayerIndex));
        OnPropertyChanged(nameof(SelectedMapGridValues));
        OnPropertyChanged(nameof(HasMapGrid));
        OnPropertyChanged(nameof(IsMapGridEmpty));
        OnPropertyChanged(nameof(IsMapSquareGridVisible));
        OnPropertyChanged(nameof(IsMapHexGridVisible));
        OnPropertyChanged(nameof(IsMapHexGridEmpty));
        OnPropertyChanged(nameof(IsMapFloorTileLayerVisible));
        OnPropertyChanged(nameof(IsMapFloorTileLayerEmpty));
        OnPropertyChanged(nameof(IsMapIsometricFloorLayerVisible));
        OnPropertyChanged(nameof(IsMapIsometricFloorLayerEmpty));
        OnPropertyChanged(nameof(IsMapIsometricObjectMarkerLayerVisible));
        OnPropertyChanged(nameof(IsMapIsometricViewVisible));
        MapGridCellDisplay = "Click a grid cell to keep its square-grid coordinates and raw value selected.";
        MapGridHoverDisplay = "Move the pointer over a grid cell to inspect its square-grid coordinates and raw value.";
        MapObjectStatusDisplay = "MAP objects have not been read.";
        MapObjectDetailsDisplay = "Select a parsed object to inspect its raw fields.";
        MapObjectPrototypeDiagnosticStatus =
            "Select an Item or Scenery object to inspect its Fallout 2 prototype.";
        MapObjectPrototypeDiagnosticDetails = string.Empty;
        MapObjectPrototypeRawByteLines = [];
        _playbackTimer.Stop();
        _playback.SetFrameCounts([]);
        var previousBitmap = PreviewBitmap;
        PreviewBitmap = null;
        previousBitmap?.Dispose();
        _previewDocument = null;
        _falloutPalette = null;
        FrameOptions.Clear();
        _selectedFrameIndex = 0;
        _selectedDirectionIndex = 0;
        OnPropertyChanged(nameof(SelectedFrameIndex));
        OnPropertyChanged(nameof(SelectedDirectionIndex));
        PreviewDetails = "Select a .FRM or .MAP entry to inspect.";
        PreviewTitle = "Resource Inspector";
        PaletteStatus = "Palette discovery uses the open archive or a configured game-data folder.";
        NotifyPlaybackState();
        OnPropertyChanged(nameof(CanExportPng));
        OnPropertyChanged(nameof(SuggestedPngFileName));
    }

    private void UpdateSelectedMapGridDisplay()
    {
        if (_selectedMapGridIndex is not int index
            || SelectedMapGridValues is not { Length: MapTileGridDecoder.ValuesPerGrid } values)
        {
            MapGridCellDisplay = "Click a grid cell to keep its square-grid coordinates and raw value selected.";
            return;
        }

        var (x, y) = MapTileGridDecoder.GetSquareGridCoordinates(index);
        var rawValue = values[index];
        var fields = new MapGridCellValueFields(rawValue);
        var floorIdNote = fields.FloorArtId == 1
            ? " (CE empty-grid check treats ID 1 as no tile)"
            : string.Empty;
        var roofIdNote = fields.RoofArtId == 1
            ? " (CE roof renderer skips ID 1)"
            : string.Empty;
        MapGridCellDisplay =
            $"Selected | Elevation slot {_selectedMapElevationIndex} | Square-grid ({x}, {y}) | Serialized index {index} | Raw value 0x{rawValue:X8}"
            + Environment.NewLine
            + $"Floor tile ID 0x{fields.FloorArtId:X3}{floorIdNote}; flag nibble 0x{fields.FloorFlagNibble:X1}; bit 12 "
            + (fields.FloorRenderSuppressionBitIsSet ? "set (CE skips floor draw)" : "clear (CE allows floor draw)")
            + Environment.NewLine
            + $"Roof tile ID 0x{fields.RoofArtId:X3}{roofIdNote}; flag nibble 0x{fields.RoofFlagNibble:X1}; bit 28 "
            + (fields.RoofRenderSuppressionBitIsSet ? "set (CE skips roof draw)" : "clear (CE allows roof draw)");
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void SetSelectedMapObject(MapObjectPlacement? placement)
    {
        if (placement is not null
            && !MatchesMapObjectFilters(placement))
        {
            placement = null;
        }

        if (SetProperty(ref _selectedMapObject, placement, nameof(SelectedMapObject)))
        {
            OnPropertyChanged(nameof(SelectedMapObjectRecordIndex));
            MapObjectDetailsDisplay = placement?.Details
                ?? "Select a parsed object to inspect its raw fields.";
            UpdateMapObjectPrototypeDiagnostic(placement);
        }
    }

    private void UpdateMapObjectPrototypeDiagnostic(MapObjectPlacement? placement)
    {
        _mapObjectPrototypeDiagnosticCancellation?.Cancel();
        _mapObjectPrototypeDiagnosticCancellation = null;
        MapObjectPrototypeDiagnosticDetails = string.Empty;
        MapObjectPrototypeRawByteLines = [];

        if (placement is null)
        {
            MapObjectPrototypeDiagnosticStatus =
                "Select an Item or Scenery object to inspect its Fallout 2 prototype.";
            return;
        }

        MapObjectPrototypeDiagnosticDetails =
            $"Selected object PID: 0x{unchecked((uint)placement.RawPid):X8}";
        if (placement.PidTypeIdentifier is not (0 or 2))
        {
            MapObjectPrototypeDiagnosticStatus =
                "Prototype diagnostics are supported only for Fallout 2 Item and Scenery objects.";
            return;
        }

        var resolver = _mapPrototypeResolver;
        if (resolver is null)
        {
            MapObjectPrototypeDiagnosticStatus =
                "Prototype diagnostics require an open Fallout 2 DAT2 archive.";
            return;
        }

        var cancellation = new CancellationTokenSource();
        _mapObjectPrototypeDiagnosticCancellation = cancellation;
        MapObjectPrototypeDiagnosticStatus = "Loading prototype diagnostics...";
        _ = LoadMapObjectPrototypeDiagnosticAsync(
            placement,
            resolver,
            cancellation);
    }

    private async Task LoadMapObjectPrototypeDiagnosticAsync(
        MapObjectPlacement placement,
        Fallout2PrototypeResolver resolver,
        CancellationTokenSource cancellation)
    {
        var cancellationToken = cancellation.Token;
        try
        {
            var diagnostic = await resolver.ResolveAsync(
                placement.RawPid,
                cancellationToken);
            var byteLines = await Task.Run(
                () => FormatPrototypeBytes(diagnostic.RawBytes, cancellationToken),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (!ReferenceEquals(_selectedMapObject, placement)
                || !ReferenceEquals(_mapPrototypeResolver, resolver))
            {
                return;
            }

            MapObjectPrototypeDiagnosticStatus = diagnostic.Status;
            MapObjectPrototypeDiagnosticDetails = FormatPrototypeDiagnosticDetails(diagnostic);
            MapObjectPrototypeRawByteLines = byteLines;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            if (ReferenceEquals(_selectedMapObject, placement)
                && ReferenceEquals(_mapPrototypeResolver, resolver))
            {
                MapObjectPrototypeDiagnosticStatus =
                    $"Could not load prototype diagnostics: {exception.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(_mapObjectPrototypeDiagnosticCancellation, cancellation))
            {
                _mapObjectPrototypeDiagnosticCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private static string FormatPrototypeDiagnosticDetails(Fallout2PrototypeDiagnostic diagnostic)
    {
        var fileName = diagnostic.VerifiedFileName is not null
            ? $"Verified file: {diagnostic.VerifiedFileName}"
            : diagnostic.ListedFileName is not null
                ? $"Listed file (unverified): {diagnostic.ListedFileName}"
                : "File: unavailable";
        var prototypePid = diagnostic.PrototypePid is int pid
            ? $"Prototype PID: 0x{unchecked((uint)pid):X8}"
            : "Prototype PID: unavailable";
        var subtype = diagnostic.Subtype is int value
            ? $"Subtype at 0x20: {value}"
            : "Subtype at 0x20: unavailable";
        var fileSize = diagnostic.FileSize is long size
            ? $"File size: {size:N0} bytes"
            : "File size: unavailable";

        return string.Join(
            Environment.NewLine,
            $"Selected object PID: 0x{unchecked((uint)diagnostic.RequestedPid):X8}",
            fileName,
            prototypePid,
            subtype,
            fileSize);
    }

    private static string[] FormatPrototypeBytes(
        ReadOnlyMemory<byte> rawBytes,
        CancellationToken cancellationToken)
    {
        const int bytesPerLine = 16;
        var lineCount = (rawBytes.Length + bytesPerLine - 1) / bytesPerLine;
        var lines = new string[lineCount];
        for (var lineIndex = 0; lineIndex < lineCount; lineIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var byteOffset = lineIndex * bytesPerLine;
            var byteCount = Math.Min(bytesPerLine, rawBytes.Length - byteOffset);
            var line = new StringBuilder(10 + byteCount * 3);
            line.Append(byteOffset.ToString("X8", CultureInfo.InvariantCulture));
            line.Append("  ");
            for (var index = 0; index < byteCount; index++)
            {
                if (index > 0)
                {
                    line.Append(' ');
                }

                line.Append(rawBytes.Span[byteOffset + index].ToString("X2", CultureInfo.InvariantCulture));
            }

            lines[lineIndex] = line.ToString();
        }

        return lines;
    }

    private void MapObjectPlacements_CollectionChanged(
        object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (_selectedMapObject is not null
            && !MapObjectPlacements.Any(
                placement => placement.RecordIndex == _selectedMapObject.RecordIndex))
        {
            SetSelectedMapObject(null);
        }

        if (!_isReplacingMapObjectPlacements)
        {
            if (!HasActiveMapObjectFilters)
            {
                RefreshSortedMapObjectPlacements();
                NotifyMapObjectSearchFeedbackChanged();
            }
            else
            {
                RefreshFilteredMapObjectPlacements();
            }
        }
    }

    private void RefreshFilteredMapObjectPlacements()
    {
        _filteredMapObjectPlacements = !HasActiveMapObjectFilters
            ? []
            : MapObjectPlacements
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
                    SetSelectedMapObject(retainedSelection);
                }
                else
                {
                    if (_sortedListReportedTransientSelectionClear)
                    {
                        OnPropertyChanged(nameof(SelectedMapObject));
                    }
                }
            }
        }

        _sortedListReportedTransientSelectionClear = false;
    }

    private static bool HasCoordinates(MapObjectPlacement placement) =>
        placement.HexY.HasValue && placement.HexX.HasValue;

    private void NotifyMapObjectSearchFeedbackChanged()
    {
        OnPropertyChanged(nameof(MapObjectSearchResultCountDisplay));
        OnPropertyChanged(nameof(HasNoMatchingMapObjects));
    }

    private bool HasActiveMapObjectFilters =>
        !string.IsNullOrWhiteSpace(_mapObjectSearchText)
        || _selectedMapObjectTypeFilter.PidTypeIdentifier.HasValue;

    private bool MatchesMapObjectFilters(MapObjectPlacement placement)
    {
        return (!_selectedMapObjectTypeFilter.PidTypeIdentifier.HasValue
                || placement.PidTypeIdentifier == _selectedMapObjectTypeFilter.PidTypeIdentifier.Value)
            && MatchesMapObjectSearch(placement, _mapObjectSearchText.Trim());
    }

    private static bool MatchesMapObjectSearch(
        MapObjectPlacement placement,
        string query)
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

    private void NotifyMapFloorTileAssetsChanged()
    {
        unchecked
        {
            _mapFloorTilesRevision++;
        }

        OnPropertyChanged(nameof(MapFloorTileBitmaps));
        OnPropertyChanged(nameof(UnavailableMapFloorIds));
        OnPropertyChanged(nameof(MapFloorTilesRevision));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
