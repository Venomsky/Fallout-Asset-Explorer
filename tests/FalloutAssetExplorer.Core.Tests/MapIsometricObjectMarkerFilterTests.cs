using Avalonia;
using FalloutAssetExplorer.App.Views;
using FalloutAssetExplorer.App.ViewModels;
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
        Assert.Equal(new Point(320, 240), marker.WorldPosition);
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
    public void EditSessionPlacementsRefreshMarkersAndRetainSelectionAcrossUndoRedo()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        var inspector = viewModel.MapInspector;
        var sourcePlacement = CreatePlacement(7, 20_099, 100, 100, elevation: 0) with
        {
            PrototypeFileName = "scenery/tree.pro"
        };
        var otherPlacement = CreatePlacement(8, 20_200, 199, 101, elevation: 0) with
        {
            PrototypeFileName = "scenery/rock.pro"
        };
        var otherElevationPlacement = CreatePlacement(9, 20_100, 99, 100, elevation: 1) with
        {
            PrototypeFileName = "scenery/other.pro"
        };
        var sourceBytes = new byte[] { 1, 2, 3 };
        var sourceData = new MapSourceData(sourceBytes);
        viewModel.MapObjectPlacements.Add(sourcePlacement);
        viewModel.MapObjectPlacements.Add(otherPlacement);
        viewModel.MapObjectPlacements.Add(otherElevationPlacement);
        inspector.SetAvailableElevations([0, 1], preferredElevationIndex: 0);
        var session = new MapEditSession(
            sourceData,
            [sourcePlacement, otherPlacement, otherElevationPlacement]);
        inspector.SetMapEditSession(session);
        inspector.SelectedMapObjectListSort = Assert.Single(
            inspector.MapObjectListSortOptions,
            option => option.SortMode == MapInspectorViewModel.MapObjectListSortMode.HexCoordinatesAscending);
        inspector.MapObjectSearchText = "scenery";

        var selectedPlacement = Assert.Single(
            inspector.MapObjectPlacements,
            placement => placement.RecordIndex == sourcePlacement.RecordIndex);
        inspector.SelectedMapObject = selectedPlacement;
        var selectedId = inspector.SelectedEditObject!.Id;
        var selectionEvents = 0;
        var placementRefreshes = 0;
        inspector.SelectedMapObjectChanged += (_, _) => selectionEvents++;
        inspector.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MapInspectorViewModel.MapObjectPlacements))
            {
                placementRefreshes++;
            }
        };
        var projection = CreateProjection();
        var originalMarker = Assert.Single(
            MapIsometricObjectMarkerFilter.GetMarkers(
                inspector.FilteredMapObjectPlacements,
                elevation: 0,
                projection),
            marker => marker.Placement.RecordIndex == sourcePlacement.RecordIndex);
        Assert.Equal(2, MapIsometricObjectMarkerFilter.GetMarkers(
            inspector.FilteredMapObjectPlacements,
            elevation: 0,
            projection).Count);

        Assert.True(session.TrySetHexTileIndex(selectedId, 20_400, out var editError), editError);

        var movedPlacement = Assert.Single(
            inspector.MapObjectPlacements,
            placement => placement.RecordIndex == sourcePlacement.RecordIndex);
        var movedMarker = Assert.Single(
            MapIsometricObjectMarkerFilter.GetMarkers(
                inspector.FilteredMapObjectPlacements,
                elevation: 0,
                projection),
            marker => marker.Placement.RecordIndex == sourcePlacement.RecordIndex);
        Assert.NotEqual(originalMarker.WorldPosition, movedMarker.WorldPosition);
        Assert.Same(movedPlacement, movedMarker.Placement);
        Assert.Same(movedPlacement, inspector.SelectedMapObject);
        Assert.Equal(selectedId, inspector.SelectedEditObject?.Id);
        Assert.Equal(0, selectionEvents);
        Assert.Equal(1, placementRefreshes);
        Assert.True(Array.IndexOf(
                inspector.SortedMapObjectPlacements.Select(placement => placement.RecordIndex).ToArray(),
                otherPlacement.RecordIndex)
            < Array.IndexOf(
                inspector.SortedMapObjectPlacements.Select(placement => placement.RecordIndex).ToArray(),
                sourcePlacement.RecordIndex));

        Assert.True(session.TrySetHexTileIndex(selectedId, 20_400, out var noOpError), noOpError);
        Assert.False(session.TrySetHexTileIndex(selectedId, 40_000, out var invalidError));
        Assert.NotNull(invalidError);
        Assert.Equal(1, placementRefreshes);

        Assert.True(session.Undo(out var undoError), undoError);
        var undoneMarker = Assert.Single(
            MapIsometricObjectMarkerFilter.GetMarkers(
                inspector.FilteredMapObjectPlacements,
                elevation: 0,
                projection),
            marker => marker.Placement.RecordIndex == sourcePlacement.RecordIndex);
        Assert.Equal(originalMarker.WorldPosition, undoneMarker.WorldPosition);
        Assert.Equal(selectedId, inspector.SelectedEditObject?.Id);
        Assert.Equal(2, placementRefreshes);

        Assert.True(session.Redo(out var redoError), redoError);
        var redoneMarker = Assert.Single(
            MapIsometricObjectMarkerFilter.GetMarkers(
                inspector.FilteredMapObjectPlacements,
                elevation: 0,
                projection),
            marker => marker.Placement.RecordIndex == sourcePlacement.RecordIndex);
        Assert.Equal(movedMarker.WorldPosition, redoneMarker.WorldPosition);
        Assert.Equal(selectedId, inspector.SelectedEditObject?.Id);
        Assert.Equal(3, placementRefreshes);
        Assert.Equal(20_099, sourcePlacement.RawHexIndex);
        Assert.Equal(100, sourcePlacement.HexX);
        Assert.Equal(100, sourcePlacement.HexY);
        Assert.Same(sourcePlacement, Assert.Single(
            viewModel.MapObjectPlacements,
            placement => placement.RecordIndex == sourcePlacement.RecordIndex));
        Assert.Equal([7, 8, 9], inspector.FilteredMapObjectPlacements
            .Select(placement => placement.RecordIndex));
        Assert.Equal(sourceBytes, sourceData.GetRange(0, sourceData.Length).ToArray());
    }

    [Fact]
    public void GetSpriteCandidates_KeepsEligibleTopLevelItemsSceneryAndWallsInInputOrder()
    {
        var placements = new[]
        {
            CreatePlacement(9, 20_099, 100, 100, elevation: 0) with
            {
                PidTypeIdentifier = 2,
                RawPid = 0x02000001,
                RawFid = 0x02000001
            },
            CreatePlacement(10, 20_099, 100, 100, elevation: 0) with
            {
                PidTypeIdentifier = 3,
                RawPid = 0x03000001,
                RawFid = 0x03000001
            },
            CreatePlacement(4, 20_099, 100, 100, elevation: 0) with
            {
                PidTypeIdentifier = 0,
                RawPid = 0x00000001,
                RawFid = 0x00000001
            },
            CreatePlacement(1, 20_099, 100, 100, elevation: 1) with
            {
                PidTypeIdentifier = 0,
                RawPid = 0x00000001,
                RawFid = 0x00000001
            },
            CreatePlacement(7, 20_099, 100, 100, elevation: 0) with
            {
                PidTypeIdentifier = 0,
                RawPid = 0x00000001,
                RawFid = 0x00000001,
                ParentRecordIndex = 9
            },
            CreatePlacement(2, -1, null, null, elevation: 0) with
            {
                PidTypeIdentifier = 0,
                RawPid = 0x00000001,
                RawFid = 0x00000001
            },
            CreatePlacement(5, 20_099, 100, 100, elevation: 0) with
            {
                PidTypeIdentifier = 1,
                RawPid = 0x01000001,
                RawFid = 0x01000001
            },
            CreatePlacement(3, 20_099, 100, 100, elevation: 0) with
            {
                PidTypeIdentifier = 2,
                RawPid = 0x02000001,
                RawFid = 0x02000001,
                UnsupportedReason = "Unsupported scenery payload."
            },
            CreatePlacement(6, 20_099, 100, 100, elevation: 0) with
            {
                PidTypeIdentifier = 3,
                RawPid = 0x03000001,
                RawFid = 0x03000001,
                UnsupportedReason = "Unsupported wall payload."
            }
        };

        var candidates = MapIsometricObjectMarkerFilter.GetSpriteCandidates(
            placements,
            0,
            CreateProjection());

        Assert.Equal(new[] { 9, 10, 4 }, candidates.Select(placement => placement.RecordIndex));
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(2, 2, true)]
    [InlineData(3, 3, true)]
    [InlineData(0, 2, false)]
    [InlineData(2, 3, false)]
    [InlineData(3, 2, false)]
    [InlineData(3, 0, false)]
    [InlineData(1, 1, false)]
    public void IsSupportedSpriteTypePair_RequiresMatchingSupportedTypes(
        int pidTypeIdentifier,
        int artType,
        bool expected)
    {
        Assert.Equal(
            expected,
            MapIsometricObjectMarkerFilter.IsSupportedSpriteTypePair(
                pidTypeIdentifier,
                artType));
    }

    [Fallout2MapArchiveDiagnosticTests.Fallout2MasterDatFact]
    public async Task GetSpriteCandidates_IncludesKnownWallFromArbridgeMapAndSelectableFrmFrame()
    {
        var archivePath = Environment.GetEnvironmentVariable("FALLOUT2_MASTER_DAT");
        Assert.False(string.IsNullOrWhiteSpace(archivePath));
        var archive = Dat2Archive.Open(archivePath!);
        var mapEntry = Assert.Single(archive.BrowserEntries, entry =>
            string.Equals(
                entry.Path.Replace('\\', '/'),
                "maps/arbridge.map",
                StringComparison.OrdinalIgnoreCase));

        using var mapStream = new MemoryStream();
        await archive.CopyEntryToAsync(mapEntry, mapStream);
        var mapBytes = mapStream.ToArray();
        var header = MapHeaderDecoder.Decode(mapBytes, FalloutGame.Fallout2);
        var decodedMap = await MapObjectPlacementDecoder.DecodeAsync(mapBytes, header, archive);
        var wall = Assert.Single(decodedMap.Objects, placement => placement.RecordIndex == 40);

        Assert.Equal(0x030003A1, wall.RawPid);
        Assert.Equal(0x030003CA, wall.RawFid);
        var candidates = MapIsometricObjectMarkerFilter.GetSpriteCandidates(
            [wall],
            wall.ElevationGroup,
            CreateProjection());
        Assert.Contains(candidates, placement => placement.RecordIndex == 40);

        var resolution = await FalloutFidArtAssetResolver.ResolveAsync(archive, wall.RawFid);
        Assert.True(resolution.Success, resolution.Error);
        Assert.Equal("art/walls/ybk1005.frm", resolution.ResourcePath);
        Assert.NotNull(resolution.Entry);

        using var frmStream = new MemoryStream();
        await archive.CopyEntryToAsync(resolution.Entry!, frmStream);
        var document = FrmDecoder.Decode(frmStream.ToArray());
        Assert.True(
            MapIsometricObjectSpriteFrameSelector.TrySelectFrame(
                wall,
                document,
                out var directionIndex,
                out var frame,
                out var frameError),
            frameError);
        Assert.Equal(0, directionIndex);
        Assert.NotNull(frame);
        Assert.Equal(16, frame.Width);
        Assert.Equal(96, frame.Height);
        Assert.True(MapIsometricObjectSpriteDiagnostics.CountNonZeroPaletteIndexPixels(frame) > 0);
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
                new Point(320, 240),
                640,
                480));
        Assert.Null(MapIsometricObjectMarkerHitTest.GetRecordIndexAtPosition(
            markers,
            camera,
            new Point(308, 238),
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
            new Point(320, 240),
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
            new Point(320, 240),
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
                new Point(320, 240),
                640,
                480));
        Assert.Null(MapIsometricObjectMarkerHitTest.GetPrototypeFileNameAtPosition(
            markers,
            camera,
            new Point(328, 240),
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
                new Point(740, 450),
                800,
                600));
        Assert.Null(MapIsometricObjectMarkerHitTest.GetRecordIndexAtPosition(
            markers,
            camera,
            new Point(715, 446),
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
                new MapIsometricFloorCameraTransform(1, -322, 0),
                new Point(0, 240),
                640,
                480));
        Assert.Null(MapIsometricObjectMarkerHitTest.GetRecordIndexAtPosition(
            markers,
            new MapIsometricFloorCameraTransform(1, -328, 0),
            new Point(0, 240),
            640,
            480));

        var filteredOutMarkers = MapIsometricObjectMarkerFilter.GetMarkers(
            placements.Skip(1).ToArray(),
            0,
            CreateProjection());
        Assert.Null(MapIsometricObjectMarkerHitTest.GetRecordIndexAtPosition(
            filteredOutMarkers,
            new MapIsometricFloorCameraTransform(1, 0, 0),
            new Point(304, 238),
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
