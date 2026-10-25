using FalloutAssetExplorer.Core;
using Avalonia;

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

        var sheetPoint = SpriteViewportTransform.ToSheetSpace(
            zoom,
            new Point(viewportX, viewportY));
        foreach (var placement in layout.Placements.Values)
        {
            var rectangle = placement.Rectangle;
            if (sheetPoint.X >= rectangle.X
                && sheetPoint.X < (long)rectangle.X + rectangle.Width
                && sheetPoint.Y >= rectangle.Y
                && sheetPoint.Y < (long)rectangle.Y + rectangle.Height)
            {
                return placement;
            }
        }

        return null;
    }
}
