using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using FalloutAssetExplorer.Core;

namespace FalloutAssetExplorer.App.ViewModels;

public sealed class SpriteExplorerViewModel : INotifyPropertyChanged, IDisposable
{
    private const string AllArchiveLocations = "All locations";
    private readonly Func<string, CancellationToken, IGameArchive> _archiveOpener;
    private readonly FalloutPaletteResolver _paletteResolver = new();
    private Bitmap? _spriteSheetBitmap;
    private FrmSpritesheetLayout? _spriteSheetLayout;
    private IGameArchive? _archive;
    private CancellationTokenSource? _sourceCancellation;
    private CancellationTokenSource? _previewCancellation;
    private CancellationTokenSource? _exportCancellation;
    private CancellationTokenSource? _spriteSheetCancellation;
    private Task _previewTask = Task.CompletedTask;
    private Task _spriteSheetTask = Task.CompletedTask;
    private int _spriteSheetVersion;
    private string _sourceArchiveDisplay = "No source archive";
    private string _searchText = string.Empty;
    private string _selectedArchiveLocation = AllArchiveLocations;
    private string _statusMessage = "Open a Fallout archive or send an FRM from Archive Browser.";
    private string _paletteStatus = "Palette discovery uses the source archive or configured game-data folder.";
    private string? _previewEntryPath;
    private string? _gameDataDirectory;
    private ArchiveEntry? _selectedEntry;
    private int _sourceVersion;
    private int _sourceRequestVersion;
    private int _previewVersion;
    private bool _isLoading;
    private bool _isExporting;
    private bool _isSpriteSheetMode;
    private bool _isAllDirectionsSpriteSheet;
    private bool _isDisposed;
    private string _spriteSheetStatus = "Select an FRM to build its sprite sheet.";

    public SpriteExplorerViewModel(
        Func<string, CancellationToken, IGameArchive>? archiveOpener = null)
    {
        _archiveOpener = archiveOpener ?? FalloutArchive.Open;
        Preview = new FrmPreviewViewModel();
        Preview.PropertyChanged += Preview_PropertyChanged;
        Zoom = new SpriteZoomModel();
        ArchiveLocationOptions = [AllArchiveLocations];
    }

    public bool IsExporting
    {
        get => _isExporting;
        private set
        {
            if (SetProperty(ref _isExporting, value))
            {
                OnPropertyChanged(nameof(CanCancelExport));
                OnPropertyChanged(nameof(CanExportCurrentFrame));
                OnPropertyChanged(nameof(CanExportCurrentDirection));
                OnPropertyChanged(nameof(CanExportAllDirections));
                OnPropertyChanged(nameof(CanExportVisibleSpriteSheet));
            }
        }
    }

    public bool CanCancelExport =>
        !_isDisposed
        && _exportCancellation is { IsCancellationRequested: false };

    public bool CanExportCurrentFrame =>
        !_isDisposed && !IsExporting && SelectedEntry is not null && Preview.CanExportPng;

    public bool CanExportCurrentDirection =>
        !_isDisposed
        && !IsExporting
        && SelectedEntry is not null
        && Preview.CanExportCurrentDirectionSpritesheet;

    public bool CanExportAllDirections =>
        !_isDisposed && !IsExporting && SelectedEntry is not null && Preview.CanExportSpritesheet;

    public bool CanExportVisibleSpriteSheet =>
        IsSpriteSheetMode
        && !_isDisposed
        && !IsExporting
        && SelectedEntry is not null
        && Preview.CanExportSpritesheet
        && _spriteSheetLayout is { Placements.Count: > 0 };

    public event PropertyChangedEventHandler? PropertyChanged;

    public FrmPreviewViewModel Preview { get; }

    public SpriteZoomModel Zoom { get; }

    public bool IsAnimationMode
    {
        get => !_isSpriteSheetMode;
        set => IsSpriteSheetMode = !value;
    }

    public bool IsSpriteSheetMode
    {
        get => _isSpriteSheetMode;
        set
        {
            if (!SetProperty(ref _isSpriteSheetMode, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsAnimationMode));
            OnPropertyChanged(nameof(DisplayBitmap));
            OnPropertyChanged(nameof(HasDisplayBitmap));
            UpdateZoomImageSize();
            if (value)
            {
                RequestSpriteSheetRender();
            }
            else
            {
                Interlocked.Increment(ref _spriteSheetVersion);
                _spriteSheetCancellation?.Cancel();
            }

            OnPropertyChanged(nameof(CanExportVisibleSpriteSheet));
        }
    }

