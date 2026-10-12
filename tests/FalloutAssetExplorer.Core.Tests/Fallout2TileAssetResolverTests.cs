using System.Text;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class Fallout2TileAssetResolverTests
{
    [Fact]
    public async Task ResolveFloorAsync_UsesZeroBasedListIndexAndMatchesNormalizedArchivePath()
    {
        var archive = new InMemoryArchive(
            ("ART\\TILES\\TILES.LST", Encoding.ASCII.GetBytes("first.frm, comment\nsecond.frm\n")),
            ("art\\tiles\\second.frm", [1, 2, 3]));

        var result = await Fallout2TileAssetResolver.ResolveFloorAsync(archive, 1);

        Assert.True(result.Success);
        Assert.Equal("second.frm", result.ResourceName);
        Assert.Equal("art/tiles/second.frm", result.ResourcePath);
        Assert.Equal("art\\tiles\\second.frm", result.Entry!.Path);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task ResolveFloorAsync_ReturnsErrorWhenIndexIsOutsideList()
    {
        var archive = ArchiveWithList("only.frm\n");

        var result = await Fallout2TileAssetResolver.ResolveFloorAsync(archive, 1);

        Assert.False(result.Success);
        Assert.Contains("outside the tile list", result.Error);
    }

    [Fact]
    public async Task ResolveFloorAsync_ReturnsErrorWhenTileListIsMissing()
    {
        var archive = new InMemoryArchive();

        var result = await Fallout2TileAssetResolver.ResolveFloorAsync(archive, 0);

        Assert.False(result.Success);
        Assert.Contains(Fallout2TileAssetResolver.TileListPath, result.Error);
        Assert.Contains("missing", result.Error);
    }

    [Fact]
    public async Task ResolveFloorAsync_RejectsUnsafeListEntry()
    {
        var archive = ArchiveWithList("../outside.frm\n");

        var result = await Fallout2TileAssetResolver.ResolveFloorAsync(archive, 0);

        Assert.False(result.Success);
        Assert.Contains("not a safe resource name", result.Error);
    }

    [Fact]
    public async Task ResolveFloorAsync_CountsBlankLinesLikeCeListReader()
    {
        var archive = ArchiveWithList("\nsecond.frm\n", ("art/tiles/second.frm", [1]));

        var result = await Fallout2TileAssetResolver.ResolveFloorAsync(archive, 1);

        Assert.True(result.Success);
        Assert.Equal("second.frm", result.ResourceName);
    }

    [Fact]
    public async Task ResolveFloorAsync_ReturnsErrorWhenResourceIsNotInArchive()
    {
        var archive = ArchiveWithList("missing.frm\n");

        var result = await Fallout2TileAssetResolver.ResolveFloorAsync(archive, 0);

        Assert.False(result.Success);
        Assert.Contains("art/tiles/missing.frm", result.Error);
    }

    [Fact]
    public async Task ResolveFloorsAsync_ReadsListOnceAndCollapsesDuplicateFloorIds()
    {
        var archive = ArchiveWithList(
            "zero.frm\none.frm\n",
            ("art/tiles/zero.frm", [1]),
            ("art/tiles/one.frm", [2]));

        var results = await Fallout2TileAssetResolver.ResolveFloorsAsync(archive, [0, 1, 1]);

        Assert.Equal(2, results.Count);
        Assert.Equal("zero.frm", results[0].ResourceName);
        Assert.Equal("one.frm", results[1].ResourceName);
        Assert.Equal(1, archive.GetCopyCount(Fallout2TileAssetResolver.TileListPath));
        Assert.Equal(0, archive.GetCopyCount("art/tiles/zero.frm"));
        Assert.Equal(0, archive.GetCopyCount("art/tiles/one.frm"));
    }

    private static InMemoryArchive ArchiveWithList(
        string list,
        params (string Path, byte[] Bytes)[] additionalEntries) =>
        new([(Fallout2TileAssetResolver.TileListPath, Encoding.ASCII.GetBytes(list)), .. additionalEntries]);

    private sealed class InMemoryArchive : IGameArchive
    {
        private readonly Dictionary<ArchiveEntry, byte[]> _contents;
        private readonly Dictionary<string, int> _copyCounts = new(StringComparer.OrdinalIgnoreCase);

        public InMemoryArchive(params (string Path, byte[] Bytes)[] entries)
        {
            _contents = entries.ToDictionary(
                item => new ArchiveEntry(item.Path, item.Bytes.Length, null, "test.dat"),
                item => item.Bytes);
            BrowserEntries = _contents.Keys.ToArray();
        }

        public string Path => "test.dat";

        public FalloutGame Game => FalloutGame.Fallout2;

        public ArchiveFormat Format => ArchiveFormat.Dat2;

        public IReadOnlyList<ArchiveEntry> BrowserEntries { get; }

        public int GetCopyCount(string path) =>
            _copyCounts.TryGetValue(path.Replace('\\', '/'), out var count) ? count : 0;

        public Task CopyEntryToAsync(
            ArchiveEntry entry,
            Stream destination,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = entry.Path.Replace('\\', '/');
            _copyCounts[path] = GetCopyCount(path) + 1;
            return destination.WriteAsync(_contents[entry], cancellationToken).AsTask();
        }
    }
}
