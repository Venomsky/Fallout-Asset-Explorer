using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FalloutAssetExplorer.App.ViewModels;

namespace FalloutAssetExplorer.App.Views;

public partial class MainWindow : Window
{
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

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        Closed -= MainWindow_Closed;
        (DataContext as ArchiveBrowserViewModel)?.Dispose();
    }

    private void About_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ArchiveBrowserViewModel viewModel)
        {
            viewModel.ReportError("Fallout Asset Explorer — read-only Fallout 1 DAT1 and Fallout 2 DAT2 browsing and entry reading are implemented. Safe file extraction is not yet available; Fallout 2 graphics preview is not supported.");
        }
    }
}
