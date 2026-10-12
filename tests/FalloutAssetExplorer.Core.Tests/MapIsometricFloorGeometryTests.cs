using Avalonia;
using FalloutAssetExplorer.App.Views;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class MapIsometricFloorGeometryTests
{
    [Theory]
    [InlineData(50, 50, 100, 100, 640, 480, 0, 0, 288, 230)]
    [InlineData(51, 50, 100, 100, 640, 480, 0, 0, 336, 218)]
    [InlineData(50, 51, 100, 100, 640, 480, 0, 0, 320, 254)]
    [InlineData(50, 50, 101, 101, 640, 480, 0, 0, 240, 218)]
    [InlineData(50, 50, 100, 100, 801, 603, 7, -9, 375, 282)]
    public void TryProjectSquareCoordinates_MatchesCeReferenceCases(
        int x,
        int y,
        int centerHexX,
        int centerHexY,
        int viewportWidth,
        int viewportHeight,
        int screenOffsetX,
        int screenOffsetY,
        int expectedScreenX,
        int expectedScreenY)
    {
        Assert.True(TryCreateTransform(
            centerHexX,
            centerHexY,
            viewportWidth,
            viewportHeight,
            screenOffsetX,
            screenOffsetY,
            out var transform));

        Assert.True(MapIsometricFloorGeometry.TryProjectSquareCoordinates(
            x,
            y,
            transform,
            out var screenPosition));

        Assert.Equal(new Point(expectedScreenX, expectedScreenY), screenPosition);
        Assert.Equal(
            ReferenceSquareToScreen(
                x,
                y,
                centerHexX,
                centerHexY,
                viewportWidth,
                viewportHeight,
                screenOffsetX,
                screenOffsetY),
            screenPosition);
    }

    [Theory]
    [InlineData(0, 0, 100, 100, 640, 480)]
    [InlineData(99, 0, 100, 100, 640, 480)]
    [InlineData(0, 99, 100, 100, 640, 480)]
    [InlineData(99, 99, 100, 100, 640, 480)]
    [InlineData(50, 50, 101, 100, 801, 603)]
    [InlineData(25, 78, 100, 101, 501, 399)]
    public void ScreenInverse_MatchesCeForCellsAndCenterParities(
        int x,
        int y,
        int centerHexX,
        int centerHexY,
        int viewportWidth,
        int viewportHeight)
    {
        Assert.True(TryCreateTransform(
            centerHexX,
            centerHexY,
            viewportWidth,
            viewportHeight,
            screenOffsetX: 13,
            screenOffsetY: -7,
            out var transform));

        var referenceAnchor = ReferenceSquareToScreen(
            x,
            y,
            centerHexX,
            centerHexY,
            viewportWidth,
            viewportHeight,
            screenOffsetX: 13,
            screenOffsetY: -7);
        var samplePoint = new Point(referenceAnchor.X, referenceAnchor.Y + 12);

        Assert.True(MapIsometricFloorGeometry.TryGetSquareCoordinatesAtScreenPosition(
            samplePoint,
            transform,
            out var actualX,
            out var actualY));
        Assert.Equal((x, y), (actualX, actualY));

        Assert.True(MapIsometricFloorGeometry.TryGetSerializedIndexAtScreenPosition(
            samplePoint,
            transform,
            out var serializedIndex));
        Assert.Equal(y * 100 + (99 - x), serializedIndex);

        var referenceInverse = ReferenceScreenToSerializedIndex(
            samplePoint,
            centerHexX,
            centerHexY,
            viewportWidth,
            viewportHeight,
            screenOffsetX: 13,
            screenOffsetY: -7);
        Assert.Equal(serializedIndex, referenceInverse);
    }

    [Theory]
    [InlineData(287, 242, 49, 49, 4_950)]
    [InlineData(287, 243, 49, 50, 5_050)]
    public void ScreenInverse_UsesFloorDivisionForNegativeNumerators(
        int screenX,
        int screenY,
        int expectedX,
        int expectedY,
        int expectedSerializedIndex)
    {
        Assert.True(TryCreateTransform(100, 100, 640, 480, 0, 0, out var transform));

        Assert.True(MapIsometricFloorGeometry.TryGetSquareCoordinatesAtScreenPosition(
            new Point(screenX, screenY),
            transform,
            out var x,
            out var y));
        Assert.Equal((expectedX, expectedY), (x, y));
        Assert.True(MapIsometricFloorGeometry.TryGetSerializedIndexAtScreenPosition(
            new Point(screenX, screenY),
            transform,
            out var serializedIndex));
        Assert.Equal(expectedSerializedIndex, serializedIndex);
    }

    [Theory]
    [InlineData(0, 0, 99)]
    [InlineData(99, 0, 0)]
    [InlineData(0, 99, 9_999)]
    [InlineData(99, 99, 9_900)]
    [InlineData(56, 31, 3_143)]
    public void SerializedIndexConversion_PreservesSquareGridOrientation(
        int x,
        int y,
        int expectedSerializedIndex)
    {
        Assert.Equal(
            expectedSerializedIndex,
            MapTileGridDecoder.GetSerializedIndex(x, y));
        Assert.Equal(
            (x, y),
            MapTileGridDecoder.GetSquareGridCoordinates(expectedSerializedIndex));

        Assert.True(TryCreateTransform(100, 100, 640, 480, 0, 0, out var transform));
        Assert.True(MapIsometricFloorGeometry.TryProjectSerializedIndex(
            expectedSerializedIndex,
            transform,
            out var actualScreenPosition));
        Assert.Equal(
            ReferenceSquareToScreen(x, y, 100, 100, 640, 480, 0, 0),
            actualScreenPosition);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(100, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 100)]
    public void TryProjectSquareCoordinates_RejectsCoordinatesOutsideGrid(int x, int y)
    {
        Assert.True(TryCreateTransform(100, 100, 640, 480, 0, 0, out var transform));

        Assert.False(MapIsometricFloorGeometry.TryProjectSquareCoordinates(
            x,
            y,
            transform,
            out _));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10_000)]
    public void TryProjectSerializedIndex_RejectsIndicesOutsideGrid(int serializedIndex)
    {
        Assert.True(TryCreateTransform(100, 100, 640, 480, 0, 0, out var transform));

        Assert.False(MapIsometricFloorGeometry.TryProjectSerializedIndex(
            serializedIndex,
            transform,
            out _));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(200, 100)]
    [InlineData(100, -1)]
    [InlineData(100, 200)]
    public void TryCreateViewTransform_RejectsInvalidCenterHexCoordinates(int centerHexX, int centerHexY)
    {
        Assert.False(MapIsometricFloorGeometry.TryCreateViewTransform(
            centerHexX,
            centerHexY,
            640,
            480,
            0,
            0,
            out _));
    }

    [Theory]
    [InlineData(0, 480)]
    [InlineData(640, 0)]
    [InlineData(-1, 480)]
    [InlineData(640, -1)]
    public void TryCreateViewTransform_RejectsInvalidViewportSize(int viewportWidth, int viewportHeight)
    {
        Assert.False(MapIsometricFloorGeometry.TryCreateViewTransform(
            100,
            100,
            viewportWidth,
            viewportHeight,
            0,
            0,
            out _));
    }

    [Fact]
    public void ScreenInverse_RejectsPositionsOutsideSquareGrid()
    {
        Assert.True(TryCreateTransform(100, 100, 640, 480, 0, 0, out var transform));
        var outsideLeft = ReferenceSquareToScreen(-1, 50, 100, 100, 640, 480, 0, 0);
        var outsideRight = ReferenceSquareToScreen(100, 50, 100, 100, 640, 480, 0, 0);
        var leftSample = new Point(outsideLeft.X, outsideLeft.Y + 12);
        var rightSample = new Point(outsideRight.X, outsideRight.Y + 12);

        Assert.False(MapIsometricFloorGeometry.TryGetSquareCoordinatesAtScreenPosition(
            leftSample,
            transform,
            out _,
            out _));
        Assert.False(MapIsometricFloorGeometry.TryGetSerializedIndexAtScreenPosition(
            rightSample,
            transform,
            out _));
    }

    [Fact]
    public void ScreenInverse_RejectsNonFiniteAndOverflowingPositions()
    {
        Assert.True(TryCreateTransform(100, 100, 640, 480, 0, 0, out var transform));

        Assert.False(MapIsometricFloorGeometry.TryGetSquareCoordinatesAtScreenPosition(
            new Point(double.MaxValue, double.MaxValue),
            transform,
            out _,
            out _));
        Assert.False(MapIsometricFloorGeometry.TryGetSerializedIndexAtScreenPosition(
            new Point(double.NaN, 0),
            transform,
            out _));
    }

    private static bool TryCreateTransform(
        int centerHexX,
        int centerHexY,
        int viewportWidth,
        int viewportHeight,
        int screenOffsetX,
        int screenOffsetY,
        out MapIsometricFloorViewTransform transform) =>
        MapIsometricFloorGeometry.TryCreateViewTransform(
            centerHexX,
            centerHexY,
            viewportWidth,
            viewportHeight,
            screenOffsetX,
            screenOffsetY,
            out transform);

    private static Point ReferenceSquareToScreen(
        int x,
        int y,
        int centerHexX,
        int centerHexY,
        int viewportWidth,
        int viewportHeight,
        int screenOffsetX,
        int screenOffsetY)
    {
        var tileX = centerHexX;
        var tileY = centerHexY;
        var tileOffsetX = (viewportWidth - 32) / 2;
        var tileOffsetY = (viewportHeight - 16) / 2;
        if ((tileX & 1) != 0)
        {
            tileX--;
            tileOffsetX -= 32;
        }

        var squareCenterX = tileX / 2;
        var squareCenterY = tileY / 2;
        var squareOffsetX = tileOffsetX - 16;
        var squareOffsetY = tileOffsetY - 2;
        if ((tileY & 1) != 0)
        {
            squareOffsetX -= 16;
            squareOffsetY -= 12;
        }

        var columnDelta = x - squareCenterX;
        var rowDelta = y - squareCenterY;
        return new Point(
            squareOffsetX + screenOffsetX + 48 * columnDelta + 32 * rowDelta,
            squareOffsetY + screenOffsetY - 12 * columnDelta + 24 * rowDelta);
    }

    private static int ReferenceScreenToSerializedIndex(
        Point screenPosition,
        int centerHexX,
        int centerHexY,
        int viewportWidth,
        int viewportHeight,
        int screenOffsetX,
        int screenOffsetY)
    {
        var tileX = centerHexX;
        var tileY = centerHexY;
        var tileOffsetX = (viewportWidth - 32) / 2;
        var tileOffsetY = (viewportHeight - 16) / 2;
        if ((tileX & 1) != 0)
        {
            tileX--;
            tileOffsetX -= 32;
        }

        var squareCenterX = tileX / 2;
        var squareCenterY = tileY / 2;
        var squareOffsetX = tileOffsetX - 16;
        var squareOffsetY = tileOffsetY - 2;
        if ((tileY & 1) != 0)
        {
            squareOffsetX -= 16;
            squareOffsetY -= 12;
        }

        var relativeX = screenPosition.X - screenOffsetX - squareOffsetX;
        var relativeY = screenPosition.Y - screenOffsetY - squareOffsetY - 12;
        var serializedColumn = MapTileGridDecoder.GridWidth - 1
            - (int)Math.Floor((3 * relativeX - 4 * relativeY) / 192)
            - squareCenterX;
        var row = (int)Math.Floor((4 * relativeY + relativeX) / 128) + squareCenterY;
        return serializedColumn + MapTileGridDecoder.GridWidth * row;
    }
}
