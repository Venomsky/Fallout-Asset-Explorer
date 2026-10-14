using Avalonia;

namespace FalloutAssetExplorer.App.Views;

public static class MapIsometricObjectSpriteGeometry
{
    public static bool TryGetSelectedSpriteWorldBounds(
        int? spriteRecordIndex,
        int? selectedRecordIndex,
        bool showObjectMarkers,
        IReadOnlyList<MapIsometricObjectMarker>? markers,
        int pixelWidth,
        int pixelHeight,
        short artShiftX,
        short artShiftY,
        out Rect imageBounds)
    {
        imageBounds = default;
        if (!showObjectMarkers
            || spriteRecordIndex is not int recordIndex
            || recordIndex != selectedRecordIndex
            || markers is null
            || markers.FirstOrDefault(marker => marker.Placement.RecordIndex == recordIndex)
                is not { Placement: not null } spriteMarker)
        {
            return false;
        }

        return TryGetImageBounds(
            spriteMarker.WorldPosition,
            new MapIsometricFloorCameraTransform(1, 0, 0),
            pixelWidth,
            pixelHeight,
            artShiftX,
            artShiftY,
            spriteMarker.Placement.RawOffsetX,
            spriteMarker.Placement.RawOffsetY,
            out imageBounds);
    }

    /// <summary>
    /// Places an FRM frame using the tile origin, direction art shift, and MAP offsets used by CE.
    /// </summary>
    /// <remarks>
    /// The existing FAE marker is relative to the CE square origin, which is 16 pixels left and
    /// 2 pixels above the CE tile origin. CE then adds (16, 8), so the object anchor is marker
    /// plus (32, 10), the direction's FRM shift, and the MAP x/y offsets. The frame rectangle uses
    /// CE's integer width/2 and height-1 alignment.
    /// </remarks>
    public static bool TryGetImageBounds(
        Point projectedHexMarker,
        MapIsometricFloorCameraTransform camera,
        int pixelWidth,
        int pixelHeight,
        short artShiftX,
        short artShiftY,
        int mapOffsetX,
        int mapOffsetY,
        out Rect imageBounds)
    {
        imageBounds = default;
        if (!camera.IsValid
            || !double.IsFinite(projectedHexMarker.X)
            || !double.IsFinite(projectedHexMarker.Y)
            || pixelWidth <= 0
            || pixelHeight <= 0)
        {
            return false;
        }

        // CE tileSetCenter defines square origin as tile origin - (16, 2);
        // _obj_render_object adds (16, 8), then art shifts and MAP offsets.
        var objectAnchor = new Point(
            projectedHexMarker.X + 32 + artShiftX + mapOffsetX,
            projectedHexMarker.Y + 10 + artShiftY + mapOffsetY);
        var screenAnchor = camera.ToScreen(objectAnchor);
        var width = pixelWidth * camera.Scale;
        var height = pixelHeight * camera.Scale;
        if (!double.IsFinite(screenAnchor.X)
            || !double.IsFinite(screenAnchor.Y)
            || !double.IsFinite(width)
            || !double.IsFinite(height))
        {
            return false;
        }

        imageBounds = new Rect(
            screenAnchor.X - (pixelWidth / 2) * camera.Scale,
            screenAnchor.Y - (pixelHeight - 1) * camera.Scale,
            width,
            height);
        return true;
    }
}
