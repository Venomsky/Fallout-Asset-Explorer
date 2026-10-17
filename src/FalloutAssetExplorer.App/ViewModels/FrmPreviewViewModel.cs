using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FalloutAssetExplorer.Core;

namespace FalloutAssetExplorer.App.ViewModels;

public sealed class FrmPreviewViewModel : INotifyPropertyChanged, IDisposable
{
    public const int SpriteSheetDirectionGap = 24;

    private static readonly string[] DirectionNames = ["NE", "E", "SE", "SW", "W", "NW"];
    private static readonly FrmSpritesheetPackingLimits SpritesheetExportLimits = new(
        maximumWidth: 16_384,
        maximumHeight: 16_384,
        maximumPixelCount: 64L * 1024 * 1024);
    private const string DefaultPreviewDetails = "Select a .FRM, .MAP, or .PRO entry to inspect.";
    private const int DefaultSpritesheetSpacing = 0;

    private readonly FrmPlaybackController _playback = new();
    private readonly DispatcherTimer _playbackTimer;
    private FrmDocument? _document;
    private FalloutPalette? _palette;
    private string? _resourcePath;
    private Bitmap? _previewBitmap;
    private byte[] _renderBuffer = [];
    private int _selectedDirectionIndex;
    private int _selectedFrameIndex;
    private bool _isCheckerboardBackground;
    private bool _isExportingPng;
    private bool _isDisposed;
    private string _previewDetails = DefaultPreviewDetails;

