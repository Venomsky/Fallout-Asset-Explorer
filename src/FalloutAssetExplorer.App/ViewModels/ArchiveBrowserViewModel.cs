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
using FalloutAssetExplorer.App.Views;
using FalloutAssetExplorer.Core;

namespace FalloutAssetExplorer.App.ViewModels;

public sealed class ArchiveBrowserViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly string[] SupportedExtensions = [".FRM", ".MAP", ".PRO", ".LST", ".INT"];

    private string _searchText = string.Empty;
    private bool _isReplacingMapObjectPlacements;
    private string _selectedExtension = ArchiveEntryFilter.AllExtensions;
    private string? _archivePath;
    private string _statusMessage = "Open a Fallout DAT archive to begin.";
    private int _selectedEntryCount;
    private ArchiveEntry? _selectedEntry;
    private bool _isLoading;
    private bool _isExtracting;
    private IGameArchive? _archive;
    private readonly FalloutPaletteResolver _paletteResolver = new();
    private CancellationTokenSource? _previewCancellation;
    private CancellationTokenSource? _mapFloorTileCancellation;
    private CancellationTokenSource? _mapObjectPrototypeDiagnosticCancellation;
    private string? _gameDataDirectory;
    private MapTileGridData? _mapTileGridData;
    private MapScopedAssetCache<Bitmap> _mapFloorBitmapCache =
        new(MapScopedAssetCache<Bitmap>.DefaultByteLimit, bitmap => bitmap.Dispose());
    private readonly Dictionary<uint, Bitmap> _mapFloorTileBitmaps = [];
    private readonly HashSet<uint> _unavailableMapFloorIds = [];
    private Fallout2PrototypeResolver? _mapPrototypeResolver;
    private bool _isMapGridDataLoaded;
    private bool _mapFloorTilesLoaded;
    private int _mapFloorTilesRevision;
    private int? _selectedMapGridIndex;
    private string _mapGridCellDisplay = "Click a grid cell to keep its square-grid coordinates and raw value selected.";
    private string _mapGridHoverDisplay = "Move the pointer over a grid cell to inspect its square-grid coordinates and raw value.";
    private string _mapObjectStatusDisplay = "MAP objects have not been read.";
    private string _mapFloorTilesStatusDisplay = "Select the floor-tile diagnostic layer to load this map's tiles.";
    private string _previewMessage = "Select a .FRM or .MAP entry to inspect.";
    private string _previewDetails = "Select a .FRM or .MAP entry to inspect.";
    private string _previewTitle = "Resource Inspector";
    private string _paletteStatus = "Palette discovery uses the open archive or a configured game-data folder.";
    private bool _isDisposed;

    public ArchiveBrowserViewModel()
    {
        ExtensionOptions = new ObservableCollection<string>(
            new[] { ArchiveEntryFilter.AllExtensions }.Concat(SupportedExtensions));
        MapObjectPlacements.CollectionChanged += MapObjectPlacements_CollectionChanged;
        MapInspector.PropertyChanged += MapInspector_PropertyChanged;
        MapInspector.SelectedMapObjectChanged += MapInspector_SelectedMapObjectChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ArchiveEntry> Entries { get; } = [];

    public ObservableCollection<ArchiveEntry> VisibleEntries { get; } = [];

    public ObservableCollection<string> ExtensionOptions { get; }

    public ObservableCollection<string> OverwritePolicyOptions { get; } =
        ["Skip existing", "Overwrite existing", "Cancel operation"];

    public MapInspectorViewModel MapInspector { get; } = new();

    public FrmPreviewViewModel FrmPreview { get; } = new();

    public ObservableCollection<MapObjectPlacement> MapObjectPlacements { get; } = [];

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

    public bool IsMapInspector => MapInspector.IsMapInspector;

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
        MapInspector.SelectedMapElevationIndex is int elevationIndex
            ? _mapTileGridData?.GetElevation(elevationIndex)?.RawValues
            : null;

    public bool HasMapGrid => SelectedMapGridValues is not null;

    public bool IsMapGridEmpty =>
        IsMapInspector
        && MapInspector.SelectedMapDiagnosticLayerIndex == MapInspectorViewModel.SquareTilesLayerIndex
        && _isMapGridDataLoaded
        && !HasMapGrid;

    public bool IsMapSquareGridVisible =>
        IsMapInspector
        && MapInspector.SelectedMapDiagnosticLayerIndex == MapInspectorViewModel.SquareTilesLayerIndex
        && HasMapGrid;

    public bool IsMapHexGridVisible =>
        IsMapInspector
        && MapInspector.SelectedMapDiagnosticLayerIndex == MapInspectorViewModel.ObjectHexesLayerIndex;

    public bool IsMapFloorTileLayerVisible =>
        IsMapInspector
        && MapInspector.SelectedMapDiagnosticLayerIndex == MapInspectorViewModel.FloorTilesLayerIndex
        && HasMapGrid;

    public bool IsMapFloorTileLayerEmpty =>
        IsMapInspector
        && MapInspector.SelectedMapDiagnosticLayerIndex == MapInspectorViewModel.FloorTilesLayerIndex
        && _isMapGridDataLoaded
        && !HasMapGrid;

    public bool IsMapIsometricFloorLayerVisible =>
        IsMapInspector
        && MapInspector.SelectedMapDiagnosticLayerIndex == MapInspectorViewModel.IsometricFloorTilesLayerIndex
        && HasMapGrid;

    public bool IsMapIsometricFloorLayerEmpty =>
        IsMapInspector
        && MapInspector.SelectedMapDiagnosticLayerIndex == MapInspectorViewModel.IsometricFloorTilesLayerIndex
        && _isMapGridDataLoaded
        && !HasMapGrid;

    public bool IsMapIsometricObjectMarkerLayerVisible =>
        IsMapInspector
        && MapInspector.SelectedMapDiagnosticLayerIndex == MapInspectorViewModel.IsometricObjectMarkersLayerIndex;

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
        FrmPreview.Dispose();
        _previewCancellation?.Cancel();
        _previewCancellation = null;
        _mapFloorTileCancellation?.Cancel();
        MapInspector.PropertyChanged -= MapInspector_PropertyChanged;
        MapInspector.SelectedMapObjectChanged -= MapInspector_SelectedMapObjectChanged;
        MapObjectPlacements.CollectionChanged -= MapObjectPlacements_CollectionChanged;
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

            var previewRoute = PreviewRouteSelector.Select(selectedEntry.Extension);
            if (previewRoute == PreviewRoute.Map)
            {
                MapInspector.SetMapInspectorActive(true);
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

                cancellation.Token.ThrowIfCancellationRequested();

                _mapTileGridData = tileGrids;
                var availableElevations = tileGrids.Grids
                    .Select(grid => grid.ElevationIndex)
                    .Concat(objectPlacements?.Objects.Select(placement => placement.ElevationGroup) ?? [])
                    .Distinct()
                    .Order()
                    .ToArray();
                int? preferredElevation = tileGrids.Grids.Count > 0
                    ? tileGrids.Grids[0].ElevationIndex
                    : null;
                MapInspector.SetAvailableElevations(availableElevations, preferredElevation);
                MapInspector.SelectedMapDiagnosticLayerIndex =
                    MapInspectorViewModel.SquareTilesLayerIndex;

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

                MapInspector.SetMapObjectPlacements(MapObjectPlacements);
                MapInspector.ClearSelectedMapObject();
                MapObjectStatusDisplay = objectPlacementError
                    ?? objectPlacements?.StatusDisplay
                    ?? "MAP object data is unavailable.";
                OnPropertyChanged(nameof(SelectedMapGridValues));
                OnPropertyChanged(nameof(HasMapGrid));
                OnPropertyChanged(nameof(IsMapGridEmpty));
                OnPropertyChanged(nameof(IsMapSquareGridVisible));
                OnPropertyChanged(nameof(IsMapHexGridVisible));
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

            if (previewRoute != PreviewRoute.Frm)
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
            PreviewMessage = selectedEntry.Path;
            PaletteStatus = paletteResolution.Palette is null
                ? paletteResolution.Diagnostic ?? "No valid Fallout palette is available; showing diagnostic grayscale."
                : archive.Game == FalloutGame.Fallout2
                    ? "Fallout 2 COLOR.PAL applied; visual color match is not verified."
                    : $"Full-color Fallout 1 palette: {paletteResolution.Source}";
            FrmPreview.SetPreview(document, paletteResolution.Palette, selectedEntry.Path);
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
            ? $"Elevation slot {MapInspector.SelectedMapElevationIndex} | Square-grid ({x}, {y}) | Serialized index {serializedIndex} | Raw value 0x{value:X8}"
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
            || MapInspector.SelectedMapDiagnosticLayerIndex is not (
                MapInspectorViewModel.FloorTilesLayerIndex
                or MapInspectorViewModel.IsometricFloorTilesLayerIndex
                or MapInspectorViewModel.IsometricObjectMarkersLayerIndex)
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
            if (!IsCurrentMapFloorTileLoad(cancellation, archive, mapTileGridData))
            {
                return;
            }

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
            if (!IsCurrentMapFloorTileLoad(cancellation, archive, mapTileGridData))
            {
                return;
            }

            var paletteResolution = await ResolvePaletteAsync(archive, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentMapFloorTileLoad(cancellation, archive, mapTileGridData))
            {
                return;
            }

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
                if (!IsCurrentMapFloorTileLoad(cancellation, archive, mapTileGridData))
                {
                    return;
                }

                MapFloorTilesStatusDisplay =
                    $"Could not render {floorIds.Count} distinct floor IDs because no Fallout palette is available.";
                return;
            }

            PaletteStatus = archive.Game == FalloutGame.Fallout2
                ? $"Fallout 2 palette applied from {paletteResolution.Source ?? "the available game data"}; visual color match is not verified."
                : $"Fallout palette applied from {paletteResolution.Source ?? "the available game data"}.";

            var failures = new List<string>();
            foreach (var floorId in floorIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsCurrentMapFloorTileLoad(cancellation, archive, mapTileGridData))
                {
                    return;
                }

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
                    if (!IsCurrentMapFloorTileLoad(cancellation, archive, mapTileGridData))
                    {
                        return;
                    }

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
                    if (!IsCurrentMapFloorTileLoad(cancellation, archive, mapTileGridData))
                    {
                        return;
                    }

                    _unavailableMapFloorIds.Add(floorIdKey);
                    failures.Add($"ID {floorId}: {exception.Message}");
                }
            }

            if (!IsCurrentMapFloorTileLoad(cancellation, archive, mapTileGridData))
            {
                return;
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
            if (!IsCurrentMapFloorTileLoad(cancellation, archive, mapTileGridData))
            {
                return;
            }

            _mapFloorTilesLoaded = true;
            foreach (var floorId in MapFloorTileDiagnosticGeometry.GetDrawableFloorIds(
                         mapTileGridData.Grids.SelectMany(grid => grid.RawValues)))
            {
                _unavailableMapFloorIds.Add(checked((uint)floorId));
            }

            MapFloorTilesStatusDisplay = $"Could not load floor tiles: {exception.Message}";
        }
        finally
        {
            if (IsCurrentMapFloorTileLoad(cancellation, archive, mapTileGridData))
            {
                _mapFloorTileCancellation = null;
                NotifyMapFloorTileAssetsChanged();
            }
        }
    }

    private bool IsCurrentMapFloorTileLoad(
        CancellationTokenSource cancellation,
        IGameArchive archive,
        MapTileGridData mapTileGridData) =>
        !_isDisposed
        && IsMapInspector
        && ReferenceEquals(_mapFloorTileCancellation, cancellation)
        && ReferenceEquals(_archive, archive)
        && ReferenceEquals(_mapTileGridData, mapTileGridData);

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

    private async Task<FalloutPaletteResolution> ResolvePaletteAsync(
        IGameArchive archive,
        CancellationToken cancellationToken) =>
        await _paletteResolver.ResolveAsync(archive, _gameDataDirectory, cancellationToken);

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
        MapInspector.SetMapObjectPlacements(MapObjectPlacements);
        MapInspector.ResetMapState();
        _selectedMapGridIndex = null;
        _isMapGridDataLoaded = false;
        MapInspector.SetMapInspectorActive(false);
        OnPropertyChanged(nameof(SelectedMapGridIndex));
        OnPropertyChanged(nameof(SelectedMapGridValues));
        OnPropertyChanged(nameof(HasMapGrid));
        OnPropertyChanged(nameof(IsMapGridEmpty));
        OnPropertyChanged(nameof(IsMapSquareGridVisible));
        OnPropertyChanged(nameof(IsMapHexGridVisible));
        OnPropertyChanged(nameof(IsMapFloorTileLayerVisible));
        OnPropertyChanged(nameof(IsMapFloorTileLayerEmpty));
        OnPropertyChanged(nameof(IsMapIsometricFloorLayerVisible));
        OnPropertyChanged(nameof(IsMapIsometricFloorLayerEmpty));
        OnPropertyChanged(nameof(IsMapIsometricObjectMarkerLayerVisible));
        OnPropertyChanged(nameof(IsMapIsometricViewVisible));
        MapGridCellDisplay = "Click a grid cell to keep its square-grid coordinates and raw value selected.";
        MapGridHoverDisplay = "Move the pointer over a grid cell to inspect its square-grid coordinates and raw value.";
        MapObjectStatusDisplay = "MAP objects have not been read.";
        if (!_isDisposed)
        {
            FrmPreview.ClearPreview();
        }

        PreviewDetails = "Select a .FRM or .MAP entry to inspect.";
        PreviewTitle = "Resource Inspector";
        PaletteStatus = "Palette discovery uses the open archive or a configured game-data folder.";
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
            $"Selected | Elevation slot {MapInspector.SelectedMapElevationIndex} | Square-grid ({x}, {y}) | Serialized index {index} | Raw value 0x{rawValue:X8}"
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

    private void UpdateMapObjectPrototypeDiagnostic(MapObjectPlacement? placement)
    {
        _mapObjectPrototypeDiagnosticCancellation?.Cancel();
        _mapObjectPrototypeDiagnosticCancellation = null;

        if (placement is null)
        {
            MapInspector.ClearMapObjectPrototypeDiagnostic();
            return;
        }

        var details = $"Selected object PID: 0x{unchecked((uint)placement.RawPid):X8}";
        if (placement.PidTypeIdentifier is not (0 or 2))
        {
            MapInspector.SetMapObjectPrototypeDiagnostic(
                "Prototype diagnostics are supported only for Fallout 2 Item and Scenery objects.",
                details,
                string.Empty,
                string.Empty,
                []);
            return;
        }

        var resolver = _mapPrototypeResolver;
        if (resolver is null)
        {
            MapInspector.SetMapObjectPrototypeDiagnostic(
                "Prototype diagnostics require an open Fallout 2 DAT2 archive.",
                details,
                string.Empty,
                string.Empty,
                []);
            return;
        }

        var cancellation = new CancellationTokenSource();
        _mapObjectPrototypeDiagnosticCancellation = cancellation;
        MapInspector.SetMapObjectPrototypeDiagnostic(
            "Loading prototype diagnostics...",
            details,
            string.Empty,
            string.Empty,
            []);
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
            var formatted = await Task.Run(
                () => (
                    Details: FormatPrototypeDiagnosticDetails(diagnostic),
                    SceneryFields: FormatPrototypeFields(diagnostic.SceneryFields),
                    ItemFields: FormatPrototypeFields(diagnostic.ItemFields),
                    ByteLines: FormatPrototypeBytes(diagnostic.RawBytes, cancellationToken)),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (!ReferenceEquals(MapInspector.SelectedMapObject, placement)
                || !ReferenceEquals(_mapPrototypeResolver, resolver))
            {
                return;
            }

            MapInspector.SetMapObjectPrototypeDiagnostic(
                diagnostic.Status,
                formatted.Details,
                formatted.SceneryFields,
                formatted.ItemFields,
                formatted.ByteLines);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            if (ReferenceEquals(MapInspector.SelectedMapObject, placement)
                && ReferenceEquals(_mapPrototypeResolver, resolver))
            {
                MapInspector.SetMapObjectPrototypeDiagnostic(
                    $"Could not load prototype diagnostics: {exception.Message}",
                    $"Selected object PID: 0x{unchecked((uint)placement.RawPid):X8}",
                    string.Empty,
                    string.Empty,
                    []);
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
            ? $"Subtype at 0x20: {value}{GetScenerySubtypeLabel(diagnostic.RequestedPid, value)}"
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

    private static string GetScenerySubtypeLabel(int pid, int subtype)
    {
        if ((uint)pid >> 24 != 2)
        {
            return string.Empty;
        }

        var name = subtype switch
        {
            0 => "Door",
            1 => "Stairs",
            2 => "Elevator",
            3 => "Ladder up",
            4 => "Ladder down",
            5 => "Generic",
            _ => null
        };
        return name is null ? string.Empty : $" ({name})";
    }

    private static string FormatPrototypeFields(
        IReadOnlyList<Fallout2PrototypeField> fields)
    {
        if (fields.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(
            Environment.NewLine,
            fields.Select(field =>
            {
                var hexadecimalFormat = field.SizeInBytes == sizeof(byte) ? "X2" : "X8";
                var hexadecimalValue =
                    $"0x{unchecked((uint)field.Value).ToString(hexadecimalFormat, CultureInfo.InvariantCulture)}";
                var valueDisplay = field.IsFlags || field.DisplayAsHex
                    ? hexadecimalValue
                    : $"{field.Value.ToString(CultureInfo.InvariantCulture)} ({hexadecimalValue})";
                if (field.Interpretation is not null)
                {
                    valueDisplay += $"; {field.Interpretation}";
                }

                return $"{field.Name}: {valueDisplay} [{field.StorageType} @ 0x{field.Offset:X2}, {field.SizeInBytes} bytes]";
            }));
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
        if (!_isReplacingMapObjectPlacements)
        {
            MapInspector.SetMapObjectPlacements(MapObjectPlacements);
        }
    }

    private void MapInspector_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MapInspectorViewModel.IsMapInspector))
        {
            OnPropertyChanged(nameof(IsMapInspector));
        }
        else if (e.PropertyName == nameof(MapInspectorViewModel.SelectedMapElevationIndex))
        {
            OnPropertyChanged(nameof(SelectedMapGridValues));
            OnPropertyChanged(nameof(HasMapGrid));
            OnPropertyChanged(nameof(IsMapGridEmpty));
            OnPropertyChanged(nameof(IsMapSquareGridVisible));
            OnPropertyChanged(nameof(IsMapFloorTileLayerVisible));
            OnPropertyChanged(nameof(IsMapFloorTileLayerEmpty));
            OnPropertyChanged(nameof(IsMapIsometricFloorLayerVisible));
            OnPropertyChanged(nameof(IsMapIsometricFloorLayerEmpty));
            OnPropertyChanged(nameof(IsMapIsometricObjectMarkerLayerVisible));
            OnPropertyChanged(nameof(IsMapIsometricViewVisible));
            UpdateSelectedMapGridDisplay();
            MapGridHoverDisplay = "Move the pointer over a grid cell to inspect its square-grid coordinates and raw value.";
        }
        else if (e.PropertyName == nameof(MapInspectorViewModel.SelectedMapDiagnosticLayerIndex))
        {
            OnPropertyChanged(nameof(IsMapGridEmpty));
            OnPropertyChanged(nameof(IsMapSquareGridVisible));
            OnPropertyChanged(nameof(IsMapHexGridVisible));
            OnPropertyChanged(nameof(IsMapFloorTileLayerVisible));
            OnPropertyChanged(nameof(IsMapFloorTileLayerEmpty));
            OnPropertyChanged(nameof(IsMapIsometricFloorLayerVisible));
            OnPropertyChanged(nameof(IsMapIsometricFloorLayerEmpty));
            OnPropertyChanged(nameof(IsMapIsometricObjectMarkerLayerVisible));
            OnPropertyChanged(nameof(IsMapIsometricViewVisible));
        }
    }

    private void MapInspector_SelectedMapObjectChanged(
        object? sender,
        MapInspectorViewModel.MapObjectSelectionChangedEventArgs e)
    {
        if (e.Source == MapInspectorViewModel.MapObjectSelectionSource.List
            && e.Placement is { } placement)
        {
            if (MapInspector.MapElevationOptions.Contains(placement.ElevationGroup))
            {
                MapInspector.SelectedMapElevationIndex = placement.ElevationGroup;
            }

            MapInspector.SelectedMapDiagnosticLayerIndex =
                MapInspectorViewModel.ObjectHexesLayerIndex;
        }

        UpdateMapObjectPrototypeDiagnostic(e.Placement);
    }

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
