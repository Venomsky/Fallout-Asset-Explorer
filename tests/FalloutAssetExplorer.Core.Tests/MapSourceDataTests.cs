using System.Buffers.Binary;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class MapSourceDataTests
{
    [Fact]
    public void ConstructorAndRangeCopyPreserveSourceWithoutExposingMutableStorage()
    {
        var sourceBytes = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
        var expectedBytes = sourceBytes.ToArray();
        var source = new MapSourceData(sourceBytes);

        sourceBytes[0] = 0xFF;
        var returnedBytes = source.GetRange(0, source.Length).ToArray();
        returnedBytes[1] = 0xFF;

        Assert.Equal(expectedBytes, source.GetRange(0, source.Length).ToArray());
    }

    [Fact]
    public void GetRangeReadsValidBoundariesAndAllowsEmptyRangeAtEnd()
    {
        var source = new MapSourceData(new byte[] { 10, 20, 30, 40 });

        Assert.Equal(new byte[] { 20, 30 }, source.GetRange(1, 2).ToArray());
        Assert.Empty(source.GetRange(4, 0).ToArray());
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(4, 1)]
    [InlineData(5, 0)]
    [InlineData(long.MaxValue, 1)]
    [InlineData(1, long.MaxValue)]
    public void GetRangeRejectsInvalidOrOverflowingBounds(long offset, long length)
    {
        var source = new MapSourceData(new byte[] { 10, 20, 30, 40 });

        Assert.Throws<ArgumentOutOfRangeException>(() => source.GetRange(offset, length));
    }

    [Fact]
    public void EmptyAndShortSourcesAreRepresentableButParserStillRejectsShortMap()
    {
        var empty = new MapSourceData(ReadOnlyMemory<byte>.Empty);
        Assert.Equal(0, empty.Length);
        Assert.Empty(empty.GetRange(0, 0).ToArray());

        var shortSource = new MapSourceData(new byte[MapHeaderDecoder.HeaderSize - 1]);
        Assert.Equal(MapHeaderDecoder.HeaderSize - 1, shortSource.Length);
        Assert.Throws<InvalidDataException>(
            () => MapHeaderDecoder.Decode(shortSource, FalloutGame.Fallout1));
    }

    [Fact]
    public async Task SourceBasedParsersKeepResultsBoundToTheSameSource()
    {
        var bytes = CreateEmptyMap();
        var source = new MapSourceData(bytes);
        var header = MapHeaderDecoder.Decode(source, FalloutGame.Fallout1);

        Assert.Same(source, header.SourceData);
        Assert.Equal(bytes, source.GetRange(0, source.Length).ToArray());

        var grids = MapTileGridDecoder.Decode(source, header);
        Assert.Same(source, grids.Header.SourceData);

        var placements = await MapObjectPlacementDecoder.DecodeAsync(source, header);
        Assert.Same(source, placements.SourceData);
        Assert.True(placements.IsComplete);
    }

    [Fact]
    public void SourceBasedTileDecoderRejectsHeaderFromDifferentSource()
    {
        var bytes = CreateEmptyMap();
        var firstSource = new MapSourceData(bytes);
        var secondSource = new MapSourceData(bytes);
        var header = MapHeaderDecoder.Decode(firstSource, FalloutGame.Fallout1);

        Assert.Throws<InvalidDataException>(
            () => MapTileGridDecoder.Decode(secondSource, header));
    }

    [Fact]
    public void SourceBasedObjectDecoderRejectsHeaderFromDifferentSource()
    {
        var bytes = CreateEmptyMap();
        var firstSource = new MapSourceData(bytes);
        var secondSource = new MapSourceData(bytes);
        var header = MapHeaderDecoder.Decode(firstSource, FalloutGame.Fallout1);

        Assert.Throws<InvalidDataException>(() =>
        {
            _ = MapObjectPlacementDecoder.DecodeAsync(secondSource, header);
        });
    }

    private static byte[] CreateEmptyMap()
    {
        const int scriptSectionSize = 5 * sizeof(int);
        const int objectSectionSize = sizeof(int) + 3 * sizeof(int);
        var bytes = new byte[MapHeaderDecoder.HeaderSize + scriptSectionSize + objectSectionSize];
        BinaryPrimitives.WriteInt32BigEndian(bytes, 19);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(0x28), 0x0E);
        return bytes;
    }
}
