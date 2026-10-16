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
    // App preview budget, not a MAP-format limit; keeps UI reads well below the archive reader's 512 MiB entry cap.
    public const long MaximumMapPreviewSizeBytes = 64L * 1024 * 1024;

    private const int MaximumIntIdentifierBytesDisplayed = 4096;
    private static readonly string[] SupportedExtensions = [".FRM", ".MAP", ".PRO", ".MSG", ".LST", ".INT"];

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
    private CancellationTokenSource? _mapObjectSpriteCancellation;
    private CancellationTokenSource? _mapObjectSpritesCancellation;
    private Task? _mapObjectSpritesTask;
    private CancellationTokenSource? _mapObjectPrototypeDiagnosticCancellation;
    private string? _gameDataDirectory;
    private MapTileGridData? _mapTileGridData;
    private MapScopedAssetCache<Bitmap> _mapFloorBitmapCache =
        new(MapScopedAssetCache<Bitmap>.DefaultByteLimit, bitmap => bitmap.Dispose());
    private MapScopedAssetCache<MapIsometricObjectSprite> _mapObjectSpriteBitmapCache =
        new(MapScopedAssetCache<MapIsometricObjectSprite>.DefaultByteLimit, sprite => sprite.Bitmap.Dispose());
    private readonly Dictionary<uint, Bitmap> _mapFloorTileBitmaps = [];
    private readonly HashSet<uint> _unavailableMapFloorIds = [];
    private Fallout2PrototypeResolver? _mapPrototypeResolver;
    private CancellationTokenSource? _standaloneProExportCancellation;
    private ArchiveEntry? _standaloneProEntry;
    private IGameArchive? _standaloneProArchive;
    private bool _isMapGridDataLoaded;
    private bool _mapFloorTilesLoaded;
    private int _mapFloorTilesRevision;
    private int? _selectedMapGridIndex;
    private int _lastMapDiagnosticLayerIndex;
    private string _mapGridCellDisplay = "Click a grid cell to keep its square-grid coordinates and raw value selected.";
    private string _mapGridHoverDisplay = "Move the pointer over a grid cell to inspect its square-grid coordinates and raw value.";
    private string _mapObjectStatusDisplay = "MAP objects have not been read.";
    private string _mapFloorTilesStatusDisplay = "Select the floor-tile diagnostic layer to load this map's tiles.";
    private string _mapObjectSpriteStatusDisplay =
        "Select a top-level Fallout 2 Item, Scenery, or Wall marker to preview its FRM sprite.";
    private string _mapObjectSpritesStatusDisplay = "Multi-sprite preview is not loaded.";
    private MapIsometricObjectSprite? _selectedMapObjectSprite;
    private IReadOnlyList<MapIsometricObjectSprite> _mapObjectSprites = [];
    private string _previewMessage = "Select a .FRM, .MAP, .PRO, .MSG, or .INT entry to inspect.";
    private string _previewDetails = "Select a .FRM, .MAP, .PRO, .MSG, or .INT entry to inspect.";
    private string _previewTitle = "Resource Inspector";
    private string _paletteStatus = "Palette discovery uses the open archive or a configured game-data folder.";
    private bool _isProInspector;
    private string _standaloneProStatus = "Select a .PRO entry to inspect.";
    private string _standaloneProDetails = string.Empty;
    private string _standaloneProFieldsDisplay = string.Empty;
    private IReadOnlyList<string> _standaloneProRawByteLines = [];
    private Fallout2StandalonePrototypeInspection? _standaloneProInspection;
    private bool _isStandaloneProExporting;
    private bool _isMsgInspector;
    private FalloutMsgParseResult? _msgParseResult;
    private string _msgStatus = "Select a .MSG entry to inspect.";
    private string _msgDetails = string.Empty;
    private bool _isIntInspector;
    private FalloutIntMetadataReadResult? _intMetadataReadResult;
    private string _intStatus = "Select a .INT entry to inspect.";
    private string _intIdentifierTableHex = string.Empty;
    private bool _isDisposed;

    public ArchiveBrowserViewModel()
    {
        ExtensionOptions = new ObservableCollection<string>(
            new[] { ArchiveEntryFilter.AllExtensions }.Concat(SupportedExtensions));
        _lastMapDiagnosticLayerIndex = MapInspector.SelectedMapDiagnosticLayerIndex;
        MapObjectPlacements.CollectionChanged += MapObjectPlacements_CollectionChanged;
        MapInspector.PropertyChanged += MapInspector_PropertyChanged;
        MapInspector.SelectedMapObjectChanged += MapInspector_SelectedMapObjectChanged;
    }

    public bool IsMsgInspector
    {
        get => _isMsgInspector;
        private set => SetProperty(ref _isMsgInspector, value);
    }

    public FalloutMsgParseResult? MsgParseResult
    {
        get => _msgParseResult;
        private set
        {
            if (SetProperty(ref _msgParseResult, value))
            {
                MsgInspector.SetParseResult(value);
            }
        }
    }

    public string MsgStatus
    {
        get => _msgStatus;
        private set => SetProperty(ref _msgStatus, value);
    }

    public string MsgDetails
    {
        get => _msgDetails;
        private set => SetProperty(ref _msgDetails, value);
    }

    public bool IsIntInspector
    {
        get => _isIntInspector;
        private set => SetProperty(ref _isIntInspector, value);
    }

    public FalloutIntMetadataReadResult? IntMetadataReadResult
    {
        get => _intMetadataReadResult;
        private set
        {
            if (SetProperty(ref _intMetadataReadResult, value))
            {
                OnPropertyChanged(nameof(HasIntDiagnostic));
                OnPropertyChanged(nameof(IntDiagnosticDisplay));
                OnPropertyChanged(nameof(IntProcedureCountDisplay));
            }
        }
    }

    public bool HasIntDiagnostic => IntMetadataReadResult?.Diagnostic is not null;

    public string IntDiagnosticDisplay =>
        IntMetadataReadResult?.Diagnostic is { } diagnostic
            ? $"{diagnostic.Code} at byte {diagnostic.ByteOffset}: {diagnostic.Message}"
            : string.Empty;

    public string IntProcedureCountDisplay =>
        IntMetadataReadResult?.Metadata is { } metadata
            ? $"Procedure count: {metadata.ProcedureCount:N0}"
            : "Procedure metadata is unavailable.";

    public string IntStatus
    {
        get => _intStatus;
        private set => SetProperty(ref _intStatus, value);
    }

    public string IntIdentifierTableHex
    {
        get => _intIdentifierTableHex;
        private set => SetProperty(ref _intIdentifierTableHex, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ArchiveEntry> Entries { get; } = [];

    public ObservableCollection<ArchiveEntry> VisibleEntries { get; } = [];

    public ObservableCollection<string> ExtensionOptions { get; }

    public ObservableCollection<string> OverwritePolicyOptions { get; } =
        ["Skip existing", "Overwrite existing", "Cancel operation"];

    public MapInspectorViewModel MapInspector { get; } = new();

    public FrmPreviewViewModel FrmPreview { get; } = new();

    public MsgInspectorViewModel MsgInspector { get; } = new();

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
        set
        {
            if (SetProperty(ref _selectedEntry, value))
            {
                OnPropertyChanged(nameof(CanCopySelectedEntryPath));
                OnPropertyChanged(nameof(SelectedEntryPathToCopy));
            }
        }
    }

    public bool CanCopySelectedEntryPath => SelectedEntryPathToCopy is not null;

    public string? SelectedEntryPathToCopy
    {
        get
        {
            var path = SelectedEntry?.Path;
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
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

    public bool IsProInspector
    {
        get => _isProInspector;
        private set
        {
            if (SetProperty(ref _isProInspector, value))
            {
                OnPropertyChanged(nameof(CanExportStandaloneProReport));
                OnPropertyChanged(nameof(CanExportStandaloneProRawBin));
            }
        }
    }

    public Fallout2StandalonePrototypeInspection? StandaloneProInspection
    {
        get => _standaloneProInspection;
        private set
        {
            if (SetProperty(ref _standaloneProInspection, value))
            {
                OnPropertyChanged(nameof(CanExportStandaloneProReport));
                OnPropertyChanged(nameof(CanExportStandaloneProRawBin));
            }
        }
    }

    public bool CanExportStandaloneProReport =>
        IsProInspector && StandaloneProInspection is not null && !_isStandaloneProExporting;

    public bool CanExportStandaloneProRawBin =>
        CanExportStandaloneProReport && StandaloneProInspection!.IsReadComplete;

    private bool IsStandaloneProExporting
    {
        get => _isStandaloneProExporting;
        set
        {
            if (SetProperty(ref _isStandaloneProExporting, value))
            {
                OnPropertyChanged(nameof(CanExportStandaloneProReport));
                OnPropertyChanged(nameof(CanExportStandaloneProRawBin));
            }
        }
    }

    public string StandaloneProStatus
    {
        get => _standaloneProStatus;
        private set => SetProperty(ref _standaloneProStatus, value);
    }

    public string StandaloneProDetails
    {
        get => _standaloneProDetails;
        private set => SetProperty(ref _standaloneProDetails, value);
    }

    public string StandaloneProFieldsDisplay
    {
        get => _standaloneProFieldsDisplay;
        private set => SetProperty(ref _standaloneProFieldsDisplay, value);
    }

    public IReadOnlyList<string> StandaloneProRawByteLines
    {
        get => _standaloneProRawByteLines;
        private set
        {
            if (SetProperty(ref _standaloneProRawByteLines, value))
            {
                OnPropertyChanged(nameof(HasStandaloneProRawBytes));
            }
        }
    }

    public bool HasStandaloneProRawBytes => StandaloneProRawByteLines.Count > 0;

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

    public string MapObjectSpriteStatusDisplay
    {
        get => _mapObjectSpriteStatusDisplay;
        private set => SetProperty(ref _mapObjectSpriteStatusDisplay, value);
    }

    public string MapObjectSpritesStatusDisplay
    {
        get => _mapObjectSpritesStatusDisplay;
        private set => SetProperty(ref _mapObjectSpritesStatusDisplay, value);
    }

    public MapIsometricObjectSprite? SelectedMapObjectSprite
    {
        get => _selectedMapObjectSprite;
        private set => SetProperty(ref _selectedMapObjectSprite, value);
    }

    public IReadOnlyList<MapIsometricObjectSprite> MapObjectSprites
    {
        get => _mapObjectSprites;
        private set => SetProperty(ref _mapObjectSprites, value);
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
        CancelMapObjectSpritesLoad();
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
        ArchiveEntry? selectedEntry = null;
        IGameArchive? previewArchive = null;
        var previewRoute = PreviewRoute.Unsupported;

        try
        {
            selectedEntry = SelectedEntry;
            if (selectedEntry is null)
            {
                PreviewMessage = "Select a .FRM, .MAP, .PRO, .MSG, or .INT entry to inspect.";
                return;
            }

            previewArchive = _archive;
            if (previewArchive is null)
            {
                PreviewMessage = "The selected entry is not available in the loaded archive.";
                return;
            }

            var archive = previewArchive;
            previewRoute = PreviewRouteSelector.Select(selectedEntry.Extension);
            if (previewRoute == PreviewRoute.Map)
            {
                MapInspector.SetMapInspectorActive(true);
                PreviewTitle = "MAP Inspector";
                if (selectedEntry.UncompressedSize < 0)
                {
                    PreviewMessage = "Could not inspect MAP: the archive reports a negative uncompressed size.";
                    return;
                }

                if (selectedEntry.UncompressedSize > MaximumMapPreviewSizeBytes)
                {
                    PreviewMessage =
                        $"MAP preview is limited to {MaximumMapPreviewSizeBytes / (1024 * 1024)} MiB "
                        + $"of uncompressed data; this entry declares {selectedEntry.UncompressedSize:N0} bytes. "
                        + "The entry was not read.";
                    return;
                }

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
                var mapDecode = await Task.Run(async () =>
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    var metadata = MapHeaderDecoder.Decode(mapData, archive.Game);
                    var tileGrids = MapTileGridDecoder.Decode(mapData, metadata);
                    var prototypeResolver = new Fallout2PrototypeResolver(archive);
                    MapObjectPlacementData? objectPlacements = null;
                    string? objectPlacementError = null;
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
                        objectPlacementError =
                            "MAP object data is malformed or truncated; header and grid inspection remain available.";
                    }

                    cancellation.Token.ThrowIfCancellationRequested();
                    return (
                        Metadata: metadata,
                        TileGrids: tileGrids,
                        ObjectPlacements: objectPlacements,
                        ObjectPlacementError: objectPlacementError,
                        PrototypeResolver: prototypeResolver);
                }, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();

                var metadata = mapDecode.Metadata;
                var tileGrids = mapDecode.TileGrids;
                var objectPlacements = mapDecode.ObjectPlacements;
                var objectPlacementError = mapDecode.ObjectPlacementError;
                _mapPrototypeResolver = mapDecode.PrototypeResolver;

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
                ScheduleMapObjectSpriteBatch();
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

            if (previewRoute == PreviewRoute.Int)
            {
                IsIntInspector = true;
                PreviewTitle = "INT Metadata Inspector";
                IntStatus = "Reading INT metadata...";

                if (selectedEntry.UncompressedSize < 0)
                {
                    IntStatus = "Could not read INT: the archive reports a negative uncompressed size.";
                    PreviewMessage = IntStatus;
                    return;
                }

                if (selectedEntry.UncompressedSize > FalloutIntMetadataReader.MaximumFileSizeBytes)
                {
                    IntStatus =
                        $"INT preview is limited to {FalloutIntMetadataReader.MaximumFileSizeBytes:N0} bytes; "
                        + "the entry was not read.";
                    PreviewMessage = IntStatus;
                    return;
                }

                PreviewMessage = "Reading INT bytes...";
                using var intBuffer = new MemoryStream();
                await archive.CopyEntryToAsync(selectedEntry, intBuffer, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (intBuffer.Length != selectedEntry.UncompressedSize)
                {
                    throw new InvalidDataException(
                        $"INT entry declares {selectedEntry.UncompressedSize} bytes but {intBuffer.Length} bytes were read.");
                }

                var intData = intBuffer.GetBuffer().AsMemory(0, checked((int)intBuffer.Length));
                var readResult = await Task.Run(
                    () => FalloutIntMetadataReader.Parse(archive.Game, intData),
                    cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();

                if (!ReferenceEquals(_previewCancellation, cancellation)
                    || !ReferenceEquals(SelectedEntry, selectedEntry)
                    || !ReferenceEquals(_archive, archive))
                {
                    return;
                }

                IntMetadataReadResult = readResult;
                if (!readResult.IsSuccess)
                {
                    IntStatus = $"Parsing failed: {IntDiagnosticDisplay}";
                    PreviewMessage = "Could not parse INT; no partial metadata was published.";
                    return;
                }

                IntIdentifierTableHex = FormatIntIdentifierTableHex(readResult.Metadata!.IdentifierTableBytes);
                IntStatus = $"Parsed {readResult.Metadata.ProcedureCount:N0} procedure records.";
                PreviewMessage = selectedEntry.Path;
                return;
            }

            if (previewRoute == PreviewRoute.Msg)
            {
                IsMsgInspector = true;
                PreviewTitle = "MSG Inspector";
                MsgStatus = "Reading MSG entry...";
                MsgDetails =
                    $"Entry: {selectedEntry.Path}{Environment.NewLine}"
                    + $"Declared size: {selectedEntry.UncompressedSize:N0} bytes. Text and audio are not decoded.";

                if (selectedEntry.UncompressedSize < 0)
                {
                    MsgStatus = "Could not read MSG: the archive reports a negative uncompressed size.";
                    PreviewMessage = MsgStatus;
                    return;
                }

                if (selectedEntry.UncompressedSize > FalloutMsgParser.MaximumFileSizeBytes)
                {
                    MsgStatus =
                        $"MSG preview is limited to {FalloutMsgParser.MaximumFileSizeBytes:N0} bytes; "
                        + "the entry was not read.";
                    PreviewMessage = MsgStatus;
                    return;
                }

                PreviewMessage = "Reading MSG bytes...";
                using var msgBuffer = new MemoryStream(checked((int)selectedEntry.UncompressedSize));
                await archive.CopyEntryToAsync(selectedEntry, msgBuffer, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (msgBuffer.Length != selectedEntry.UncompressedSize)
                {
                    throw new InvalidDataException(
                        $"MSG entry declares {selectedEntry.UncompressedSize} bytes but {msgBuffer.Length} bytes were read.");
                }

                var messageData = msgBuffer.GetBuffer().AsMemory(0, checked((int)msgBuffer.Length));
                var parseResult = await Task.Run(
                    () => FalloutMsgParser.Parse(messageData),
                    cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();

                if (!ReferenceEquals(_previewCancellation, cancellation)
                    || !ReferenceEquals(SelectedEntry, selectedEntry)
                    || !ReferenceEquals(_archive, archive))
                {
                    return;
                }

                MsgParseResult = parseResult;
                if (!parseResult.IsSuccess)
                {
                    var diagnostic = parseResult.Diagnostic!;
                    MsgStatus =
                        $"Parsing failed at byte {diagnostic.ByteOffset}: {diagnostic.Message}";
                    PreviewMessage = "Could not parse MSG; no entries were published.";
                    return;
                }

                MsgStatus = parseResult.Entries.Count == 0
                    ? "Parsing succeeded; the file contains no entries."
                    : $"Parsed {parseResult.Entries.Count:N0} entries. Duplicate IDs and source order are preserved.";
                PreviewMessage = selectedEntry.Path;
                return;
            }

            if (previewRoute == PreviewRoute.Pro)
            {
                IsProInspector = true;
                PreviewTitle = "PRO Inspector";
                StandaloneProStatus = "Reading standalone PRO entry...";
                StandaloneProDetails =
                    $"Entry: {selectedEntry.Path}{Environment.NewLine}"
                    + "Provenance: selected directly from the archive; not verified through a MAP PID or prototype list.";

                var inspection = await new Fallout2PrototypeResolver(archive)
                    .InspectStandaloneEntryAsync(selectedEntry, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();

                var formatted = await Task.Run(
                    () => (
                        Fields: FormatPrototypeFields(
                            inspection.ItemFields.Count > 0
                                ? inspection.ItemFields
                                : inspection.SceneryFields),
                        ByteLines: FormatPrototypeBytes(inspection.RawBytes, cancellation.Token)),
                    cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();

                if (!ReferenceEquals(_previewCancellation, cancellation)
                    || !ReferenceEquals(SelectedEntry, selectedEntry)
                    || !ReferenceEquals(_archive, archive))
                {
                    return;
                }

                StandaloneProStatus = inspection.Status;
                StandaloneProDetails = BuildStandalonePrototypeDetails(inspection);
                StandaloneProFieldsDisplay = formatted.Fields;
                StandaloneProRawByteLines = formatted.ByteLines;
                _standaloneProEntry = selectedEntry;
                _standaloneProArchive = archive;
                StandaloneProInspection = inspection;
                PreviewMessage = selectedEntry.Path;
                return;
            }

            if (previewRoute != PreviewRoute.Frm)
            {
                PreviewMessage = "Select a .FRM, .MAP, .PRO, .MSG, or .INT entry to inspect.";
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
                PreviewMessage = previewRoute switch
                {
                    PreviewRoute.Map => $"Could not inspect MAP: {exception.Message}",
                    PreviewRoute.Pro => $"Could not inspect standalone PRO: {exception.Message}",
                    PreviewRoute.Msg => $"Could not read MSG: {exception.Message}",
                    PreviewRoute.Int => $"Could not read INT: {exception.Message}",
                    _ => $"Could not preview FRM: {exception.Message}"
                };
                if (previewRoute == PreviewRoute.Msg)
                {
                    MsgStatus = $"Could not read MSG: {exception.Message}";
                }

                if (previewRoute == PreviewRoute.Pro
                    && ReferenceEquals(_previewCancellation, cancellation)
                    && ReferenceEquals(SelectedEntry, selectedEntry)
                    && ReferenceEquals(_archive, previewArchive))
                {
                    StandaloneProStatus = $"Could not inspect standalone PRO: {exception.Message}";
                }

                if (previewRoute == PreviewRoute.Int
                    && ReferenceEquals(_previewCancellation, cancellation)
                    && ReferenceEquals(SelectedEntry, selectedEntry)
                    && ReferenceEquals(_archive, previewArchive))
                {
                    IntStatus = $"Could not read INT: {exception.Message}";
                }
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

    public Task ExportStandaloneProReportAsync(
        Stream destination,
        Fallout2StandalonePrototypeInspection inspection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        return ExportStandaloneProDataAsync(
            destination,
            inspection,
            Encoding.UTF8.GetBytes(Fallout2PrototypeReportFormatter.Format(inspection)),
            requireCompleteRead: false,
            cancellationToken);
    }

    public Task ExportStandaloneProRawBinAsync(
        Stream destination,
        Fallout2StandalonePrototypeInspection inspection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        return ExportStandaloneProDataAsync(
            destination,
            inspection,
            inspection.RawBytes.ToArray(),
            requireCompleteRead: true,
            cancellationToken);
    }

    private async Task ExportStandaloneProDataAsync(
        Stream destination,
        Fallout2StandalonePrototypeInspection inspection,
        byte[] bytes,
        bool requireCompleteRead,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!IsCurrentStandaloneProInspection(inspection))
        {
            throw new OperationCanceledException("Export canceled because the selected PRO entry changed.");
        }

        if (requireCompleteRead && !inspection.IsReadComplete)
        {
            throw new InvalidDataException("Cannot export BIN: the PRO data was not read completely.");
        }

        if (IsStandaloneProExporting)
        {
            throw new InvalidOperationException("A PRO export is already in progress.");
        }

        using var exportCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _standaloneProExportCancellation = exportCancellation;
        IsStandaloneProExporting = true;
        try
        {
            exportCancellation.Token.ThrowIfCancellationRequested();
            if (!IsCurrentStandaloneProInspection(inspection))
            {
                throw new OperationCanceledException(exportCancellation.Token);
            }

            await destination.WriteAsync(bytes, exportCancellation.Token);
            exportCancellation.Token.ThrowIfCancellationRequested();
            if (!IsCurrentStandaloneProInspection(inspection))
            {
                throw new OperationCanceledException(exportCancellation.Token);
            }
        }
        finally
        {
            if (ReferenceEquals(_standaloneProExportCancellation, exportCancellation))
            {
                _standaloneProExportCancellation = null;
            }

            IsStandaloneProExporting = false;
        }
    }

    public bool IsCurrentStandaloneProInspection(Fallout2StandalonePrototypeInspection inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        return IsProInspector
        && ReferenceEquals(StandaloneProInspection, inspection)
        && ReferenceEquals(SelectedEntry, _standaloneProEntry)
        && ReferenceEquals(_archive, _standaloneProArchive);
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
                    var bitmap = CreateFrameBitmap(frame, palette);
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

    public async Task PrepareSelectedMapObjectSpriteAsync()
    {
        var placement = MapInspector.SelectedMapObject;
        var archive = _archive;

        if (_isDisposed
            || !IsMapInspector
            || MapInspector.SelectedMapDiagnosticLayerIndex
                != MapInspectorViewModel.IsometricObjectMarkersLayerIndex
            || placement is null
            || archive is null
            || !MapObjectPlacements.Contains(placement))
        {
            CancelMapObjectSpriteLoad();
            SelectedMapObjectSprite = null;
            MapObjectSpriteStatusDisplay =
                "Select a top-level Fallout 2 Item, Scenery, or Wall marker to preview its FRM sprite.";
            return;
        }

        if (archive.Game != FalloutGame.Fallout2 || archive.Format != ArchiveFormat.Dat2)
        {
            CancelMapObjectSpriteLoad();
            SelectedMapObjectSprite = null;
            MapObjectSpriteStatusDisplay =
                "MAP object sprite preview currently requires a Fallout 2 DAT2 archive.";
            return;
        }

        var fid = FalloutFidDecoder.Decode(placement.RawFid);
        if (!MapIsometricObjectMarkerFilter.IsSupportedSpriteTypePair(
                placement.PidTypeIdentifier,
                fid.ArtType))
        {
            CancelMapObjectSpriteLoad();
            SelectedMapObjectSprite = null;
            MapObjectSpriteStatusDisplay =
                $"Object #{placement.RecordIndex} is not an eligible object for sprite preview because of its unsupported PID/FID type pair " +
                $"({placement.PidTypeIdentifier}/{fid.ArtType}); only matching Item 0/0, Scenery 2/2, and Wall 3/3 pairs are supported. The marker remains visible.";
            return;
        }

        var isItem = placement.PidTypeIdentifier == 0;
        var spriteType = placement.PidTypeIdentifier switch
        {
            0 => "Item",
            2 => "Scenery",
            3 => "Wall",
            _ => throw new InvalidOperationException("A supported sprite pair has an unsupported PID type.")
        };
        var spriteTypeLower = spriteType.ToLowerInvariant();
        if (!isItem && placement.UnsupportedReason is { } unsupportedReason)
        {
            CancelMapObjectSpriteLoad();
            SelectedMapObjectSprite = null;
            MapObjectSpriteStatusDisplay =
                $"{spriteType} object #{placement.RecordIndex} cannot be previewed because its MAP placement is unsupported: {unsupportedReason}. The marker remains visible.";
            return;
        }

        if (placement.ParentRecordIndex is not null
            || placement.InventoryDepth != 0
            || placement.ElevationGroup != MapInspector.SelectedMapElevationIndex
            || !HasValidMapObjectHexPosition(placement))
        {
            CancelMapObjectSpriteLoad();
            SelectedMapObjectSprite = null;
            MapObjectSpriteStatusDisplay =
                $"Object #{placement.RecordIndex} is not an eligible top-level Fallout 2 {spriteType} at a valid hex on the selected elevation; its marker remains visible.";
            return;
        }

        if (SelectedMapObjectSprite is { } currentSprite
            && currentSprite.RecordIndex == placement.RecordIndex)
        {
            return;
        }

        var cacheKey = string.Create(
            CultureInfo.InvariantCulture,
            $"{placement.RawFid:X8}:{placement.RawRotation}:{placement.RawFrame}");
        if (_mapObjectSpriteBitmapCache.TryGet(cacheKey, out var cachedSprite)
            && cachedSprite is not null)
        {
            SelectedMapObjectSprite = cachedSprite with { RecordIndex = placement.RecordIndex };
            MapObjectSpriteStatusDisplay = FormatPreparedMapObjectSpriteStatus(
                cachedSprite,
                placement,
                spriteTypeLower,
                "Using cached");
            return;
        }

        if (MapObjectSprites.FirstOrDefault(
                sprite => sprite.RecordIndex == placement.RecordIndex) is { } batchSprite)
        {
            SelectedMapObjectSprite = batchSprite;
            MapObjectSpriteStatusDisplay = FormatPreparedMapObjectSpriteStatus(
                batchSprite,
                placement,
                spriteTypeLower,
                "Prepared");
            return;
        }

        if (_mapObjectSpritesCancellation is not null)
        {
            if (_mapObjectSpritesTask is { } batchTask)
            {
                await batchTask;
            }

            if (!ReferenceEquals(MapInspector.SelectedMapObject, placement)
                || !IsMapInspector
                || MapInspector.SelectedMapDiagnosticLayerIndex
                    != MapInspectorViewModel.IsometricObjectMarkersLayerIndex
                || MapInspector.SelectedMapElevationIndex != placement.ElevationGroup)
            {
                return;
            }

            if (MapObjectSprites.FirstOrDefault(
                    sprite => sprite.RecordIndex == placement.RecordIndex) is { } completedBatchSprite)
            {
                SelectedMapObjectSprite = completedBatchSprite;
                MapObjectSpriteStatusDisplay = FormatPreparedMapObjectSpriteStatus(
                    completedBatchSprite,
                    placement,
                    spriteTypeLower,
                    "Prepared");
                return;
            }
        }

        CancelMapObjectSpriteLoad();
        SelectedMapObjectSprite = null;
        MapObjectSpriteStatusDisplay = $"Loading {spriteTypeLower} sprite for object #{placement.RecordIndex}...";
        using var cancellation = new CancellationTokenSource();
        _mapObjectSpriteCancellation = cancellation;
        var cancellationToken = cancellation.Token;

        try
        {
            var result = await TryPrepareMapObjectSpriteAsync(
                placement,
                archive,
                spriteTypeLower,
                cancellationToken,
                () => IsCurrentMapObjectSpriteLoad(cancellation, archive, placement));
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentMapObjectSpriteLoad(cancellation, archive, placement))
            {
                return;
            }

            if (result.Sprite is not null)
            {
                SelectedMapObjectSprite = result.Sprite;
                MapObjectSpriteStatusDisplay = FormatPreparedMapObjectSpriteStatus(
                    result.Sprite,
                    placement,
                    spriteTypeLower,
                    "Prepared");
            }
            else if (result.Error is not null)
            {
                SelectedMapObjectSprite = null;
                MapObjectSpriteStatusDisplay = result.Error;
            }
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
            or InvalidOperationException
            or OverflowException)
        {
            if (IsCurrentMapObjectSpriteLoad(cancellation, archive, placement))
            {
                SelectedMapObjectSprite = null;
                MapObjectSpriteStatusDisplay =
                    $"Could not prepare {spriteTypeLower} sprite for object #{placement.RecordIndex}: {exception.Message}. The marker remains visible.";
            }
        }
        finally
        {
            if (ReferenceEquals(_mapObjectSpriteCancellation, cancellation))
            {
                _mapObjectSpriteCancellation = null;
            }
        }
    }

    public async Task PrepareMapObjectSpritesAsync()
    {
        var archive = _archive;
        if (_isDisposed
            || !IsMapInspector
            || !IsMapIsometricSpriteLayer(MapInspector.SelectedMapDiagnosticLayerIndex)
            || archive is null)
        {
            CancelMapObjectSpritesLoad();
            MapObjectSprites = [];
            MapObjectSpritesStatusDisplay = "Multi-sprite preview is not loaded.";
            return;
        }

        CancelMapObjectSpriteLoad();
        CancelMapObjectSpritesLoad();
        if (archive.Game != FalloutGame.Fallout2 || archive.Format != ArchiveFormat.Dat2)
        {
            MapObjectSprites = [];
            MapObjectSpritesStatusDisplay =
                "Multi-sprite preview currently requires a Fallout 2 DAT2 archive.";
            return;
        }

        if (MapInspector.SelectedMapElevationIndex is not int elevation
            || !MapIsometricFloorGeometry.TryCreateDefaultViewTransform(out var projection))
        {
            MapObjectSprites = [];
            MapObjectSpritesStatusDisplay = "No selected elevation is available for multi-sprite preview.";
            return;
        }

        var placements = MapObjectPlacements.ToArray();
        var candidates = MapIsometricObjectMarkerFilter.GetSpriteCandidates(
            placements,
            elevation,
            projection);
        MapObjectSprites = [];
        if (candidates.Count == 0)
        {
            SelectedMapObjectSprite = null;
            MapObjectSpritesStatusDisplay =
                $"No eligible Item, supported Scenery, or supported Wall objects have sprites on elevation {elevation}.";
            return;
        }

        var cancellation = new CancellationTokenSource();
        _mapObjectSpritesCancellation = cancellation;
        var cancellationToken = cancellation.Token;
        var prepared = new List<MapIsometricObjectSprite>(candidates.Count);
        var failures = new List<string>();
        SelectedMapObjectSprite = null;
        MapObjectSpritesStatusDisplay =
            $"Preparing sprites for {candidates.Count:N0} eligible objects on elevation {elevation}...";

        try
        {
            foreach (var placement in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsCurrentMapObjectSpritesLoad(
                        cancellation,
                        archive,
                        elevation,
                        placements))
                {
                    return;
                }

                var spriteType = placement.PidTypeIdentifier switch
                {
                    0 => "item",
                    2 => "scenery",
                    3 => "wall",
                    _ => throw new InvalidOperationException(
                        "A sprite candidate has an unsupported PID type.")
                };
                var result = await TryPrepareMapObjectSpriteAsync(
                    placement,
                    archive,
                    spriteType,
                    cancellationToken,
                    () => IsCurrentMapObjectSpritesLoad(
                        cancellation,
                        archive,
                        elevation,
                        placements));
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsCurrentMapObjectSpritesLoad(cancellation, archive, elevation, placements))
                {
                    return;
                }

                if (result.Sprite is { } sprite)
                {
                    prepared.Add(sprite);
                    if (MapInspector.SelectedMapObject?.RecordIndex == placement.RecordIndex)
                    {
                        SelectedMapObjectSprite = sprite;
                        MapObjectSpriteStatusDisplay = FormatPreparedMapObjectSpriteStatus(
                            sprite,
                            placement,
                            spriteType,
                            "Prepared");
                    }
                }
                else if (result.Error is { } error)
                {
                    failures.Add(error);
                    if (MapInspector.SelectedMapObject?.RecordIndex == placement.RecordIndex)
                    {
                        SelectedMapObjectSprite = null;
                        MapObjectSpriteStatusDisplay = error;
                    }
                }
            }

            if (!IsCurrentMapObjectSpritesLoad(cancellation, archive, elevation, placements))
            {
                return;
            }

            MapObjectSprites = prepared.ToArray();
            var summary =
                $"Prepared {prepared.Count:N0} of {candidates.Count:N0} eligible sprites on elevation {elevation}. " +
                "Sprite order follows MAP placements and is provisional, not verified against Fallout draw order.";
            MapObjectSpritesStatusDisplay = failures.Count == 0
                ? summary
                : $"{summary} {string.Join(" | ", failures.Take(3))}";

            if (MapInspector.SelectedMapObject is { } selected
                && prepared.FirstOrDefault(sprite => sprite.RecordIndex == selected.RecordIndex) is { } selectedSprite)
            {
                SelectedMapObjectSprite = selectedSprite;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_mapObjectSpritesCancellation, cancellation))
            {
                _mapObjectSpritesCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private async Task<(MapIsometricObjectSprite? Sprite, string? Error)> TryPrepareMapObjectSpriteAsync(
        MapObjectPlacement placement,
        IGameArchive archive,
        string spriteTypeLower,
        CancellationToken cancellationToken,
        Func<bool> isCurrent)
    {
        var cacheKey = string.Create(
            CultureInfo.InvariantCulture,
            $"{placement.RawFid:X8}:{placement.RawRotation}:{placement.RawFrame}");
        if (_mapObjectSpriteBitmapCache.TryGet(cacheKey, out var cachedSprite)
            && cachedSprite is not null)
        {
            return (cachedSprite with { RecordIndex = placement.RecordIndex }, null);
        }

        try
        {
            var resolution = await FalloutFidArtAssetResolver.ResolveAsync(
                archive,
                placement.RawFid,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!isCurrent())
            {
                return (null, null);
            }

            if (!resolution.Success || resolution.Entry is null || resolution.ResourcePath is null)
            {
                return (null,
                    $"Could not resolve {spriteTypeLower} FID for object #{placement.RecordIndex}: {resolution.Error ?? "no resource entry was returned"}. The marker remains visible.");
            }

            if (!string.Equals(resolution.Entry.Extension, ".FRM", StringComparison.OrdinalIgnoreCase))
            {
                return (null,
                    $"Resolved {spriteTypeLower} resource '{resolution.Entry.Path}' is not an FRM; the marker remains visible.");
            }

            if (resolution.Entry.UncompressedSize is <= 0 or > FrmDecoder.MaximumFileSize)
            {
                return (null,
                    $"{char.ToUpperInvariant(spriteTypeLower[0])}{spriteTypeLower[1..]} FRM '{resolution.Entry.Path}' is outside the supported preview size limit; the marker remains visible.");
            }

            using var buffer = new MemoryStream(checked((int)resolution.Entry.UncompressedSize));
            await archive.CopyEntryToAsync(resolution.Entry, buffer, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!isCurrent())
            {
                return (null, null);
            }

            if (buffer.Length != resolution.Entry.UncompressedSize)
            {
                throw new InvalidDataException(
                    $"FRM entry declares {resolution.Entry.UncompressedSize} bytes but {buffer.Length} bytes were read.");
            }

            var document = await Task.Run(
                () => FrmDecoder.Decode(buffer.GetBuffer().AsMemory(0, checked((int)buffer.Length))),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!isCurrent())
            {
                return (null, null);
            }

            if (!MapIsometricObjectSpriteFrameSelector.TrySelectFrame(
                    placement,
                    document,
                    out var directionIndex,
                    out var frame,
                    out var frameError)
                || frame is null)
            {
                return (null,
                    $"Cannot select {spriteTypeLower} sprite for object #{placement.RecordIndex}: {frameError ?? "invalid direction or frame"}. The marker remains visible.");
            }

            var paletteResolution = await ResolvePaletteAsync(archive, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!isCurrent())
            {
                return (null, null);
            }

            if (paletteResolution.Palette is null)
            {
                return (null,
                    $"Cannot color-decode {spriteTypeLower} sprite for object #{placement.RecordIndex}: {paletteResolution.Diagnostic ?? "no Fallout palette is available"}. The marker remains visible.");
            }

            var nonZeroPaletteIndexPixelCount =
                MapIsometricObjectSpriteDiagnostics.CountNonZeroPaletteIndexPixels(frame);
            var bitmap = CreateFrameBitmap(frame, paletteResolution.Palette);
            if (!isCurrent())
            {
                bitmap.Dispose();
                return (null, null);
            }

            var bitmapSize = checked((long)frame.Width * frame.Height * 4);
            var direction = document.Directions[directionIndex];
            var preparedSprite = new MapIsometricObjectSprite(
                placement.RecordIndex,
                bitmap,
                resolution.ResourcePath,
                directionIndex,
                placement.RawFrame,
                direction.ShiftX,
                direction.ShiftY,
                frame.OffsetX,
                frame.OffsetY,
                nonZeroPaletteIndexPixelCount);
            if (!_mapObjectSpriteBitmapCache.TryAdd(
                    cacheKey,
                    preparedSprite,
                    bitmapSize,
                    out var cacheError))
            {
                bitmap.Dispose();
                return (null,
                    $"Could not cache {spriteTypeLower} sprite for object #{placement.RecordIndex}: {cacheError}. The marker remains visible.");
            }

            return (preparedSprite, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidDataException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or InvalidOperationException
            or OverflowException)
        {
            return (null,
                $"Could not prepare {spriteTypeLower} sprite for object #{placement.RecordIndex}: {exception.Message}. The marker remains visible.");
        }
    }

    private static string FormatPreparedMapObjectSpriteStatus(
        MapIsometricObjectSprite sprite,
        MapObjectPlacement placement,
        string spriteTypeLower,
        string state) =>
        $"{state} {spriteTypeLower} sprite for object #{placement.RecordIndex}; " +
        $"FRM '{sprite.ResourcePath}'; bitmap {sprite.Bitmap.PixelSize.Width}x{sprite.Bitmap.PixelSize.Height}; " +
        $"direction {sprite.DirectionIndex}, frame {sprite.FrameIndex}; " +
        $"source pixels with palette index != 0: {sprite.NonZeroPaletteIndexPixelCount}; " +
        $"frame offset ({sprite.FrameOffsetX}, {sprite.FrameOffsetY}); " +
        $"direction shift ({sprite.ArtShiftX}, {sprite.ArtShiftY}); " +
        $"MAP offsets ({placement.RawOffsetX}, {placement.RawOffsetY}); " +
        "CE tile, art-shift, and MAP-offset placement applied. The marker remains visible as a reference.";

    private static bool HasValidMapObjectHexPosition(MapObjectPlacement placement) =>
        (uint)placement.RawHexIndex < MapObjectPlacementDecoder.HexGridTileCount
        && placement.HexX is int hexX
        && placement.HexY is int hexY
        && MapObjectPlacementDecoder.GetHexCoordinates(placement.RawHexIndex) == (hexX, hexY);

    private bool IsCurrentMapObjectSpriteLoad(
        CancellationTokenSource cancellation,
        IGameArchive archive,
        MapObjectPlacement placement) =>
        !_isDisposed
        && IsMapInspector
        && MapInspector.SelectedMapDiagnosticLayerIndex
            == MapInspectorViewModel.IsometricObjectMarkersLayerIndex
        && ReferenceEquals(_mapObjectSpriteCancellation, cancellation)
        && ReferenceEquals(_archive, archive)
        && ReferenceEquals(MapInspector.SelectedMapObject, placement)
        && MapObjectPlacements.Contains(placement)
        && MapInspector.SelectedMapElevationIndex == placement.ElevationGroup;

    private bool IsCurrentMapObjectSpritesLoad(
        CancellationTokenSource cancellation,
        IGameArchive archive,
        int elevation,
        IReadOnlyList<MapObjectPlacement> placements) =>
        !_isDisposed
        && IsMapInspector
        && IsMapIsometricSpriteLayer(MapInspector.SelectedMapDiagnosticLayerIndex)
        && ReferenceEquals(_mapObjectSpritesCancellation, cancellation)
        && ReferenceEquals(_archive, archive)
        && MapInspector.SelectedMapElevationIndex == elevation
        && placements.SequenceEqual(MapObjectPlacements);

    private void CancelMapObjectSpriteLoad()
    {
        _mapObjectSpriteCancellation?.Cancel();
        _mapObjectSpriteCancellation = null;
    }

    private void CancelMapObjectSpritesLoad()
    {
        _mapObjectSpritesCancellation?.Cancel();
        _mapObjectSpritesCancellation = null;
    }

    private void ScheduleMapObjectSpriteBatch()
    {
        if (IsMapInspector
            && IsMapIsometricSpriteLayer(MapInspector.SelectedMapDiagnosticLayerIndex))
        {
            _mapObjectSpritesTask = PrepareMapObjectSpritesAsync();
        }
        else
        {
            CancelMapObjectSpritesLoad();
            MapObjectSprites = [];
            MapObjectSpritesStatusDisplay = "Multi-sprite preview is not loaded.";
        }
    }

    private static bool IsMapIsometricSpriteLayer(int layerIndex) =>
        layerIndex is MapInspectorViewModel.IsometricFloorTilesLayerIndex
            or MapInspectorViewModel.IsometricObjectMarkersLayerIndex;

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

    private static Bitmap CreateFrameBitmap(FrmFrame frame, FalloutPalette palette)
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

    private static string FormatIntIdentifierTableHex(ReadOnlySpan<byte> bytes)
    {
        var displayedLength = Math.Min(bytes.Length, MaximumIntIdentifierBytesDisplayed);
        var builder = new StringBuilder();
        for (var offset = 0; offset < displayedLength; offset += 16)
        {
            if (offset > 0)
            {
                builder.AppendLine();
            }

            builder.Append(offset.ToString("X8", CultureInfo.InvariantCulture));
            builder.Append("  ");
            var lineEnd = Math.Min(offset + 16, displayedLength);
            for (var index = offset; index < lineEnd; index++)
            {
                if (index > offset)
                {
                    builder.Append(' ');
                }

                builder.Append(bytes[index].ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        if (displayedLength < bytes.Length)
        {
            if (displayedLength > 0)
            {
                builder.AppendLine();
            }

            builder.Append(
                $"... {bytes.Length - displayedLength:N0} more bytes omitted from this preview.");
        }

        return builder.ToString();
    }

    private void ClearPreview()
    {
        _standaloneProExportCancellation?.Cancel();
        _standaloneProExportCancellation = null;
        _standaloneProEntry = null;
        _standaloneProArchive = null;
        StandaloneProInspection = null;
        _mapFloorTileCancellation?.Cancel();
        _mapFloorTileCancellation = null;
        _mapObjectSpriteCancellation?.Cancel();
        _mapObjectSpriteCancellation = null;
        CancelMapObjectSpritesLoad();
        _mapObjectPrototypeDiagnosticCancellation?.Cancel();
        _mapObjectPrototypeDiagnosticCancellation = null;
        _mapPrototypeResolver = null;
        _mapFloorBitmapCache.Dispose();
        _mapObjectSpriteBitmapCache.Dispose();
        if (!_isDisposed)
        {
            _mapFloorBitmapCache = new MapScopedAssetCache<Bitmap>(
                MapScopedAssetCache<Bitmap>.DefaultByteLimit,
                bitmap => bitmap.Dispose());
            _mapObjectSpriteBitmapCache = new MapScopedAssetCache<MapIsometricObjectSprite>(
                MapScopedAssetCache<MapIsometricObjectSprite>.DefaultByteLimit,
                sprite => sprite.Bitmap.Dispose());
        }

        SelectedMapObjectSprite = null;
        MapObjectSprites = [];
        MapObjectSpritesStatusDisplay = "Multi-sprite preview is not loaded.";
        MapObjectSpriteStatusDisplay =
            "Select a top-level Fallout 2 Item, Scenery, or Wall marker to preview its FRM sprite.";
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
        IsProInspector = false;
        IsMsgInspector = false;
        MsgParseResult = null;
        MsgStatus = "Select a .MSG entry to inspect.";
        MsgDetails = string.Empty;
        IsIntInspector = false;
        IntMetadataReadResult = null;
        IntStatus = "Select a .INT entry to inspect.";
        IntIdentifierTableHex = string.Empty;
        StandaloneProStatus = "Select a .PRO entry to inspect.";
        StandaloneProDetails = string.Empty;
        StandaloneProFieldsDisplay = string.Empty;
        StandaloneProRawByteLines = [];
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

        PreviewDetails = "Select a .FRM, .MAP, .PRO, .MSG, or .INT entry to inspect.";
        PreviewTitle = "Resource Inspector";
        PaletteStatus = "Palette discovery uses the open archive or a configured game-data folder.";
    }

    private static string BuildStandalonePrototypeDetails(
        Fallout2StandalonePrototypeInspection inspection)
    {
        var details = new List<string>
        {
            $"Entry: {inspection.EntryPath}",
            $"Provenance: {inspection.Provenance}",
            inspection.Game is not null && inspection.Format is not null
                ? $"Archive: {inspection.Game} {inspection.Format}"
                : "Archive: unavailable"
        };

        if (inspection.PidType is int pidType)
        {
            details.Add(inspection.PidTypeName is null
                ? $"PID type: unsupported ({pidType})"
                : $"PID type: {inspection.PidTypeName} ({pidType})");
        }

        if (inspection.PrototypePid is int prototypePid)
        {
            details.Add($"Embedded PID: 0x{unchecked((uint)prototypePid):X8}");
        }

        if (inspection.Subtype is int subtype)
        {
            details.Add(inspection.SubtypeName is null
                ? $"Subtype: unsupported ({subtype})"
                : $"Subtype: {inspection.SubtypeName} ({subtype})");
        }

        if (inspection.FileSize is long fileSize)
        {
            details.Add($"Uncompressed size: {fileSize:N0} bytes");
        }

        if (inspection.RawBytes.Length > 0)
        {
            details.Add($"Raw bytes retained: {inspection.RawBytes.Length:N0}");
        }

        return string.Join(Environment.NewLine, details);
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
        var isIsometricLayerSwitch = false;
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
            var currentLayerIndex = MapInspector.SelectedMapDiagnosticLayerIndex;
            isIsometricLayerSwitch =
                IsMapIsometricSpriteLayer(_lastMapDiagnosticLayerIndex)
                && IsMapIsometricSpriteLayer(currentLayerIndex);
            _lastMapDiagnosticLayerIndex = currentLayerIndex;
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

        if (e.PropertyName == nameof(MapInspectorViewModel.IsMapInspector)
            || e.PropertyName == nameof(MapInspectorViewModel.SelectedMapElevationIndex)
            || e.PropertyName == nameof(MapInspectorViewModel.SelectedMapDiagnosticLayerIndex))
        {
            if (!isIsometricLayerSwitch)
            {
                ScheduleMapObjectSpriteBatch();
            }
        }
    }

    private void MapInspector_SelectedMapObjectChanged(
        object? sender,
        MapInspectorViewModel.MapObjectSelectionChangedEventArgs e)
    {
        CancelMapObjectSpriteLoad();
        SelectedMapObjectSprite = null;
        MapObjectSpriteStatusDisplay =
            "Select a top-level Fallout 2 Item, Scenery, or Wall marker to preview its FRM sprite.";

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
