using Avalonia;
using FalloutAssetExplorer.Core;

namespace FalloutAssetExplorer.App.Views;

public readonly record struct MapIsometricObjectMarker(
    MapObjectPlacement Placement,
    Point WorldPosition);

public static class MapIsometricObjectMarkerFilter
{
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