    public bool IsAllDirectionsSpriteSheet
    {
        get => _isAllDirectionsSpriteSheet;
        set
        {
            if (!SetProperty(ref _isAllDirectionsSpriteSheet, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SpriteSheetSummary));
            if (IsSpriteSheetMode)
            {
                RequestSpriteSheetRender();
            }
        }
    }

    public Bitmap? DisplayBitmap =>
        IsSpriteSheetMode ? _spriteSheetBitmap : Preview.PreviewBitmap;

    public bool HasDisplayBitmap => DisplayBitmap is not null;

    public bool HasSpriteSheetSource => Preview.HasSpriteSheetData;

    public string SpriteSheetStatus
    {
        get => _spriteSheetStatus;
        private set => SetProperty(ref _spriteSheetStatus, value);
    }

    public string SpriteSheetSummary
    {
        get
        {
            if (!HasSpriteSheetSource)
            {
                return "No FRM loaded";
            }

            if (IsAllDirectionsSpriteSheet)
            {
                return $"All directions · {Preview.DirectionOptions.Count} directions";
            }

            var directionName = (uint)Preview.SelectedDirectionIndex < (uint)Preview.DirectionOptions.Count
                ? Preview.DirectionOptions[Preview.SelectedDirectionIndex]
                : "Selected direction";
            return $"{directionName} · {Preview.FrameOptions.Count} frames";
        }
    }

    public FrmSpritesheetLayout? SpriteSheetLayout => _spriteSheetLayout;

    public int SpriteSheetWidth => _spriteSheetLayout?.Width ?? 0;

    public int SpriteSheetHeight => _spriteSheetLayout?.Height ?? 0;

    public FrmSpritesheetFrameKey? SelectedSpriteSheetFrameKey =>
        Preview.SelectedSpriteSheetFrameKey;

    public Task SpriteSheetRenderTask => _spriteSheetTask;
    public ObservableCollection<ArchiveEntry> FrmEntries { get; } = [];

    public ObservableCollection<ArchiveEntry> VisibleEntries { get; } = [];

    public ObservableCollection<string> ArchiveLocationOptions { get; }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool CanOpenArchive => !_isDisposed && !IsLoading;

