using Avalonia;
using FalloutAssetExplorer.Core;

namespace FalloutAssetExplorer.App.Views;

public static class MapIsometricFloorGeometry
{
    public static bool TryCreateViewTransform(
        int centerHexX,
        int centerHexY,
        int viewportWidth,
        int viewportHeight,
        int screenOffsetX,
        int screenOffsetY,
        out MapIsometricFloorViewTransform transform)
    {
        transform = default;
        if ((uint)centerHexX >= MapObjectPlacementDecoder.HexGridWidth
            || (uint)centerHexY >= MapObjectPlacementDecoder.HexGridHeight
            || viewportWidth <= 0
            || viewportHeight <= 0)
        {
            return false;
        }

        transform = new MapIsometricFloorViewTransform(
            centerHexX,
            centerHexY,
            viewportWidth,
            viewportHeight,
            screenOffsetX,
            screenOffsetY);
        return true;
    }

    public static bool TryProjectSquareCoordinates(
        int x,
        int y,
        MapIsometricFloorViewTransform transform,
        out Point screenPosition)
    {
        screenPosition = default;
        if (!transform.IsValid
            || (uint)x >= MapTileGridDecoder.GridWidth
            || (uint)y >= MapTileGridDecoder.GridHeight)
        {
            return false;
        }

        var columnDelta = x - transform.SquareCenterX;
        var rowDelta = y - transform.SquareCenterY;
        screenPosition = new Point(
            transform.SquareOffsetX + (double)transform.ScreenOffsetX + 48 * columnDelta + 32 * rowDelta,
            transform.SquareOffsetY + (double)transform.ScreenOffsetY - 12 * columnDelta + 24 * rowDelta);
        return true;
    }

    public static bool TryProjectSerializedIndex(
        int serializedIndex,
        MapIsometricFloorViewTransform transform,
        out Point screenPosition)
    {
        screenPosition = default;
        if ((uint)serializedIndex >= MapTileGridDecoder.ValuesPerGrid)
        {
            return false;
        }

        var (x, y) = MapTileGridDecoder.GetSquareGridCoordinates(serializedIndex);
        return TryProjectSquareCoordinates(x, y, transform, out screenPosition);
    }

    public static bool TryGetSquareCoordinatesAtScreenPosition(
        Point screenPosition,
        MapIsometricFloorViewTransform transform,
        out int x,
        out int y)
    {
        x = default;
        y = default;
        if (!transform.IsValid
            || !double.IsFinite(screenPosition.X)
            || !double.IsFinite(screenPosition.Y))
        {
            return false;
        }

        var relativeX = screenPosition.X - transform.ScreenOffsetX - transform.SquareOffsetX;
        var relativeY = screenPosition.Y - transform.ScreenOffsetY - transform.SquareOffsetY - 12;
        var projectedX = Math.Floor((3 * relativeX - 4 * relativeY) / 192) + transform.SquareCenterX;
        var projectedY = Math.Floor((4 * relativeY + relativeX) / 128) + transform.SquareCenterY;
        if (!double.IsFinite(projectedX)
            || !double.IsFinite(projectedY)
            || projectedX < 0 || projectedX >= MapTileGridDecoder.GridWidth
            || projectedY < 0 || projectedY >= MapTileGridDecoder.GridHeight)
        {
            return false;
        }

        // CE's inverse flips X to return a serialized column; this method returns square-grid X.
        x = (int)projectedX;
        y = (int)projectedY;
        return true;
    }

    public static bool TryGetSerializedIndexAtScreenPosition(
        Point screenPosition,
        MapIsometricFloorViewTransform transform,
        out int serializedIndex)
    {
        serializedIndex = default;
        if (!TryGetSquareCoordinatesAtScreenPosition(screenPosition, transform, out var x, out var y))
        {
            return false;
        }

        serializedIndex = MapTileGridDecoder.GetSerializedIndex(x, y);
        return true;
    }
}

/// <summary>
/// Mirrors <c>tileSetCenter</c>: an odd center hex X is reduced by one before deriving the square center.
/// Screen offsets are additional integer-pixel translations after the CE viewport offsets.
/// </summary>
public readonly record struct MapIsometricFloorViewTransform(
    int CenterHexX,
    int CenterHexY,
    int ViewportWidth,
    int ViewportHeight,
    int ScreenOffsetX,
    int ScreenOffsetY)
{
    public bool IsValid =>
        (uint)CenterHexX < MapObjectPlacementDecoder.HexGridWidth
        && (uint)CenterHexY < MapObjectPlacementDecoder.HexGridHeight
        && ViewportWidth > 0
        && ViewportHeight > 0;

    internal int SquareCenterX => (CenterHexX & ~1) / 2;

    internal int SquareCenterY => CenterHexY / 2;

    internal int SquareOffsetX =>
        (ViewportWidth - 32) / 2
        - 16
        - ((CenterHexX & 1) != 0 ? 32 : 0)
        - ((CenterHexY & 1) != 0 ? 16 : 0);

    internal int SquareOffsetY =>
        (ViewportHeight - 16) / 2
        - 2
        - ((CenterHexY & 1) != 0 ? 12 : 0);
}
