using Avalonia;
using FalloutAssetExplorer.Core;

namespace FalloutAssetExplorer.App.Views;

public static class MapObjectFloorSampleGeometry
{
    private const int DefaultCenterHexX = 100;
    private const int DefaultCenterHexY = 100;
    private const int SampleOffsetX = 2;
    private const int SampleOffsetY = 8;

    public static bool TryGetSampledFloorCell(
        MapObjectPlacement placement,
        out int x,
        out int y)
    {
        ArgumentNullException.ThrowIfNull(placement);
        x = default;
        y = default;
        if ((uint)placement.RawHexIndex >= MapObjectPlacementDecoder.HexGridTileCount
            || placement.HexX is not int hexX
            || placement.HexY is not int hexY
            || (uint)hexX >= MapObjectPlacementDecoder.HexGridWidth
            || (uint)hexY >= MapObjectPlacementDecoder.HexGridHeight
            || MapObjectPlacementDecoder.GetHexCoordinates(placement.RawHexIndex) != (hexX, hexY))
        {
            return false;
        }

        return TryGetSampledFloorCell(hexX, hexY, out x, out y);
    }

    public static bool TryGetSampledFloorCell(
        int hexX,
        int hexY,
        out int x,
        out int y) =>
        TryGetSampledFloorCell(
            hexX,
            hexY,
            DefaultCenterHexX,
            DefaultCenterHexY,
            out x,
            out y);

    public static bool TryGetSampledFloorCell(
        int hexX,
        int hexY,
        int centerTileX,
        int centerTileY,
        out int x,
        out int y)
    {
        x = default;
        y = default;
        if ((uint)hexX >= MapObjectPlacementDecoder.HexGridWidth
            || (uint)hexY >= MapObjectPlacementDecoder.HexGridHeight
            || (uint)centerTileX >= MapObjectPlacementDecoder.HexGridWidth
            || (uint)centerTileY >= MapObjectPlacementDecoder.HexGridHeight)
        {
            return false;
        }

        // tileSetCenter normalizes the projection's horizontal center to an even hex X.
        centerTileX &= ~1;

        var columnDelta = hexX - centerTileX;
        var rowDelta = hexY - centerTileY;

        // tileToScreenXY with screen offsets set to zero; the same offsets are
        // applied to the square-grid origin below and therefore cancel.
        var screenX = 48 * (columnDelta / 2) + 16 * rowDelta;
        var screenY = 12 * (columnDelta / -2) + 12 * rowDelta;
        if ((hexX & 1) != 0)
        {
            if (hexX <= centerTileX)
            {
                screenX -= 16;
                screenY += 12;
            }
            else
            {
                screenX += 32;
            }
        }

        screenX += SampleOffsetX;
        screenY += SampleOffsetY;

        var squareCenterX = centerTileX / 2;
        var squareCenterY = centerTileY / 2;
        var squareOffsetX = -16 - ((centerTileY & 1) != 0 ? 16 : 0);
        var squareOffsetY = -2 - ((centerTileY & 1) != 0 ? 12 : 0);
        var relativeX = screenX - squareOffsetX;
        var relativeY = screenY - squareOffsetY - 12;

        var squareCoordinateX = FloorDivide(3 * relativeX - 4 * relativeY, 192) + squareCenterX;
        var squareCoordinateY = FloorDivide(4 * relativeY + relativeX, 128) + squareCenterY;
        x = squareCoordinateX;
        y = squareCoordinateY;
        return (uint)x < MapTileGridDecoder.GridWidth
            && (uint)y < MapTileGridDecoder.GridHeight;
    }

    public static IReadOnlyList<MapObjectFloorSample> GetSamples(
        IEnumerable<MapObjectPlacement> placements,
        int? elevation)
    {
        ArgumentNullException.ThrowIfNull(placements);
        if (elevation is not int selectedElevation)
        {
            return [];
        }

        var samples = new List<MapObjectFloorSample>();
        foreach (var placement in placements)
        {
            if (placement.ElevationGroup == selectedElevation
                && TryGetSampledFloorCell(placement, out var x, out var y))
            {
                samples.Add(new MapObjectFloorSample(placement, x, y));
            }
        }

        return samples;
    }

    public static bool TrySelectAtScreenPosition(
        IReadOnlyList<MapObjectFloorSample> samples,
        int? currentRecordIndex,
        double positionX,
        double positionY,
        MapFloorGridViewTransform transform,
        double viewWidth,
        double viewHeight,
        out MapObjectPlacement? selectedPlacement)
    {
        selectedPlacement = null;
        if (!MapFloorTileDiagnosticGeometry.TryGetSerializedIndex(
                positionX,
                positionY,
                transform,
                viewWidth,
                viewHeight,
                out var serializedIndex))
        {
            return false;
        }

        var (x, y) = MapTileGridDecoder.GetSquareGridCoordinates(serializedIndex);
        var sameCell = samples
            .Where(sample => sample.X == x && sample.Y == y)
            .Select(sample => sample.Placement)
            .OrderBy(placement => placement.RecordIndex)
            .ToArray();
        if (sameCell.Length == 0)
        {
            return false;
        }

        var currentIndex = Array.FindIndex(
            sameCell,
            placement => placement.RecordIndex == currentRecordIndex);
        selectedPlacement = sameCell[(currentIndex + 1) % sameCell.Length];
        return true;
    }

    private static int FloorDivide(int value, int divisor) =>
        value >= 0 ? value / divisor : (value + 1) / divisor - 1;
}

public readonly record struct MapObjectFloorSample(MapObjectPlacement Placement, int X, int Y)
{
    public int SerializedIndex => MapTileGridDecoder.GetSerializedIndex(X, Y);
}

public sealed class MapObjectPlacementSelectedEventArgs(MapObjectPlacement placement) : EventArgs
{
    public MapObjectPlacement Placement { get; } = placement;
}
