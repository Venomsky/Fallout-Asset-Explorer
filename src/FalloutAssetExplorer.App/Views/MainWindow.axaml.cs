using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FalloutAssetExplorer.App.ViewModels;
using FalloutAssetExplorer.Core;

namespace FalloutAssetExplorer.App.Views;

public partial class MainWindow : Window
{
    private GridLength[]? _normalWorkspaceRowHeights;
    private GridLength? _mapWorkspaceHeightBeforeCollapse;
    private GridLength? _mapWorkspaceSplitterHeightBeforeCollapse;
    private GridLength? _mapRecordsHeightBeforeCollapse;
    private GridLength? _mapRecordsSplitterHeightBeforeCollapse;
    private bool _isMapWorkspaceCollapsed;
    private bool _isMapRecordsCollapsed;
    private bool _isMapMaximized;

    public MainWindow()
    {
        InitializeComponent();
        Closed += MainWindow_Closed;
    }

    private async void OpenArchive_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ArchiveBrowserViewModel viewModel)
        {
            return;
        }

        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open Fallout archive",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Fallout DAT archives") { Patterns = ["*.dat", "*.DAT"] },
                    FilePickerFileTypes.All
                ]
            });

            if (files.Count > 0)
            {
                await viewModel.LoadArchiveAsync(files[0].Path.LocalPath);
            }
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or NotSupportedException)
        {
            viewModel.ReportError($"Could not open the archive picker: {exception.Message}");
        }
    }

    private async void SetGameDataFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ArchiveBrowserViewModel viewModel)
        {
            return;
        }

        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Fallout game-data folder",
                AllowMultiple = false
            });

            if (folders.Count > 0)
            {
                await viewModel.SetGameDataDirectoryAsync(folders[0].Path.LocalPath);
            }
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or NotSupportedException
            or ArgumentException)
        {
            viewModel.ReportError($"Could not configure the game-data folder: {exception.Message}");
        }
    }

    private async void EntriesGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is ArchiveBrowserViewModel viewModel)
        {
            viewModel.UpdateSelectionCount(EntriesGrid.SelectedItems.Count);
            await viewModel.PreviewSelectedEntryAsync();
        }
    }

    private void MapGridView_CellInspected(object? sender, MapGridCellInspectedEventArgs e)
    {
        if (DataContext is ArchiveBrowserViewModel viewModel)
        {
            viewModel.InspectMapGridCell(e.X, e.Y, e.SerializedIndex, e.RawValue);
        }
    }

    private void MapGridView_CellSelected(object? sender, MapGridCellInspectedEventArgs e)
    {
        if (DataContext is ArchiveBrowserViewModel viewModel)
        {
            viewModel.SelectMapGridCell(e.SerializedIndex);
        }
    }

    private void MapFloorTileView_ObjectSelected(
        object? sender,
        MapObjectPlacementSelectedEventArgs e)
    {
        if (DataContext is ArchiveBrowserViewModel viewModel)
        {
            viewModel.SelectMapObjectFromFloorMarker(e.Placement);
        }
    }

    private void MapIsometricFloorView_ObjectSelectionChanged(
        object? sender,
        MapObjectMarkerSelectionChangedEventArgs e)
    {
        if (DataContext is ArchiveBrowserViewModel viewModel)
        {
            viewModel.SelectMapObjectFromIsometricMarker(e.RecordIndex);
        }
    }

    private void ClearMapObjectSearch_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ArchiveBrowserViewModel viewModel)
        {
            viewModel.MapObjectSearchText = string.Empty;
        }
    }

    private void ToggleMapWorkspace_Click(object? sender, RoutedEventArgs e)
    {
        var workspaceRow = PreviewLayoutGrid.RowDefinitions[4];
        var workspaceSplitterRow = PreviewLayoutGrid.RowDefinitions[3];
        if (_isMapWorkspaceCollapsed)
        {
            workspaceRow.Height = _mapWorkspaceHeightBeforeCollapse
                ?? new GridLength(1, GridUnitType.Star);
            workspaceSplitterRow.Height = _mapWorkspaceSplitterHeightBeforeCollapse
                ?? new GridLength(6);
            _mapWorkspaceHeightBeforeCollapse = null;
            _mapWorkspaceSplitterHeightBeforeCollapse = null;
            MapWorkspaceToggleButton.Content = "Hide workspace";
            _isMapWorkspaceCollapsed = false;
            return;
        }

        _mapWorkspaceHeightBeforeCollapse = workspaceRow.Height;
        _mapWorkspaceSplitterHeightBeforeCollapse = workspaceSplitterRow.Height;
        workspaceRow.Height = new GridLength(0);
        workspaceSplitterRow.Height = new GridLength(0);
        MapWorkspaceToggleButton.Content = "Show workspace";
        _isMapWorkspaceCollapsed = true;
    }

    private void ToggleMapRecords_Click(object? sender, RoutedEventArgs e)
    {
        var recordsRow = RecordPrototypeGrid.RowDefinitions[0];
        var recordsSplitterRow = RecordPrototypeGrid.RowDefinitions[1];
        if (_isMapRecordsCollapsed)
        {
            recordsRow.Height = _mapRecordsHeightBeforeCollapse
                ?? new GridLength(1, GridUnitType.Star);
            recordsSplitterRow.Height = _mapRecordsSplitterHeightBeforeCollapse
                ?? new GridLength(6);
            _mapRecordsHeightBeforeCollapse = null;
            _mapRecordsSplitterHeightBeforeCollapse = null;
            MapRecordsToggleButton.Content = "Hide records";
            _isMapRecordsCollapsed = false;
            return;
        }

        _mapRecordsHeightBeforeCollapse = recordsRow.Height;
        _mapRecordsSplitterHeightBeforeCollapse = recordsSplitterRow.Height;
        recordsRow.Height = new GridLength(0);
        recordsSplitterRow.Height = new GridLength(0);
        MapRecordsToggleButton.Content = "Show records";
        _isMapRecordsCollapsed = true;
    }

    private void ToggleMapMaximized_Click(object? sender, RoutedEventArgs e)
    {
        if (!_isMapMaximized)
        {
            _normalWorkspaceRowHeights = PreviewLayoutGrid.RowDefinitions
                .Select(row => row.Height)
                .ToArray();
            EntriesGrid.IsVisible = false;
            Grid.SetColumnSpan(PreviewPane, 2);
            MapWorkspaceToggleButton.IsEnabled = false;
            MapRecordsToggleButton.IsEnabled = false;
            for (var rowIndex = 3; rowIndex < PreviewLayoutGrid.RowDefinitions.Count; rowIndex++)
            {
                PreviewLayoutGrid.RowDefinitions[rowIndex].Height = new GridLength(0);
            }

            MapMaximizeButton.Content = "Restore workspace";
            _isMapMaximized = true;
            return;
        }

        EntriesGrid.IsVisible = true;
        Grid.SetColumnSpan(PreviewPane, 1);
        MapWorkspaceToggleButton.IsEnabled = true;
        MapRecordsToggleButton.IsEnabled = true;
        if (_normalWorkspaceRowHeights is { } rowHeights)
        {
            for (var rowIndex = 0; rowIndex < rowHeights.Length; rowIndex++)
            {
                PreviewLayoutGrid.RowDefinitions[rowIndex].Height = rowHeights[rowIndex];
            }
        }

        _normalWorkspaceRowHeights = null;
        MapMaximizeButton.Content = "Maximize map";
        _isMapMaximized = false;
    }

    private async void MapDiagnosticLayer_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo
            && combo.SelectedIndex is 2 or 3 or 4
            && DataContext is ArchiveBrowserViewModel viewModel)
        {
            viewModel.SelectedMapDiagnosticLayerIndex = combo.SelectedIndex;
            await viewModel.PrepareMapFloorTilesAsync();
        }
    }

    private void MapHexFit_Click(object? sender, RoutedEventArgs e) => MapHexGridView.FitToView();

    private void MapHexCenter_Click(object? sender, RoutedEventArgs e) => MapHexGridView.CenterOnSelection();

    private void MapFloorFit_Click(object? sender, RoutedEventArgs e) => MapFloorTileView.FitToView();

    private void MapIsometricFloorFit_Click(object? sender, RoutedEventArgs e) =>
        MapIsometricFloorView.FitToView();

    private async void ExtractSelected_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ArchiveBrowserViewModel viewModel)
        {
            return;
        }

        await TryExtractAsync(
            viewModel,
            EntriesGrid.SelectedItems.OfType<ArchiveEntry>().ToList(),
            "selected entries");
    }

    private async void ExtractVisible_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ArchiveBrowserViewModel viewModel)
        {
            return;
        }

        await TryExtractAsync(
            viewModel,
            viewModel.VisibleEntries.ToList(),
            "visible entries");
    }

    private async void ExtractAll_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ArchiveBrowserViewModel viewModel)
        {
            return;
        }

        await TryExtractAsync(
            viewModel,
            viewModel.Entries.ToList(),
            "all archive entries");
    }

    private void TogglePlayback_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ArchiveBrowserViewModel viewModel)
        {
            viewModel.TogglePlayback();
        }
    }

    private async void ExportPng_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ArchiveBrowserViewModel viewModel || !viewModel.CanExportPng)
        {
            return;
        }

        try
        {
            var pngFileType = new FilePickerFileType("PNG image")
            {
                Patterns = ["*.png"]
            };
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export FRM frame as PNG",
                SuggestedFileName = viewModel.SuggestedPngFileName,
                DefaultExtension = "png",
                FileTypeChoices = [pngFileType],
                SuggestedFileType = pngFileType,
                ShowOverwritePrompt = true
            });

            if (file is null)
            {
                return;
            }

            await using var destination = await file.OpenWriteAsync();
            await viewModel.ExportSelectedFramePngAsync(destination);
            viewModel.ReportError($"Exported PNG: {file.Name}");
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or NotSupportedException
            or ArgumentException
            or InvalidDataException)
        {
            viewModel.ReportError($"Could not export PNG: {exception.Message}");
        }
    }

    private async Task TryExtractAsync(
        ArchiveBrowserViewModel viewModel,
        IReadOnlyList<ArchiveEntry> entries,
        string scopeLabel)
    {
        if (entries.Count == 0)
        {
            viewModel.ReportError($"No {scopeLabel} are available to extract.");
            return;
        }

        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = $"Select destination for {scopeLabel}",
                AllowMultiple = false
            });

            if (folders.Count == 0)
            {
                return;
            }

            await viewModel.ExtractEntriesAsync(entries, folders[0].Path.LocalPath, scopeLabel);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or NotSupportedException
            or ArgumentException)
        {
            viewModel.ReportError($"Could not extract {scopeLabel}: {exception.Message}");
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        Closed -= MainWindow_Closed;
        (DataContext as ArchiveBrowserViewModel)?.Dispose();
    }

    private void About_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ArchiveBrowserViewModel viewModel)
        {
            viewModel.ReportError("Fallout Asset Explorer — DAT1 and DAT2 browsing, entry reading, safe archive extraction, and combined FRM previews are supported. Fallout 2 preview colors have not been visually verified against the game.");
        }
    }
}
