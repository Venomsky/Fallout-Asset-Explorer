using FalloutAssetExplorer.App;
using FalloutAssetExplorer.App.ViewModels;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class MsgSaveAsServiceTests
{
    [Fact]
    public void CanSaveAs_IsEnabledOnlyAfterSuccessfulApply()
    {
        var viewModel = new MsgInspectorViewModel();
        viewModel.SetParseResult(FalloutMsgParser.Parse("{5}{}{original}"u8.ToArray()));
        viewModel.SelectedMessage = Assert.Single(viewModel.VisibleMessages);
        viewModel.SelectedTextEncoding = viewModel.TextEncodingOptions[0];

        Assert.False(viewModel.CanSaveAs);
        viewModel.BeginEdit();
        Assert.False(viewModel.CanSaveAs);
        viewModel.DraftText = "patched";
        viewModel.ApplyEdit();

        Assert.True(viewModel.CanSaveAs);
    }

    [Fact]
    public async Task SaveAsync_CancellationDoesNotWriteOutput()
    {
        using var directory = new TemporaryDirectory();

        var result = await MsgSaveAsService.SaveAsync(
            [1, 2, 3],
            destinationPath: null,
            originalSourcePath: null,
            overwriteConfirmed: false);

        Assert.Null(result);
        Assert.Empty(Directory.GetFileSystemEntries(directory.Path));
    }

    [Fact]
    public async Task SaveAsync_WritesExactPatchedBytes()
    {
        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, "patched.msg");
        byte[] expected = [0x00, 0x7B, 0x80, 0xFF, 0x0D, 0x0A, 0x7D];

        var savedPath = await MsgSaveAsService.SaveAsync(
            expected,
            destination,
            originalSourcePath: null,
            overwriteConfirmed: false);

        Assert.Equal(Path.GetFullPath(destination), savedPath);
        Assert.Equal(expected, await File.ReadAllBytesAsync(destination));
        Assert.Equal([destination], Directory.GetFiles(directory.Path));
    }

    [Fact]
    public async Task SaveAsync_RejectsTheOriginalSourcePath()
    {
        using var directory = new TemporaryDirectory();
        var source = Path.Combine(directory.Path, "original.dat");
        byte[] original = [4, 5, 6];
        await File.WriteAllBytesAsync(source, original);
        var destination = OperatingSystem.IsWindows()
            ? source.ToUpperInvariant()
            : source;

        await Assert.ThrowsAsync<IOException>(() =>
            MsgSaveAsService.SaveAsync(
                [7, 8, 9],
                destination,
                source,
                overwriteConfirmed: true));

        Assert.Equal(original, await File.ReadAllBytesAsync(source));
    }

    [Fact]
    public async Task SaveAsync_RejectsExistingDestinationUnlessOverwriteWasConfirmed()
    {
        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, "existing.msg");
        byte[] existing = [1, 3, 5];
        byte[] replacement = [2, 4, 6];
        await File.WriteAllBytesAsync(destination, existing);

        await Assert.ThrowsAsync<IOException>(() =>
            MsgSaveAsService.SaveAsync(
                replacement,
                destination,
                originalSourcePath: null,
                overwriteConfirmed: false));
        Assert.Equal(existing, await File.ReadAllBytesAsync(destination));

        await MsgSaveAsService.SaveAsync(
            replacement,
            destination,
            originalSourcePath: null,
            overwriteConfirmed: true);

        Assert.Equal(replacement, await File.ReadAllBytesAsync(destination));
        Assert.Equal([destination], Directory.GetFiles(directory.Path));
    }

    [Fact]
    public async Task SaveAsync_FailedPublicationCleansTemporaryFileAndKeepsOutputForRetry()
    {
        using var directory = new TemporaryDirectory();
        var destinationDirectory = Path.Combine(directory.Path, "destination");
        Directory.CreateDirectory(destinationDirectory);
        var unrelatedFile = Path.Combine(destinationDirectory, "keep.bin");
        byte[] unrelatedBytes = [10, 20, 30];
        await File.WriteAllBytesAsync(unrelatedFile, unrelatedBytes);
        var viewModel = CreatePatchedOutput();
        var outputBeforeSave = viewModel.PatchedOutputBytes!;

        var failure = await Assert.ThrowsAnyAsync<IOException>(() =>
            MsgSaveAsService.SaveAsync(
                outputBeforeSave,
                destinationDirectory,
                originalSourcePath: null,
                overwriteConfirmed: false));

        viewModel.ReportSaveAsFailure(failure.Message);

        Assert.True(viewModel.CanSaveAs);
        Assert.Equal(outputBeforeSave, viewModel.PatchedOutputBytes);
        Assert.Contains("available for retry", viewModel.PatchedOutputStatus);
        Assert.Equal(unrelatedBytes, await File.ReadAllBytesAsync(unrelatedFile));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp", SearchOption.TopDirectoryOnly));
        Assert.Equal([unrelatedFile], Directory.GetFiles(destinationDirectory));
    }

    [Fact]
    public async Task SaveAsync_UpdatesStatusAfterSuccessfulSave()
    {
        using var directory = new TemporaryDirectory();
        var viewModel = CreatePatchedOutput();
        var destination = Path.Combine(directory.Path, "saved.msg");
        var output = viewModel.PatchedOutputBytes!;
        var originalSnapshot = viewModel.ParseResult!.SourceSnapshot!.ToArray();

        Assert.True(viewModel.CanSaveAs);
        var savedPath = await MsgSaveAsService.SaveAsync(
            output,
            destination,
            originalSourcePath: null,
            overwriteConfirmed: false);

        viewModel.MarkOutputSaved(savedPath!);

        Assert.Equal(output, await File.ReadAllBytesAsync(destination));
        Assert.Equal(originalSnapshot, viewModel.ParseResult.SourceSnapshot!.ToArray());
        Assert.Contains(savedPath!, viewModel.PatchedOutputStatus);
        Assert.Contains("Saved standalone MSG", viewModel.PatchedOutputStatus);
        Assert.Contains("retail compatibility has not been verified", viewModel.PatchedOutputStatus);
    }

    private static MsgInspectorViewModel CreatePatchedOutput()
    {
        var viewModel = new MsgInspectorViewModel();
        viewModel.SetParseResult(FalloutMsgParser.Parse("{5}{}{original}"u8.ToArray()));
        viewModel.SelectedMessage = Assert.Single(viewModel.VisibleMessages);
        viewModel.SelectedTextEncoding = viewModel.TextEncodingOptions[0];
        viewModel.BeginEdit();
        viewModel.DraftText = "patched";
        viewModel.ApplyEdit();
        return viewModel;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"fae-msg-save-as-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
