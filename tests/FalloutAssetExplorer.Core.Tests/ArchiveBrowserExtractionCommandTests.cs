using System.Buffers.Binary;
using System.ComponentModel;
using System.Text;
using FalloutAssetExplorer.App.ViewModels;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class ArchiveBrowserExtractionCommandTests
{
    [Fact]
    public async Task ExtractionAvailability_TracksArchiveSelectionAndFiltering()
    {
        using var firstArchive = new TemporaryArchive(CreateDat1Archive("ART/CRITTERS/HAPOWRAG.FRM"));
        using var emptyArchive = new TemporaryArchive(CreateEmptyDat1Archive());
        using var viewModel = new ArchiveBrowserViewModel();
        var notifiedProperties = new List<string?>();
        viewModel.PropertyChanged += OnPropertyChanged;

        Assert.False(viewModel.CanExtractSelected);
        Assert.False(viewModel.CanExtractVisible);
        Assert.False(viewModel.CanExtractAll);

        await viewModel.LoadArchiveAsync(firstArchive.Path);

        Assert.True(viewModel.CanExtractAll);
        Assert.True(viewModel.CanExtractVisible);
        Assert.False(viewModel.CanExtractSelected);
        Assert.Contains(nameof(viewModel.CanExtractAll), notifiedProperties);

        notifiedProperties.Clear();
        viewModel.UpdateSelectionCount(1);

        Assert.True(viewModel.CanExtractSelected);
        Assert.Contains(nameof(viewModel.CanExtractSelected), notifiedProperties);

        notifiedProperties.Clear();
        viewModel.SelectedExtension = ".MSG";

        Assert.False(viewModel.CanExtractVisible);
        Assert.True(viewModel.CanExtractAll);
        Assert.Contains(nameof(viewModel.CanExtractVisible), notifiedProperties);

        viewModel.SelectedExtension = ArchiveEntryFilter.AllExtensions;
        Assert.True(viewModel.CanExtractVisible);

        await viewModel.LoadArchiveAsync(emptyArchive.Path);

        Assert.False(viewModel.CanExtractSelected);
        Assert.False(viewModel.CanExtractVisible);
        Assert.False(viewModel.CanExtractAll);

        void OnPropertyChanged(object? sender, PropertyChangedEventArgs args) =>
            notifiedProperties.Add(args.PropertyName);
    }

    private static byte[] CreateEmptyDat1Archive()
    {
        byte[] archive = new byte[16];
        return archive;
    }

    private static byte[] CreateDat1Archive(string entryPath)
    {
        var separator = entryPath.LastIndexOf('/');
        var folder = separator < 0 ? "." : entryPath[..separator];
        var fileName = separator < 0 ? entryPath : entryPath[(separator + 1)..];
        var payload = new byte[] { 0x46, 0x52, 0x4D };
        using var stream = new MemoryStream();

        WriteUInt32BigEndian(stream, 1);
        stream.Write(new byte[12]);
        WriteName(stream, folder);
        WriteUInt32BigEndian(stream, 1);
        stream.Write(new byte[12]);
        WriteName(stream, fileName);
        WriteUInt32BigEndian(stream, Dat1Archive.RawEntryType);
        var offsetPosition = stream.Position;
        WriteUInt32BigEndian(stream, 0);
        WriteUInt32BigEndian(stream, checked((uint)payload.Length));
        WriteUInt32BigEndian(stream, checked((uint)payload.Length));

        var payloadOffset = checked((uint)stream.Position);
        stream.Position = offsetPosition;
        WriteUInt32BigEndian(stream, payloadOffset);
        stream.Position = stream.Length;
        stream.Write(payload);
        return stream.ToArray();
    }

    private static void WriteName(Stream stream, string value)
    {
        var bytes = Encoding.Latin1.GetBytes(value);
        stream.WriteByte(checked((byte)bytes.Length));
        stream.Write(bytes);
    }

    private static void WriteUInt32BigEndian(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private sealed class TemporaryArchive : IDisposable
    {
        public TemporaryArchive(byte[] content)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"fae-command-test-{Guid.NewGuid():N}.dat");
            File.WriteAllBytes(Path, content);
        }

        public string Path { get; }

        public void Dispose() => File.Delete(Path);
    }
}
