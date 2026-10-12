using Avalonia;
using FalloutAssetExplorer.App.Views;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class MapIsometricObjectMarkerFilterTests
{
    [Fact]
    public void GetMarkers_FiltersElevationInventoryAndInvalidPositions()
    {
        var placements = new[]
        {
            CreatePlacement(0, 20_099, 100, 100, elevation: 0),
            CreatePlacement(1, 20_099, 100, 100, elevation: 1),
            CreatePlacement(2, 20_099, 100, 100, elevation: 0) with { ParentRecordIndex = 0 },
            CreatePlacement(3, -1, null, null, elevation: 0),
            CreatePlacement(4, 20_099, 99, 100, elevation: 0)
        };

        var markers = MapIsometricObjectMarkerFilter.GetMarkers(
            placements,
            0,
            CreateProjection());

        var marker = Assert.Single(markers);
        Assert.Equal(0, marker.Placement.RecordIndex);
        Assert.Equal(new Point(288, 230), marker.WorldPosition);
    }

    [Fact]
    public void GetMarkers_KeepsValidRecordsFromPartialDataAndUnknownPidTypes()
    {
        var partialRecord = CreatePlacement(
            recordIndex: 7,
            rawHexIndex: 20_099,
            hexX: 100,
            hexY: 100,
            elevation: 2) with
        {
            PidTypeIdentifier = 255,
            UnsupportedReason = "Prototype layout is unavailable."
        };

        var marker = Assert.Single(MapIsometricObjectMarkerFilter.GetMarkers(
            [partialRecord],
            2,
            CreateProjection()));

        Assert.Same(partialRecord, marker.Placement);
        Assert.Equal(255, marker.Placement.PidTypeIdentifier);
        Assert.Equal("Prototype layout is unavailable.", marker.Placement.UnsupportedReason);
    }

    [Fact]
    public void GetMarkers_ReturnsEmptyForMissingDataOrElevation()
    {
        var placement = CreatePlacement(0, 20_099, 100, 100, elevation: 0);
        var projection = CreateProjection();

        Assert.Empty(MapIsometricObjectMarkerFilter.GetMarkers(null, 0, projection));
        Assert.Empty(MapIsometricObjectMarkerFilter.GetMarkers([placement], null, projection));
        Assert.Empty(MapIsometricObjectMarkerFilter.GetMarkers([], 0, projection));
    }

    [Fact]
    public void HitTest_SelectsMarkerAndIgnoresClicksOutsideItsRenderedCircle()
    {
        var markers = MapIsometricObjectMarkerFilter.GetMarkers(
            [CreatePlacement(12, 20_099, 100, 100, elevation: 0)],
            0,
            CreateProjection());
        var camera = new MapIsometricFloorCameraTransform(1, 0, 0);

        Assert.Equal(
            12,
            MapIsometricObjectMarkerHitTest.GetRecordIndexAtPosition(
                markers,
                camera,
                new Point(288, 230),
                640,
                480));
        Assert.Null(MapIsometricObjectMarkerHitTest.GetRecordIndexAtPosition(
            markers,
            camera,
            new Point(292, 230),
            640,
            480));
    }

    [Fact]
    public void HoverTarget_ReturnsVerifiedPrototypeFileNameAndTooltipText()
    {
        const string prototypeFileName = "scenery/example.pro";
        var placement = CreatePlacement(12, 20_099, 100, 100, elevation: 0) with
        {
            PrototypeFileName = prototypeFileName
        };
        var markers = MapIsometricObjectMarkerFilter.GetMarkers(
            [placement],
            0,
            CreateProjection());

        var resolvedFileName = MapIsometricObjectMarkerHitTest.GetPrototypeFileNameAtPosition(
            markers,
            new MapIsometricFloorCameraTransform(1, 0, 0),
            new Point(288, 230),
            640,
            480);

        Assert.Equal(prototypeFileName, resolvedFileName);
        Assert.Equal(
            $"Prototype file: {prototypeFileName}",
            MapIsometricObjectMarkerHitTest.FormatPrototypeFileToolTip(resolvedFileName));
    }

    [Fact]
    public void HoverTarget_DoesNotCreateTooltipForMarkerWithoutVerifiedFileName()
    {
        var markers = MapIsometricObjectMarkerFilter.GetMarkers(
            [CreatePlacement(12, 20_099, 100, 100, elevation: 0)],
            0,
            CreateProjection());

        var resolvedFileName = MapIsometricObjectMarkerHitTest.GetPrototypeFileNameAtPosition(
            markers,
            new MapIsometricFloorCameraTransform(1, 0, 0),
            new Point(288, 230),
            640,
            480);

        Assert.Null(resolvedFileName);
        Assert.Null(MapIsometricObjectMarkerHitTest.FormatPrototypeFileToolTip(resolvedFileName));
        Assert.Null(MapIsometricObjectMarkerHitTest.FormatPrototypeFileToolTip(" "));
    }

    [Fact]
    public void HoverTarget_ClearsWhenPointerMovesAwayFromMarker()
    {
        var placement = CreatePlacement(12, 20_099, 100, 100, elevation: 0) with
        {
            PrototypeFileName = "items/example.pro"
        };
        var markers = MapIsometricObjectMarkerFilter.GetMarkers(
            [placement],
            0,
            CreateProjection());
        var camera = new MapIsometricFloorCameraTransform(1, 0, 0);

        Assert.Equal(
            "items/example.pro",
            MapIsometricObjectMarkerHitTest.GetPrototypeFileNameAtPosition(
                markers,
                camera,
                new Point(288, 230),
                640,
                480));
        Assert.Null(MapIsometricObjectMarkerHitTest.GetPrototypeFileNameAtPosition(
            markers,
            camera,
            new Point(300, 230),
            640,
            480));
    }

    [Fact]
    public void HitTest_UsesZoomedAndPannedScreenPositionAndMarkerRadius()
    {
        var markers = MapIsometricObjectMarkerFilter.GetMarkers(
            [CreatePlacement(12, 20_099, 100, 100, elevation: 0)],
            0,
            CreateProjection());
        var camera = new MapIsometricFloorCameraTransform(2, 100, -30);

        Assert.Equal(6, MapIsometricObjectMarkerHitTest.GetMarkerRadius(camera.Scale));
        Assert.Equal(
            12,
            MapIsometricObjectMarkerHitTest.GetRecordIndexAtPosition(
                markers,
                camera,
                new Point(680, 433),
                800,
                600));
        Assert.Null(MapIsometricObjectMarkerHitTest.GetRecordIndexAtPosition(
            markers,
            camera,
            new Point(683, 430),
            800,
            600));
    }

    [Fact]
    public void HitTest_PicksNearestOverlapAndBreaksExactTiesByRenderOrder()
    {
        var markers = new[]
        {
            new MapIsometricObjectMarker(
                CreatePlacement(12, 20_099, 100, 100, elevation: 0),
                new Point(100, 100)),
            new MapIsometricObjectMarker(
                CreatePlacement(13, 20_099, 100, 100, elevation: 0),
                new Point(100, 100)),
            new MapIsometricObjectMarker(
                CreatePlacement(14, 20_098, 101, 100, elevation: 0),
                new Point(103, 100))
        };
        var camera = new MapIsometricFloorCameraTransform(2, 0, 0);

        Assert.Equal(
            14,
            MapIsometricObjectMarkerHitTest.GetRecordIndexAtPosition(
                markers,
                camera,
                new Point(203, 200),
                640,
                480));
        Assert.Equal(
            13,
            MapIsometricObjectMarkerHitTest.GetRecordIndexAtPosition(
                markers,
                camera,
                new Point(201, 200),
                640,
                480));
    }

    [Fact]
    public void HitTest_RespectsViewportClippingAndFilteredOutRecords()
    {
        var placements = new[]
        {
            CreatePlacement(12, 20_099, 100, 100, elevation: 0),
            CreatePlacement(13, 20_099, 100, 100, elevation: 0) with { ParentRecordIndex = 12 },
            CreatePlacement(14, -1, null, null, elevation: 0)
        };
        var markers = MapIsometricObjectMarkerFilter.GetMarkers(
            placements,
            0,
            CreateProjection());

        Assert.Equal(
            12,
            MapIsometricObjectMarkerHitTest.GetRecordIndexAtPosition(
                markers,
                new MapIsometricFloorCameraTransform(1, -290, 0),
                new Point(0, 230),
                640,
                480));
        Assert.Null(MapIsometricObjectMarkerHitTest.GetRecordIndexAtPosition(
            markers,
            new MapIsometricFloorCameraTransform(1, -300, 0),
            new Point(0, 230),
            640,
            480));

        var filteredOutMarkers = MapIsometricObjectMarkerFilter.GetMarkers(
            placements.Skip(1).ToArray(),
            0,
            CreateProjection());
        Assert.Null(MapIsometricObjectMarkerHitTest.GetRecordIndexAtPosition(
            filteredOutMarkers,
            new MapIsometricFloorCameraTransform(1, 0, 0),
            new Point(288, 230),
            640,
            480));
    }

    private static MapIsometricFloorViewTransform CreateProjection()
    {
        Assert.True(MapIsometricFloorGeometry.TryCreateViewTransform(
            100,
            100,
            640,
            480,
            0,
            0,
            out var projection));
        return projection;
    }

    private static MapObjectPlacement CreatePlacement(
        int recordIndex,
        int rawHexIndex,
        int? hexX,
        int? hexY,
        int elevation) =>
        new(
            RecordIndex: recordIndex,
            RecordOffset: 0,
            ElevationGroup: elevation,
            RawObjectId: 0,
            RawHexIndex: rawHexIndex,
            HexX: hexX,
            HexY: hexY,
            RawOffsetX: 0,
            RawOffsetY: 0,
            RawScreenX: 0,
            RawScreenY: 0,
            RawFrame: 0,
            RawRotation: 0,
            RawFid: 0,
            RawObjectFlags: 0,
            StoredElevation: elevation,
            RawPid: 0x03000001,
            PidTypeIdentifier: 3,
            RawCid: 0,
            RawLightDistance: 0,
            RawLightIntensity: 0,
            RawField74: 0,
            RawScriptId: 0,
            RawScriptField: 0,
            RawInventoryLength: 0,
            RawInventoryCapacity: 0,
            RawInventoryPointer: 0,
            RawPayloadWords: Array.Empty<int>(),
            Warning: null,
            UnsupportedReason: null);
}
