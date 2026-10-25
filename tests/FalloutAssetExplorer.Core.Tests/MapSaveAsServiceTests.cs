using FalloutAssetExplorer.App;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class MapSaveAsServiceTests
{
    [Fact]
    public async Task SaveAsync_CancellationDoesNotWriteOutput()
    {
        using var directory = new TemporaryDirectory();
        byte[] output = [1, 2, 3];

        var result = await MapSaveAsService.SaveAsync(
            output,
            destinationPath: null,
            originalArchivePath: null,
            overwriteConfirmed: false);

        Assert.Null(result);
        Assert.Equal([1, 2, 3], output);
        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public async Task SaveAsync_WritesExactPatchedBytes()
    {
        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, "edited.map");
        byte[] output = [0x00, 0x07, 0x80, 0xFF, 0x0D, 0x0A];

        var result = await MapSaveAsService.SaveAsync(
            output,
            destination,
            originalArchivePath: null,
            overwriteConfirmed: false);

        Assert.Equal(Path.GetFullPath(destination), result);
        Assert.Equal(output, await File.ReadAllBytesAsync(destination));
        Assert.Equal([destination], Directory.GetFiles(directory.Path));
    }

    [Fact]
    public async Task SaveAsync_RejectsNormalizedOriginalArchivePath()
    {
        using var directory = new TemporaryDirectory();
        var source = Path.Combine(directory.Path, "original.dat");
        byte[] original = [4, 5, 6];
        await File.WriteAllBytesAsync(source, original);
        var destination = OperatingSystem.IsWindows()
            ? Path.Combine(directory.Path, "nested", "..", "ORIGINAL.DAT")
            : Path.Combine(directory.Path, "nested", "..", "original.dat");

        await Assert.ThrowsAsync<IOException>(() =>
            MapSaveAsService.SaveAsync(
                [7, 8, 9],
                destination,
                source,
                overwriteConfirmed: true));

        Assert.Equal(original, await File.ReadAllBytesAsync(source));
        Assert.Equal([source], Directory.GetFiles(directory.Path));
    }

    [Fact]
    public async Task SaveAsync_CancellationTokenAndWriteFailurePreserveOutputBytes()
    {
        using var directory = new TemporaryDirectory();
        byte[] output = [10, 11, 12];
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            MapSaveAsService.SaveAsync(
                output,
                Path.Combine(directory.Path, "cancelled.map"),
                originalArchivePath: null,
                overwriteConfirmed: false,
                cancellation.Token));
        Assert.False(File.Exists(Path.Combine(directory.Path, "cancelled.map")));
        Assert.Equal([10, 11, 12], output);

        var existingDestination = Path.Combine(directory.Path, "existing.map");
        byte[] existing = [20, 21];
        await File.WriteAllBytesAsync(existingDestination, existing);
        await Assert.ThrowsAsync<IOException>(() =>
            MapSaveAsService.SaveAsync(
                output,
                existingDestination,
                originalArchivePath: null,
                overwriteConfirmed: false));

        Assert.Equal(existing, await File.ReadAllBytesAsync(existingDestination));
        Assert.Equal([10, 11, 12], output);
        Assert.Equal([existingDestination], Directory.GetFiles(directory.Path));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"fae-map-save-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
