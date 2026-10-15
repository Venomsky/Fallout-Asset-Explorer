using Avalonia;
using FalloutAssetExplorer.Core;

namespace FalloutAssetExplorer.App.Views;

public readonly record struct MapIsometricObjectMarker(
    MapObjectPlacement Placement,
    Point WorldPosition);

public static class MapIsometricObjectMarkerFilter
{
    public static bool IsSupportedSpriteTypePair(int pidTypeIdentifier, int artType) =>
        pidTypeIdentifier == artType && pidTypeIdentifier is 0 or 2 or 3;

    public static IReadOnlyList<MapObjectPlacement> GetSpriteCandidates(
        IReadOnlyList<MapObjectPlacement>? placements,
        int? elevation,
        MapIsometricFloorViewTransform projection)
    {
        var candidates = new List<MapObjectPlacement>();
        foreach (var marker in GetMarkers(placements, elevation, projection))
        {
            var placement = marker.Placement;
            var fid = FalloutFidDecoder.Decode(placement.RawFid);
            if (IsSupportedSpriteTypePair(placement.PidTypeIdentifier, fid.ArtType)
                && (placement.PidTypeIdentifier == 0 || placement.UnsupportedReason is null))
            {
                candidates.Add(placement);
            }
        }

        return candidates;
    }

    public static IReadOnlyList<MapIsometricObjectMarker> GetMarkers(
        IReadOnlyList<MapObjectPlacement>? placements,
        int? elevation,
        MapIsometricFloorViewTransform projection)
    {
        if (placements is null || elevation is not int selectedElevation)
        {
            return Array.Empty<MapIsometricObjectMarker>();
        }

        var markers = new List<MapIsometricObjectMarker>();
        foreach (var placement in placements)
        {
            if (placement.ElevationGroup != selectedElevation
                || placement.ParentRecordIndex is not null
                || !MapIsometricObjectMarkerGeometry.TryGetWorldPosition(
                    placement,
                    projection,
                    out var worldPosition))
            {
                continue;
            }

            markers.Add(new MapIsometricObjectMarker(placement, worldPosition));
        }

        return markers;
    }
}

public static class MapIsometricObjectMarkerHitTest
{
    public static double GetMarkerRadius(double cameraScale) =>
        Math.Clamp(cameraScale * 3, 2.5, 6);

    public static int? GetRecordIndexAtPosition(
        IReadOnlyList<MapIsometricObjectMarker> markers,
        MapIsometricFloorCameraTransform camera,
        Point position,
        double viewWidth,
        double viewHeight) =>
        GetMarkerAtPosition(markers, camera, position, viewWidth, viewHeight)?.Placement.RecordIndex;

    public static string? GetPrototypeFileNameAtPosition(
        IReadOnlyList<MapIsometricObjectMarker> markers,
        MapIsometricFloorCameraTransform camera,
        Point position,
        double viewWidth,
        double viewHeight)
    {
        var marker = GetMarkerAtPosition(markers, camera, position, viewWidth, viewHeight);
        var prototypeFileName = marker?.Placement.PrototypeFileName;
        return string.IsNullOrWhiteSpace(prototypeFileName) ? null : prototypeFileName;
    }

    public static string? FormatPrototypeFileToolTip(string? prototypeFileName) =>
        string.IsNullOrWhiteSpace(prototypeFileName)
            ? null
            : $"Prototype file: {prototypeFileName}";

    private static MapIsometricObjectMarker? GetMarkerAtPosition(
        IReadOnlyList<MapIsometricObjectMarker> markers,
        MapIsometricFloorCameraTransform camera,
        Point position,
        double viewWidth,
        double viewHeight)
    {
        ArgumentNullException.ThrowIfNull(markers);
        if (!camera.IsValid
            || !double.IsFinite(position.X)
            || !double.IsFinite(position.Y)
            || !double.IsFinite(viewWidth)
            || !double.IsFinite(viewHeight)
            || viewWidth <= 0
            || viewHeight <= 0
            || position.X < 0
            || position.X >= viewWidth
            || position.Y < 0
            || position.Y >= viewHeight)
        {
            return null;
        }

        var radius = GetMarkerRadius(camera.Scale);
        var radiusSquared = radius * radius;
        var closestDistanceSquared = double.PositiveInfinity;
        var closestMarkerIndex = -1;
        for (var index = 0; index < markers.Count; index++)
        {
            var center = camera.ToScreen(markers[index].WorldPosition);
            var bounds = new Rect(
                center.X - radius,
                center.Y - radius,
                radius * 2,
                radius * 2);
            if (!MapIsometricFloorViewportGeometry.IntersectsViewport(bounds, viewWidth, viewHeight))
            {
                continue;
            }

            var deltaX = position.X - center.X;
            var deltaY = position.Y - center.Y;
            var distanceSquared = deltaX * deltaX + deltaY * deltaY;
            if (distanceSquared > radiusSquared)
            {
                continue;
            }

            if (distanceSquared < closestDistanceSquared
                || (distanceSquared == closestDistanceSquared && index > closestMarkerIndex))
            {
                closestDistanceSquared = distanceSquared;
                closestMarkerIndex = index;
            }
        }

        return closestMarkerIndex >= 0
            ? markers[closestMarkerIndex]
            : null;
    }
}

public sealed class MapObjectMarkerSelectionChangedEventArgs(int? recordIndex) : EventArgs
{
    public int? RecordIndex { get; } = recordIndex;
}
