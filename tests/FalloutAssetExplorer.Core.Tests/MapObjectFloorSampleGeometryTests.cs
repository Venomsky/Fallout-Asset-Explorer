using Avalonia;
using FalloutAssetExplorer.App.Views;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class MapObjectFloorSampleGeometryTests
{
    [Theory]
    [InlineData(100, 100, 50, 50)]
    [InlineData(101, 100, 50, 50)]
    [InlineData(99, 100, 49, 50)]
    [InlineData(100, 101, 50, 50)]
    [InlineData(0, 0, 0, 0)]
    [InlineData(199, 0, 99, 0)]
    [InlineData(0, 199, 0, 99)]
    [InlineData(199, 199, 99, 99)]
    public void TryGetSampledFloorCell_ProjectsHexTileThroughCeGravitySample(
        int hexX,
        int hexY,
        int expectedX,
        int expectedY)
    {
        Assert.True(MapObjectFloorSampleGeometry.TryGetSampledFloorCell(
            hexX,
            hexY,
            out var x,
            out var y));

        Assert.Equal(expectedX, x);
        Assert.Equal(expectedY, y);
    }

    [Fact]
    public void TryGetSampledFloorCell_MatchesCeSquareIndexForArbridgeRegression()
    {
        const int rawHexIndex = 12_486;
        var expectedSquareIndex = GetCeGravitySampleSquareIndex(
            rawHexIndex,
            centerHexX: 100,
            centerHexY: 100,
            viewportWidth: 640,
            viewportHeight: 480);

        Assert.Equal(3_143, expectedSquareIndex);
        Assert.Equal((56, 31), MapTileGridDecoder.GetSquareGridCoordinates(expectedSquareIndex));
        Assert.True(MapObjectFloorSampleGeometry.TryGetSampledFloorCell(
            113,
            62,
            out var x,
            out var y));

        var actualSquareIndex = MapTileGridDecoder.GetSerializedIndex(x, y);
        Assert.Equal(expectedSquareIndex, actualSquareIndex);
        Assert.NotEqual(3_156, actualSquareIndex);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(99, 0)]
    [InlineData(100, 100)]
    [InlineData(101, 101)]
    [InlineData(199, 199)]
    public void TryGetSampledFloorCell_IsIndependentOfCameraCenter(
        int hexX,
        int hexY)
    {
        Assert.True(MapObjectFloorSampleGeometry.TryGetSampledFloorCell(
            hexX,
            hexY,
            100,
            100,
            out var defaultX,
            out var defaultY));
        Assert.True(MapObjectFloorSampleGeometry.TryGetSampledFloorCell(
            hexX,
            hexY,
            81,
            101,
            out var shiftedX,
            out var shiftedY));

        Assert.Equal((defaultX, defaultY), (shiftedX, shiftedY));
        Assert.Equal(
            GetCeGravitySampleSquareIndex(hexY * MapObjectPlacementDecoder.HexGridWidth
                + MapObjectPlacementDecoder.HexGridWidth - 1 - hexX, 100, 100, 640, 480),
            MapTileGridDecoder.GetSerializedIndex(defaultX, defaultY));
        Assert.Equal(
            GetCeGravitySampleSquareIndex(hexY * MapObjectPlacementDecoder.HexGridWidth
                + MapObjectPlacementDecoder.HexGridWidth - 1 - hexX, 81, 101, 640, 480),
            MapTileGridDecoder.GetSerializedIndex(shiftedX, shiftedY));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(200, 100)]
    [InlineData(100, -1)]
    [InlineData(100, 200)]
    public void TryGetSampledFloorCell_RejectsInvalidHexCoordinates(int hexX, int hexY)
    {
        Assert.False(MapObjectFloorSampleGeometry.TryGetSampledFloorCell(
            hexX,
            hexY,
            out _,
            out _));
    }

    [Fact]
    public void TryGetSampledFloorCell_RejectsUnplacedInvalidAndMismatchedRecords()
    {
        Assert.False(MapObjectFloorSampleGeometry.TryGetSampledFloorCell(
            CreatePlacement(recordIndex: 1, rawHexIndex: -1, hexX: null, hexY: null),
            out _,
            out _));
        Assert.False(MapObjectFloorSampleGeometry.TryGetSampledFloorCell(
            CreatePlacement(recordIndex: 2, rawHexIndex: 40_000, hexX: null, hexY: null),
            out _,
            out _));
        Assert.False(MapObjectFloorSampleGeometry.TryGetSampledFloorCell(
            CreatePlacement(recordIndex: 3, rawHexIndex: 20_099, hexX: 99, hexY: 100),
            out _,
            out _));
    }

    [Fact]
    public void GetSamples_FiltersElevationAndInvalidTilePositions()
    {
        var placements = new[]
        {
            CreatePlacement(recordIndex: 1, rawHexIndex: 20_099, hexX: 100, hexY: 100, elevation: 0),
            CreatePlacement(recordIndex: 2, rawHexIndex: 20_099, hexX: 100, hexY: 100, elevation: 1),
            CreatePlacement(recordIndex: 3, rawHexIndex: -1, hexX: null, hexY: null, elevation: 0)
        };

        var samples = MapObjectFloorSampleGeometry.GetSamples(placements, 0);

        Assert.Single(samples);
        Assert.Equal(1, samples[0].Placement.RecordIndex);
        Assert.Equal((50, 50), (samples[0].X, samples[0].Y));
        Assert.Empty(MapObjectFloorSampleGeometry.GetSamples(placements, null));
    }

    [Fact]
    public void TrySelectAtScreenPosition_CyclesCoLocatedObjectsAfterFloorTransform()
    {
        Assert.True(MapFloorTileDiagnosticGeometry.TryGetFitTransform(800, 600, out var fit));
        var zoomed = MapFloorTileDiagnosticGeometry.ZoomAt(fit, 300, 280, 3, 800, 600);
        var panned = MapFloorTileDiagnosticGeometry.Pan(zoomed, -35, 22, 800, 600);
        var placements = new[]
        {
            CreatePlacement(recordIndex: 2, rawHexIndex: 20_099, hexX: 100, hexY: 100),
            CreatePlacement(recordIndex: 1, rawHexIndex: 20_299, hexX: 100, hexY: 101)
        };
        var samples = MapObjectFloorSampleGeometry.GetSamples(placements, 0);
        var markerCellCenter = panned.ToScreen(new Point(50.5, 50.5));

        Assert.True(MapObjectFloorSampleGeometry.TrySelectAtScreenPosition(
            samples,
            null,
            markerCellCenter.X,
            markerCellCenter.Y,
            panned,
            800,
            600,
            out var firstSelection));
        Assert.Equal(1, firstSelection!.RecordIndex);

        Assert.True(MapObjectFloorSampleGeometry.TrySelectAtScreenPosition(
            samples,
            firstSelection.RecordIndex,
            markerCellCenter.X,
            markerCellCenter.Y,
            panned,
            800,
            600,
            out var secondSelection));
        Assert.Equal(2, secondSelection!.RecordIndex);
    }

    [Fact]
    public void TrySelectAtScreenPosition_RejectsClicksOutsideGridBounds()
    {
        Assert.True(MapFloorTileDiagnosticGeometry.TryGetFitTransform(800, 600, out var fit));
        var sample = MapObjectFloorSampleGeometry.GetSamples(
            [CreatePlacement(recordIndex: 1, rawHexIndex: 20_099, hexX: 100, hexY: 100)],
            0);

        Assert.False(MapObjectFloorSampleGeometry.TrySelectAtScreenPosition(
            sample,
            null,
            fit.ToScreen(new Point(100, 50)).X,
            fit.ToScreen(new Point(100, 50)).Y,
            fit,
            800,
            600,
            out _));
    }

    private static MapObjectPlacement CreatePlacement(
        int recordIndex,
        int rawHexIndex,
        int? hexX,
        int? hexY,
        int elevation = 0,
        int pidType = 1) =>
        new(
            recordIndex,
            0,
            elevation,
            0,
            rawHexIndex,
            hexX,
            hexY,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            elevation,
            0,
            pidType,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            [],
            null,
            null);

    private static int GetCeGravitySampleSquareIndex(
        int rawHexIndex,
        int centerHexX,
        int centerHexY,
        int viewportWidth,
        int viewportHeight)
    {
        var centerTile = centerHexY * MapObjectPlacementDecoder.HexGridWidth
            + MapObjectPlacementDecoder.HexGridWidth - 1 - centerHexX;
        var tileX = MapObjectPlacementDecoder.HexGridWidth - 1 - centerTile % MapObjectPlacementDecoder.HexGridWidth;
        var tileY = centerTile / MapObjectPlacementDecoder.HexGridWidth;
        var tileOffsetX = (viewportWidth - 32) / 2;
        var tileOffsetY = (viewportHeight - 16) / 2;
        if ((tileX & 1) != 0)
        {
            tileX--;
            tileOffsetX -= 32;
        }

        var squareX = tileX / 2;
        var squareY = tileY / 2;
        var squareOffsetX = tileOffsetX - 16;
        var squareOffsetY = tileOffsetY - 2;
        if ((tileY & 1) != 0)
        {
            squareOffsetY -= 12;
            squareOffsetX -= 16;
        }

        var hexX = MapObjectPlacementDecoder.HexGridWidth - 1
            - rawHexIndex % MapObjectPlacementDecoder.HexGridWidth;
        var hexY = rawHexIndex / MapObjectPlacementDecoder.HexGridWidth;
        var screenX = tileOffsetX + 48 * ((hexX - tileX) / 2);
        var screenY = tileOffsetY + 12 * ((hexX - tileX) / -2);
        if ((hexX & 1) != 0)
        {
            if (hexX <= tileX)
            {
                screenX -= 16;
                screenY += 12;
            }
            else
            {
                screenX += 32;
            }
        }

        screenX += 16 * (hexY - tileY) + 2;
        screenY += 12 * (hexY - tileY) + 8;
        var relativeX = screenX - squareOffsetX;
        var relativeY = screenY - squareOffsetY - 12;
        var coordX = FloorDivide(3 * relativeX - 4 * relativeY, 192) + squareX;
        var coordY = FloorDivide(4 * relativeY + relativeX, 128) + squareY;
        coordX = MapTileGridDecoder.GridWidth - 1 - coordX;

        return (uint)coordX < MapTileGridDecoder.GridWidth
            && (uint)coordY < MapTileGridDecoder.GridHeight
                ? coordX + MapTileGridDecoder.GridWidth * coordY
                : -1;
    }

    private static int FloorDivide(int value, int divisor) =>
        value >= 0 ? value / divisor : (value + 1) / divisor - 1;
}
