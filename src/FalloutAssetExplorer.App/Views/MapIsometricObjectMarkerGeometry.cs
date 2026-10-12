using Avalonia;
using FalloutAssetExplorer.Core;

namespace FalloutAssetExplorer.App.Views;

public static class MapIsometricObjectMarkerGeometry
{
    private const int ReferenceCenterHexX = 100;
    private const int ReferenceCenterHexY = 100;
    private const int ReferenceSquareX = 50;
    private const int ReferenceSquareY = 50;

    /// <summary>
    /// Projects a decoded object's hex position into the isometric floor's world coordinates.
    /// </summary>
    /// <remarks>
    /// The hex projection is relative to (100, 100), so other projection centers are not supported.
    /// Elevation, inventory ownership, and raw object offsets are intentionally not applied here.
    /// </remarks>
    public static bool TryGetWorldPosition(
        MapObjectPlacement placement,
        MapIsometricFloorViewTransform projection,
        out Point worldPosition)
    {
        ArgumentNullException.ThrowIfNull(placement);
        worldPosition = default;

        if (!projection.IsValid
            || projection.CenterHexX != ReferenceCenterHexX
            || projection.CenterHexY != ReferenceCenterHexY
            || (uint)placement.RawHexIndex >= MapObjectPlacementDecoder.HexGridTileCount
            || placement.HexX is not int hexX
            || placement.HexY is not int hexY
            || MapObjectPlacementDecoder.GetHexCoordinates(placement.RawHexIndex) != (hexX, hexY)
            || !MapIsometricFloorGeometry.TryProjectSquareCoordinates(
                ReferenceSquareX,
                ReferenceSquareY,
                projection,
                out var floorReference))
        {
            return false;
        }

        var hexOffset = MapHexGridDiagnosticGeometry.GetRelativeScreenPosition(hexX, hexY);
        worldPosition = new Point(
            floorReference.X + hexOffset.X,
            floorReference.Y + hexOffset.Y);
        return double.IsFinite(worldPosition.X) && double.IsFinite(worldPosition.Y);
    }
}
