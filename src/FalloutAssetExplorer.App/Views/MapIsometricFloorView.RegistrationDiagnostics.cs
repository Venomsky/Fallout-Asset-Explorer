using System.Globalization;
using Avalonia;
using Avalonia.Media;
using FalloutAssetExplorer.Core;

namespace FalloutAssetExplorer.App.Views;

public sealed partial class MapIsometricFloorView
{
    private static readonly IBrush RegistrationPanelBrush =
        new SolidColorBrush(Color.FromArgb(224, 23, 26, 31));
    private static readonly IBrush RegistrationTextBrush =
        new SolidColorBrush(Color.Parse("#F3F5F8"));
    private static readonly IBrush RegistrationMarkerBrush =
        new SolidColorBrush(Color.Parse("#FF4FD8"));
    private static readonly IBrush RegistrationTileBrush =
        new SolidColorBrush(Color.Parse("#00E5FF"));
    private static readonly IBrush RegistrationCenterBrush =
        new SolidColorBrush(Color.Parse("#FFE66D"));
    private static readonly IPen RegistrationMarkerPen = new Pen(RegistrationMarkerBrush, 2);
    private static readonly IPen RegistrationTilePen = new Pen(RegistrationTileBrush, 1.5);
    private static readonly IPen RegistrationCenterPen = new Pen(RegistrationCenterBrush, 1.5);
    private static readonly Typeface RegistrationTypeface = new("Consolas");

    public static readonly StyledProperty<bool> ShowRegistrationDiagnosticsProperty =
        AvaloniaProperty.Register<MapIsometricFloorView, bool>(
            nameof(ShowRegistrationDiagnostics),
            defaultValue: false);

    public bool ShowRegistrationDiagnostics
    {
        get => GetValue(ShowRegistrationDiagnosticsProperty);
        set => SetValue(ShowRegistrationDiagnosticsProperty, value);
    }

    private void DrawRegistrationDiagnostics(
        DrawingContext context,
        MapIsometricFloorViewTransform projection,
        MapIsometricFloorCameraTransform camera,
        IReadOnlyList<MapIsometricObjectMarker> markers)
    {
        var selectedRecordIndex = SelectedRecordIndex;
        var selectedMarker = FindSelectedMarker(markers, selectedRecordIndex);
        var lines = BuildRegistrationDiagnosticLines(
            selectedMarker,
            projection,
            camera,
            out var nearbyTiles,
            out var sampleCenter);

        DrawRegistrationPanel(context, lines);
        if (selectedMarker is MapIsometricObjectMarker marker)
        {
            var markerScreen = camera.ToScreen(marker.WorldPosition);
            DrawRegistrationCross(
                context,
                markerScreen,
                RegistrationMarkerPen,
                7,
                "marker view-local");
        }

        foreach (var tile in nearbyTiles)
        {
            if (tile.Status != MapIsometricFloorRegistrationTileStatus.Drawn
                || tile.ScreenBounds is not Rect screenBounds)
            {
                continue;
            }

            context.DrawRectangle(null, RegistrationTilePen, screenBounds);
            DrawRectangleReferences(context, tile, screenBounds);
        }

        if (sampleCenter is Point center)
        {
            var centerScreen = camera.ToScreen(center);
            DrawRegistrationCross(
                context,
                centerScreen,
                RegistrationCenterPen,
                5,
                "sampled floor anchor view-local");
        }
    }

