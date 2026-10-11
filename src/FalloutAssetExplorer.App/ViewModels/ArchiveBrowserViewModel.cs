using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FalloutAssetExplorer.Core;

namespace FalloutAssetExplorer.App.ViewModels;

public sealed class ArchiveBrowserViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly string[] SupportedExtensions = [".FRM", ".MAP", ".PRO", ".LST", ".INT"];
    private static readonly string[] DirectionNames = ["NE", "E", "SE", "SW", "W", "NW"];

    private string _searchText = string.Empty;
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
    private FrmDocument? _previewDocument;
    private Bitmap? _previewBitmap;
    private FalloutPalette? _falloutPalette;
    private string? _gameDataDirectory;
    private byte[] _renderBuffer = [];
    private MapTileGridData? _mapTileGridData;
    private int? _selectedMapElevationIndex;
    private bool _isMapInspector;
    private bool _isMapGridDataLoaded;
    private int? _selectedMapGridIndex;
    private string _mapGridCellDisplay = "Click a grid cell to keep its square-grid coordinates and raw value selected.";
    private string _mapGridHoverDisplay = "Move the pointer over a grid cell to inspect its square-grid coordinates and raw value.";
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
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ArchiveEntry> Entries { get; } = [];

    public ObservableCollection<ArchiveEntry> VisibleEntries { get; } = [];

    public ObservableCollection<string> ExtensionOptions { get; }

    public ObservableCollection<string> DirectionOptions { get; }

    public ObservableCollection<string> OverwritePolicyOptions { get; } =
        ["Skip existing", "Overwrite existing", "Cancel operation"];

    public ObservableCollection<int> FrameOptions { get; } = [];

    public ObservableCollection<int> MapElevationOptions { get; } = [];

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
                OnPropertyChanged(nameof(SelectedMapGridValues));
                OnPropertyChanged(nameof(HasMapGrid));
                OnPropertyChanged(nameof(IsMapGridEmpty));
                UpdateSelectedMapGridDisplay();
                MapGridHoverDisplay = "Move the pointer over a grid cell to inspect its square-grid coordinates and raw value.";
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

    public bool IsMapGridEmpty => _isMapInspector && _isMapGridDataLoaded && !HasMapGrid;

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
                _mapTileGridData = tileGrids;
                MapElevationOptions.Clear();
                foreach (var grid in tileGrids.Grids)
                {
                    MapElevationOptions.Add(grid.ElevationIndex);
                }

                _selectedMapElevationIndex = MapElevationOptions.Count > 0
                    ? MapElevationOptions[0]
                    : null;

                _isMapGridDataLoaded = true;
                OnPropertyChanged(nameof(SelectedMapElevationIndex));
                OnPropertyChanged(nameof(SelectedMapGridValues));
                OnPropertyChanged(nameof(HasMapGrid));
                OnPropertyChanged(nameof(IsMapGridEmpty));
                UpdateSelectedMapGridDisplay();
                MapGridHoverDisplay = "Move the pointer over a grid cell to inspect its square-grid coordinates and raw value.";
                PreviewMessage = selectedEntry.Path;
                PreviewDetails = BuildMapHeaderDetails(metadata, tileGrids);
                PaletteStatus = "Neutral diagnostic cells; values are not encoded as terrain. Coordinates are square-grid only, not hex/object positions. Scripts and objects are not parsed.";
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
        _mapTileGridData = null;
        _selectedMapElevationIndex = null;
        _selectedMapGridIndex = null;
        _isMapInspector = false;
        _isMapGridDataLoaded = false;
        MapElevationOptions.Clear();
        OnPropertyChanged(nameof(IsMapInspector));
        OnPropertyChanged(nameof(SelectedMapElevationIndex));
        OnPropertyChanged(nameof(SelectedMapGridIndex));
        OnPropertyChanged(nameof(SelectedMapGridValues));
        OnPropertyChanged(nameof(HasMapGrid));
        OnPropertyChanged(nameof(IsMapGridEmpty));
        MapGridCellDisplay = "Click a grid cell to keep its square-grid coordinates and raw value selected.";
        MapGridHoverDisplay = "Move the pointer over a grid cell to inspect its square-grid coordinates and raw value.";
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
        MapGridCellDisplay =
            $"Selected | Elevation slot {_selectedMapElevationIndex} | Square-grid ({x}, {y}) | Serialized index {index} | Raw value 0x{values[index]:X8}";
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
