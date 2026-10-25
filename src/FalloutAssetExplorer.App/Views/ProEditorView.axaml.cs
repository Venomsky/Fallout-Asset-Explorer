using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FalloutAssetExplorer.App.ViewModels;
using FalloutAssetExplorer.Core;

namespace FalloutAssetExplorer.App.Views;

public partial class ProEditorView : UserControl
{
    public ProEditorView()
    {
        InitializeComponent();
    }

    private void Undo_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProEditorViewModel viewModel)
        {
            viewModel.Undo();
        }
    }

    private void Redo_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProEditorViewModel viewModel)
        {
            viewModel.Redo();
        }
    }

    private async void VerifyStandaloneProSample_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProEditorViewModel viewModel)
        {
            return;
        }

        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider is null)
        {
            viewModel.ReportSampleVerificationFailure("Could not access the standalone PRO file picker.");
            return;
        }

        try
        {
            var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select one standalone Fallout 2 PRO sample",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Fallout 2 prototype files") { Patterns = ["*.PRO", "*.pro"] }
                ]
            });

            if (files.Count == 0)
            {
                viewModel.ReportSampleVerificationCancelled();
                return;
            }

            await using var stream = await files[0].OpenReadAsync();
            var bytes = await ReadBoundedAsync(
                stream,
                Fallout2PrototypeSampleVerifier.MaximumSampleFileSizeBytes);
            viewModel.VerifyStandaloneSample(files[0].Name, bytes);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException
            or InvalidOperationException
            or NotSupportedException
            or ArgumentException)
        {
            viewModel.ReportSampleVerificationFailure(
                $"Could not inspect the selected PRO sample: {exception.Message}");
        }
    }

    internal static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximumBytes)
    {
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var remainingIncludingOverflowByte = maximumBytes + 1 - (int)output.Length;
            var readLength = Math.Min(buffer.Length, remainingIncludingOverflowByte);
            var read = await stream.ReadAsync(buffer.AsMemory(0, readLength));
            if (read == 0)
            {
                return output.ToArray();
            }

            output.Write(buffer, 0, read);
            if (output.Length > maximumBytes)
            {
                throw new InvalidDataException(
                    $"The selected file exceeds the {maximumBytes:N0}-byte inspection limit.");
            }
        }
    }
}
