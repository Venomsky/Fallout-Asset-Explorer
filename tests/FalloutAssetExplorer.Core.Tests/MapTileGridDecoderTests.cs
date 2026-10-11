using System.Buffers.Binary;
using FalloutAssetExplorer.App.Views;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class MapTileGridDecoderTests
{
    [Theory]
    [InlineData(0, 0, 100, 100, 99)]
    [InlineData(99.999, 0, 100, 100, 0)]
    [InlineData(0, 99.999, 100, 100, 9999)]
    [InlineData(99.999, 99.999, 100, 100, 9900)]
    [InlineData(150, 50, 500, 300, 1683)]
    [InlineData(50, 150, 300, 500, 1683)]
    public void DiagnosticGeometry_MapsPointerPositionToSerializedCell(
        double positionX,
        double positionY,
        double width,
        double height,
        int expectedIndex)
    {
        Assert.True(MapGridDiagnosticGeometry.TryGetSerializedIndex(
            positionX,
            positionY,
            width,
            height,
            out var index));
        Assert.Equal(expectedIndex, index);
    }

    [Theory]
    [InlineData(99.99, 150, 500, 300)]
    [InlineData(400, 150, 500, 300)]
    [InlineData(150, 99.99, 300, 500)]
    [InlineData(150, 400, 300, 500)]
    [InlineData(0, 0, 0, 100)]
    [InlineData(double.NaN, 0, 100, 100)]
    public void DiagnosticGeometry_RejectsLetterboxAndOutOfBoundsPositions(
        double positionX,
        double positionY,
        double width,
        double height)
    {
        Assert.False(MapGridDiagnosticGeometry.TryGetSerializedIndex(
            positionX,
            positionY,
            width,
            height,
            out var index));
        Assert.Equal(-1, index);
    }

    [Theory]
    [InlineData(0, 99, 0)]
    [InlineData(99, 0, 0)]
    [InlineData(100, 99, 1)]
    [InlineData(9999, 0, 99)]
    public void GetSquareGridCoordinates_MapsSerializedIndex(int index, int expectedX, int expectedY)
    {
        Assert.Equal(
            (expectedX, expectedY),
            MapTileGridDecoder.GetSquareGridCoordinates(index));
    }

    [Theory]
    [InlineData(0, 0, 99)]
    [InlineData(99, 0, 0)]
    [InlineData(99, 1, 100)]
    [InlineData(0, 99, 9999)]
    public void GetSerializedIndex_MapsSquareGridCoordinates(int x, int y, int expectedIndex)
    {
        Assert.Equal(expectedIndex, MapTileGridDecoder.GetSerializedIndex(x, y));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(MapTileGridDecoder.ValuesPerGrid)]
    public void GetSquareGridCoordinates_RejectsOutOfRangeIndex(int index)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MapTileGridDecoder.GetSquareGridCoordinates(index));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(MapTileGridDecoder.GridWidth, 0)]
    [InlineData(0, -1)]
    [InlineData(0, MapTileGridDecoder.GridHeight)]
    public void GetSerializedIndex_RejectsOutOfRangeCoordinate(int x, int y)
    {
        Assert.ThrowsAny<ArgumentOutOfRangeException>(
            () => MapTileGridDecoder.GetSerializedIndex(x, y));
    }

    [Theory]
    [InlineData(FalloutGame.Fallout1, 19)]
    [InlineData(FalloutGame.Fallout2, 19)]
    [InlineData(FalloutGame.Fallout2, 20)]
    public void Decode_ReadsPresentGridsInSerializedOrder(FalloutGame game, int version)
    {
        var data = MapFixture.Create(version, flags: 0x04);
        var tileOffset = MapHeaderDecoder.HeaderSize;
        WriteUInt32(data, tileOffset, 0xFEDCBA98);
        WriteUInt32(data, tileOffset + sizeof(uint), 0xA1B2C3D4);
        WriteUInt32(data, tileOffset + MapTileGridDecoder.BytesPerGrid, 0x12345678);
        var header = MapHeaderDecoder.Decode(data, game);

        var decoded = MapTileGridDecoder.Decode(data, header);

        Assert.Equal(new[] { 0, 2 }, decoded.Grids.Select(grid => grid.ElevationIndex));
        Assert.All(decoded.Grids, grid => Assert.Equal(MapTileGridDecoder.ValuesPerGrid, grid.RawValues.Length));
        Assert.Equal(0xFEDCBA98u, decoded.Grids[0].RawValues[0]);
        Assert.Equal(0xA1B2C3D4u, decoded.Grids[0].RawValues[1]);
        Assert.Equal(0x12345678u, decoded.Grids[1].RawValues[0]);
        Assert.Equal(100, MapTileGridDecoder.GridWidth);
        Assert.Equal(100, MapTileGridDecoder.GridHeight);
    }

    [Fact]
    public void Decode_StartsGridsAfterGlobalAndLocalVariableSections()
    {
        var data = MapFixture.Create(
            version: 19,
            flags: 0x0C,
            globalVariableCount: 1,
            localVariableCount: 2);
        var tileOffset = MapHeaderDecoder.HeaderSize + 3 * sizeof(uint);
        WriteUInt32(data, tileOffset, 0x10203040);
        var header = MapHeaderDecoder.Decode(data, FalloutGame.Fallout1);

        var decoded = MapTileGridDecoder.Decode(data, header);

        Assert.Equal(tileOffset, header.TileDataOffset);
        Assert.Equal(0x10203040u, Assert.Single(decoded.Grids).RawValues[0]);
    }

    [Fact]
    public void Decode_ReturnsNoGridsWhenAllElevationsAreAbsent()
    {
        var data = MapFixture.Create(version: 19, flags: 0x0E);
        var header = MapHeaderDecoder.Decode(data, FalloutGame.Fallout1);

        var decoded = MapTileGridDecoder.Decode(data, header);

        Assert.Empty(decoded.Grids);
        Assert.Equal(0, header.TileDataLength);
    }

    [Fact]
    public void Decode_ReadsThreeElevationGrids()
    {
        var data = MapFixture.Create(version: 20, flags: 0);
        var header = MapHeaderDecoder.Decode(data, FalloutGame.Fallout2);

        var decoded = MapTileGridDecoder.Decode(data, header);

        Assert.Equal(3, decoded.Grids.Count);
        Assert.Equal(new[] { 0, 1, 2 }, decoded.Grids.Select(grid => grid.ElevationIndex));
        Assert.All(decoded.Grids, grid => Assert.Equal(MapTileGridDecoder.ValuesPerGrid, grid.RawValues.Length));
        Assert.Equal(3L * MapTileGridDecoder.BytesPerGrid, header.TileDataLength);
    }

    [Fact]
    public void MapTileGridData_GetElevationSelectsOnlyPresentSlots()
    {
        var elevationZero = new MapTileGrid(0, new uint[MapTileGridDecoder.ValuesPerGrid]);
        var elevationTwo = new MapTileGrid(2, new uint[MapTileGridDecoder.ValuesPerGrid]);
        var data = new MapTileGridData(
            MapHeaderDecoder.Decode(MapFixture.Create(version: 19, flags: 0x0A), FalloutGame.Fallout1),
            [elevationZero, elevationTwo]);

        Assert.Same(elevationZero, data.GetElevation(0));
        Assert.Null(data.GetElevation(1));
        Assert.Same(elevationTwo, data.GetElevation(2));
    }

    [Fact]
    public void Decode_RejectsTruncatedGridData()
    {
        var data = MapFixture.Create(version: 19, flags: 0x0C);
        var header = MapHeaderDecoder.Decode(data, FalloutGame.Fallout1);
        Array.Resize(ref data, data.Length - 1);

        Assert.Throws<InvalidDataException>(() => MapTileGridDecoder.Decode(data, header));
    }

    [Fact]
    public void Decode_RejectsMetadataWithEntryLengthThatDoesNotMatchData()
    {
        var data = MapFixture.Create(version: 19, flags: 0x0E);
        var header = MapHeaderDecoder.Decode(data, FalloutGame.Fallout1) with { EntryLength = data.Length + 1 };

        Assert.Throws<InvalidDataException>(() => MapTileGridDecoder.Decode(data, header));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(MapHeaderDecoder.HeaderSize, -1)]
    [InlineData(long.MaxValue, 0)]
    [InlineData(MapHeaderDecoder.HeaderSize, long.MaxValue)]
    public void Decode_RejectsInconsistentTileOffsetsAndLengths(long offset, long length)
    {
        var data = MapFixture.Create(version: 19, flags: 0x0E);
        var header = MapHeaderDecoder.Decode(data, FalloutGame.Fallout1) with
        {
            TileDataOffset = offset,
            TileDataLength = length
        };

        Assert.Throws<InvalidDataException>(() => MapTileGridDecoder.Decode(data, header));
    }

    [Fact]
    public void Decode_RejectsHeaderVersionUnsupportedByItsGameContext()
    {
        var data = MapFixture.Create(version: 20, flags: 0x0E);
        var header = MapHeaderDecoder.Decode(data, FalloutGame.Fallout2);

        Assert.Throws<InvalidDataException>(
            () => MapTileGridDecoder.Decode(data, header with { Game = FalloutGame.Fallout1 }));
    }

    [Fact]
    public void Decode_PreservesRawUnsignedValueBits()
    {
        var data = MapFixture.Create(version: 19, flags: 0x0C);
        WriteUInt32(data, MapHeaderDecoder.HeaderSize, uint.MaxValue);
        var header = MapHeaderDecoder.Decode(data, FalloutGame.Fallout1);

        var decoded = MapTileGridDecoder.Decode(data, header);

        Assert.Equal(uint.MaxValue, decoded.Grids[0].RawValues[0]);
    }

    private static void WriteUInt32(byte[] data, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset, sizeof(uint)), value);

    private static class MapFixture
    {
        public static byte[] Create(
            int version,
            int flags,
            int globalVariableCount = 0,
            int localVariableCount = 0)
        {
            var presentElevations = MapHeaderDecoder.ElevationCount
                - Enumerable.Range(0, MapHeaderDecoder.ElevationCount)
                    .Count(elevation => (flags & (1 << (elevation + 1))) != 0);
            var data = new byte[MapHeaderDecoder.HeaderSize
                + (globalVariableCount + localVariableCount) * sizeof(uint)
                + presentElevations * MapTileGridDecoder.BytesPerGrid];
            BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0x00), version);
            BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0x28), flags);
            BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0x20), localVariableCount);
            BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0x30), globalVariableCount);
            return data;
        }
    }
}