    public string SourceArchiveDisplay
    {
        get => _sourceArchiveDisplay;
        private set => SetProperty(ref _sourceArchiveDisplay, value);
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

    public string SelectedArchiveLocation
    {
        get => _selectedArchiveLocation;
        set
        {
            if (SetProperty(ref _selectedArchiveLocation, value))
            {
                RefreshVisibleEntries();
            }
        }
    }

    public ArchiveEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (value is not null
                && !FrmEntries.Any(entry => ReferenceEquals(entry, value)))
            {
                value = null;
            }

            if (SetProperty(ref _selectedEntry, value))
            {
                CancelPngExport();
                Zoom.ResetForResource();
                OnPropertyChanged(nameof(SelectedEntryDisplay));
                OnPropertyChanged(nameof(SelectedEntrySizeDisplay));
                OnPropertyChanged(nameof(CanExportCurrentFrame));
                OnPropertyChanged(nameof(CanExportCurrentDirection));
                OnPropertyChanged(nameof(CanExportAllDirections));
                OnPropertyChanged(nameof(CanExportVisibleSpriteSheet));
                OnPropertyChanged(nameof(SpriteSheetSummary));
                CancelSpriteSheetRender(clearBitmap: true);
                StartPreviewLoad(value);
            }
        }
    }

    public string SelectedEntryDisplay => SelectedEntry?.Path ?? "No FRM selected";

    public string SelectedEntrySizeDisplay => SelectedEntry is { } entry
        ? $"{entry.UncompressedSize:N0} bytes"
        : string.Empty;

    public string? PreviewEntryPath
    {
        get => _previewEntryPath;
        private set => SetProperty(ref _previewEntryPath, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string PaletteStatus
    {
        get => _paletteStatus;
        private set => SetProperty(ref _paletteStatus, value);
    }

    public bool IsFilterResultEmpty =>
        _archive is not null
        && FrmEntries.Count > 0
        && VisibleEntries.Count == 0;

    public void SetGameDataDirectory(string? path)
    {
        _gameDataDirectory = path;
    }

    public async Task RefreshPaletteAsync()
    {
        if (_isDisposed || _archive is null || SelectedEntry is null)
        {
            return;
        }

        StartPreviewLoad(SelectedEntry);
        await _previewTask;
    }

    public void ReportError(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        StatusMessage = message;
    }

    public bool IsCurrentExportSelection(
        ArchiveEntry entry,
        int? directionIndex = null,
        int? frameIndex = null) =>
        !_isDisposed
        && ReferenceEquals(SelectedEntry, entry)
        && string.Equals(PreviewEntryPath, entry.Path, StringComparison.OrdinalIgnoreCase)
        && (!directionIndex.HasValue || Preview.SelectedDirectionIndex == directionIndex.Value)
        && (!frameIndex.HasValue || Preview.SelectedFrameIndex == frameIndex.Value);

    public Task ExportCurrentFramePngAsync(
        string destinationPath,
        bool overwriteConfirmed = false) =>
        ExportPngAsync(
            destinationPath,
            overwriteConfirmed,
            (destination, cancellationToken) =>
                Preview.ExportSelectedFramePngAsync(destination, cancellationToken));

    public Task ExportCurrentDirectionSpritesheetPngAsync(
        string destinationPath,
        bool overwriteConfirmed = false) =>
        ExportPngAsync(
            destinationPath,
            overwriteConfirmed,
            (destination, cancellationToken) =>
                Preview.ExportCurrentDirectionSpritesheetPngAsync(destination, cancellationToken));

    public Task ExportAllDirectionsSpritesheetPngAsync(
        string destinationPath,
        bool overwriteConfirmed = false) =>
        ExportPngAsync(
            destinationPath,
            overwriteConfirmed,
            (destination, cancellationToken) =>
                Preview.ExportAllDirectionsSeparatedSpritesheetPngAsync(destination, cancellationToken));

    public Task ExportVisibleSpriteSheetPngAsync(
        string destinationPath,
        bool allDirections,
        int selectedDirectionIndex,
        bool overwriteConfirmed = false) =>
        ExportPngAsync(
            destinationPath,
            overwriteConfirmed,
            (destination, cancellationToken) =>
                Preview.ExportVisibleSpriteSheetPngAsync(
                    destination,
                    allDirections,
                    selectedDirectionIndex,
                    cancellationToken));

    public void CancelExport() => CancelPngExport();

    public async Task OpenSourceAsync(
        string archivePath,
        string? selectedEntryPath = null,
        string? gameDataDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        var version = Interlocked.Increment(ref _sourceRequestVersion);
        CancelPngExport();
        _sourceCancellation?.Cancel();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _sourceCancellation = cancellation;
        IsLoading = true;
        StatusMessage = "Opening archive and reading its entry list...";

        IGameArchive? openedArchive = null;
        try
        {
            var fullPath = Path.GetFullPath(archivePath);
            openedArchive = await Task.Run(
                () => _archiveOpener(fullPath, cancellation.Token),
                cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!IsCurrentSourceRequest(version, cancellation))
            {
                return;
            }

            Interlocked.Increment(ref _sourceVersion);
            CancelPreviewLoad();
            await _previewTask;
            cancellation.Token.ThrowIfCancellationRequested();
            if (!IsCurrentSourceRequest(version, cancellation))
            {
                return;
            }

            var previousArchive = _archive;
            _archive = openedArchive;
            openedArchive = null;
            if (previousArchive is IDisposable previousDisposable)
            {
                previousDisposable.Dispose();
            }

            _gameDataDirectory = gameDataDirectory ?? _gameDataDirectory;
            SourceArchiveDisplay = _archive.Path;
            _paletteStatus = "Palette discovery uses the source archive or configured game-data folder.";
            OnPropertyChanged(nameof(PaletteStatus));
            FrmEntries.Clear();
            foreach (var entry in _archive.BrowserEntries.Where(IsFrmEntry))
            {
                FrmEntries.Add(entry);
            }

            ArchiveLocationOptions.Clear();
            ArchiveLocationOptions.Add(AllArchiveLocations);
            foreach (var location in FrmEntries
                         .Select(entry => entry.ArchiveLocation)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .Order(StringComparer.OrdinalIgnoreCase))
            {
                ArchiveLocationOptions.Add(location);
            }

            RefreshVisibleEntries();
            SelectedArchiveLocation = AllArchiveLocations;
            SearchText = string.Empty;
            SelectedEntry = null;
            OnPropertyChanged(nameof(IsFilterResultEmpty));

            if (FrmEntries.Count == 0)
            {
                StatusMessage = "The selected archive contains no .FRM entries.";
                return;
            }

            if (selectedEntryPath is not null)
            {
                var matches = FrmEntries
                    .Where(entry => string.Equals(
                        entry.Path,
                        selectedEntryPath,
                        StringComparison.OrdinalIgnoreCase))
                    .Take(2)
                    .ToArray();
                if (matches.Length == 1)
                {
                    SelectedEntry = matches[0];
                }
                else if (matches.Length > 1)
                {
                    StatusMessage =
                        $"The FRM path '{selectedEntryPath}' is duplicated in this archive; select an entry from the list.";
                    return;
                }
                else
                {
                    StatusMessage =
                        $"The FRM entry '{selectedEntryPath}' was not found in the selected archive.";
                    return;
                }
            }

            if (SelectedEntry is null)
            {
                StatusMessage = $"Loaded {FrmEntries.Count:N0} FRM entries. Select one to preview it.";
                return;
            }

            await _previewTask;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedSourceFailure(exception))
        {
            if (IsCurrentSourceRequest(version, cancellation))
            {
                StatusMessage = $"Could not open Sprite Explorer source: {exception.Message}";
            }
        }
        finally
        {
            if (openedArchive is IDisposable openedDisposable)
            {
                openedDisposable.Dispose();
            }

            if (ReferenceEquals(_sourceCancellation, cancellation))
            {
                _sourceCancellation = null;
                IsLoading = false;
            }

            cancellation.Dispose();
        }
    }

    public Task SelectEntryAsync(ArchiveEntry? entry)
    {
        SelectedEntry = entry;
        return _previewTask;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Interlocked.Increment(ref _sourceVersion);
        Interlocked.Increment(ref _sourceRequestVersion);
        Interlocked.Increment(ref _previewVersion);
        _sourceCancellation?.Cancel();
        _previewCancellation?.Cancel();
        CancelPngExport();
        CancelSpriteSheetRender(clearBitmap: true);
        _sourceCancellation = null;
        _previewCancellation = null;
        IsLoading = false;
        Preview.ClearPreview();
        Preview.PropertyChanged -= Preview_PropertyChanged;
        Preview.Dispose();

        var archive = _archive;
        _archive = null;
        if (archive is IDisposable disposable)
        {
            if (_previewTask.IsCompleted)
            {
                disposable.Dispose();
            }
            else
            {
                _ = _previewTask.ContinueWith(
                    completedTask =>
                    {
                        if (completedTask.IsFaulted)
                        {
                            System.Diagnostics.Debug.WriteLine(completedTask.Exception);
                        }

                        disposable.Dispose();
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
    }

    private void StartPreviewLoad(ArchiveEntry? entry)
    {
        var version = Interlocked.Increment(ref _previewVersion);
        _previewCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _previewCancellation = cancellation;
        var previousTask = _previewTask;
        var archive = _archive;
        var sourceVersion = Volatile.Read(ref _sourceVersion);
        PreviewEntryPath = null;
        CancelSpriteSheetRender(clearBitmap: true);
        Preview.ClearPreview();
        PaletteStatus = "No palette has been resolved for the current selection.";
        StatusMessage = entry is null
            ? _archive is null ? "Open a source archive before selecting an FRM." : "Select an FRM entry to preview."
            : $"Loading {entry.Path}...";
        OnPropertyChanged(nameof(CanExportCurrentFrame));
        OnPropertyChanged(nameof(CanExportCurrentDirection));
        OnPropertyChanged(nameof(CanExportAllDirections));
        _previewTask = LoadPreviewAfterAsync(
            previousTask,
            archive,
            entry,
            sourceVersion,
            version,
            cancellation);
    }

    private async Task LoadPreviewAfterAsync(
        Task previousTask,
        IGameArchive? archive,
        ArchiveEntry? entry,
        int sourceVersion,
        int previewVersion,
        CancellationTokenSource cancellation)
    {
        try
        {
            await previousTask;
            cancellation.Token.ThrowIfCancellationRequested();
            if (!IsCurrentPreviewRequest(archive, entry, sourceVersion, previewVersion, cancellation))
            {
                return;
            }

            if (entry is null)
            {
                return;
            }

            if (archive is null)
            {
                StatusMessage = "Open a source archive before previewing an FRM.";
                return;
            }

            if (entry.UncompressedSize is <= 0 or > FrmDecoder.MaximumFileSize)
            {
                throw new InvalidDataException(
                    $"FRM size must be between 1 and {FrmDecoder.MaximumFileSize:N0} bytes.");
            }

            using var buffer = new MemoryStream(checked((int)entry.UncompressedSize));
            await archive.CopyEntryToAsync(entry, buffer, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (buffer.Length != entry.UncompressedSize)
            {
                throw new InvalidDataException(
                    $"Entry declares {entry.UncompressedSize:N0} bytes but {buffer.Length:N0} bytes were read.");
            }

            var document = await Task.Run(
                () => FrmDecoder.Decode(buffer.GetBuffer().AsMemory(0, checked((int)buffer.Length))),
                cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            var paletteResolution = await _paletteResolver.ResolveAsync(
                archive,
                _gameDataDirectory,
                cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!IsCurrentPreviewRequest(archive, entry, sourceVersion, previewVersion, cancellation))
            {
                return;
            }

            Preview.SetPreview(document, paletteResolution.Palette, entry.Path);
            PreviewEntryPath = entry.Path;
            PaletteStatus = paletteResolution.Palette is null
                ? paletteResolution.Diagnostic ?? "No valid Fallout palette is available; showing diagnostic grayscale."
                : $"Palette: {paletteResolution.Source}";
            StatusMessage = $"Preview loaded: {entry.Path}";
            OnPropertyChanged(nameof(HasSpriteSheetSource));
            OnPropertyChanged(nameof(SpriteSheetSummary));
            OnPropertyChanged(nameof(CanExportVisibleSpriteSheet));
            if (IsSpriteSheetMode)
            {
                RequestSpriteSheetRender();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedSourceFailure(exception))
        {
            if (IsCurrentPreviewRequest(archive, entry, sourceVersion, previewVersion, cancellation))
            {
                StatusMessage = $"Could not preview FRM: {exception.Message}";
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

    private void CancelPreviewLoad()
    {
        Interlocked.Increment(ref _previewVersion);
        _previewCancellation?.Cancel();
    }

    private async Task ExportPngAsync(
        string destinationPath,
        bool overwriteConfirmed,
        Func<Stream, CancellationToken, Task> write)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(write);
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        var entry = SelectedEntry
            ?? throw new InvalidOperationException("Select an FRM before exporting a PNG.");
        if (IsExporting)
        {
            throw new InvalidOperationException("A Sprite Explorer PNG export is already in progress.");
        }

        var sourceVersion = Volatile.Read(ref _sourceVersion);
        var sourceRequestVersion = Volatile.Read(ref _sourceRequestVersion);
        var cancellation = new CancellationTokenSource();
        _exportCancellation = cancellation;
        IsExporting = true;
        StatusMessage = $"Exporting PNG for {entry.Path}...";

        try
        {
            await SpriteExportFileWriter.WriteAtomicallyAsync(
                destinationPath,
                overwriteConfirmed,
                write,
                cancellation.Token);
            if (IsCurrentPngExport(entry, sourceVersion, sourceRequestVersion, cancellation))
            {
                StatusMessage = $"Exported PNG: {Path.GetFileName(destinationPath)}";
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (IsCurrentPngExport(entry, sourceVersion, sourceRequestVersion, cancellation))
            {
                StatusMessage = "PNG export cancelled.";
            }
        }
        catch (Exception exception) when (IsExpectedExportFailure(exception))
        {
            if (IsCurrentPngExport(entry, sourceVersion, sourceRequestVersion, cancellation))
            {
                StatusMessage = $"Could not export PNG: {exception.Message}";
            }

            throw;
        }
        finally
        {
            if (ReferenceEquals(_exportCancellation, cancellation))
            {
                _exportCancellation = null;
                IsExporting = false;
            }

            cancellation.Dispose();
        }
    }

    private void CancelPngExport()
    {
        var cancellation = _exportCancellation;
        if (cancellation is null || cancellation.IsCancellationRequested)
        {
            return;
        }

        cancellation.Cancel();
        OnPropertyChanged(nameof(CanCancelExport));
    }

    private bool IsCurrentPngExport(
        ArchiveEntry entry,
        int sourceVersion,
        int sourceRequestVersion,
        CancellationTokenSource cancellation) =>
        !_isDisposed
        && sourceVersion == Volatile.Read(ref _sourceVersion)
        && sourceRequestVersion == Volatile.Read(ref _sourceRequestVersion)
        && ReferenceEquals(_exportCancellation, cancellation)
        && ReferenceEquals(SelectedEntry, entry);

    private void Preview_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        if (e.PropertyName == nameof(FrmPreviewViewModel.PreviewBitmap))
        {
            OnPropertyChanged(nameof(DisplayBitmap));
            OnPropertyChanged(nameof(HasDisplayBitmap));
            if (!IsSpriteSheetMode)
            {
                UpdateZoomImageSize();
            }
        }

        if (e.PropertyName is nameof(FrmPreviewViewModel.SelectedDirectionIndex)
            or nameof(FrmPreviewViewModel.SelectedFrameIndex)
            or nameof(FrmPreviewViewModel.FrameOptions)
            or nameof(FrmPreviewViewModel.SelectedSpriteSheetFrameKey))
        {
            OnPropertyChanged(nameof(SpriteSheetSummary));
            OnPropertyChanged(nameof(SelectedSpriteSheetFrameKey));
            if (IsSpriteSheetMode
                && !IsAllDirectionsSpriteSheet
                && e.PropertyName == nameof(FrmPreviewViewModel.SelectedDirectionIndex)
                && SelectedEntry is { } selectedEntry
                && string.Equals(
                    PreviewEntryPath,
                    selectedEntry.Path,
                    StringComparison.OrdinalIgnoreCase))
            {
                RequestSpriteSheetRender();
            }
        }

        if (e.PropertyName is nameof(FrmPreviewViewModel.HasSpriteSheetData)
            or nameof(FrmPreviewViewModel.CanExportSpritesheet))
        {
            OnPropertyChanged(nameof(HasSpriteSheetSource));
            OnPropertyChanged(nameof(SpriteSheetSummary));
            OnPropertyChanged(nameof(CanExportVisibleSpriteSheet));
        }

        if (e.PropertyName is nameof(FrmPreviewViewModel.CanExportPng)
            or nameof(FrmPreviewViewModel.CanExportCurrentDirectionSpritesheet)
            or nameof(FrmPreviewViewModel.CanExportSpritesheet))
        {
            OnPropertyChanged(nameof(CanExportCurrentFrame));
            OnPropertyChanged(nameof(CanExportCurrentDirection));
            OnPropertyChanged(nameof(CanExportAllDirections));
            OnPropertyChanged(nameof(CanExportVisibleSpriteSheet));
        }
    }

    private void RequestSpriteSheetRender()
    {
        CancelSpriteSheetRender(clearBitmap: true);
        if (_isDisposed || !IsSpriteSheetMode || !Preview.HasSpriteSheetData)
        {
            SpriteSheetStatus = "Select a valid FRM with a resolved palette to build its sprite sheet.";
            OnPropertyChanged(nameof(CanExportVisibleSpriteSheet));
            return;
        }

        var cancellation = new CancellationTokenSource();
        _spriteSheetCancellation = cancellation;
        var version = Interlocked.Increment(ref _spriteSheetVersion);
        var directionIndex = Preview.SelectedDirectionIndex;
        var allDirections = IsAllDirectionsSpriteSheet;
        SpriteSheetStatus = "Building sprite sheet...";
        _spriteSheetTask = RenderSpriteSheetAsync(version, directionIndex, allDirections, cancellation);
    }

    private async Task RenderSpriteSheetAsync(
        int version,
        int directionIndex,
        bool allDirections,
        CancellationTokenSource cancellation)
    {
        try
        {
            var (png, layout) = await Preview.RenderSpriteSheetPngAsync(
                allDirections,
                directionIndex,
                cancellation.Token);
            using (png)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (!IsCurrentSpriteSheetRequest(version, cancellation))
                {
                    return;
                }

                var bitmap = new Bitmap(png);
                ReplaceSpriteSheetBitmap(bitmap);
                _spriteSheetLayout = layout;
                OnPropertyChanged(nameof(SpriteSheetLayout));
                OnPropertyChanged(nameof(SpriteSheetWidth));
                OnPropertyChanged(nameof(SpriteSheetHeight));
                OnPropertyChanged(nameof(DisplayBitmap));
                OnPropertyChanged(nameof(HasDisplayBitmap));
                UpdateZoomImageSize();
                OnPropertyChanged(nameof(CanExportVisibleSpriteSheet));
                var directionCount = layout.Placements.Keys
                    .Select(key => key.DirectionIndex)
                    .Distinct()
                    .Count();
                SpriteSheetStatus = allDirections
                    ? $"Sprite sheet ready: {layout.Placements.Count} frames across {directionCount} directions."
                    : $"Sprite sheet ready: {layout.Placements.Count} frames.";
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedSourceFailure(exception))
        {
            if (IsCurrentSpriteSheetRequest(version, cancellation))
            {
                SpriteSheetStatus = $"Could not build sprite sheet: {exception.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(_spriteSheetCancellation, cancellation))
            {
                _spriteSheetCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private bool IsCurrentSpriteSheetRequest(int version, CancellationTokenSource cancellation) =>
        !_isDisposed
        && version == Volatile.Read(ref _spriteSheetVersion)
        && ReferenceEquals(_spriteSheetCancellation, cancellation)
        && !cancellation.IsCancellationRequested
        && IsSpriteSheetMode;

    private void CancelSpriteSheetRender(bool clearBitmap)
    {
        Interlocked.Increment(ref _spriteSheetVersion);
        _spriteSheetCancellation?.Cancel();
        if (!clearBitmap)
        {
            return;
        }

        ReplaceSpriteSheetBitmap(null);
        _spriteSheetLayout = null;
        OnPropertyChanged(nameof(SpriteSheetLayout));
        OnPropertyChanged(nameof(SpriteSheetWidth));
        OnPropertyChanged(nameof(SpriteSheetHeight));
        OnPropertyChanged(nameof(DisplayBitmap));
        OnPropertyChanged(nameof(HasDisplayBitmap));
        OnPropertyChanged(nameof(CanExportVisibleSpriteSheet));
        if (IsSpriteSheetMode)
        {
            UpdateZoomImageSize();
        }
    }

    private void ReplaceSpriteSheetBitmap(Bitmap? bitmap)
    {
        var previous = _spriteSheetBitmap;
        if (ReferenceEquals(previous, bitmap))
        {
            return;
        }

        _spriteSheetBitmap = bitmap;
        previous?.Dispose();
    }

    private void UpdateZoomImageSize()
    {
        var bitmap = DisplayBitmap;
        Zoom.SetImageSize(bitmap?.PixelSize.Width ?? 0, bitmap?.PixelSize.Height ?? 0);
    }

    private void RefreshVisibleEntries()
    {
        var search = SearchText.Trim();
        var selectedLocation = SelectedArchiveLocation;
        VisibleEntries.Clear();
        foreach (var entry in FrmEntries.Where(entry =>
                     (search.Length == 0 || entry.Path.Contains(search, StringComparison.OrdinalIgnoreCase))
                     && (string.Equals(selectedLocation, AllArchiveLocations, StringComparison.Ordinal)
                         || string.Equals(entry.ArchiveLocation, selectedLocation, StringComparison.OrdinalIgnoreCase))))
        {
            VisibleEntries.Add(entry);
        }

        OnPropertyChanged(nameof(IsFilterResultEmpty));
    }

    private bool IsCurrentSourceRequest(int version, CancellationTokenSource cancellation) =>
        !_isDisposed
        && version == Volatile.Read(ref _sourceRequestVersion)
        && ReferenceEquals(_sourceCancellation, cancellation)
        && !cancellation.IsCancellationRequested;

    private bool IsCurrentPreviewRequest(
        IGameArchive? archive,
        ArchiveEntry? entry,
        int sourceVersion,
        int previewVersion,
        CancellationTokenSource cancellation) =>
        !_isDisposed
        && previewVersion == Volatile.Read(ref _previewVersion)
        && sourceVersion == Volatile.Read(ref _sourceVersion)
        && ReferenceEquals(_previewCancellation, cancellation)
        && ReferenceEquals(_archive, archive)
        && ReferenceEquals(SelectedEntry, entry)
        && !cancellation.IsCancellationRequested;

    private static bool IsFrmEntry(ArchiveEntry entry) =>
        string.Equals(entry.Extension, ".FRM", StringComparison.OrdinalIgnoreCase);

    private static bool IsExpectedSourceFailure(Exception exception) =>
        exception is InvalidDataException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or InvalidOperationException;

    private static bool IsExpectedExportFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or InvalidOperationException
            or System.Security.SecurityException;

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
        OnPropertyChanged(propertyName);
        if (propertyName == nameof(IsLoading))
        {
            OnPropertyChanged(nameof(CanOpenArchive));
        }

        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
