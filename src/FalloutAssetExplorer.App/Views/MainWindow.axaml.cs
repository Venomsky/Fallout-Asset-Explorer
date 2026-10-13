using System.ComponentModel;
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
    private (GridLength Workspace, GridLength Splitter)? _mapWorkspaceRowsBeforeMaximize;
    private GridLength? _viewportHeightBeforeMap;
    private GridLength? _splitterHeightBeforeMap;
    private GridLength? _workspaceHeightBeforeMap;
    private ArchiveBrowserViewModel? _observedViewModel;
    private bool _isMapInspectorActive;
    private bool _isMapWorkspaceCollapsed;
    private bool _isMapMaximized;

    public MainWindow()
    {
        InitializeComponent();
        MapInspectorControl.WorkspaceToggleRequested += MapWorkspaceToggleRequested;
        MapInspectorControl.RecordsToggleRequested += MapRecordsToggleRequested;
        MapInspectorControl.MaximizeToggleRequested += MapMaximizeToggleRequested;
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
            ObserveViewModel(viewModel);
            viewModel.UpdateSelectionCount(EntriesGrid.SelectedItems.Count);
            await viewModel.PreviewSelectedEntryAsync();
        }
    }

    private void ObserveViewModel(ArchiveBrowserViewModel viewModel)
    {
        if (ReferenceEquals(_observedViewModel, viewModel))
        {
            return;
        }

        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged -= ArchiveBrowserViewModel_PropertyChanged;
        }

        _observedViewModel = viewModel;
        _observedViewModel.PropertyChanged += ArchiveBrowserViewModel_PropertyChanged;
        SetMapInspectorLayout(viewModel.IsMapInspector);
    }

    private void ArchiveBrowserViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ArchiveBrowserViewModel.IsMapInspector)
            && sender is ArchiveBrowserViewModel viewModel)
        {
            SetMapInspectorLayout(viewModel.IsMapInspector);
        }
    }

    private void SetMapInspectorLayout(bool isMapInspector)
    {
        if (_isMapInspectorActive == isMapInspector)
        {
            return;
        }

        var rows = PreviewLayoutGrid.RowDefinitions;
        if (isMapInspector)
        {
            _viewportHeightBeforeMap = rows[2].Height;
            _splitterHeightBeforeMap = rows[3].Height;
            _workspaceHeightBeforeMap = rows[4].Height;
            MapInspectorControl.SetPreviewSplitRows(rows[2].Height, rows[3].Height, rows[4].Height);
            rows[3].Height = new GridLength(0);
            _isMapInspectorActive = true;
            return;
        }

        var mapRows = MapInspectorControl.GetPreviewSplitRows();
        if (_isMapMaximized)
        {
            if (_normalWorkspaceRowHeights is { Length: > 4 } normalRows)
            {
                normalRows[2] = _viewportHeightBeforeMap ?? mapRows.Viewport;
                normalRows[3] = _splitterHeightBeforeMap ?? mapRows.Splitter;
                normalRows[4] = _workspaceHeightBeforeMap ?? mapRows.Workspace;
            }
        }
        else
        {
            rows[2].Height = mapRows.Viewport;
            rows[3].Height = mapRows.Splitter;
            rows[4].Height = mapRows.Workspace;
        }

        _viewportHeightBeforeMap = null;
        _splitterHeightBeforeMap = null;
        _workspaceHeightBeforeMap = null;
        _isMapInspectorActive = false;
    }

    private void MapWorkspaceToggleRequested(object? sender, RoutedEventArgs e)
    {
        if (_isMapWorkspaceCollapsed)
        {
            MapInspectorControl.SetMapWorkspaceRows(
                _mapWorkspaceHeightBeforeCollapse ?? new GridLength(1, GridUnitType.Star),
                _mapWorkspaceSplitterHeightBeforeCollapse ?? new GridLength(6));
            _mapWorkspaceHeightBeforeCollapse = null;
            _mapWorkspaceSplitterHeightBeforeCollapse = null;
            _isMapWorkspaceCollapsed = false;
        }
        else
        {
            var rows = MapInspectorControl.GetMapWorkspaceRows();
            _mapWorkspaceHeightBeforeCollapse = rows.Workspace;
            _mapWorkspaceSplitterHeightBeforeCollapse = rows.Splitter;
            MapInspectorControl.SetMapWorkspaceRows(new GridLength(0), new GridLength(0));
            _isMapWorkspaceCollapsed = true;
        }

        MapInspectorControl.UpdateShellControls(_isMapWorkspaceCollapsed, _isMapMaximized);
    }

    private void MapRecordsToggleRequested(object? sender, RoutedEventArgs e) =>
        MapInspectorControl.ToggleRecords();

    private void MapMaximizeToggleRequested(object? sender, RoutedEventArgs e)
    {
        if (!_isMapMaximized)
        {
            _normalWorkspaceRowHeights = PreviewLayoutGrid.RowDefinitions
                .Select(row => row.Height)
                .ToArray();
            if (!_isMapWorkspaceCollapsed)
            {
                _mapWorkspaceRowsBeforeMaximize = MapInspectorControl.GetMapWorkspaceRows();
                MapInspectorControl.SetMapWorkspaceRows(new GridLength(0), new GridLength(0));
            }

            EntriesGrid.IsVisible = false;
            Grid.SetColumnSpan(PreviewPane, 2);
            MapInspectorControl.UpdateShellControls(_isMapWorkspaceCollapsed, true);
            for (var rowIndex = 3; rowIndex < PreviewLayoutGrid.RowDefinitions.Count; rowIndex++)
            {
                PreviewLayoutGrid.RowDefinitions[rowIndex].Height = new GridLength(0);
            }

            _isMapMaximized = true;
            return;
        }

        EntriesGrid.IsVisible = true;
        Grid.SetColumnSpan(PreviewPane, 1);
        if (_normalWorkspaceRowHeights is { } rowHeights)
        {
            for (var rowIndex = 0; rowIndex < rowHeights.Length; rowIndex++)
            {
                PreviewLayoutGrid.RowDefinitions[rowIndex].Height = rowHeights[rowIndex];
            }
        }

        if (!_isMapWorkspaceCollapsed && _mapWorkspaceRowsBeforeMaximize is { } workspaceRows)
        {
            MapInspectorControl.SetMapWorkspaceRows(workspaceRows.Workspace, workspaceRows.Splitter);
        }

        _mapWorkspaceRowsBeforeMaximize = null;
        MapInspectorControl.UpdateShellControls(_isMapWorkspaceCollapsed, false);
        _normalWorkspaceRowHeights = null;
        _isMapMaximized = false;
    }

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
            viewModel.FrmPreview.TogglePlayback();
        }
    }

    private async void ExportPng_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ArchiveBrowserViewModel viewModel || !viewModel.FrmPreview.CanExportPng)
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
                SuggestedFileName = viewModel.FrmPreview.SuggestedPngFileName,
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
            await viewModel.FrmPreview.ExportSelectedFramePngAsync(destination);
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
        MapInspectorControl.WorkspaceToggleRequested -= MapWorkspaceToggleRequested;
        MapInspectorControl.RecordsToggleRequested -= MapRecordsToggleRequested;
        MapInspectorControl.MaximizeToggleRequested -= MapMaximizeToggleRequested;
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged -= ArchiveBrowserViewModel_PropertyChanged;
        }

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