    private IReadOnlyList<string> BuildRegistrationDiagnosticLines(
        MapIsometricObjectMarker? selectedMarker,
        MapIsometricFloorViewTransform projection,
        MapIsometricFloorCameraTransform camera,
        out IReadOnlyList<MapIsometricFloorRegistrationTile> nearbyTiles,
        out Point? sampleCenter)
    {
        var lines = new List<string>
        {
            "MAP coordinate registration (diagnostic only; no expected anchor is asserted)",
            string.Create(
                CultureInfo.InvariantCulture,
                $"Viewport local=(0,0) size=({Bounds.Width:R},{Bounds.Height:R}); screen origin px={GetRegistrationScreenOriginDisplay()}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Camera scale={camera.Scale:R} offset=({camera.OffsetX:R},{camera.OffsetY:R}); projection center hex=({projection.CenterHexX},{projection.CenterHexY})")
        };

        nearbyTiles = [];
        sampleCenter = null;
        if (selectedMarker is not MapIsometricObjectMarker marker)
        {
            lines.Add(SelectedRecordIndex is null
                ? "Select one top-level MAP object to inspect its marker and nearby floor cells."
                : $"Selected record #{SelectedRecordIndex} has no marker in this elevation/view.");
            return lines;
        }

        var placement = marker.Placement;
        var screenAnchor = camera.ToScreen(marker.WorldPosition);
        lines.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"Record #{placement.RecordIndex} byte={placement.RecordOffset} order=#{placement.RecordIndex} {placement.ObjectTypeDisplay} elev={placement.ElevationGroup} PID=0x{placement.RawPid:X8} FID=0x{placement.RawFid:X8}"));
        lines.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"Hex index={placement.RawHexIndex} coords=({placement.HexX?.ToString(CultureInfo.InvariantCulture) ?? "?"},{placement.HexY?.ToString(CultureInfo.InvariantCulture) ?? "?"}); raw offsets=({placement.RawOffsetX},{placement.RawOffsetY}) screen fields=({placement.RawScreenX},{placement.RawScreenY})"));
        lines.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"Marker anchor world=({marker.WorldPosition.X:R},{marker.WorldPosition.Y:R}); view-local screen=({screenAnchor.X:R},{screenAnchor.Y:R})"));

        if (!MapObjectFloorSampleGeometry.TryGetSampledFloorCell(
                placement,
                out var sampleX,
                out var sampleY))
        {
            lines.Add("Existing floor-sample mapping: unavailable for this object.");
            return lines;
        }

        if (MapIsometricFloorGeometry.TryProjectSquareCoordinates(
                sampleX,
                sampleY,
                projection,
                out var sampleWorldAnchor))
        {
            sampleCenter = sampleWorldAnchor;
        }

        nearbyTiles = MapIsometricFloorRegistrationDiagnosticGeometry.GetNearbyTiles(
            Values,
            sampleX,
            sampleY,
            GetTileBitmapSize,
            projection,
            camera,
            Bounds.Width,
            Bounds.Height);
        lines.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"Existing MapObjectFloorSampleGeometry center=({sampleX},{sampleY}); nearby square cells: 3x3"));
        foreach (var tile in nearbyTiles)
        {
            var worldBounds = FormatRect(tile.WorldBounds);
            var screenBounds = FormatRect(tile.ScreenBounds);
            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"cell ({tile.X},{tile.Y}) index={tile.SerializedIndex} floorId={tile.FloorArtId} {tile.Status}; W={worldBounds}; local S={screenBounds}"));
        }

        return lines;
    }

    private PixelSize? GetTileBitmapSize(uint floorArtId) =>
        TileBitmaps is { } bitmaps && bitmaps.TryGetValue(floorArtId, out var bitmap)
            ? bitmap.PixelSize
            : null;

    private string GetRegistrationScreenOriginDisplay()
    {
        if (VisualRoot is null)
        {
            return "unavailable-detached";
        }

        try
        {
            var origin = this.PointToScreen(default);
            return string.Create(
                CultureInfo.InvariantCulture,
                $"({origin.X},{origin.Y})");
        }
        catch (InvalidOperationException)
        {
            return "unavailable-unrealized";
        }
    }

    private static MapIsometricObjectMarker? FindSelectedMarker(
        IReadOnlyList<MapIsometricObjectMarker> markers,
        int? selectedRecordIndex)
    {
        if (selectedRecordIndex is not int recordIndex)
        {
            return null;
        }

        foreach (var marker in markers)
        {
            if (marker.Placement.RecordIndex == recordIndex
                && marker.Placement.ParentRecordIndex is null)
            {
                return marker;
            }
        }

        return null;
    }

    private void DrawRegistrationPanel(DrawingContext context, IReadOnlyList<string> lines)
    {
        const double padding = 8;
        const double lineHeight = 13;
        var panelWidth = Math.Max(0, Math.Min(1000, Bounds.Width - padding * 2));
        var panelBounds = new Rect(
            padding,
            padding,
            panelWidth,
            padding * 2 + lines.Count * lineHeight);
        context.FillRectangle(RegistrationPanelBrush, panelBounds);
        for (var index = 0; index < lines.Count; index++)
        {
            var formattedText = new FormattedText(
                lines[index],
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                RegistrationTypeface,
                9,
                RegistrationTextBrush);
            context.DrawText(
                formattedText,
                new Point(panelBounds.X + padding, panelBounds.Y + padding + index * lineHeight));
        }
    }

    private static void DrawRectangleReferences(
        DrawingContext context,
        MapIsometricFloorRegistrationTile tile,
        Rect bounds)
    {
        var corners = new (string Label, Point Position)[]
        {
            ("TL", bounds.TopLeft),
            ("TR", bounds.TopRight),
            ("BL", bounds.BottomLeft),
            ("BR", bounds.BottomRight)
        };
        foreach (var (label, position) in corners)
        {
            DrawRegistrationCross(
                context,
                position,
                RegistrationTilePen,
                4,
                $"tile {tile.SerializedIndex} {label} view-local");
        }

        DrawRegistrationCross(
            context,
            bounds.Center,
            RegistrationCenterPen,
            5,
            $"tile {tile.SerializedIndex} center view-local");
    }

    private static void DrawRegistrationCross(
        DrawingContext context,
        Point position,
        IPen pen,
        double radius,
        string label)
    {
        context.DrawLine(
            pen,
            new Point(position.X - radius, position.Y),
            new Point(position.X + radius, position.Y));
        context.DrawLine(
            pen,
            new Point(position.X, position.Y - radius),
            new Point(position.X, position.Y + radius));
        var formattedText = new FormattedText(
            label,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            RegistrationTypeface,
            8,
            pen.Brush);
        context.DrawText(formattedText, new Point(position.X + radius + 2, position.Y + radius + 1));
    }

    private static string FormatRect(Rect? rect) =>
        rect is Rect bounds
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"({bounds.X:R},{bounds.Y:R},{bounds.Width:R},{bounds.Height:R})")
            : "unavailable";
}