    public FrmPreviewViewModel()
    {
        DirectionOptions = new ObservableCollection<string>(
            DirectionNames.Select((name, index) => $"{name} ({index})"));
        _playbackTimer = new DispatcherTimer
        {
            Interval = GetPlaybackInterval()
        };
        _playbackTimer.Tick += PlaybackTimer_Tick;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<string> DirectionOptions { get; }

    public ObservableCollection<int> FrameOptions { get; } = [];

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

    public bool CanPlay => _document is not null && _playback.CanPlay;

    public bool CanExportPng =>
        !_isDisposed
        && !_isExportingPng
        && _document is not null
        && _palette is not null
        && (uint)_selectedDirectionIndex < (uint)_document.Directions.Count
        && (uint)_selectedFrameIndex
            < (uint)_document.Directions[_selectedDirectionIndex].Frames.Count;

    public bool CanExportSpritesheet =>
        !_isDisposed
        && !_isExportingPng
        && _palette is not null
        && HasSpriteSheetData;

    public bool HasSpriteSheetData =>
        !_isDisposed
        && _document is not null
        && _document.Directions.Count > 0
        && _document.Directions.Any(direction => direction.Frames.Count > 0);

    public bool CanExportCurrentDirectionSpritesheet =>
        CanExportSpritesheet
        && _document is not null
        && (uint)_selectedDirectionIndex < (uint)_document.Directions.Count
        && _document.Directions[_selectedDirectionIndex].Frames.Count > 0;

    public bool IsExportingPng => _isExportingPng;

    public string SuggestedPngFileName
    {
        get
        {
            var fileName = _resourcePath is null
                ? "FRM"
                : Path.GetFileNameWithoutExtension(_resourcePath);
            var directionName = _document is not null
                && (uint)_selectedDirectionIndex < (uint)_document.Directions.Count
                ? _document.Directions[_selectedDirectionIndex].Name
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

    public string SuggestedSpritesheetPngFileName
    {
        get => CreateSuggestedPngFileName("_spritesheet", "FRM_spritesheet");
    }

    public string SuggestedCurrentDirectionSpritesheetPngFileName
    {
        get
        {
            var directionName = _document is not null
                && (uint)_selectedDirectionIndex < (uint)_document.Directions.Count
                ? _document.Directions[_selectedDirectionIndex].Name
                : DirectionNames[0];
            return CreateSuggestedPngFileName($"_{directionName}_spritesheet", "FRM_spritesheet");
        }
    }

    public string SuggestedAllDirectionsSpritesheetPngFileName =>
        CreateSuggestedPngFileName("_all-directions_spritesheet", "FRM_all-directions_spritesheet");

    public bool IsPlaying => _playback.IsPlaying;

    public string PlayPauseButtonText => IsPlaying ? "Pause" : "Play";

    public string CurrentFrameDisplay => _document is null
        ? "Frame —"
        : $"Frame {_playback.CurrentFrameIndex + 1} / {_playback.CurrentDirectionFrameCount}";

    public FrmSpritesheetFrameKey? SelectedSpriteSheetFrameKey =>
        _document is not null
        && (uint)_selectedDirectionIndex < (uint)_document.Directions.Count
            ? new FrmSpritesheetFrameKey(
                _document.Directions[_selectedDirectionIndex].Index,
                _selectedFrameIndex)
            : null;

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

    public string PreviewDetails
    {
        get => _previewDetails;
        private set => SetProperty(ref _previewDetails, value);
    }

    public int SelectedDirectionIndex
    {
        get => _selectedDirectionIndex;
        set
        {
            if (_document is null
                || value < 0
                || value >= _document.Directions.Count
                || value == _selectedDirectionIndex)
            {
                return;
            }

            _playback.SelectDirection(value);
            _playbackTimer.Stop();
            SetProperty(ref _selectedDirectionIndex, value);
            SetProperty(ref _selectedFrameIndex, _playback.CurrentFrameIndex, nameof(SelectedFrameIndex));
            RefreshFrameOptions();
            RefreshPreviewFrame();
            NotifyPlaybackState();
            OnPropertyChanged(nameof(SuggestedPngFileName));
            OnPropertyChanged(nameof(SuggestedCurrentDirectionSpritesheetPngFileName));
            OnPropertyChanged(nameof(CanExportCurrentDirectionSpritesheet));
        }
    }

    public int SelectedFrameIndex
    {
        get => _selectedFrameIndex;
        set
        {
            if (_document is null
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

    public void SetPreview(FrmDocument document, FalloutPalette? palette, string resourcePath)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcePath);

        _playbackTimer.Stop();
        _document = document;
        _palette = palette;
        _resourcePath = resourcePath;
        _playback.SetFrameCounts(document.Directions.Select(direction => direction.Frames.Count).ToArray());
        _selectedDirectionIndex = _playback.DirectionIndex;
        _selectedFrameIndex = _playback.CurrentFrameIndex;

        RefreshFrameOptions();

        OnPropertyChanged(nameof(SelectedDirectionIndex));
        OnPropertyChanged(nameof(SelectedFrameIndex));
        NotifyPlaybackState();
        OnPropertyChanged(nameof(CanExportPng));
        OnPropertyChanged(nameof(CanExportSpritesheet));
        OnPropertyChanged(nameof(CanExportCurrentDirectionSpritesheet));
        OnPropertyChanged(nameof(HasSpriteSheetData));
        OnPropertyChanged(nameof(SuggestedPngFileName));
        OnPropertyChanged(nameof(SuggestedSpritesheetPngFileName));
        OnPropertyChanged(nameof(SuggestedCurrentDirectionSpritesheetPngFileName));
        OnPropertyChanged(nameof(SuggestedAllDirectionsSpritesheetPngFileName));
        RefreshPreviewFrame();
    }

    public void ClearPreview()
    {
        if (_isDisposed)
        {
            return;
        }

        ResetPreviewState();
    }

    public void SelectFrameByDirectionIndex(int directionIndex, int frameIndex)
    {
        if (_document is null)
        {
            throw new InvalidOperationException("A valid FRM must be loaded before selecting a sprite-sheet frame.");
        }

        var directionPosition = -1;
        for (var index = 0; index < _document.Directions.Count; index++)
        {
            if (_document.Directions[index].Index == directionIndex)
            {
                directionPosition = index;
                break;
            }
        }

        if (directionPosition < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(directionIndex));
        }

        if ((uint)frameIndex >= (uint)_document.Directions[directionPosition].Frames.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(frameIndex));
        }

        if (directionPosition == _selectedDirectionIndex && frameIndex == _selectedFrameIndex)
        {
            return;
        }

        _playbackTimer.Stop();
        _playback.SelectDirection(directionPosition);
        _playback.SelectFrame(frameIndex);
        SetProperty(ref _selectedDirectionIndex, directionPosition, nameof(SelectedDirectionIndex));
        SetProperty(ref _selectedFrameIndex, frameIndex, nameof(SelectedFrameIndex));
        RefreshFrameOptions();
        RefreshPreviewFrame();
        NotifyPlaybackState();
        OnPropertyChanged(nameof(SelectedSpriteSheetFrameKey));
        OnPropertyChanged(nameof(SuggestedPngFileName));
        OnPropertyChanged(nameof(SuggestedCurrentDirectionSpritesheetPngFileName));
        OnPropertyChanged(nameof(CanExportCurrentDirectionSpritesheet));
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

    public async Task ExportSelectedFramePngAsync(
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!CanExportPng || _document is null || _palette is null)
        {
            throw new InvalidOperationException(
                "PNG export requires a selected, valid FRM frame and a resolved Fallout COLOR.PAL.");
        }

        var document = _document;
        var palette = _palette;
        var directionIndex = _document.Directions[_selectedDirectionIndex].Index;
        var frameIndex = _selectedFrameIndex;
        await RunPngExportAsync(
            () => FrmPngExporter.Write(
                document,
                directionIndex,
                frameIndex,
                palette,
                destination,
                cancellationToken),
            cancellationToken);
    }

    public async Task ExportSpritesheetPngAsync(
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!CanExportSpritesheet || _document is null || _palette is null)
        {
            throw new InvalidOperationException(
                "Spritesheet export requires a valid FRM document and a resolved Fallout COLOR.PAL.");
        }

        var document = _document;
        var palette = _palette;
        var assetName = Path.GetFileName(_resourcePath);
        if (string.IsNullOrWhiteSpace(assetName))
        {
            assetName = _resourcePath;
        }

        await RunPngExportAsync(
            () => WriteSpritesheetPng(document, palette, destination, assetName, cancellationToken),
            cancellationToken);
    }

    public async Task ExportCurrentDirectionSpritesheetPngAsync(
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!CanExportCurrentDirectionSpritesheet || _document is null || _palette is null)
        {
            throw new InvalidOperationException(
                "Direction spritesheet export requires a valid selected FRM direction and a resolved Fallout COLOR.PAL.");
        }

        var document = _document;
        var palette = _palette;
        var directionIndex = _selectedDirectionIndex;
        var assetName = Path.GetFileName(_resourcePath);
        if (string.IsNullOrWhiteSpace(assetName))
        {
            assetName = _resourcePath;
        }

        await RunPngExportAsync(
            () => FrmSpritesheetPngExporter.WriteDirection(
                document,
                directionIndex,
                palette,
                destination,
                SpritesheetExportLimits,
                DefaultSpritesheetSpacing,
                assetName,
                cancellationToken),
            cancellationToken);
    }

