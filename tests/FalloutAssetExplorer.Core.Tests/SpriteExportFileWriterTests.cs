using FalloutAssetExplorer.App;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class SpriteExportFileWriterTests
{
    [Fact]
    public async Task WriteAtomicallyAsync_RejectsExistingFileWithoutOverwriteConfirmation()
    {
        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, "sprite.png");
        var original = new byte[] { 1, 2, 3 };
        await File.WriteAllBytesAsync(destination, original);
        var writerInvoked = false;

        await Assert.ThrowsAsync<IOException>(() =>
            SpriteExportFileWriter.WriteAtomicallyAsync(
                destination,
                overwriteConfirmed: false,
                (_, _) =>
                {
                    writerInvoked = true;
                    return Task.CompletedTask;
                }));

        Assert.False(writerInvoked);
        Assert.Equal(original, await File.ReadAllBytesAsync(destination));
        Assert.Equal([destination], Directory.GetFiles(directory.Path));
    }

    [Fact]
    public async Task WriteAtomicallyAsync_PreservesExistingFileWhenWriterFails()
    {
        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, "sprite.png");
        var original = new byte[] { 4, 5, 6 };
        await File.WriteAllBytesAsync(destination, original);

        await Assert.ThrowsAsync<IOException>(() =>
            SpriteExportFileWriter.WriteAtomicallyAsync(
                destination,
                overwriteConfirmed: true,
                (_, _) => Task.FromException(new IOException("synthetic export failure"))));

        Assert.Equal(original, await File.ReadAllBytesAsync(destination));
        Assert.Equal([destination], Directory.GetFiles(directory.Path));
    }

    [Fact]
    public async Task WriteAtomicallyAsync_CancellationRemovesTemporaryFileAndDoesNotPublishOutput()
    {
        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, "sprite.png");
        using var cancellation = new CancellationTokenSource();
        var writerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writerRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var export = SpriteExportFileWriter.WriteAtomicallyAsync(
            destination,
            overwriteConfirmed: false,
            async (stream, token) =>
            {
                writerStarted.TrySetResult();
                await writerRelease.Task.WaitAsync(token);
                await stream.WriteAsync(new byte[] { 7, 8, 9 }, token);
            },
            cancellation.Token);

        await writerStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => export);
        Assert.False(File.Exists(destination));
        Assert.Empty(Directory.GetFiles(directory.Path));
        writerRelease.TrySetResult();
    }

    [Fact]
    public async Task WriteAtomicallyAsync_CancellationPreservesExistingFileAndRemovesTemporaryFile()
    {
        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, "sprite.png");
        var original = new byte[] { 13, 14, 15 };
        await File.WriteAllBytesAsync(destination, original);
        using var cancellation = new CancellationTokenSource();
        var writerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writerRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var export = SpriteExportFileWriter.WriteAtomicallyAsync(
            destination,
            overwriteConfirmed: true,
            async (stream, token) =>
            {
                await stream.WriteAsync(new byte[] { 99, 98, 97 }, token);
                writerStarted.TrySetResult();
                await writerRelease.Task.WaitAsync(token);
            },
            cancellation.Token);

        await writerStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => export);
        Assert.Equal(original, await File.ReadAllBytesAsync(destination));
        Assert.Equal([destination], Directory.GetFiles(directory.Path));
    }

    [Fact]
    public async Task WriteAtomicallyAsync_PublishesCompletedOutput()
    {
        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, "sprite.png");

        await SpriteExportFileWriter.WriteAtomicallyAsync(
            destination,
            overwriteConfirmed: false,
            async (stream, token) =>
                await stream.WriteAsync(new byte[] { 10, 11, 12 }, token));

        Assert.Equal(new byte[] { 10, 11, 12 }, await File.ReadAllBytesAsync(destination));
        Assert.Equal([destination], Directory.GetFiles(directory.Path));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"fae-sprite-export-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