public enum MapIsometricFloorRegistrationTileStatus
{
    RenderSuppressed,
    Empty,
    BitmapUnavailable,
    InvalidBitmapSize,
    ProjectionUnavailable,
    OutsideViewport,
    Drawn
}

public readonly record struct MapIsometricFloorRegistrationTile(
    int X,
    int Y,
    int SerializedIndex,
    uint FloorArtId,
    bool IsRenderSkipped,
    PixelSize? BitmapSize,
    Point? WorldAnchor,
    Rect? WorldBounds,
    Rect? ScreenBounds,
    MapIsometricFloorRegistrationTileStatus Status);

public static class MapIsometricFloorRegistrationDiagnosticGeometry
{
    public const int NearbyTileRadius = 1;

    public static IReadOnlyList<MapIsometricFloorRegistrationTile> GetNearbyTiles(
        uint[]? values,
        int centerX,
        int centerY,
        Func<uint, PixelSize?> getBitmapSize,
        MapIsometricFloorViewTransform projection,
        MapIsometricFloorCameraTransform camera,
        double viewWidth,
        double viewHeight)
    {
        ArgumentNullException.ThrowIfNull(getBitmapSize);
        if (values is not { Length: MapTileGridDecoder.ValuesPerGrid })
        {
            return [];
        }

        if ((uint)centerX >= MapTileGridDecoder.GridWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(centerX));
        }