    public async Task ExportAllDirectionsSeparatedSpritesheetPngAsync(
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!CanExportSpritesheet || _document is null || _palette is null)
        {
            throw new InvalidOperationException(
                "Spritesheet export requires a valid FRM document and a resolved Fallout COLOR.PAL.");
        }

        var document = _document;
        var palette = _palette;
        var assetName = Path.GetFileName(_resourcePath);
        if (string.IsNullOrWhiteSpace(assetName))
        {
            assetName = _resourcePath;
        }

        await RunPngExportAsync(
            () => FrmSpritesheetPngExporter.WriteAllDirectionsSeparated(
                document,
                palette,
                destination,
                SpritesheetExportLimits,
                DefaultSpritesheetSpacing,
                assetName: assetName,
                cancellationToken: cancellationToken),
            cancellationToken);
    }

    public async Task ExportVisibleSpriteSheetPngAsync(
        Stream destination,
        bool allDirections,
        int selectedDirectionIndex,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!CanExportSpritesheet || _document is null || _palette is null)
        {
            throw new InvalidOperationException(
                "Sprite-sheet export requires a valid FRM document and a resolved Fallout COLOR.PAL.");
        }

        var document = _document;
        var palette = _palette;
        int? directionIndex = allDirections
            ? null
            : document.Directions[(uint)selectedDirectionIndex < (uint)document.Directions.Count
                ? selectedDirectionIndex
                : throw new ArgumentOutOfRangeException(nameof(selectedDirectionIndex))].Index;
        var assetName = Path.GetFileName(_resourcePath);
        if (string.IsNullOrWhiteSpace(assetName))
        {
            assetName = _resourcePath;
        }

        await RunPngExportAsync(
            () => FrmSpritesheetPngExporter.WriteAndGetLayout(
                document,
                palette,
                destination,
                SpritesheetExportLimits,
                directionIndex,
                separateDirections: allDirections,
                frameSpacing: DefaultSpritesheetSpacing,
                directionSpacing: SpriteSheetDirectionGap,
                assetName: assetName,
                cancellationToken: cancellationToken),
            cancellationToken);
    }

    public async Task<(MemoryStream Png, FrmSpritesheetLayout Layout)> RenderSpriteSheetPngAsync(
        bool allDirections,
        int selectedDirectionIndex,
        CancellationToken cancellationToken = default)
    {
        if (_isDisposed || _document is null || _palette is null)
        {
            throw new InvalidOperationException(
                "Sprite-sheet preview requires a valid FRM document and a resolved Fallout COLOR.PAL.");
        }

        var document = _document;
        var palette = _palette;
        int? directionIndex = allDirections
            ? null
            : document.Directions[(uint)selectedDirectionIndex < (uint)document.Directions.Count
                ? selectedDirectionIndex
                : throw new ArgumentOutOfRangeException(nameof(selectedDirectionIndex))].Index;
        var assetName = Path.GetFileName(_resourcePath);
        if (string.IsNullOrWhiteSpace(assetName))
        {
            assetName = _resourcePath;
        }

        return await Task.Run(() =>
        {
            var png = new MemoryStream();
            var completed = false;
            try
            {
                var layout = FrmSpritesheetPngExporter.WriteAndGetLayout(
                    document,
                    palette,
                    png,
                    SpritesheetExportLimits,
                    directionIndex,
                    separateDirections: allDirections,
                    frameSpacing: DefaultSpritesheetSpacing,
                    directionSpacing: SpriteSheetDirectionGap,
                    assetName: assetName,
                    cancellationToken: cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                png.Position = 0;
                completed = true;
                return (png, layout);
            }
            finally
            {
                if (!completed)
                {
                    png.Dispose();
                }
            }
        }, cancellationToken);
    }

    internal static void WriteSpritesheetPng(
        FrmDocument document,
        FalloutPalette palette,
        Stream destination,
        string? assetName,
        CancellationToken cancellationToken = default) =>
        FrmSpritesheetPngExporter.Write(
            document,
            palette,
            destination,
            SpritesheetExportLimits,
            DefaultSpritesheetSpacing,
            assetName,
            cancellationToken);

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _playbackTimer.Stop();
        _playbackTimer.Tick -= PlaybackTimer_Tick;
        ResetPreviewState();
    }

    private void RefreshPreviewFrame()
    {
        if (_document is null)
        {
            return;
        }

        if ((uint)_selectedDirectionIndex >= (uint)_document.Directions.Count
            || (uint)_selectedFrameIndex
                >= (uint)_document.Directions[_selectedDirectionIndex].Frames.Count)
        {
            ReplacePreviewBitmap(null);
            PreviewDetails = DefaultPreviewDetails;
            OnPropertyChanged(nameof(CurrentFrameDisplay));
            OnPropertyChanged(nameof(SelectedSpriteSheetFrameKey));
            OnPropertyChanged(nameof(CanExportPng));
            OnPropertyChanged(nameof(CanExportCurrentDirectionSpritesheet));
            OnPropertyChanged(nameof(SuggestedPngFileName));
            return;
        }

        var direction = _document.Directions[_selectedDirectionIndex];
        var frame = direction.Frames[_selectedFrameIndex];
        var requiredLength = checked(frame.Pixels.Length * 4);
        if (_renderBuffer.Length < requiredLength)
        {
            Array.Resize(ref _renderBuffer, requiredLength);
        }

        var rgba = _renderBuffer.AsSpan(0, requiredLength);
        if (_palette is not null)
        {
            _palette.CopyRgba(frame, rgba);
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

        var bitmap = CreatePreviewBitmap(frame, requiredLength);
        ReplacePreviewBitmap(bitmap);
        PreviewDetails = BuildPreviewDetails(_document, _selectedDirectionIndex, _selectedFrameIndex);
        OnPropertyChanged(nameof(CurrentFrameDisplay));
        OnPropertyChanged(nameof(SelectedSpriteSheetFrameKey));
        OnPropertyChanged(nameof(CanExportPng));
        OnPropertyChanged(nameof(CanExportCurrentDirectionSpritesheet));
        OnPropertyChanged(nameof(SuggestedPngFileName));
    }

    private Bitmap CreatePreviewBitmap(FrmFrame frame, int requiredLength)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(frame.Width, frame.Height),
            new Vector(96, 96),
            PixelFormat.Rgba8888,
            AlphaFormat.Premul);

        try
        {
            using var framebuffer = bitmap.Lock();
            if (framebuffer.RowBytes == frame.Width * 4)
            {
                System.Runtime.InteropServices.Marshal.Copy(
                    _renderBuffer,
                    0,
                    framebuffer.Address,
                    requiredLength);
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

    private void ResetPreviewState()
    {
        _playbackTimer.Stop();
        _playback.SetFrameCounts([]);
        _document = null;
        _palette = null;
        _resourcePath = null;
        FrameOptions.Clear();
        _selectedFrameIndex = 0;
        _selectedDirectionIndex = 0;
        OnPropertyChanged(nameof(SelectedFrameIndex));
        OnPropertyChanged(nameof(SelectedDirectionIndex));
        ReplacePreviewBitmap(null);
        PreviewDetails = DefaultPreviewDetails;
        NotifyPlaybackState();
        OnPropertyChanged(nameof(CanExportPng));
        OnPropertyChanged(nameof(CanExportSpritesheet));
        OnPropertyChanged(nameof(CanExportCurrentDirectionSpritesheet));
        OnPropertyChanged(nameof(HasSpriteSheetData));
        OnPropertyChanged(nameof(SuggestedPngFileName));
        OnPropertyChanged(nameof(SuggestedSpritesheetPngFileName));
        OnPropertyChanged(nameof(SuggestedCurrentDirectionSpritesheetPngFileName));
        OnPropertyChanged(nameof(SuggestedAllDirectionsSpritesheetPngFileName));
    }

    private void ReplacePreviewBitmap(Bitmap? bitmap)
    {
        var previousBitmap = PreviewBitmap;
        if (ReferenceEquals(previousBitmap, bitmap))
        {
            return;
        }

        PreviewBitmap = bitmap;
        previousBitmap?.Dispose();
    }

    private void RefreshFrameOptions()
    {
        FrameOptions.Clear();
        if (_document is null || (uint)_selectedDirectionIndex >= (uint)_document.Directions.Count)
        {
            return;
        }

        var frameCount = _document.Directions[_selectedDirectionIndex].Frames.Count;
        for (var index = 0; index < frameCount; index++)
        {
            FrameOptions.Add(index);
        }
    }

    private void NotifyPlaybackState()
    {
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(PlayPauseButtonText));
        OnPropertyChanged(nameof(CurrentFrameDisplay));
    }

    private async Task RunPngExportAsync(Action write, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _isExportingPng = true;
        NotifyPngExportState();

        try
        {
            await Task.Run(write, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            _isExportingPng = false;
            if (!_isDisposed)
            {
                NotifyPngExportState();
            }
        }
    }

    private void NotifyPngExportState()
    {
        OnPropertyChanged(nameof(IsExportingPng));
        OnPropertyChanged(nameof(CanExportPng));
        OnPropertyChanged(nameof(CanExportSpritesheet));
        OnPropertyChanged(nameof(CanExportCurrentDirectionSpritesheet));
    }

    private string CreateSuggestedPngFileName(string suffix, string fallback)
    {
        var fileName = _resourcePath is null
            ? "FRM"
            : Path.GetFileNameWithoutExtension(_resourcePath);
        var unsanitized = $"{fileName}{suffix}";
        var invalidCharacters = Path.GetInvalidFileNameChars()
            .Concat("<>:\"/\\|?*")
            .ToHashSet();
        var sanitized = new string(unsanitized
            .Select(character => invalidCharacters.Contains(character) ? '_' : character)
            .ToArray())
            .TrimEnd('.', ' ');
        return $"{(string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized)}.png";
    }

    private TimeSpan GetPlaybackInterval() =>
        TimeSpan.FromMilliseconds(1000d / _playback.FramesPerSecond);

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
