using System.Text;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class FalloutFidArtAssetResolverTests
{
    [Fact]
    public void Decode_PreservesRawFidAndExtractsAuditedBitFields()
    {
        const int rawFid = unchecked((int)0xFABCD123);

        var fid = FalloutFidDecoder.Decode(rawFid);

        Assert.Equal(rawFid, fid.RawValue);
        Assert.Equal(0x123, fid.ArtListIndex);
        Assert.Equal(0xD, fid.WeaponCode);
        Assert.Equal(0xBC, fid.AnimationType);
        Assert.Equal(0xA, fid.ArtType);
        Assert.Equal(0x7, fid.UpperBits);
    }

    [Theory]
    [InlineData(0, "items")]
    [InlineData(2, "scenery")]
    [InlineData(3, "walls")]
    [InlineData(4, "tiles")]
    [InlineData(5, "misc")]
    [InlineData(6, "intrface")]
    [InlineData(7, "inven")]
    [InlineData(9, "backgrnd")]
    [InlineData(10, "skilldex")]
    public async Task ResolveAsync_UsesDirectArtTypeListAndZeroBasedIndex(int artType, string directory)
    {
        var archive = CreateArchive(
            FalloutGame.Fallout2,
            ArchiveFormat.Dat2,
            ListEntry($"art/{directory}/{directory}.lst", "first.frm\nsecond.frm\n"),
            ($"art/{directory}/second.frm", [1, 2]));

        var result = await FalloutFidArtAssetResolver.ResolveAsync(archive, CreateFid(artType, 1));

        Assert.True(result.Success);
        Assert.Equal(1, result.Fid.ArtListIndex);
        Assert.Equal("second.frm", result.ResourceName);
        Assert.Equal($"art/{directory}/second.frm", result.ResourcePath);
        Assert.Equal($"art/{directory}/second.frm", result.Entry!.Path);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData(0, "items")]
    [InlineData(2, "scenery")]
    [InlineData(3, "walls")]
    [InlineData(5, "misc")]
    public async Task ResolveAsync_UsesCeSeparatorsForNonTileArtLists(int artType, string directory)
    {
        var archive = CreateArchive(
            FalloutGame.Fallout2,
            ArchiveFormat.Dat2,
            ListEntry($"art/{directory}/{directory}.lst", "asset.frm \t; comment\n"),
            ($"art/{directory}/asset.frm", [1, 2]));

        var result = await FalloutFidArtAssetResolver.ResolveAsync(archive, CreateFid(artType, 0));

        Assert.True(result.Success);
        Assert.Equal("asset.frm", result.ResourceName);
        Assert.Equal($"art/{directory}/asset.frm", result.ResourcePath);
    }

    [Fact]
    public async Task ResolveAsync_PreservesOrdinaryArtListNameWithoutPadding()
    {
        var archive = CreateArchive(
            FalloutGame.Fallout2,
            ArchiveFormat.Dat2,
            ListEntry("art/walls/walls.lst", "ybk1007.frm\n"),
            ("art/walls/ybk1007.frm", [1]));

        var result = await FalloutFidArtAssetResolver.ResolveAsync(archive, CreateFid(3, 0));

        Assert.True(result.Success);
        Assert.Equal("ybk1007.frm", result.ResourceName);
        Assert.Equal("art/walls/ybk1007.frm", result.ResourcePath);
    }

    [Fact]
    public async Task ResolveAsync_PreservesEmptyArtListRowsAndTheirIndices()
    {
        var archive = CreateArchive(
            FalloutGame.Fallout2,
            ArchiveFormat.Dat2,
            ListEntry("art/walls/walls.lst", "first.frm\n\n; comment\nthird.frm\n"),
            ("art/walls/first.frm", [1]),
            ("art/walls/third.frm", [3]));

        var emptyLine = await FalloutFidArtAssetResolver.ResolveAsync(archive, CreateFid(3, 1));
        var commentLine = await FalloutFidArtAssetResolver.ResolveAsync(archive, CreateFid(3, 2));
        var third = await FalloutFidArtAssetResolver.ResolveAsync(archive, CreateFid(3, 3));

        Assert.False(emptyLine.Success);
        Assert.Contains("empty, malformed, or unsafe", emptyLine.Error);
        Assert.False(commentLine.Success);
        Assert.Contains("empty, malformed, or unsafe", commentLine.Error);
        Assert.True(third.Success);
        Assert.Equal("third.frm", third.ResourceName);
        Assert.Equal("art/walls/third.frm", third.ResourcePath);
    }

    [Fact]
    public async Task ResolveAsync_RejectsTraversalAfterApplyingCeArtListSeparators()
    {
        var archive = CreateArchive(
            FalloutGame.Fallout2,
            ArchiveFormat.Dat2,
            ListEntry("art/walls/walls.lst", "../outside.frm ; comment\n"));

        var result = await FalloutFidArtAssetResolver.ResolveAsync(archive, CreateFid(3, 0));

        Assert.False(result.Success);
        Assert.Contains("empty, malformed, or unsafe", result.Error);
        Assert.Null(result.ResourcePath);
    }

    [Fact]
    public async Task ResolveAsync_ResolvesPaddedYbk1005WallListEntryAtItsOriginalIndex()
    {
        var list = string.Concat(Enumerable.Repeat("unused.frm\n", 970))
            + "ybk1005.frm \n";
        var archive = CreateArchive(
            FalloutGame.Fallout2,
            ArchiveFormat.Dat2,
            ListEntry("art/walls/walls.lst", list),
            ("art/walls/YBK1005.FRM", [1, 2]));

        var result = await FalloutFidArtAssetResolver.ResolveAsync(archive, CreateFid(3, 970));

        Assert.True(result.Success);
        Assert.Equal(970, result.Fid.ArtListIndex);
        Assert.Equal("ybk1005.frm", result.ResourceName);
        Assert.Equal("art/walls/ybk1005.frm", result.ResourcePath);
        Assert.Equal("art/walls/YBK1005.FRM", result.Entry!.Path);
    }

    [Theory]
    [InlineData(FalloutGame.Fallout1, ArchiveFormat.Dat1)]
    [InlineData(FalloutGame.Fallout2, ArchiveFormat.Dat2)]
    public async Task ResolveAsync_UsesTheArchiveContractForBothDatFormats(
        FalloutGame game,
        ArchiveFormat format)
    {
        var archive = CreateArchive(
            game,
            format,
            ListEntry("art/items/items.lst", "item.frm\n"),
            ("art/items/item.frm", [1]));

        var result = await FalloutFidArtAssetResolver.ResolveAsync(archive, CreateFid(0, 0));

        Assert.True(result.Success);
        Assert.Equal("art/items/item.frm", result.ResourcePath);
    }

    [Fact]
    public async Task ResolveAsync_ResolvesTileFidToTheExistingFloorAssetPath()
    {
        var archive = CreateArchive(
            FalloutGame.Fallout2,
            ArchiveFormat.Dat2,
            ListEntry("art/tiles/tiles.lst", "first.frm\nsecond.frm\n"),
            ("art/tiles/second.frm", [1]));

        var fidResult = await FalloutFidArtAssetResolver.ResolveAsync(archive, CreateFid(4, 1));
        var floorResult = await Fallout2TileAssetResolver.ResolveFloorAsync(archive, 1);

        Assert.True(fidResult.Success);
        Assert.True(floorResult.Success);
        Assert.Equal(floorResult.ResourcePath, fidResult.ResourcePath);
        Assert.Equal(floorResult.Entry, fidResult.Entry);
    }

    [Fact]
    public async Task ResolveAsync_UsesTileListSeparatorsAndIgnoresCommaComment()
    {
        var archive = CreateArchive(
            FalloutGame.Fallout2,
            ArchiveFormat.Dat2,
            ListEntry("art/tiles/tiles.lst", "first.frm, comment\n"),
            ("art/tiles/first.frm", [1]));

        var result = await FalloutFidArtAssetResolver.ResolveAsync(archive, CreateFid(4, 0));

        Assert.True(result.Success);
        Assert.Equal("first.frm", result.ResourceName);
        Assert.Equal("art/tiles/first.frm", result.ResourcePath);
    }

    [Fact]
    public async Task ResolveAsync_TruncatesTileListNamesToTwelveCharacters()
    {
        const string resourceName = "abcdefgh.frm";
        var archive = CreateArchive(
            FalloutGame.Fallout2,
            ArchiveFormat.Dat2,
            ListEntry("art/tiles/tiles.lst", $"{resourceName}extra\n"),
            ($"art/tiles/{resourceName}", [1]));

        var result = await FalloutFidArtAssetResolver.ResolveAsync(archive, CreateFid(4, 0));

        Assert.True(result.Success);
        Assert.Equal(resourceName, result.ResourceName);
        Assert.Equal($"art/tiles/{resourceName}", result.ResourcePath);
    }

    [Fact]
    public async Task ResolveAsync_ResolvesLastTwelveBitIndex()
    {
        var list = string.Concat(Enumerable.Repeat("unused.frm\n", 0xFFF)) + "last.frm\n";
        var archive = CreateArchive(
            FalloutGame.Fallout1,
            ArchiveFormat.Dat1,
            ListEntry("art/items/items.lst", list),
            ("art/items/last.frm", [1]));

        var result = await FalloutFidArtAssetResolver.ResolveAsync(archive, CreateFid(0, 0xFFF));

        Assert.True(result.Success);
        Assert.Equal(0xFFF, result.Fid.ArtListIndex);
        Assert.Equal("art/items/last.frm", result.ResourcePath);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsFailureForAnOutOfRangeListIndex()
    {
        var archive = CreateArchive(
            FalloutGame.Fallout2,
            ArchiveFormat.Dat2,
            ListEntry("art/items/items.lst", "only.frm\n"));

        var result = await FalloutFidArtAssetResolver.ResolveAsync(archive, CreateFid(0, 1));

        Assert.False(result.Success);
        Assert.Contains("outside", result.Error);
        Assert.Null(result.ResourcePath);
    }

    [Fact]
    public async Task ResolveAsync_ReportsMissingListAndMissingResource()
    {
        var missingListArchive = CreateArchive(FalloutGame.Fallout2, ArchiveFormat.Dat2);
        var missingList = await FalloutFidArtAssetResolver.ResolveAsync(
            missingListArchive,
            CreateFid(2, 0));

        Assert.False(missingList.Success);
        Assert.Contains("art/scenery/scenery.lst", missingList.Error);
        Assert.Null(missingList.ResourcePath);

        var missingResourceArchive = CreateArchive(
            FalloutGame.Fallout2,
            ArchiveFormat.Dat2,
            ListEntry("art/scenery/scenery.lst", "missing.frm\n"));
        var missingResource = await FalloutFidArtAssetResolver.ResolveAsync(
            missingResourceArchive,
            CreateFid(2, 0));

        Assert.False(missingResource.Success);
        Assert.Contains("art/scenery/missing.frm", missingResource.Error);
        Assert.Null(missingResource.ResourcePath);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("../outside.frm\n")]
    [InlineData("thirteenchars.frm\n")]
    public async Task ResolveAsync_RejectsEmptyUnsafeOrOverlongSelectedNames(string list)
    {
        var archive = CreateArchive(
            FalloutGame.Fallout2,
            ArchiveFormat.Dat2,
            ListEntry("art/items/items.lst", list));

        var result = await FalloutFidArtAssetResolver.ResolveAsync(archive, CreateFid(0, 0));

        Assert.False(result.Success);
        Assert.Contains("empty, malformed, or unsafe", result.Error);
        Assert.Null(result.ResourcePath);
    }

    [Fact]
    public async Task ResolveAsync_RejectsNonAsciiListData()
    {
        var archive = new InMemoryArchive(
            FalloutGame.Fallout2,
            ArchiveFormat.Dat2,
            ("art/items/items.lst", [0xFF]));

        var result = await FalloutFidArtAssetResolver.ResolveAsync(archive, CreateFid(0, 0));

        Assert.False(result.Success);
        Assert.Contains("non-ASCII", result.Error);
        Assert.Null(result.ResourcePath);
    }

    [Theory]
    [InlineData(1, "Critter FIDs")]
    [InlineData(8, "Head FIDs")]
    [InlineData(11, "art type 11")]
    [InlineData(15, "art type 15")]
    public async Task ResolveAsync_RejectsSpecialAndUnknownArtTypes(int artType, string expectedError)
    {
        var archive = CreateArchive(FalloutGame.Fallout2, ArchiveFormat.Dat2);

        var result = await FalloutFidArtAssetResolver.ResolveAsync(archive, CreateFid(artType, 0));

        Assert.False(result.Success);
        Assert.Contains(expectedError, result.Error);
        Assert.Null(result.ResourcePath);
        Assert.Equal(CreateFid(artType, 0), result.Fid.RawValue);
    }

    [Fact]
    public async Task ResolveAsync_RejectsDuplicateListPaths()
    {
        var archive = CreateArchive(
            FalloutGame.Fallout2,
            ArchiveFormat.Dat2,
            ListEntry("art/items/items.lst", "item.frm\n"),
            ListEntry("ART\\ITEMS\\ITEMS.LST", "duplicate.frm\n"));

        var result = await FalloutFidArtAssetResolver.ResolveAsync(archive, CreateFid(0, 0));

        Assert.False(result.Success);
        Assert.Contains("multiple entries", result.Error);
    }

    [Fact]
    public async Task ResolveAsync_ThrowsForNullArchive()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => FalloutFidArtAssetResolver.ResolveAsync(null!, CreateFid(0, 0)));
    }

    private static int CreateFid(int artType, int artListIndex) =>
        unchecked((int)(((uint)artType << 24) | (uint)artListIndex));

    private static InMemoryArchive CreateArchive(
        FalloutGame game,
        ArchiveFormat format,
        params (string Path, byte[] Bytes)[] entries) =>
        new(game, format, entries);

    private static (string Path, byte[] Bytes) ListEntry(string path, string content) =>
        (path, Encoding.ASCII.GetBytes(content));

    private sealed class InMemoryArchive : IGameArchive
    {
        private readonly Dictionary<ArchiveEntry, byte[]> _contents;

        public InMemoryArchive(
            FalloutGame game,
            ArchiveFormat format,
            params (string Path, byte[] Bytes)[] entries)
        {
            Game = game;
            Format = format;
            _contents = entries.ToDictionary(
                entry => new ArchiveEntry(entry.Path, entry.Bytes.Length, null, "synthetic.dat"),
                entry => entry.Bytes);
            BrowserEntries = _contents.Keys.ToArray();
        }

        public string Path => "synthetic.dat";

        public FalloutGame Game { get; }

        public ArchiveFormat Format { get; }

        public IReadOnlyList<ArchiveEntry> BrowserEntries { get; }

        public Task CopyEntryToAsync(
            ArchiveEntry entry,
            Stream destination,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return destination.WriteAsync(_contents[entry], cancellationToken).AsTask();
        }
    }
}
