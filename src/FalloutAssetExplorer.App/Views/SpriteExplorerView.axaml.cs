using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using FalloutAssetExplorer.App.ViewModels;
using FalloutAssetExplorer.Core;

namespace FalloutAssetExplorer.App.Views;

public partial class SpriteExplorerView : UserControl
{
    private SpriteZoomModel? _observedZoom;
    private SpriteExplorerViewModel? _observedViewModel;
    private bool _isPanning;
    private bool _isPointerPressed;
    private bool _isSpriteSheetClickCandidate;
    private bool _didPan;
    private Point _pointerDownPosition;
    private Point _lastPanPosition;

    public SpriteExplorerView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_observedZoom is not null)
            {
                _observedZoom.PropertyChanged -= Zoom_PropertyChanged;
            }

            if (_observedViewModel is not null)
            {
                _observedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            }

            _observedViewModel = DataContext as SpriteExplorerViewModel;
            if (_observedViewModel is not null)
            {
                _observedViewModel.PropertyChanged += ViewModel_PropertyChanged;
            }

            _observedZoom = _observedViewModel?.Zoom;
            if (_observedZoom is not null)
            {
                _observedZoom.PropertyChanged += Zoom_PropertyChanged;
                _observedZoom.SetViewportSize(
                    SpriteViewport.Bounds.Width,
                    SpriteViewport.Bounds.Height);
            }

            ApplyViewportTransform();
            ApplySpriteSheetOverlay();
        };
    }

    private async void OpenArchive_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SpriteExplorerViewModel viewModel)
        {
            return;
        }

        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider is null)
        {
            viewModel.ReportError("Could not access the archive file picker.");
            return;
        }

        try
        {
            var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open Fallout archive in Sprite Explorer",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Fallout DAT archives") { Patterns = ["*.dat", "*.DAT"] },
                    FilePickerFileTypes.All
                ]
            });

            if (files.Count > 0)
            {
                await viewModel.OpenSourceAsync(files[0].Path.LocalPath);
            }
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException
            or InvalidOperationException
            or NotSupportedException
            or ArgumentException)
        {
            viewModel.ReportError($"Could not open the archive picker: {exception.Message}");
        }
    }

    private void TogglePlayback_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SpriteExplorerViewModel viewModel)
        {
            viewModel.Preview.TogglePlayback();
        }
    }

    private async void ExportCurrentFrame_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SpriteExplorerViewModel viewModel
            || viewModel.SelectedEntry is not { } entry)
        {
            return;
        }

        var directionIndex = viewModel.Preview.SelectedDirectionIndex;
        var frameIndex = viewModel.Preview.SelectedFrameIndex;
        await ExportPngAsync(
            viewModel,
            entry,
            directionIndex,
            frameIndex,
            "Export current FRM frame as PNG",
            viewModel.Preview.SuggestedPngFileName,
            (target, path) => target.ExportCurrentFramePngAsync(path, overwriteConfirmed: true));
    }

    private async void ExportCurrentDirection_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SpriteExplorerViewModel viewModel
            || viewModel.SelectedEntry is not { } entry)
        {
            return;
        }

        var directionIndex = viewModel.Preview.SelectedDirectionIndex;
        await ExportPngAsync(
            viewModel,
            entry,
            directionIndex,
            null,
            "Export current FRM direction as PNG spritesheet",
            viewModel.Preview.SuggestedCurrentDirectionSpritesheetPngFileName,
            (target, path) => target.ExportCurrentDirectionSpritesheetPngAsync(path, overwriteConfirmed: true));
    }

    private async void ExportAllDirections_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SpriteExplorerViewModel viewModel
            || viewModel.SelectedEntry is not { } entry)
        {
            return;
        }

        await ExportPngAsync(
            viewModel,
            entry,
            null,
            null,
            "Export all FRM directions as PNG spritesheet",
            viewModel.Preview.SuggestedAllDirectionsSpritesheetPngFileName,
            (target, path) => target.ExportAllDirectionsSpritesheetPngAsync(path, overwriteConfirmed: true));
    }

    private async void ExportVisibleSpriteSheet_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SpriteExplorerViewModel viewModel
            || viewModel.SelectedEntry is not { } entry)
        {
            return;
        }

        var allDirections = viewModel.IsAllDirectionsSpriteSheet;
        var directionIndex = viewModel.Preview.SelectedDirectionIndex;
        int? directionConstraint = allDirections ? null : directionIndex;
        var fileName = allDirections
            ? viewModel.Preview.SuggestedAllDirectionsSpritesheetPngFileName
            : viewModel.Preview.SuggestedCurrentDirectionSpritesheetPngFileName;

        await ExportPngAsync(
            viewModel,
            entry,
            directionConstraint,
            null,
            "Export visible sprite sheet as PNG",
            fileName,
            (target, path) => target.ExportVisibleSpriteSheetPngAsync(
                path,
                allDirections,
                directionIndex,
                overwriteConfirmed: true));
    }

    private async Task ExportPngAsync(
        SpriteExplorerViewModel viewModel,
        ArchiveEntry entry,
        int? directionIndex,
        int? frameIndex,
        string title,
        string suggestedFileName,
        Func<SpriteExplorerViewModel, string, Task> export)
    {
        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider is null)
        {
            viewModel.ReportError("Could not access the PNG save dialog.");
            return;
        }

        var pngFileType = new FilePickerFileType("PNG image")
        {
            Patterns = ["*.png"]
        };

        try
        {
            var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = suggestedFileName,
                DefaultExtension = "png",
                FileTypeChoices = [pngFileType],
                SuggestedFileType = pngFileType,
                ShowOverwritePrompt = true
            });
            if (file is null)
            {
                return;
            }

            if (!viewModel.IsCurrentExportSelection(entry, directionIndex, frameIndex))
            {
                viewModel.ReportError("PNG export cancelled because the selected FRM or frame changed.");
                return;
            }

            await export(viewModel, file.Path.LocalPath);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException
            or InvalidOperationException
            or NotSupportedException
            or ArgumentException)
        {
            if (viewModel.IsCurrentExportSelection(entry, directionIndex, frameIndex))
            {
                viewModel.ReportError($"Could not export PNG: {exception.Message}");
            }
        }
    }

    private void CancelExport_Click(object? sender, RoutedEventArgs e)
    {
        (DataContext as SpriteExplorerViewModel)?.CancelExport();
    }

    private void SpriteViewport_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        _observedZoom?.SetViewportSize(e.NewSize.Width, e.NewSize.Height);
        ApplyViewportTransform();
    }

    private void Zoom_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        ApplyViewportTransform();

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SpriteExplorerViewModel.DisplayBitmap)
            or nameof(SpriteExplorerViewModel.HasDisplayBitmap))
        {
            ApplyViewportTransform();
        }

        if (e.PropertyName is nameof(SpriteExplorerViewModel.SpriteSheetLayout)
            or nameof(SpriteExplorerViewModel.SelectedSpriteSheetFrameKey)
            or nameof(SpriteExplorerViewModel.IsSpriteSheetMode))
        {
            ApplySpriteSheetOverlay();
        }
    }

    private void ApplyViewportTransform()
    {
        if (DataContext is not SpriteExplorerViewModel viewModel)
        {
            return;
        }

        var zoom = viewModel.Zoom;
        var transform = new MatrixTransform(
            new Matrix(zoom.Zoom, 0, 0, zoom.Zoom, zoom.OffsetX, zoom.OffsetY));
        SpriteImage.RenderTransform = transform;
        SpriteSheetFrameOverlay.RenderTransform = new MatrixTransform(transform.Matrix);
    }

    private void ApplySpriteSheetOverlay()
    {
        var viewModel = DataContext as SpriteExplorerViewModel;
        SpriteSheetFrameOverlay.SetLayout(
            viewModel?.SpriteSheetLayout,
            viewModel?.SelectedSpriteSheetFrameKey);
        ApplyViewportTransform();
    }

    private void SpriteViewport_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not SpriteExplorerViewModel viewModel)
        {
            return;
        }

        var position = e.GetPosition(SpriteViewport);
        viewModel.Zoom.ZoomBy(Math.Pow(1.2, e.Delta.Y), position.X, position.Y);
        e.Handled = true;
    }

    private void SpriteViewport_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not SpriteExplorerViewModel viewModel
            || !e.GetCurrentPoint(SpriteViewport).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (!viewModel.Zoom.CanPan && !viewModel.IsSpriteSheetMode)
        {
            return;
        }

        _isPointerPressed = true;
        _isSpriteSheetClickCandidate = viewModel.IsSpriteSheetMode;
        _didPan = false;
        _pointerDownPosition = e.GetPosition(SpriteViewport);
        _lastPanPosition = _pointerDownPosition;
        _isPanning = viewModel.Zoom.CanPan;
        e.Pointer.Capture(SpriteViewport);
        e.Handled = _isPanning || _isSpriteSheetClickCandidate;
    }

    private void SpriteViewport_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isPointerPressed || DataContext is not SpriteExplorerViewModel viewModel)
        {
            return;
        }

        var position = e.GetPosition(SpriteViewport);
        if (Math.Abs(position.X - _pointerDownPosition.X) >= 4
            || Math.Abs(position.Y - _pointerDownPosition.Y) >= 4)
        {
            _didPan = true;
        }

        if (_isPanning)
        {
            viewModel.Zoom.PanBy(
                position.X - _lastPanPosition.X,
                position.Y - _lastPanPosition.Y);
        }

        _lastPanPosition = position;
        e.Handled = _isPanning || _isSpriteSheetClickCandidate;
    }

    private void SpriteViewport_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isPointerPressed)
        {
            return;
        }

        var viewModel = DataContext as SpriteExplorerViewModel;
        var position = e.GetPosition(SpriteViewport);
        var selectFrame = _isSpriteSheetClickCandidate && !_didPan;
        var placement = selectFrame && viewModel?.SpriteSheetLayout is { } layout
            ? SpriteSheetHitTester.HitTest(layout, viewModel.Zoom, position.X, position.Y)
            : null;
        var handled = _isPanning || _isSpriteSheetClickCandidate;
        _isPointerPressed = false;
        _isPanning = false;
        _isSpriteSheetClickCandidate = false;
        _didPan = false;
        e.Pointer.Capture(null);
        if (placement is not null && viewModel is not null)
        {
            viewModel.Preview.SelectFrameByDirectionIndex(
                placement.Frame.Key.DirectionIndex,
                placement.Frame.Key.FrameIndex);
        }

        e.Handled = handled;
    }

    private void SpriteViewport_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isPointerPressed = false;
        _isPanning = false;
        _isSpriteSheetClickCandidate = false;
        _didPan = false;
    }

    private void ZoomIn_Click(object? sender, RoutedEventArgs e) => ZoomBy(1.25);

    private void ZoomOut_Click(object? sender, RoutedEventArgs e) => ZoomBy(1 / 1.25);

    private void Zoom100_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as SpriteExplorerViewModel)?.Zoom.Set100Percent();

    private void ZoomFit_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as SpriteExplorerViewModel)?.Zoom.Fit();

    private void ZoomBy(double factor)
    {
        if (DataContext is not SpriteExplorerViewModel viewModel)
        {
            return;
        }

        var bounds = SpriteViewport.Bounds;
        viewModel.Zoom.ZoomBy(factor, bounds.Width / 2, bounds.Height / 2);
    }
}
