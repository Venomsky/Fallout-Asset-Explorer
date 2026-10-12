using Avalonia;
using FalloutAssetExplorer.App.Views;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class MapIsometricFloorViewportGeometryTests
{
    private static readonly Rect ContentBounds = new(-100, -50, 200, 100);

    [Fact]
    public void Fit_ContainsAndCentersWholeContentBounds()
    {
        Assert.True(MapIsometricFloorViewportGeometry.TryGetFitTransform(
            ContentBounds,
            800,
            600,
            out var fit));

        Assert.Equal(3.88, fit.Scale, 6);
        Assert.Equal(new Point(400, 300), fit.ToScreen(new Point(0, 0)));
        Assert.Equal(new Point(12, 106), fit.ToScreen(new Point(-100, -50)));
        Assert.Equal(new Point(788, 494), fit.ToScreen(new Point(100, 50)));
    }

    [Fact]
    public void ZoomAt_KeepsWorldPointUnderCursorStationaryAndClampsZoom()
    {
        Assert.True(MapIsometricFloorViewportGeometry.TryGetFitTransform(
            ContentBounds,
            800,
            600,
            out var fit));
        var cursor = new Point(372, 268);
        var worldAnchor = fit.ToWorld(cursor);

        var zoomed = MapIsometricFloorViewportGeometry.ZoomAt(
            fit,
            cursor.X,
            cursor.Y,
            2,
            ContentBounds,
            800,
            600);
        Assert.Equal(cursor.X, zoomed.ToScreen(worldAnchor).X, 6);
        Assert.Equal(cursor.Y, zoomed.ToScreen(worldAnchor).Y, 6);

        var zoomedOut = MapIsometricFloorViewportGeometry.ZoomAt(
            fit,
            cursor.X,
            cursor.Y,
            1e-100,
            ContentBounds,
            800,
            600);
        var zoomedIn = MapIsometricFloorViewportGeometry.ZoomAt(
            fit,
            cursor.X,
            cursor.Y,
            1e100,
            ContentBounds,
            800,
            600);
        Assert.Equal(fit.Scale * MapIsometricFloorViewportGeometry.MinZoom, zoomedOut.Scale, 6);
        Assert.Equal(fit.Scale * MapIsometricFloorViewportGeometry.MaxZoom, zoomedIn.Scale, 6);
    }

    [Fact]
    public void Pan_ClampsViewWithoutLosingTheContent()
    {
        Assert.True(MapIsometricFloorViewportGeometry.TryGetFitTransform(
            ContentBounds,
            800,
            600,
            out var fit));

        var panned = MapIsometricFloorViewportGeometry.Pan(
            fit,
            100_000,
            -100_000,
            ContentBounds,
            800,
            600);

        var screenBounds = MapIsometricFloorViewportGeometry.GetImageBounds(
            panned,
            new Point(ContentBounds.X, ContentBounds.Y),
            (int)ContentBounds.Width,
            (int)ContentBounds.Height);
        Assert.True(MapIsometricFloorViewportGeometry.IntersectsViewport(screenBounds, 800, 600));
        Assert.True(screenBounds.Right >= 12);
        Assert.True(screenBounds.Left <= 788);
    }

    [Fact]
    public void ImageBounds_KeepNaturalDimensionsAndCullOnlyFullyOffscreenImages()
    {
        var camera = new MapIsometricFloorCameraTransform(1, 0, 0);
        var overhangingImage = MapIsometricFloorViewportGeometry.GetImageBounds(
            camera,
            new Point(-20, 25),
            48,
            32);
        var offscreenImage = MapIsometricFloorViewportGeometry.GetImageBounds(
            camera,
            new Point(-60, 25),
            48,
            32);

        Assert.Equal(new Rect(-20, 25, 48, 32), overhangingImage);
        Assert.True(MapIsometricFloorViewportGeometry.IntersectsViewport(overhangingImage, 100, 100));
        Assert.False(MapIsometricFloorViewportGeometry.IntersectsViewport(offscreenImage, 100, 100));
    }

    [Fact]
    public void Resize_PreservesViewedWorldCenterAndCanRefit()
    {
        Assert.True(MapIsometricFloorViewportGeometry.TryGetFitTransform(
            ContentBounds,
            800,
            600,
            out var fit));
        var zoomed = MapIsometricFloorViewportGeometry.ZoomAt(
            fit,
            400,
            300,
            2,
            ContentBounds,
            800,
            600);
        var oldWorldCenter = zoomed.ToWorld(new Point(400, 300));

        Assert.True(MapIsometricFloorViewportGeometry.TryGetResizedTransform(
            zoomed,
            800,
            600,
            500,
            400,
            ContentBounds,
            out var resized));
        var newWorldCenter = resized.ToWorld(new Point(250, 200));
        Assert.Equal(oldWorldCenter.X, newWorldCenter.X, 6);
        Assert.Equal(oldWorldCenter.Y, newWorldCenter.Y, 6);

        Assert.True(MapIsometricFloorViewportGeometry.TryGetFitTransform(
            ContentBounds,
            500,
            400,
            out var refit));
        Assert.True(refit.Scale < resized.Scale);
    }
}
