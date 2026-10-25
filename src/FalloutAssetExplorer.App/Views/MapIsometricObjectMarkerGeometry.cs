using Avalonia;
using FalloutAssetExplorer.Core;

namespace FalloutAssetExplorer.App.Views;

public static class MapIsometricObjectMarkerGeometry
{
    private const int ReferenceCenterHexX = 100;
    private const int ReferenceCenterHexY = 100;
    private const int ReferenceSquareX = 50;
    private const int ReferenceSquareY = 50;

    /// <summary>CE adds this offset to the hex-tile origin (top-left of the 32x16 hex cell) to get the object anchor.</summary>
    public const int HexOriginToAnchorX = 16;

    public const int HexOriginToAnchorY = 8;

    /// <summary>
    /// Projects a decoded object's hex position to the engine object anchor (hex-cell center) in floor world coordinates.
    /// </summary>
    /// <remarks>
    /// The hex projection is relative to (100, 100), so other projection centers are not supported.
    /// The square-grid origin is 16 pixels left and 2 pixels above the corresponding hex-tile origin,
    /// and the object anchor is (16, 8) pixels right of and below the hex-tile origin, as in CE's
    /// <c>tileToScreenXY</c> callers that place objects. FRM art shifts, MAP offsets, elevation, and
    /// inventory ownership are intentionally not applied here.
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
            floorReference.X + MapIsometricFloorGeometry.SquareOriginToHexOriginX + HexOriginToAnchorX + hexOffset.X,
            floorReference.Y + MapIsometricFloorGeometry.SquareOriginToHexOriginY + HexOriginToAnchorY + hexOffset.Y);
        return double.IsFinite(worldPosition.X) && double.IsFinite(worldPosition.Y);
    }
}
