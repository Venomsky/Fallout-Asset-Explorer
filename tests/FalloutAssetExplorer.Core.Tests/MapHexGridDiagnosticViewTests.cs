using Avalonia;
using FalloutAssetExplorer.App.Views;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class MapHexGridDiagnosticViewTests
{
    [Theory]
    [InlineData(100, 100, 0, 0)]
    [InlineData(101, 100, 32, 0)]
    [InlineData(99, 100, -16, 12)]
    [InlineData(102, 100, 48, -12)]
    [InlineData(100, 101, 16, 12)]
    public void GetRelativeScreenPosition_UsesSourceColumnParity(
        int hexX,
        int hexY,
        double expectedX,
        double expectedY)
    {
        Assert.Equal(
            new Point(expectedX, expectedY),
            MapHexGridDiagnosticGeometry.GetRelativeScreenPosition(hexX, hexY));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(199, 199)]
    public void TryGetScreenPosition_AcceptsGridBoundaries(int hexX, int hexY)
    {
        Assert.True(MapHexGridDiagnosticGeometry.TryGetScreenPosition(
            hexX,
            hexY,
            800,
            500,
            out var position));
        Assert.InRange(position.X, 0, 800);
        Assert.InRange(position.Y, 0, 500);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(200, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 200)]
    public void TryGetScreenPosition_RejectsCoordinatesOutsideGrid(int hexX, int hexY)
    {
        Assert.False(MapHexGridDiagnosticGeometry.TryGetScreenPosition(
            hexX,
            hexY,
            800,
            500,
            out _));
    }

    [Fact]
    public void TryGetScreenPosition_ScalesToDifferentViewSizes()
    {
        Assert.True(MapHexGridDiagnosticGeometry.TryGetScreenPosition(
            101,
            100,
            800,
            500,
            out var small));
        Assert.True(MapHexGridDiagnosticGeometry.TryGetScreenPosition(
            101,
            100,
            1600,
            1000,
            out var large));

        Assert.True(MapHexGridDiagnosticGeometry.GetScale(1600, 1000)
            > MapHexGridDiagnosticGeometry.GetScale(800, 500));
        Assert.True(large.X > small.X);
        Assert.True(large.Y > small.Y);
        Assert.InRange(large.X, 0, 1600);
        Assert.InRange(large.Y, 0, 1000);
    }

    [Fact]
    public void MarkerStyle_ShrinksAndLowersWallAndMiscMarkers()
    {
        const double scale = 10;
        var normal = MapHexGridDiagnosticGeometry.GetMarkerRadius(2, scale);

        Assert.Equal(4, normal, 6);
        Assert.True(MapHexGridDiagnosticGeometry.GetMarkerRadius(3, scale) < normal);
        Assert.True(MapHexGridDiagnosticGeometry.GetMarkerRadius(5, scale) < normal);
        Assert.True(MapHexGridDiagnosticGeometry.GetMarkerDrawOrder(3)
            < MapHexGridDiagnosticGeometry.GetMarkerDrawOrder(1));
        Assert.True(MapHexGridDiagnosticGeometry.GetMarkerDrawOrder(5)
            < MapHexGridDiagnosticGeometry.GetMarkerDrawOrder(0));
    }

    [Fact]
    public void GetPlacedPlacements_FiltersElevationAndUnplacedObjects()
    {
        var placements = new[]
        {
            CreatePlacement(recordIndex: 0, elevation: 0, hexX: 10, hexY: 20),
            CreatePlacement(recordIndex: 1, elevation: 1, hexX: 10, hexY: 20),
            CreatePlacement(recordIndex: 2, elevation: 0, hexX: null, hexY: null)
        };

        var visible = MapHexGridDiagnosticGeometry.GetPlacedPlacements(placements, 0);

        Assert.Equal(new[] { 0 }, visible.Select(placement => placement.RecordIndex));
        Assert.Empty(MapHexGridDiagnosticGeometry.GetPlacedPlacements(placements, null));
    }

    [Fact]
    public void TrySelectPlacementAtPosition_CyclesRecordsAtSameHex()
    {
        var placements = new[]
        {
            CreatePlacement(recordIndex: 3, elevation: 0, hexX: 50, hexY: 75),
            CreatePlacement(recordIndex: 7, elevation: 0, hexX: 50, hexY: 75),
            CreatePlacement(recordIndex: 8, elevation: 1, hexX: 50, hexY: 75)
        };
        Assert.True(MapHexGridDiagnosticGeometry.TryGetScreenPosition(
            50,
            75,
            800,
            500,
            out var point));

        Assert.True(MapHexGridDiagnosticGeometry.TrySelectPlacementAtPosition(
            placements,
            0,
            null,
            point.X,
            point.Y,
            800,
            500,
            out var first));
        Assert.True(MapHexGridDiagnosticGeometry.TrySelectPlacementAtPosition(
            placements,
            0,
            first!.RecordIndex,
            point.X,
            point.Y,
            800,
            500,
            out var second));
        Assert.True(MapHexGridDiagnosticGeometry.TrySelectPlacementAtPosition(
            placements,
            0,
            second!.RecordIndex,
            point.X,
            point.Y,
            800,
            500,
            out var third));

        Assert.Equal(new[] { 3, 7, 3 }, new[]
        {
            first.RecordIndex,
            second.RecordIndex,
            third!.RecordIndex
        });
    }

    [Fact]
    public void ViewTransform_InverseRoundTripsAndBaseMatchesLegacyPosition()
    {
        Assert.True(MapHexGridDiagnosticGeometry.TryGetBaseTransform(800, 500, out var baseTransform));
        Assert.True(MapHexGridDiagnosticGeometry.TryGetScreenPosition(120, 60, 800, 500, out var legacy));
        Assert.True(MapHexGridDiagnosticGeometry.TryGetScreenPosition(120, 60, baseTransform, out var viaTransform));
        Assert.Equal(legacy.X, viaTransform.X, 9);
        Assert.Equal(legacy.Y, viaTransform.Y, 9);

        var transform = new MapHexViewTransform(3.5, -120, 45);
        var projected = MapHexGridDiagnosticGeometry.GetRelativeScreenPosition(33, 77);
        var back = transform.ToProjected(transform.ToScreen(projected));
        Assert.Equal(projected.X, back.X, 9);
        Assert.Equal(projected.Y, back.Y, 9);
    }

    [Fact]
    public void ZoomAt_KeepsPointUnderCursorAndClampsScale()
    {
        Assert.True(MapHexGridDiagnosticGeometry.TryGetBaseTransform(800, 500, out var start));
        var cursor = new Point(300, 220);
        var anchor = start.ToProjected(cursor);

        var zoomed = MapHexGridDiagnosticGeometry.ZoomAt(start, cursor.X, cursor.Y, 2, 800, 500);

        Assert.Equal(start.Scale * 2, zoomed.Scale, 9);
        var after = zoomed.ToScreen(anchor);
        Assert.Equal(cursor.X, after.X, 6);
        Assert.Equal(cursor.Y, after.Y, 6);

        var max = MapHexGridDiagnosticGeometry.ZoomAt(start, cursor.X, cursor.Y, 1e9, 800, 500);
        var min = MapHexGridDiagnosticGeometry.ZoomAt(start, cursor.X, cursor.Y, 1e-9, 800, 500);
        Assert.Equal(start.Scale * MapHexGridDiagnosticGeometry.MaxZoom, max.Scale, 9);
        Assert.Equal(start.Scale * MapHexGridDiagnosticGeometry.MinZoom, min.Scale, 9);
    }

    [Fact]
    public void Pan_CannotMoveGridCompletelyOutOfView()
    {
        Assert.True(MapHexGridDiagnosticGeometry.TryGetBaseTransform(800, 500, out var start));

        var panned = MapHexGridDiagnosticGeometry.Pan(start, 1e7, -1e7, 800, 500);

        Assert.True(MapHexGridDiagnosticGeometry.TryGetScreenPosition(0, 0, panned, out var topLeft));
        Assert.True(MapHexGridDiagnosticGeometry.TryGetScreenPosition(199, 199, panned, out var bottomRight));
        Assert.True(topLeft.X <= 800 && topLeft.Y >= -500);
        Assert.True(bottomRight.X >= 0 || topLeft.X >= 0);
    }

    [Fact]
    public void Fit_ContainsAllPlacedObjectsOfElevationWithMargin()
    {
        var placements = new[]
        {
            CreatePlacement(0, 0, 90, 90),
            CreatePlacement(1, 0, 110, 120),
            CreatePlacement(2, 0, 100, 100),
            CreatePlacement(3, 1, 0, 0),
            CreatePlacement(4, 0, null, null)
        };

        Assert.True(MapHexGridDiagnosticGeometry.TryGetFitTransform(placements, 0, 800, 500, out var fit));

        foreach (var placement in placements.Where(item => item.ElevationGroup == 0 && item.HexX is not null))
        {
            Assert.True(MapHexGridDiagnosticGeometry.TryGetScreenPosition(
                placement.HexX!.Value,
                placement.HexY!.Value,
                fit,
                out var point));
            Assert.InRange(point.X, 16 - 1e-6, 800 - 16 + 1e-6);
            Assert.InRange(point.Y, 16 - 1e-6, 500 - 16 + 1e-6);
        }
    }

    [Fact]
    public void Fit_EmptyElevationRestoresWholeGridAndSinglePointIsFinite()
    {
        Assert.True(MapHexGridDiagnosticGeometry.TryGetBaseTransform(800, 500, out var whole));
        var placements = new[] { CreatePlacement(0, 0, 50, 50) };

        Assert.True(MapHexGridDiagnosticGeometry.TryGetFitTransform(placements, 2, 800, 500, out var empty));
        Assert.Equal(whole, empty);

        Assert.True(MapHexGridDiagnosticGeometry.TryGetFitTransform(placements, 0, 800, 500, out var single));
        Assert.True(single.IsValid);
        Assert.True(MapHexGridDiagnosticGeometry.TryGetScreenPosition(50, 50, single, out var point));
        Assert.Equal(400, point.X, 6);
        Assert.Equal(250, point.Y, 6);
    }

    [Fact]
    public void CenteredTransform_PutsHexAtViewCenterKeepingScale()
    {
        var current = new MapHexViewTransform(2.5, 10, 20);

        Assert.True(MapHexGridDiagnosticGeometry.TryGetCenteredTransform(current, 140, 30, 800, 500, out var centered));

        Assert.Equal(current.Scale, centered.Scale);
        Assert.True(MapHexGridDiagnosticGeometry.TryGetScreenPosition(140, 30, centered, out var point));
        Assert.Equal(400, point.X, 6);
        Assert.Equal(250, point.Y, 6);
        Assert.False(MapHexGridDiagnosticGeometry.TryGetCenteredTransform(current, 200, 0, 800, 500, out _));
    }

    [Fact]
    public void HitTest_AfterZoomAndPanUsesTransformAndStillCyclesSameHex()
    {
        var placements = new[]
        {
            CreatePlacement(3, 0, 50, 75),
            CreatePlacement(7, 0, 50, 75),
            CreatePlacement(9, 0, 52, 75)
        };
        Assert.True(MapHexGridDiagnosticGeometry.TryGetBaseTransform(800, 500, out var start));
        var transform = MapHexGridDiagnosticGeometry.Pan(
            MapHexGridDiagnosticGeometry.ZoomAt(start, 200, 200, 6, 800, 500),
            -30,
            25,
            800,
            500);
        Assert.True(MapHexGridDiagnosticGeometry.TryGetScreenPosition(50, 75, transform, out var point));

        Assert.True(MapHexGridDiagnosticGeometry.TrySelectPlacementAtPosition(
            placements, 0, null, point.X, point.Y, transform, out var first));
        Assert.True(MapHexGridDiagnosticGeometry.TrySelectPlacementAtPosition(
            placements, 0, first!.RecordIndex, point.X, point.Y, transform, out var second));
        Assert.Equal(3, first.RecordIndex);
        Assert.Equal(7, second!.RecordIndex);

        Assert.True(MapHexGridDiagnosticGeometry.TryGetScreenPosition(52, 75, transform, out var other));
        Assert.True(MapHexGridDiagnosticGeometry.TrySelectPlacementAtPosition(
            placements, 0, null, other.X, other.Y, transform, out var neighbour));
        Assert.Equal(9, neighbour!.RecordIndex);

        // The old untransformed position of the hex no longer hits anything.
        Assert.True(MapHexGridDiagnosticGeometry.TryGetScreenPosition(50, 75, start, out var oldPoint));
        Assert.False(MapHexGridDiagnosticGeometry.TrySelectPlacementAtPosition(
            placements, 0, null, oldPoint.X, oldPoint.Y, transform, out _));
    }

    private static MapObjectPlacement CreatePlacement(
        int recordIndex,
        int elevation,
        int? hexX,
        int? hexY) =>
        new(
            recordIndex,
            recordIndex * 4,
            elevation,
            0,
            0,
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
            0x01000001,
            1,
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
}
