using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FalloutAssetExplorer.App.ViewModels;
using FalloutAssetExplorer.Core;

namespace FalloutAssetExplorer.App.Views;

public partial class MainWindow : Window
{
    private GridLength? _mapWorkspaceHeightBeforeCollapse;
    private GridLength? _mapWorkspaceSplitterHeightBeforeCollapse;
    private bool _isMapWorkspaceCollapsed;

    public MainWindow()
    {
        InitializeComponent();
        MapInspectorControl.WorkspaceToggleRequested += MapWorkspaceToggleRequested;
        MapInspectorControl.RecordsToggleRequested += MapRecordsToggleRequested;
        MapInspectorControl.MapSaveAsRequested += MapSaveAsRequested;
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
            var selectedEntries = EntriesGrid.SelectedItems.OfType<ArchiveEntry>().ToArray();
            viewModel.UpdateSelectionCount(selectedEntries.Length);
            viewModel.UpdateSelectedFrmCount(
                selectedEntries.Count(entry =>
                    string.Equals(entry.Extension, ".FRM", StringComparison.OrdinalIgnoreCase)));
            await viewModel.PreviewSelectedEntryAsync();
        }
    }

    private async void MsgFilesList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not ArchiveBrowserViewModel viewModel)
        {
            return;
        }

        if (MsgFilesList.SelectedItem is MsgArchiveFileItem selectedFile)
        {
            await viewModel.SelectMsgFileAsync(selectedFile);
        }

        MsgFilesList.SelectedItem = viewModel.SelectedMsgFile;
    }

    private void BeginMsgTextEdit_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ArchiveBrowserViewModel viewModel)
        {
            viewModel.MsgInspector.BeginEdit();
        }
    }

    private void ApplyMsgTextEdit_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ArchiveBrowserViewModel viewModel)
        {
            viewModel.MsgInspector.ApplyEdit();
        }
    }

    private void CancelMsgTextEdit_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ArchiveBrowserViewModel viewModel)
        {
            viewModel.MsgInspector.CancelEdit();
        }
    }

    private async void SaveMsgAs_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ArchiveBrowserViewModel viewModel
            || !viewModel.MsgInspector.CanSaveAs
            || viewModel.MsgInspector.PatchedOutputBytes is not { } outputBytes)
        {
            return;
        }

        var msgInspector = viewModel.MsgInspector;
        var outputVersion = msgInspector.PatchedOutputVersion;
        var originalSourcePath = msgInspector.OriginalSourcePath;

        try
        {
            var msgFileType = new FilePickerFileType("Fallout MSG file")
            {
                Patterns = ["*.msg", "*.MSG"]
            };
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save patched MSG as",
                SuggestedFileName = "edited.msg",
                DefaultExtension = "msg",
                FileTypeChoices = [msgFileType],
                SuggestedFileType = msgFileType,
                ShowOverwritePrompt = true
            });

            if (file is null)
            {
                return;
            }

            if (!msgInspector.IsCurrentPatchedOutput(outputVersion))
            {
                msgInspector.ReportSaveAsFailure(
                    "The MSG output changed while choosing a destination. Start Save As again.");
                return;
            }

            var destinationPath = file.Path.LocalPath;
            var savedPath = await MsgSaveAsService.SaveAsync(
                outputBytes,
                destinationPath,
                originalSourcePath,
                overwriteConfirmed: File.Exists(destinationPath));
            if (savedPath is null)
            {
                return;
            }

            if (msgInspector.IsCurrentPatchedOutput(outputVersion))
            {
                msgInspector.MarkOutputSaved(savedPath);
            }
            else
            {
                viewModel.ReportError(
                    $"Saved the previously selected MSG output to {savedPath}; "
                    + "the current MSG source changed while saving.");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or NotSupportedException
            or ArgumentException
            or System.Security.SecurityException)
        {
            if (msgInspector.IsCurrentPatchedOutput(outputVersion))
            {
                msgInspector.ReportSaveAsFailure(exception.Message);
            }
            else
            {
                viewModel.ReportError(
                    $"Could not save the previously selected MSG output: {exception.Message} "
                    + "The current MSG source changed while saving.");
            }
        }
    }

    private async void MapSaveAsRequested(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ArchiveBrowserViewModel viewModel
            || viewModel.CreateMapSaveCandidate() is not { } candidate)
        {
            return;
        }

        try
        {
            var mapFileType = new FilePickerFileType("Fallout MAP file")
            {
                Patterns = ["*.map", "*.MAP"]
            };
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save edited MAP as",
                SuggestedFileName = candidate.SuggestedFileName,
                DefaultExtension = "map",
                FileTypeChoices = [mapFileType],
                SuggestedFileType = mapFileType,
                ShowOverwritePrompt = true
            });

            if (file is null)
            {
                return;
            }

            var destinationPath = file.Path.LocalPath;
            await viewModel.SaveMapAsAsync(
                candidate,
                destinationPath,
                overwriteConfirmed: File.Exists(destinationPath));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or NotSupportedException
            or ArgumentException
            or System.Security.SecurityException)
        {
            viewModel.ReportMapSaveAsFailure(candidate, exception.Message);
        }
    }

    private void NavigateToScriptInt_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ArchiveBrowserViewModel viewModel)
        {
            viewModel.NavigateToSelectedScriptInt();
        }
    }

    private void ReturnToScriptList_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ArchiveBrowserViewModel viewModel)
        {
            viewModel.ReturnToScriptList();
        }
    }

    private async void CopyResourcePath_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ArchiveBrowserViewModel viewModel
            || viewModel.SelectedEntryPathToCopy is not { } path)
        {
            return;
        }

        try
        {
            var clipboard = Clipboard;
            if (clipboard is null)
            {
                viewModel.ReportError("Could not access the system clipboard.");
                return;
            }

            await clipboard.SetTextAsync(path);
        }
        catch (Exception exception) when (
            exception is IOException
            or InvalidOperationException
            or NotSupportedException
            or OperationCanceledException
            or UnauthorizedAccessException
            or ExternalException)
        {
            viewModel.ReportError($"Could not copy resource path: {exception.Message}");
        }
    }

    private async void OpenSelectedFrmInSpriteExplorer_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ArchiveBrowserViewModel viewModel)
        {
            await viewModel.OpenSelectedFrmInSpriteExplorerAsync();
        }
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

        MapInspectorControl.UpdateShellControls(_isMapWorkspaceCollapsed);
    }

    private void MapRecordsToggleRequested(object? sender, RoutedEventArgs e) =>
        MapInspectorControl.ToggleRecords();

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

    private async void ExportSelectedFrms_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ArchiveBrowserViewModel viewModel)
        {
            return;
        }

        await TryExportFrmsAsync(
            viewModel,
            EntriesGrid.SelectedItems.OfType<ArchiveEntry>().ToArray(),
            "selected");
    }

    private async void ExportVisibleFrms_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ArchiveBrowserViewModel viewModel)
        {
            return;
        }

        await TryExportFrmsAsync(viewModel, viewModel.VisibleEntries.ToArray(), "visible");
    }

    private void CancelFrmExport_Click(object? sender, RoutedEventArgs e)
    {
        (DataContext as ArchiveBrowserViewModel)?.CancelFrmBatchExport();
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

    private async void ExportSpritesheet_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ArchiveBrowserViewModel viewModel
            || !viewModel.FrmPreview.CanExportSpritesheet)
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
                Title = "Export current FRM spritesheet as PNG",
                SuggestedFileName = viewModel.FrmPreview.SuggestedSpritesheetPngFileName,
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
            await viewModel.FrmPreview.ExportSpritesheetPngAsync(destination);
            viewModel.ReportError($"Exported spritesheet PNG: {file.Name}");
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or NotSupportedException
            or ArgumentException
            or InvalidDataException)
        {
            viewModel.ReportError($"Could not export spritesheet PNG: {exception.Message}");
        }
    }

    private async void ExportStandaloneProReport_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ArchiveBrowserViewModel viewModel
            || !viewModel.CanExportStandaloneProReport
            || viewModel.StandaloneProInspection is not { } inspection)
        {
            return;
        }

        try
        {
            var reportFileType = new FilePickerFileType("PRO inspection report")
            {
                Patterns = ["*.txt"]
            };
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export PRO inspection report",
                SuggestedFileName = GetStandaloneProSuggestedName(inspection.EntryPath, ".report.txt"),
                DefaultExtension = "txt",
                FileTypeChoices = [reportFileType],
                SuggestedFileType = reportFileType,
                ShowOverwritePrompt = true
            });

            if (file is null)
            {
                return;
            }

            if (!viewModel.IsCurrentStandaloneProInspection(inspection))
            {
                viewModel.ReportError("Export canceled because the selected PRO entry changed while choosing the destination.");
                return;
            }

            await using var destination = await file.OpenWriteAsync();
            await viewModel.ExportStandaloneProReportAsync(destination, inspection);
            viewModel.ReportError($"Saved PRO inspection report: {file.Name}");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or NotSupportedException
            or ArgumentException
            or InvalidDataException)
        {
            viewModel.ReportError($"Could not export the PRO report: {exception.Message}");
        }
    }

    private async void ExportStandaloneProRawBin_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ArchiveBrowserViewModel viewModel
            || !viewModel.CanExportStandaloneProRawBin
            || viewModel.StandaloneProInspection is not { } inspection)
        {
            return;
        }

        try
        {
            var binaryFileType = new FilePickerFileType("Raw PRO data")
            {
                Patterns = ["*.bin"]
            };
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export raw PRO bytes",
                SuggestedFileName = GetStandaloneProSuggestedName(inspection.EntryPath, ".bin"),
                DefaultExtension = "bin",
                FileTypeChoices = [binaryFileType],
                SuggestedFileType = binaryFileType,
                ShowOverwritePrompt = true
            });

            if (file is null)
            {
                return;
            }

            if (!viewModel.IsCurrentStandaloneProInspection(inspection))
            {
                viewModel.ReportError("Export canceled because the selected PRO entry changed while choosing the destination.");
                return;
            }

            await using var destination = await file.OpenWriteAsync();
            await viewModel.ExportStandaloneProRawBinAsync(destination, inspection);
            viewModel.ReportError($"Saved raw PRO bytes: {file.Name}");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or NotSupportedException
            or ArgumentException
            or InvalidDataException)
        {
            viewModel.ReportError($"Could not export raw PRO BIN: {exception.Message}");
        }
    }

    private static string GetStandaloneProSuggestedName(string entryPath, string suffix)
    {
        var normalizedPath = entryPath.Replace('\\', '/');
        var fileName = normalizedPath[(normalizedPath.LastIndexOf('/') + 1)..];
        return $"{fileName}{suffix}";
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

    private async Task TryExportFrmsAsync(
        ArchiveBrowserViewModel viewModel,
        IReadOnlyList<ArchiveEntry> entries,
        string scopeLabel)
    {
        if (!entries.Any(entry =>
                string.Equals(entry.Extension, ".FRM", StringComparison.OrdinalIgnoreCase)))
        {
            viewModel.ReportError($"No {scopeLabel} FRM entries are available to export.");
            return;
        }

        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = $"Select destination for {scopeLabel} FRM spritesheets",
                AllowMultiple = false
            });

            if (folders.Count == 0)
            {
                return;
            }

            await viewModel.ExportFrmSpritesheetsAsync(entries, folders[0].Path.LocalPath);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or NotSupportedException
            or ArgumentException)
        {
            viewModel.ReportError($"Could not export {scopeLabel} FRM spritesheets: {exception.Message}");
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        Closed -= MainWindow_Closed;
        MapInspectorControl.WorkspaceToggleRequested -= MapWorkspaceToggleRequested;
        MapInspectorControl.RecordsToggleRequested -= MapRecordsToggleRequested;
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
