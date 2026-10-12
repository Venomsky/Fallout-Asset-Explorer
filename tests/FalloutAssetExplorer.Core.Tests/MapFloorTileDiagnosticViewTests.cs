using Avalonia;
using FalloutAssetExplorer.App.Views;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class MapFloorTileDiagnosticViewTests
{
    [Theory]
    [InlineData(0, 99, 0)]
    [InlineData(99, 0, 0)]
    [InlineData(9999, 0, 99)]
    public void GetCellInfo_UsesSquareGridSerialization(int index, int expectedX, int expectedY)
    {
        var cell = MapFloorTileDiagnosticGeometry.GetCellInfo(index, 0x00000123);

        Assert.Equal(expectedX, cell.X);
        Assert.Equal(expectedY, cell.Y);
        Assert.Equal(index, cell.SerializedIndex);
        Assert.Equal(0x123u, cell.FloorArtId);
        Assert.False(cell.IsRenderSkipped);
    }

    [Fact]
    public void GetCellInfo_PreservesFloorIdZeroAndReportsRenderSkipSeparately()
    {
        var drawableZero = MapFloorTileDiagnosticGeometry.GetCellInfo(0, 0);
        var skippedZero = MapFloorTileDiagnosticGeometry.GetCellInfo(1, 0x00001000);

        Assert.Equal(0u, drawableZero.FloorArtId);
        Assert.False(drawableZero.IsRenderSkipped);
        Assert.Equal(0u, skippedZero.FloorArtId);
        Assert.True(skippedZero.IsRenderSkipped);
    }

    [Fact]
    public void GetDrawableFloorIds_DeduplicatesIdsAndExcludesRenderSkippedCells()
    {
        var ids = MapFloorTileDiagnosticGeometry.GetDrawableFloorIds(
            [0, 0x00001000, 1, 0x00001001, 0]);

        Assert.Equal([0], ids);
    }

    [Fact]
    public void GetCellBounds_UsesSquareCoordinatesAndContainedImageKeepsAspectRatio()
    {
        var cell = MapFloorTileDiagnosticGeometry.GetCellBounds(new Rect(10, 20, 100, 100), 1, 99, 0);
        var image = MapFloorTileDiagnosticGeometry.ContainImage(cell, 2, 1);

        Assert.Equal(new Rect(109, 20, 1, 1), cell);
        Assert.Equal(new Rect(109, 20.25, 1, 0.5), image);
    }

    [Fact]
    public void GetCellInfo_RejectsSerializedIndicesOutsideSquareGrid()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MapFloorTileDiagnosticGeometry.GetCellInfo(10000, 0));
    }

    [Fact]
    public void FitTransform_ContainsWholeGridAndCentersItInAvailableView()
    {
        Assert.True(MapFloorTileDiagnosticGeometry.TryGetFitTransform(800, 500, out var fit));

        Assert.True(fit.IsValid);
        Assert.Equal(4.76, fit.Scale, 6);
        Assert.Equal(12, fit.OffsetY, 6);
        Assert.Equal(162, fit.OffsetX, 6);
        Assert.Equal(new Point(162, 12), fit.ToScreen(new Point(0, 0)));
        Assert.Equal(new Point(638, 488), fit.ToScreen(new Point(100, 100)));
        Assert.False(MapFloorTileDiagnosticGeometry.TryGetFitTransform(24, 100, out _));
    }

    [Fact]
    public void ZoomAt_ClampsToMinimumAndMaximumZoom()
    {
        Assert.True(MapFloorTileDiagnosticGeometry.TryGetFitTransform(600, 600, out var fit));

        var zoomedOut = MapFloorTileDiagnosticGeometry.ZoomAt(fit, 300, 300, 1e-100, 600, 600);
        var zoomedIn = MapFloorTileDiagnosticGeometry.ZoomAt(fit, 300, 300, 1e100, 600, 600);

        Assert.Equal(fit.Scale * MapFloorTileDiagnosticGeometry.MinZoom, zoomedOut.Scale, 6);
        Assert.Equal(fit.Scale * MapFloorTileDiagnosticGeometry.MaxZoom, zoomedIn.Scale, 6);
    }

    [Fact]
    public void ZoomAt_KeepsGridPointUnderCursorStationary()
    {
        Assert.True(MapFloorTileDiagnosticGeometry.TryGetFitTransform(600, 600, out var fit));
        var cursor = new Point(247, 319);
        var gridPoint = fit.ToGrid(cursor);

        var zoomed = MapFloorTileDiagnosticGeometry.ZoomAt(fit, cursor.X, cursor.Y, 2.5, 600, 600);

        Assert.Equal(gridPoint.X, zoomed.ToGrid(cursor).X, 6);
        Assert.Equal(gridPoint.Y, zoomed.ToGrid(cursor).Y, 6);
    }

    [Fact]
    public void Pan_MovesGridAndClampsItSoTheMapCannotBeLost()
    {
        Assert.True(MapFloorTileDiagnosticGeometry.TryGetFitTransform(600, 600, out var fit));
        var panned = MapFloorTileDiagnosticGeometry.Pan(fit, 30, -20, 600, 600);

        Assert.Equal(fit.OffsetX + 30, panned.OffsetX, 6);
        Assert.Equal(fit.OffsetY - 20, panned.OffsetY, 6);

        var farPanned = MapFloorTileDiagnosticGeometry.Pan(panned, 1e8, -1e8, 600, 600);
        Assert.True(MapFloorTileDiagnosticGeometry.TryGetVisibleCells(farPanned, 600, 600, out var visible));
        Assert.InRange(visible.StartX, 0, 99);
        Assert.InRange(visible.EndX, visible.StartX, 99);
        Assert.InRange(visible.StartY, 0, 99);
        Assert.InRange(visible.EndY, visible.StartY, 99);
    }

    [Fact]
    public void ScreenToCell_Uses100By100GridAfterZoomAndPanAndRejectsOutsideEdges()
    {
        Assert.True(MapFloorTileDiagnosticGeometry.TryGetFitTransform(600, 600, out var fit));
        var zoomed = MapFloorTileDiagnosticGeometry.ZoomAt(fit, 250, 270, 3, 600, 600);
        var panned = MapFloorTileDiagnosticGeometry.Pan(zoomed, 20, -15, 600, 600);
        var cellPoint = panned.ToScreen(new Point(42.5, 47.5));

        Assert.True(MapFloorTileDiagnosticGeometry.TryGetSerializedIndex(
            cellPoint.X, cellPoint.Y, panned, 600, 600, out var index));
        Assert.Equal(MapTileGridDecoder.GetSerializedIndex(42, 47), index);
        Assert.False(MapFloorTileDiagnosticGeometry.TryGetSerializedIndex(
            fit.ToScreen(new Point(MapTileGridDecoder.GridWidth, 50)).X,
            fit.ToScreen(new Point(MapTileGridDecoder.GridWidth, 50)).Y,
            fit,
            600,
            600,
            out _));
        Assert.False(MapFloorTileDiagnosticGeometry.TryGetSerializedIndex(
            -1, 10, panned, 600, 600, out _));
    }

    [Fact]
    public void VisibleCellRange_OnlyIncludesGridCellsIntersectingViewport()
    {
        Assert.True(MapFloorTileDiagnosticGeometry.TryGetFitTransform(600, 600, out var fit));
        var zoomed = MapFloorTileDiagnosticGeometry.ZoomAt(fit, 300, 300, 8, 600, 600);

        Assert.True(MapFloorTileDiagnosticGeometry.TryGetVisibleCells(zoomed, 600, 600, out var visible));
        Assert.InRange(visible.StartX, 0, 99);
        Assert.InRange(visible.EndX, visible.StartX, 99);
        Assert.InRange(visible.StartY, 0, 99);
        Assert.InRange(visible.EndY, visible.StartY, 99);
        Assert.True(visible.EndX - visible.StartX + 1 < MapTileGridDecoder.GridWidth);
        Assert.True(visible.EndY - visible.StartY + 1 < MapTileGridDecoder.GridHeight);
    }

    [Fact]
    public void Resize_PreservesViewedGridCenterAndFitCanRestoreWholeGrid()
    {
        Assert.True(MapFloorTileDiagnosticGeometry.TryGetFitTransform(600, 600, out var fit));
        var zoomed = MapFloorTileDiagnosticGeometry.ZoomAt(fit, 300, 300, 2, 600, 600);

        Assert.True(MapFloorTileDiagnosticGeometry.TryGetResizedTransform(
            zoomed, 600, 600, 800, 400, out var resized));

        Assert.Equal(new Point(400, 200), resized.ToScreen(new Point(50, 50)));
        Assert.True(MapFloorTileDiagnosticGeometry.TryGetFitTransform(800, 400, out var refit));
        Assert.Equal(3.76, refit.Scale, 6);
    }
}
