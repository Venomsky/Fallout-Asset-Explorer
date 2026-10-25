using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Media.Imaging;
using FalloutAssetExplorer.Core;

namespace FalloutAssetExplorer.App.Views;

public sealed partial class MapIsometricFloorView
{
    private static readonly object ViewDiagnosticLock = new();
    private static readonly string? ViewDiagnosticFilePath = GetViewDiagnosticFilePath();
    private static long _viewDiagnosticSequence;
    private static long _viewDiagnosticPairSequence;
    private long _viewDiagnosticPanPairId;

    private static bool IsViewDiagnosticsEnabled => ViewDiagnosticFilePath is not null;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        WriteViewDiagnostic("Attached", "view attached to visual tree");
    }

    private static bool IsViewDiagnosticProperty(AvaloniaProperty property) =>
        property == BoundsProperty
        || property == ValuesProperty
        || property == TileBitmapsProperty
        || property == AssetRevisionProperty
        || property == PlacementsProperty
        || property == ElevationProperty
        || property == ShowObjectMarkersProperty
        || property == ShowObjectSpritesProperty
        || property == ObjectSpritesProperty
        || property == InitialObjectMarkersZoomFactorProperty
        || property == MapIdentityProperty;

    private long BeginViewDiagnostic(string eventType, string reason)
    {
        if (ViewDiagnosticFilePath is null)
        {
            return 0;
        }

        var pairId = Interlocked.Increment(ref _viewDiagnosticPairSequence);
        WriteViewDiagnostic($"{eventType}.Before", reason, pairId);
        return pairId;
    }

    private void EndViewDiagnostic(string eventType, string reason, long pairId) =>
        WriteViewDiagnostic($"{eventType}.After", reason, pairId);

    private void WriteViewDiagnostic(string eventType, string reason, long pairId = 0)
    {
        var path = ViewDiagnosticFilePath;
        if (path is null)
        {
            return;
        }

        var cameraDisplay = TryGetCurrentCamera(out var camera)
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"scale={camera.Scale:R};offsetX={camera.OffsetX:R};offsetY={camera.OffsetY:R}")
            : "uninitialized";
        var contentBoundsDisplay = TryGetContentBounds(out var contentBounds)
            ? FormatRect(contentBounds)
            : "unavailable";
        var screenOriginDisplay = GetScreenOriginDisplay();
        var tileDisplay = GetStableTileDisplay(cameraDisplay != "uninitialized", camera);
        var objectDisplay = GetStableObjectDisplay(cameraDisplay != "uninitialized", camera);
        var sequence = Interlocked.Increment(ref _viewDiagnosticSequence);
        var line = string.Join(
            "\t",
            $"time={DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)}",
            $"seq={sequence:D6}",
            $"pair={pairId:D6}",
            $"event={Sanitize(eventType)}",
            $"reason={Sanitize(reason)}",
            $"map={Sanitize(MapIdentity ?? "unavailable")}",
            $"bounds={FormatRect(Bounds)}",
            $"screenOriginPx={screenOriginDisplay}",
            $"camera={cameraDisplay}",
            $"fit={_isFitToView}",
            $"elevation={Elevation?.ToString(CultureInfo.InvariantCulture) ?? "unavailable"}",
            $"assetRevision={AssetRevision}",
            $"tileBitmaps={TileBitmaps?.Count ?? 0}",
            $"objectSprites={ObjectSprites?.Count ?? 0}",
            $"contentBounds={contentBoundsDisplay}",
            $"sampleTile={tileDisplay}",
            $"sampleObject={objectDisplay}");

        try
        {
            lock (ViewDiagnosticLock)
            {
                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (IOException exception)
        {
            Debug.WriteLine($"MAP view diagnostics could not append to '{path}': {exception}");
        }
        catch (UnauthorizedAccessException exception)
        {
            Debug.WriteLine($"MAP view diagnostics could not append to '{path}': {exception}");
        }
    }

    private string GetScreenOriginDisplay()
    {
        if (VisualRoot is null)
        {
            return "unavailable-detached";
        }

        try
        {
            var origin = this.PointToScreen(default);
            return $"{origin.X},{origin.Y}";
        }
        catch (InvalidOperationException)
        {
            return "unavailable-unrealized";
        }
    }

    private string GetStableTileDisplay(bool hasCamera, MapIsometricFloorCameraTransform camera)
    {
        if (Values is not { Length: MapTileGridDecoder.ValuesPerGrid } values
            || !TryGetProjectionTransform(out var projection))
        {
            return "unavailable";
        }

        for (var index = 0; index < values.Length; index++)
        {
            var cell = MapFloorTileDiagnosticGeometry.GetCellInfo(index, values[index]);
            if (cell.IsRenderSkipped
                || cell.FloorArtId == MapFloorTileDiagnosticGeometry.CeEmptyFloorArtId
                || !MapIsometricFloorGeometry.TryProjectSerializedIndex(
                    index,
                    projection,
                    out var worldAnchor))
            {
                continue;
            }

            var localAnchor = hasCamera ? camera.ToScreen(worldAnchor) : (Point?)null;
            var imageBounds = hasCamera
                && TileBitmaps is { } bitmaps
                && bitmaps.TryGetValue(cell.FloorArtId, out var bitmap)
                && bitmap.PixelSize.Width > 0
                && bitmap.PixelSize.Height > 0
                ? FormatRect(MapIsometricFloorViewportGeometry.GetImageBounds(
                    camera,
                    worldAnchor,
                    bitmap.PixelSize.Width,
                    bitmap.PixelSize.Height))
                : "unavailable";
            return string.Create(
                CultureInfo.InvariantCulture,
                $"index={index};id={cell.FloorArtId};world={FormatPoint(worldAnchor)};localAnchor={(localAnchor is Point point ? FormatPoint(point) : "unavailable")};imageBounds={imageBounds}");
        }

        return "none";
    }

    private string GetStableObjectDisplay(bool hasCamera, MapIsometricFloorCameraTransform camera)
    {
        if (!TryGetProjectionTransform(out var projection))
        {
            return "unavailable";
        }

        var markers = GetObjectMarkers(projection);
        if (markers.Count == 0)
        {
            return "none";
        }

        var marker = markers[0];
        var localAnchor = hasCamera ? camera.ToScreen(marker.WorldPosition) : (Point?)null;
        var spriteBounds = "unavailable";
        if (hasCamera
            && ShowObjectSprites
            && _objectSpritesByRecord.TryGetValue(marker.Placement.RecordIndex, out var sprite)
            && MapIsometricObjectSpriteGeometry.TryGetImageBounds(
                marker.WorldPosition,
                camera,
                sprite.Bitmap.PixelSize.Width,
                sprite.Bitmap.PixelSize.Height,
                sprite.ArtShiftX,
                sprite.ArtShiftY,
                marker.Placement.RawOffsetX,
                marker.Placement.RawOffsetY,
                out var imageBounds))
        {
            spriteBounds = FormatRect(imageBounds);
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"record={marker.Placement.RecordIndex};hex={marker.Placement.RawHexIndex};world={FormatPoint(marker.WorldPosition)};localAnchor={(localAnchor is Point point ? FormatPoint(point) : "unavailable")};spriteBounds={spriteBounds}");
    }

    private static string? GetViewDiagnosticFilePath()
    {
        var path = Environment.GetEnvironmentVariable("FAE_MAP_VIEW_DIAGNOSTICS");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var fileName = Path.GetFileName(path);
            if (string.Equals(fileName, "map-diagnostic-new.log", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fileName, "map-diagnostic-summary.log", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fileName, "map-diagnostic.log", StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine("MAP view diagnostics refused a protected diagnostic-log filename.");
                return null;
            }
        }
        catch (ArgumentException exception)
        {
            Debug.WriteLine($"MAP view diagnostics path is invalid: {exception}");
            return null;
        }

        return path;
    }

    private static string FormatDiagnosticValue(object? value) =>
        value switch
        {
            null => "null",
            uint[] values => $"uint[{values.Length}]",
            IReadOnlyDictionary<uint, Bitmap> bitmaps => $"tileBitmaps[{bitmaps.Count}]",
            IReadOnlyList<MapObjectPlacement> placements => $"placements[{placements.Count}]",
            IReadOnlyList<MapIsometricObjectSprite> sprites => $"sprites[{sprites.Count}]",
            _ => Sanitize(Convert.ToString(value, CultureInfo.InvariantCulture) ?? value.GetType().Name)
        };

    private static string FormatRect(Rect rect) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"x={rect.X:R};y={rect.Y:R};width={rect.Width:R};height={rect.Height:R}");

    private static string FormatPoint(Point point) =>
        string.Create(CultureInfo.InvariantCulture, $"x={point.X:R};y={point.Y:R}");

    private static string Sanitize(string value) =>
        value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