        if ((uint)centerY >= MapTileGridDecoder.GridHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(centerY));
        }

        var nearbyTiles = new List<MapIsometricFloorRegistrationTile>();
        for (var y = Math.Max(0, centerY - NearbyTileRadius);
             y <= Math.Min(MapTileGridDecoder.GridHeight - 1, centerY + NearbyTileRadius);
             y++)
        {
            for (var x = Math.Max(0, centerX - NearbyTileRadius);
                 x <= Math.Min(MapTileGridDecoder.GridWidth - 1, centerX + NearbyTileRadius);
                 x++)
            {
                var serializedIndex = MapTileGridDecoder.GetSerializedIndex(x, y);
                var cell = MapFloorTileDiagnosticGeometry.GetCellInfo(serializedIndex, values[serializedIndex]);
                if (cell.IsRenderSkipped)
                {
                    nearbyTiles.Add(CreateTile(cell, null, null, null, MapIsometricFloorRegistrationTileStatus.RenderSuppressed));
                    continue;
                }

                if (cell.FloorArtId == MapFloorTileDiagnosticGeometry.CeEmptyFloorArtId)
                {
                    nearbyTiles.Add(CreateTile(cell, null, null, null, MapIsometricFloorRegistrationTileStatus.Empty));
                    continue;
                }

                if (getBitmapSize(cell.FloorArtId) is not PixelSize bitmapSize)
                {
                    nearbyTiles.Add(CreateTile(cell, null, null, null, MapIsometricFloorRegistrationTileStatus.BitmapUnavailable));
                    continue;
                }

                if (bitmapSize.Width <= 0 || bitmapSize.Height <= 0)
                {
                    nearbyTiles.Add(CreateTile(cell, bitmapSize, null, null, MapIsometricFloorRegistrationTileStatus.InvalidBitmapSize));
                    continue;
                }

                if (!projection.IsValid
                    || !camera.IsValid
                    || !MapIsometricFloorGeometry.TryProjectSquareCoordinates(
                        x,
                        y,
                        projection,
                        out var worldAnchor))
                {
                    nearbyTiles.Add(CreateTile(cell, bitmapSize, null, null, MapIsometricFloorRegistrationTileStatus.ProjectionUnavailable));
                    continue;
                }

                var screenBounds = MapIsometricFloorViewportGeometry.GetImageBounds(
                    camera,
                    worldAnchor,
                    bitmapSize.Width,
                    bitmapSize.Height);
                var worldBounds = new Rect(
                    worldAnchor,
                    new Size(bitmapSize.Width, bitmapSize.Height));
                var status = MapIsometricFloorViewportGeometry.IntersectsViewport(
                    screenBounds,
                    viewWidth,
                    viewHeight)
                    ? MapIsometricFloorRegistrationTileStatus.Drawn
                    : MapIsometricFloorRegistrationTileStatus.OutsideViewport;
                nearbyTiles.Add(CreateTile(
                    cell,
                    bitmapSize,
                    worldAnchor,
                    worldBounds,
                    status,
                    screenBounds));
            }
        }

        return nearbyTiles;
    }

    private static MapIsometricFloorRegistrationTile CreateTile(
        MapFloorTileCellInfo cell,
        PixelSize? bitmapSize,
        Point? worldAnchor,
        Rect? worldBounds,
        MapIsometricFloorRegistrationTileStatus status,
        Rect? screenBounds = null) =>
        new(
            cell.X,
            cell.Y,
            cell.SerializedIndex,
            cell.FloorArtId,
            cell.IsRenderSkipped,
            bitmapSize,
            worldAnchor,
            worldBounds,
            screenBounds,
            status);
}
