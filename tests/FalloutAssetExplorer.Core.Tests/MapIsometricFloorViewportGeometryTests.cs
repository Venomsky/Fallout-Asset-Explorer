using Avalonia;
using FalloutAssetExplorer.App.Views;
using FalloutAssetExplorer.Core;
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
    public void IncludeBounds_WithoutValidSpriteBoundsPreservesFloorFit()
    {
        var withoutSprite = MapIsometricFloorViewportGeometry.IncludeBounds(ContentBounds, null);
        var withInvalidSprite = MapIsometricFloorViewportGeometry.IncludeBounds(ContentBounds, default(Rect));

        Assert.Equal(ContentBounds, withoutSprite);
        Assert.Equal(ContentBounds, withInvalidSprite);
        Assert.True(MapIsometricFloorViewportGeometry.TryGetFitTransform(
            withoutSprite!.Value,
            800,
            600,
            out var originalFit));
        Assert.True(MapIsometricFloorViewportGeometry.TryGetFitTransform(
            withInvalidSprite!.Value,
            800,
            600,
            out var unchangedFit));
        Assert.Equal(originalFit, unchangedFit);
    }

    [Fact]
    public void IncludeBounds_RejectsInvalidFloorBounds()
    {
        Assert.Null(MapIsometricFloorViewportGeometry.IncludeBounds(default, ContentBounds));
    }

    [Theory]
    [InlineData(double.NaN, 0, 1, 1)]
    [InlineData(0, double.NaN, 1, 1)]
    [InlineData(double.PositiveInfinity, 0, 1, 1)]
    [InlineData(double.NegativeInfinity, 0, 1, 1)]
    [InlineData(0, double.PositiveInfinity, 1, 1)]
    [InlineData(0, double.NegativeInfinity, 1, 1)]
    [InlineData(0, 0, double.NaN, 1)]
    [InlineData(0, 0, double.PositiveInfinity, 1)]
    [InlineData(0, 0, 1, double.NaN)]
    [InlineData(0, 0, 1, double.PositiveInfinity)]
    [InlineData(double.MaxValue * 0.75, 0, double.MaxValue * 0.5, 1)]
    [InlineData(0, double.MaxValue * 0.75, 1, double.MaxValue * 0.5)]
    public void IncludeBounds_RejectsNonFiniteBoundsAndPreservesFloorForInvalidAdditionalBounds(
        double x,
        double y,
        double width,
        double height)
    {
        var invalidBounds = new Rect(x, y, width, height);

        Assert.Null(MapIsometricFloorViewportGeometry.IncludeBounds(invalidBounds, ContentBounds));
        Assert.Equal(ContentBounds, MapIsometricFloorViewportGeometry.IncludeBounds(ContentBounds, invalidBounds));
    }

    [Fact]
    public void IncludeBounds_PreservesFloorWhenAdditionalRightOrBottomOverflows()
    {
        var rightOverflow = new Rect(
            double.MaxValue * 0.75,
            0,
            double.MaxValue * 0.5,
            1);
        var bottomOverflow = new Rect(
            0,
            double.MaxValue * 0.75,
            1,
            double.MaxValue * 0.5);

        Assert.Equal(ContentBounds, MapIsometricFloorViewportGeometry.IncludeBounds(ContentBounds, rightOverflow));
        Assert.Equal(ContentBounds, MapIsometricFloorViewportGeometry.IncludeBounds(ContentBounds, bottomOverflow));
    }

    [Fact]
    public void IncludeBounds_PreservesFloorWhenUnionDimensionsOverflow()
    {
        var floorBounds = new Rect(-double.MaxValue * 0.5, 0, double.MaxValue * 0.25, 1);
        var spriteBounds = new Rect(double.MaxValue * 0.5, 0, double.MaxValue * 0.25, 1);

        Assert.True(double.IsFinite(floorBounds.Right));
        Assert.True(double.IsFinite(spriteBounds.Right));
        Assert.Equal(floorBounds, MapIsometricFloorViewportGeometry.IncludeBounds(floorBounds, spriteBounds));
    }

    [Fact]
    public void Fit_IncludesLargeShiftedSpriteAndKeepsBothBoundsInsideViewportMargins()
    {
        var identityCamera = new MapIsometricFloorCameraTransform(1, 0, 0);
        Assert.True(MapIsometricObjectSpriteGeometry.TryGetImageBounds(
            new Point(-260, -190),
            identityCamera,
            240,
            180,
            25,
            -30,
            60,
            -50,
            out var spriteBounds));
        var contentBounds = MapIsometricFloorViewportGeometry.IncludeBounds(
            ContentBounds,
            spriteBounds);

        Assert.True(contentBounds.HasValue);
        var includedBounds = contentBounds.Value;
        Assert.NotEqual(ContentBounds, includedBounds);
        Assert.Equal(Math.Min(ContentBounds.Left, spriteBounds.Left), includedBounds.Left);
        Assert.Equal(Math.Min(ContentBounds.Top, spriteBounds.Top), includedBounds.Top);
        Assert.Equal(Math.Max(ContentBounds.Right, spriteBounds.Right), includedBounds.Right);
        Assert.Equal(Math.Max(ContentBounds.Bottom, spriteBounds.Bottom), includedBounds.Bottom);
        Assert.True(MapIsometricFloorViewportGeometry.TryGetFitTransform(
            includedBounds,
            800,
            600,
            out var fit));

        var margin = 12;
        foreach (var point in new[]
                 {
                     new Point(includedBounds.Left, includedBounds.Top),
                     new Point(includedBounds.Right, includedBounds.Top),
                     new Point(includedBounds.Left, includedBounds.Bottom),
                     new Point(includedBounds.Right, includedBounds.Bottom)
                 })
        {
            var screenPoint = fit.ToScreen(point);
            Assert.InRange(screenPoint.X, margin, 800 - margin);
            Assert.InRange(screenPoint.Y, margin, 600 - margin);
        }
    }

    [Fact]
    public void Fit_IncludesBoundsFromMultipleSpriteMarkers()
    {
        var markers = new[]
        {
            CreateMarker(10, worldPosition: new Point(-520, -240)),
            CreateMarker(11, worldPosition: new Point(420, 260))
        };
        Assert.True(MapIsometricObjectSpriteGeometry.TryGetSpriteWorldBounds(
            10,
            markers,
            180,
            120,
            8,
            -5,
            out var firstSpriteBounds));
        Assert.True(MapIsometricObjectSpriteGeometry.TryGetSpriteWorldBounds(
            11,
            markers,
            140,
            200,
            -11,
            12,
            out var secondSpriteBounds));

        var combinedBounds = MapIsometricFloorViewportGeometry.IncludeBounds(
            ContentBounds,
            firstSpriteBounds);
        Assert.True(combinedBounds.HasValue);
        combinedBounds = MapIsometricFloorViewportGeometry.IncludeBounds(
            combinedBounds.Value,
            secondSpriteBounds);

        Assert.True(combinedBounds.HasValue);
        var bounds = combinedBounds.Value;
        Assert.Equal(Math.Min(ContentBounds.Left, Math.Min(firstSpriteBounds.Left, secondSpriteBounds.Left)), bounds.Left);
        Assert.Equal(Math.Min(ContentBounds.Top, Math.Min(firstSpriteBounds.Top, secondSpriteBounds.Top)), bounds.Top);
        Assert.Equal(Math.Max(ContentBounds.Right, Math.Max(firstSpriteBounds.Right, secondSpriteBounds.Right)), bounds.Right);
        Assert.Equal(Math.Max(ContentBounds.Bottom, Math.Max(firstSpriteBounds.Bottom, secondSpriteBounds.Bottom)), bounds.Bottom);
        Assert.True(MapIsometricFloorViewportGeometry.TryGetFitTransform(
            bounds,
            800,
            600,
            out var fit));

        foreach (var rect in new[] { ContentBounds, firstSpriteBounds, secondSpriteBounds })
        {
            foreach (var point in new[]
                     {
                         new Point(rect.Left, rect.Top),
                         new Point(rect.Right, rect.Top),
                         new Point(rect.Left, rect.Bottom),
                         new Point(rect.Right, rect.Bottom)
                     })
            {
                var screenPoint = fit.ToScreen(point);
                Assert.InRange(screenPoint.X, 12, 788);
                Assert.InRange(screenPoint.Y, 12, 588);
            }
        }
    }

    [Fact]
    public void SelectedSpriteWorldBounds_RequiresMatchingSelectionAndMarker()
    {
        var marker = CreateMarker(recordIndex: 7, rawOffsetX: 13, rawOffsetY: -9);
        var markers = new[] { marker };

        Assert.True(MapIsometricObjectSpriteGeometry.TryGetSelectedSpriteWorldBounds(
            7,
            7,
            showObjectMarkers: true,
            markers,
            pixelWidth: 20,
            pixelHeight: 30,
            artShiftX: 4,
            artShiftY: -3,
            out var imageBounds));
        Assert.True(MapIsometricObjectSpriteGeometry.TryGetImageBounds(
            marker.WorldPosition,
            new MapIsometricFloorCameraTransform(1, 0, 0),
            20,
            30,
            4,
            -3,
            13,
            -9,
            out var expectedBounds));
        Assert.Equal(expectedBounds, imageBounds);

        Assert.False(MapIsometricObjectSpriteGeometry.TryGetSelectedSpriteWorldBounds(
            7,
            8,
            showObjectMarkers: true,
            markers,
            20,
            30,
            4,
            -3,
            out _));
        Assert.False(MapIsometricObjectSpriteGeometry.TryGetSelectedSpriteWorldBounds(
            7,
            7,
            showObjectMarkers: true,
            [],
            20,
            30,
            4,
            -3,
            out _));
        Assert.False(MapIsometricObjectSpriteGeometry.TryGetSelectedSpriteWorldBounds(
            null,
            7,
            showObjectMarkers: true,
            markers,
            20,
            30,
            4,
            -3,
            out _));
        Assert.False(MapIsometricObjectSpriteGeometry.TryGetSelectedSpriteWorldBounds(
            7,
            7,
            showObjectMarkers: false,
            markers,
            20,
            30,
            4,
            -3,
            out _));
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

    private static MapIsometricObjectMarker CreateMarker(
        int recordIndex,
        int rawOffsetX = 0,
        int rawOffsetY = 0,
        Point? worldPosition = null)
    {
        var placement = new MapObjectPlacement(
            RecordIndex: recordIndex,
            RecordOffset: 0,
            ElevationGroup: 0,
            RawObjectId: 0,
            RawHexIndex: 20_099,
            HexX: 100,
            HexY: 100,
            RawOffsetX: rawOffsetX,
            RawOffsetY: rawOffsetY,
            RawScreenX: 0,
            RawScreenY: 0,
            RawFrame: 0,
            RawRotation: 0,
            RawFid: 0x02000001,
            RawObjectFlags: 0,
            StoredElevation: 0,
            RawPid: 0x02000001,
            PidTypeIdentifier: 2,
            RawCid: 0,
            RawLightDistance: 0,
            RawLightIntensity: 0,
            RawField74: 0,
            RawScriptId: 0,
            RawScriptField: 0,
            RawInventoryLength: 0,
            RawInventoryCapacity: 0,
            RawInventoryPointer: 0,
            RawPayloadWords: [],
            Warning: null,
            UnsupportedReason: null);
        return new MapIsometricObjectMarker(placement, worldPosition ?? new Point(50, 80));
    }
}
