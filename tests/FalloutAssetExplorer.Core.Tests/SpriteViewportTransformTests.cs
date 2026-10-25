using Avalonia;
using FalloutAssetExplorer.App.ViewModels;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class SpriteViewportTransformTests
{
    [Fact]
    public void At100Percent_FrameBoundsMatchTheSheetRectanglesUsedByTheOverlay()
    {
        var layout = CreateLayout();
        var zoom = new SpriteZoomModel();
        zoom.SetViewportSize(layout.Width, layout.Height);
        zoom.SetImageSize(layout.Width, layout.Height);
        zoom.Set100Percent();

        foreach (var placement in layout.Placements.Values)
        {
            var rectangle = placement.Rectangle;
            var expected = new Rect(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);

            Assert.Equal(expected, SpriteViewportTransform.ToViewportBounds(rectangle, zoom));
            Assert.Equal(
                placement.Frame.Key,
                SpriteSheetHitTester.HitTest(
                    layout,
                    zoom,
                    rectangle.X + rectangle.Width / 2d,
                    rectangle.Y + rectangle.Height / 2d)?.Frame.Key);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void FrameBoundsAndHitTestingStayAlignedAtIntegerAndFractionalZoom(double zoomFactor)
    {
        var layout = CreateLayout();
        var zoom = CreateZoom(layout, new Size(120, 90));
        if (zoomFactor != 1)
        {
            zoom.ZoomBy(zoomFactor, 37.5, 22.25);
        }

        foreach (var placement in layout.Placements.Values)
        {
            var rectangle = placement.Rectangle;
            var screenBounds = SpriteViewportTransform.ToViewportBounds(rectangle, zoom);
            var expected = new Rect(
                rectangle.X * zoom.Zoom + zoom.OffsetX,
                rectangle.Y * zoom.Zoom + zoom.OffsetY,
                rectangle.Width * zoom.Zoom,
                rectangle.Height * zoom.Zoom);
            Assert.Equal(expected, screenBounds);

            var point = new Point(
                screenBounds.X + screenBounds.Width / 2,
                screenBounds.Y + screenBounds.Height / 2);
            Assert.Equal(
                placement.Frame.Key,
                SpriteSheetHitTester.HitTest(layout, zoom, point.X, point.Y)?.Frame.Key);
        }
    }

    [Fact]
    public void FrameBoundsAndHitTestingRemainAlignedAfterPanAndViewportResize()
    {
        var layout = CreateLayout();
        var zoom = CreateZoom(layout, new Size(20, 18));
        zoom.ZoomBy(4, 10, 9);
        zoom.PanBy(0, 4);
        zoom.SetViewportSize(18, 16);

        var target = layout.Placements[new FrmSpritesheetFrameKey(0, 0)];
        var screenBounds = SpriteViewportTransform.ToViewportBounds(target.Rectangle, zoom);
        var visiblePoint = new Point(
            (Math.Max(0, screenBounds.X) + Math.Min(18, screenBounds.Right)) / 2,
            (Math.Max(0, screenBounds.Y) + Math.Min(16, screenBounds.Bottom)) / 2);

        Assert.Equal(4, zoom.Zoom);
        Assert.Equal(new Rect(-12, 0, 16, 12), screenBounds);
        Assert.Equal(
            target.Frame.Key,
            SpriteSheetHitTester.HitTest(layout, zoom, visiblePoint.X, visiblePoint.Y)?.Frame.Key);
    }

    private static FrmSpritesheetLayout CreateLayout() =>
        FrmSpritesheetPacker.Pack(
        [
            new FrmSpritesheetFrame(new FrmSpritesheetFrameKey(0, 0), 4, 3, 0, 0, 0, 0),
            new FrmSpritesheetFrame(new FrmSpritesheetFrameKey(0, 1), 3, 2, 0, 0, 0, 0),
            new FrmSpritesheetFrame(new FrmSpritesheetFrameKey(0, 2), 2, 5, 0, 0, 0, 0)
        ],
        spacing: 1,
        new FrmSpritesheetPackingLimits(32, 32, 1024));

    private static SpriteZoomModel CreateZoom(FrmSpritesheetLayout layout, Size viewportSize)
    {
        var zoom = new SpriteZoomModel();
        zoom.SetViewportSize(viewportSize.Width, viewportSize.Height);
        zoom.SetImageSize(layout.Width, layout.Height);
        zoom.Set100Percent();
        return zoom;
    }
}
