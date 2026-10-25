using Avalonia;

namespace FalloutAssetExplorer.App.Views;

public static class MapIsometricObjectSpriteGeometry
{
    public static bool TryGetSelectedSpriteWorldBounds(
        int? spriteRecordIndex,
        int? selectedRecordIndex,
        bool showObjectSprites,
        IReadOnlyList<MapIsometricObjectMarker>? markers,
        int pixelWidth,
        int pixelHeight,
        short artShiftX,
        short artShiftY,
        out Rect imageBounds)
    {
        imageBounds = default;
        if (!showObjectSprites
            || spriteRecordIndex is not int recordIndex
            || recordIndex != selectedRecordIndex
            || markers is null
            || markers.FirstOrDefault(marker => marker.Placement.RecordIndex == recordIndex)
                is not { Placement: not null } spriteMarker)
        {
            return false;
        }

        return TryGetSpriteWorldBounds(
            spriteMarker,
            pixelWidth,
            pixelHeight,
            artShiftX,
            artShiftY,
            out imageBounds);
    }

    public static bool TryGetSpriteWorldBounds(
        MapIsometricObjectSprite sprite,
        IReadOnlyList<MapIsometricObjectMarker>? markers,
        out Rect imageBounds)
    {
        ArgumentNullException.ThrowIfNull(sprite);
        return TryGetSpriteWorldBounds(
            sprite.RecordIndex,
            markers,
            sprite.Bitmap.PixelSize.Width,
            sprite.Bitmap.PixelSize.Height,
            sprite.ArtShiftX,
            sprite.ArtShiftY,
            out imageBounds);
    }

    public static bool TryGetSpriteWorldBounds(
        int recordIndex,
        IReadOnlyList<MapIsometricObjectMarker>? markers,
        int pixelWidth,
        int pixelHeight,
        short artShiftX,
        short artShiftY,
        out Rect imageBounds)
    {
        imageBounds = default;
        if (markers is null
            || markers.FirstOrDefault(marker => marker.Placement.RecordIndex == recordIndex)
                is not { Placement: not null } spriteMarker)
        {
            return false;
        }

        return TryGetSpriteWorldBounds(
            spriteMarker,
            pixelWidth,
            pixelHeight,
            artShiftX,
            artShiftY,
            out imageBounds);
    }

    private static bool TryGetSpriteWorldBounds(
        MapIsometricObjectMarker spriteMarker,
        int pixelWidth,
        int pixelHeight,
        short artShiftX,
        short artShiftY,
        out Rect imageBounds)
    {
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
    /// The marker is at the CE object anchor, i.e. the tile origin plus (16, 8). CE then adds the
    /// direction's FRM shift and MAP x/y offsets. The frame rectangle uses CE's integer width/2
    /// and height-1 alignment.
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

        var objectAnchor = new Point(
            projectedHexMarker.X + artShiftX + mapOffsetX,
            projectedHexMarker.Y + artShiftY + mapOffsetY);
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
