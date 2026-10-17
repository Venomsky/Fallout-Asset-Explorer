using FalloutAssetExplorer.Core;

namespace FalloutAssetExplorer.App.ViewModels;

public static class SpriteSheetHitTester
{
    public static FrmSpritesheetPlacement? HitTest(
        FrmSpritesheetLayout layout,
        SpriteZoomModel zoom,
        double viewportX,
        double viewportY)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(zoom);
        if (!zoom.HasImage || !double.IsFinite(viewportX) || !double.IsFinite(viewportY))
        {
            return null;
        }

        var sheetX = (viewportX - zoom.OffsetX) / zoom.Zoom;
        var sheetY = (viewportY - zoom.OffsetY) / zoom.Zoom;
        foreach (var placement in layout.Placements.Values)
        {
            var rectangle = placement.Rectangle;
            if (sheetX >= rectangle.X
                && sheetX < (long)rectangle.X + rectangle.Width
                && sheetY >= rectangle.Y
                && sheetY < (long)rectangle.Y + rectangle.Height)
            {
                return placement;
            }
        }

        return null;
    }
}
