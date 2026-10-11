using System.Buffers.Binary;
using System.Text;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class MapHeaderDecoderTests
{
    [Theory]
    [InlineData(FalloutGame.Fallout1, 19)]
    [InlineData(FalloutGame.Fallout2, 19)]
    [InlineData(FalloutGame.Fallout2, 20)]
    public void Decode_AcceptsSupportedGameAndVersion(FalloutGame game, int version)
    {
        var data = MapFixture.Create(version: version);

        var metadata = MapHeaderDecoder.Decode(data, game);

        Assert.Equal(game, metadata.Game);
        Assert.Equal(version, metadata.Version);
        Assert.Equal(MapHeaderDecoder.HeaderSize, metadata.EntryLength);
        Assert.Equal(MapHeaderDecoder.HeaderSize, metadata.GlobalVariablesOffset);
        Assert.Equal(0, metadata.GlobalVariablesLength);
        Assert.Equal(MapHeaderDecoder.HeaderSize, metadata.LocalVariablesOffset);
        Assert.Equal(0, metadata.LocalVariablesLength);
        Assert.Equal(MapHeaderDecoder.HeaderSize, metadata.TileDataOffset);
        Assert.Equal(0, metadata.TileDataLength);
        Assert.Equal(0, metadata.PresentElevationCount);
    }

    [Fact]
    public void Decode_RejectsVersionTwentyInFalloutOneContext()
    {
        var data = MapFixture.Create(version: 20);

        var exception = Assert.Throws<InvalidDataException>(
            () => MapHeaderDecoder.Decode(data, FalloutGame.Fallout1));

        Assert.Contains("Fallout 1 MAP version 20", exception.Message);
    }

    [Theory]
    [InlineData(FalloutGame.Fallout1, 18)]
    [InlineData(FalloutGame.Fallout1, 20)]
    [InlineData(FalloutGame.Fallout2, 21)]
    public void Decode_RejectsUnsupportedVersions(FalloutGame game, int version)
    {
        var data = MapFixture.Create(version: version);

        Assert.Throws<InvalidDataException>(() => MapHeaderDecoder.Decode(data, game));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(MapHeaderDecoder.HeaderSize - 1)]
    public void Decode_RejectsDataShorterThanHeader(int length)
    {
        Assert.Throws<InvalidDataException>(
            () => MapHeaderDecoder.Decode(new byte[length], FalloutGame.Fallout1));
    }

    [Fact]
    public void Decode_UsesConfirmedHeaderFieldsAndPreservesRawNameAndReservedBytes()
    {
        var data = MapFixture.Create(
            version: 19,
            localVariableCount: 2,
            globalVariableCount: 3,
            flags: unchecked((int)0x80000000));
        var rawName = Encoding.ASCII.GetBytes("MAPNAME.MAP\0\0\0\0\0");
        rawName.CopyTo(data, 0x04);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0x14), 12345);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0x18), 2);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0x1C), 5);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0x24), 7);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0x2C), 4);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0x34), 9);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0x38), 0xfedcba98);
        data[0x3C] = 0xA5;
        data[MapHeaderDecoder.HeaderSize - 1] = 0x5A;

        var metadata = MapHeaderDecoder.Decode(data, FalloutGame.Fallout1);

        Assert.Equal(rawName, metadata.RawName);
        Assert.Equal(12345, metadata.EnteringTile);
        Assert.Equal(2, metadata.EnteringElevation);
        Assert.Equal(5, metadata.EnteringRotation);
        Assert.Equal(2, metadata.LocalVariableCount);
        Assert.Equal(7, metadata.ScriptIndex);
        Assert.Equal(unchecked((int)0x80000000), metadata.Flags);
        Assert.Equal(4, metadata.Darkness);
        Assert.Equal(3, metadata.GlobalVariableCount);
        Assert.Equal(9, metadata.MapIndex);
        Assert.Equal(0xfedcba98u, metadata.LastVisitTimeRaw);
        Assert.Equal(0xA5, metadata.ReservedHeaderData[0]);
        Assert.Equal(0x5A, metadata.ReservedHeaderData[^1]);
        Assert.Equal(3, metadata.PresentElevationCount);
        Assert.Equal(236, metadata.GlobalVariablesOffset);
        Assert.Equal(12, metadata.GlobalVariablesLength);
        Assert.Equal(248, metadata.LocalVariablesOffset);
        Assert.Equal(8, metadata.LocalVariablesLength);
        Assert.Equal(256, metadata.TileDataOffset);
        Assert.Equal(3L * MapHeaderDecoder.BytesPerElevation, metadata.TileDataLength);
    }

    [Fact]
    public void Decode_UsesElevationAbsenceFlagsToCalculateTileSection()
    {
        var data = MapFixture.Create(flags: 0x0a);

        var metadata = MapHeaderDecoder.Decode(data, FalloutGame.Fallout1);

        Assert.Equal(1, metadata.PresentElevationCount);
        Assert.Equal(MapHeaderDecoder.BytesPerElevation, metadata.TileDataLength);
        Assert.Equal(data.Length, metadata.TileDataOffset + metadata.TileDataLength);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void Decode_RejectsNegativeVariableCounts(int globalCount, int localCount)
    {
        var data = MapFixture.Create(
            globalVariableCount: globalCount,
            localVariableCount: localCount,
            length: MapHeaderDecoder.HeaderSize);

        Assert.Throws<InvalidDataException>(
            () => MapHeaderDecoder.Decode(data, FalloutGame.Fallout1));
    }

    [Fact]
    public void Decode_RejectsGlobalVariableSectionTruncatedAtBoundary()
    {
        var data = MapFixture.Create(globalVariableCount: 1, length: MapHeaderDecoder.HeaderSize);

        Assert.Contains(
            "variable and tile sections",
            Assert.Throws<InvalidDataException>(
                () => MapHeaderDecoder.Decode(data, FalloutGame.Fallout1)).Message);
    }

    [Fact]
    public void Decode_RejectsLocalVariableSectionTruncatedAtBoundary()
    {
        var data = MapFixture.Create(
            globalVariableCount: 1,
            localVariableCount: 1,
            length: MapHeaderDecoder.HeaderSize + sizeof(int));

        Assert.Contains(
            "variable and tile sections",
            Assert.Throws<InvalidDataException>(
                () => MapHeaderDecoder.Decode(data, FalloutGame.Fallout1)).Message);
    }

    [Fact]
    public void Decode_RejectsTileSectionTruncatedAtBoundary()
    {
        var data = MapFixture.Create(flags: 0x0c, length: MapHeaderDecoder.HeaderSize);

        Assert.Contains(
            "variable and tile sections",
            Assert.Throws<InvalidDataException>(
                () => MapHeaderDecoder.Decode(data, FalloutGame.Fallout1)).Message);
    }

    [Theory]
    [InlineData(int.MaxValue, 0)]
    [InlineData(0, int.MaxValue)]
    public void Decode_RejectsCountsWhoseSectionsCannotFitInEntry(int globalCount, int localCount)
    {
        var data = MapFixture.Create(
            globalVariableCount: globalCount,
            localVariableCount: localCount,
            length: MapHeaderDecoder.HeaderSize);

        Assert.Throws<InvalidDataException>(
            () => MapHeaderDecoder.Decode(data, FalloutGame.Fallout1));
    }

    private static class MapFixture
    {
        public static byte[] Create(
            int version = 19,
            int globalVariableCount = 0,
            int localVariableCount = 0,
            int flags = 0x0e,
            int? length = null)
        {
            var presentElevations = MapHeaderDecoder.ElevationCount
                - Enumerable.Range(0, MapHeaderDecoder.ElevationCount)
                    .Count(elevation => (flags & (1 << (elevation + 1))) != 0);
            var minimumLength = (long)MapHeaderDecoder.HeaderSize
                + ((long)globalVariableCount + localVariableCount) * sizeof(int)
                + (long)presentElevations * MapHeaderDecoder.BytesPerElevation;
            var data = new byte[length ?? checked((int)Math.Max(MapHeaderDecoder.HeaderSize, minimumLength))];
            BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0x00), version);
            BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0x20), localVariableCount);
            BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0x28), flags);
            BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0x30), globalVariableCount);
            return data;
        }
    }
}
