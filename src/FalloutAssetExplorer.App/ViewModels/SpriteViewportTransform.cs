using Avalonia;
using FalloutAssetExplorer.Core;

namespace FalloutAssetExplorer.App.ViewModels;

public static class SpriteViewportTransform
{
    public static Matrix CreateMatrix(SpriteZoomModel zoom)
    {
        ArgumentNullException.ThrowIfNull(zoom);
        return new Matrix(zoom.Zoom, 0, 0, zoom.Zoom, zoom.OffsetX, zoom.OffsetY);
    }

    public static Point ToSheetSpace(SpriteZoomModel zoom, Point viewportPoint)
    {
        ArgumentNullException.ThrowIfNull(zoom);
        return new Point(
            (viewportPoint.X - zoom.OffsetX) / zoom.Zoom,
            (viewportPoint.Y - zoom.OffsetY) / zoom.Zoom);
    }

    public static Rect ToViewportBounds(FrmAtlasRectangle sheetRectangle, SpriteZoomModel zoom)
    {
        var matrix = CreateMatrix(zoom);
        var topLeft = matrix.Transform(new Point(sheetRectangle.X, sheetRectangle.Y));
        var bottomRight = matrix.Transform(
            new Point(
                (long)sheetRectangle.X + sheetRectangle.Width,
                (long)sheetRectangle.Y + sheetRectangle.Height));
        return new Rect(topLeft, bottomRight);
    }
}
