using FalloutAssetExplorer.Core;
using FalloutAssetExplorer.App.ViewModels;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class MapGridInspectionViewModelTests
{
    [Fact]
    public void HoveringAnotherCellDoesNotChangePersistentSelection()
    {
        using var viewModel = new ArchiveBrowserViewModel
        {
            SelectedMapGridIndex = 100
        };
        var selectedDisplay = viewModel.MapGridCellDisplay;

        viewModel.InspectMapGridCell(0, 1, 199, 0xFEDCBA98);

        Assert.Equal(100, viewModel.SelectedMapGridIndex);
        Assert.Equal(selectedDisplay, viewModel.MapGridCellDisplay);
        Assert.Contains("Serialized index 199", viewModel.MapGridHoverDisplay);
        Assert.Contains("0xFEDCBA98", viewModel.MapGridHoverDisplay);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10000)]
    public void Selection_RejectsIndicesOutsideFixedGrid(int index)
    {
        using var viewModel = new ArchiveBrowserViewModel();

        viewModel.SelectedMapGridIndex = index;

        Assert.Null(viewModel.SelectedMapGridIndex);
    }

    [Fact]
    public void SelectingMapObject_SwitchesToItsElevationAndHexDiagnosticLayer()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        viewModel.MapInspector.SetAvailableElevations([0, 1], preferredElevationIndex: 0);
        var placement = new MapObjectPlacement(
            4,
            128,
            1,
            0,
            123,
            76,
            9,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            1,
            0x01000001,
            1,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            [],
            null,
            null);

        viewModel.MapInspector.SelectedMapObject = placement;

        Assert.Equal(1, viewModel.MapInspector.SelectedMapElevationIndex);
        Assert.Equal(1, viewModel.MapInspector.SelectedMapDiagnosticLayerIndex);
        Assert.Equal(placement.Details, viewModel.MapInspector.MapObjectDetailsDisplay);
    }

    [Fact]
    public void PrototypeDiagnostics_FollowSelectionChangesAndClearWhenDeselected()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        var item = CreateMarkerPlacement(1, elevation: 0) with
        {
            RawPid = 0x00000023,
            PidTypeIdentifier = 0
        };
        var scenery = CreateMarkerPlacement(2, elevation: 0) with
        {
            RawPid = 0x02000045,
            PidTypeIdentifier = 2
        };

        viewModel.MapInspector.SelectedMapObject = item;

        Assert.Contains("0x00000023", viewModel.MapObjectPrototypeDiagnosticDetails);
        Assert.Equal(
            "Prototype diagnostics require an open Fallout 2 DAT2 archive.",
            viewModel.MapObjectPrototypeDiagnosticStatus);

        viewModel.MapInspector.SelectedMapObject = scenery;

        Assert.Contains("0x02000045", viewModel.MapObjectPrototypeDiagnosticDetails);
        Assert.DoesNotContain("0x00000023", viewModel.MapObjectPrototypeDiagnosticDetails);
        Assert.Empty(viewModel.MapObjectPrototypeRawByteLines);

        viewModel.MapInspector.SelectedMapObject = null;

        Assert.Equal(
            "Select an Item or Scenery object to inspect its Fallout 2 prototype.",
            viewModel.MapObjectPrototypeDiagnosticStatus);
        Assert.Empty(viewModel.MapObjectPrototypeDiagnosticDetails);
        Assert.Empty(viewModel.MapObjectPrototypeRawByteLines);
    }

    [Fact]
    public void ClearingMapPlacements_ClearsChildSelectionAndParentPrototypeDiagnostic()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        var item = CreateMarkerPlacement(1, elevation: 0) with
        {
            RawPid = 0x00000023,
            PidTypeIdentifier = 0
        };
        viewModel.MapObjectPlacements.Add(item);
        viewModel.MapInspector.SelectedMapObject = item;

        Assert.Equal(
            "Prototype diagnostics require an open Fallout 2 DAT2 archive.",
            viewModel.MapObjectPrototypeDiagnosticStatus);

        viewModel.MapObjectPlacements.Clear();

        Assert.Null(viewModel.MapInspector.SelectedMapObject);
        Assert.Empty(viewModel.MapInspector.SortedMapObjectPlacements);
        Assert.Equal("Select a parsed object to inspect its raw fields.", viewModel.MapInspector.MapObjectDetailsDisplay);
        Assert.Equal(
            "Select an Item or Scenery object to inspect its Fallout 2 prototype.",
            viewModel.MapObjectPrototypeDiagnosticStatus);
    }

    [Fact]
    public void FloorTileLayer_IsAddedWithoutChangingExistingDiagnosticLayerOrder()
    {
        using var viewModel = new ArchiveBrowserViewModel();

        Assert.Equal(
            [
                "Square tiles 100 x 100",
                "Object hexes 200 x 200",
                "Floor tiles 100 x 100",
                "Floor tiles isometric (experimental)",
                "Object markers isometric"
            ],
            viewModel.MapInspector.MapDiagnosticLayerOptions);

        viewModel.MapInspector.SelectedMapDiagnosticLayerIndex = 2;

        Assert.Equal(2, viewModel.MapInspector.SelectedMapDiagnosticLayerIndex);
        viewModel.MapInspector.SelectedMapDiagnosticLayerIndex = 3;
        Assert.Equal(3, viewModel.MapInspector.SelectedMapDiagnosticLayerIndex);
        viewModel.MapInspector.SelectedMapDiagnosticLayerIndex = 4;
        Assert.Equal(4, viewModel.MapInspector.SelectedMapDiagnosticLayerIndex);
    }

    [Fact]
    public void MapElevationOptions_AreDistinctAndUsePreferredOrFirstAvailableElevation()
    {
        var viewModel = new MapInspectorViewModel();

        viewModel.SetAvailableElevations([2, 1, 2, 0], preferredElevationIndex: 1);

        Assert.Equal([0, 1, 2], viewModel.MapElevationOptions);
        Assert.Equal(1, viewModel.SelectedMapElevationIndex);

        viewModel.SetAvailableElevations([3, 1, 3], preferredElevationIndex: 8);

        Assert.Equal([1, 3], viewModel.MapElevationOptions);
        Assert.Equal(1, viewModel.SelectedMapElevationIndex);

        viewModel.SetAvailableElevations([3, 1, 3], preferredElevationIndex: null);

        Assert.Equal([1, 3], viewModel.MapElevationOptions);
        Assert.Equal(1, viewModel.SelectedMapElevationIndex);

        viewModel.SetAvailableElevations([], preferredElevationIndex: 0);

        Assert.Empty(viewModel.MapElevationOptions);
        Assert.Null(viewModel.SelectedMapElevationIndex);
    }

    [Fact]
    public void MapSelections_RejectIndicesOutsideAvailableOptions()
    {
        var viewModel = new MapInspectorViewModel();
        viewModel.SetAvailableElevations([0, 2], preferredElevationIndex: 2);

        viewModel.SelectedMapElevationIndex = 1;
        viewModel.SelectedMapDiagnosticLayerIndex = -1;
        viewModel.SelectedMapDiagnosticLayerIndex = viewModel.MapDiagnosticLayerOptions.Count;

        Assert.Equal(2, viewModel.SelectedMapElevationIndex);
        Assert.Equal(MapInspectorViewModel.SquareTilesLayerIndex, viewModel.SelectedMapDiagnosticLayerIndex);

        viewModel.SelectedMapDiagnosticLayerIndex = MapInspectorViewModel.IsometricObjectMarkersLayerIndex;

        Assert.Equal(MapInspectorViewModel.IsometricObjectMarkersLayerIndex, viewModel.SelectedMapDiagnosticLayerIndex);
    }

    [Fact]
    public void SelectedElevation_NotifiesElevationDependentProperties()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        viewModel.MapInspector.SetAvailableElevations([0, 1], preferredElevationIndex: 0);
        var mapHeaderData = new byte[
            MapHeaderDecoder.HeaderSize + MapHeaderDecoder.BytesPerElevation];
        mapHeaderData[3] = 19;
        mapHeaderData[0x2B] = 0x0C;
        var gridData = new MapTileGridData(
            MapHeaderDecoder.Decode(mapHeaderData, FalloutGame.Fallout1),
            [new MapTileGrid(0, new uint[MapTileGridDecoder.ValuesPerGrid])]);
        var gridDataField = typeof(ArchiveBrowserViewModel).GetField(
            "_mapTileGridData",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(gridDataField);
        gridDataField.SetValue(viewModel, gridData);
        viewModel.MapInspector.SetMapInspectorActive(true);
        viewModel.MapInspector.SelectedMapDiagnosticLayerIndex =
            MapInspectorViewModel.SquareTilesLayerIndex;

        Assert.Equal(0, viewModel.MapInspector.SelectedMapElevationIndex);
        Assert.True(viewModel.IsMapSquareGridVisible);

        var changedProperties = new HashSet<string?>();
        var inspectorChangedProperties = new HashSet<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);
        viewModel.MapInspector.PropertyChanged += (_, args) =>
            inspectorChangedProperties.Add(args.PropertyName);

        viewModel.MapInspector.SelectedMapElevationIndex = 1;

        Assert.False(viewModel.IsMapSquareGridVisible);
        Assert.Contains(nameof(viewModel.MapInspector.IsMapHexGridEmpty), inspectorChangedProperties);
        Assert.Contains(nameof(viewModel.SelectedMapGridValues), changedProperties);
        Assert.Contains(nameof(viewModel.HasMapGrid), changedProperties);
        Assert.Contains(nameof(viewModel.IsMapGridEmpty), changedProperties);
        Assert.Contains(nameof(viewModel.IsMapSquareGridVisible), changedProperties);
        Assert.Contains(nameof(viewModel.IsMapFloorTileLayerVisible), changedProperties);
        Assert.Contains(nameof(viewModel.IsMapFloorTileLayerEmpty), changedProperties);
        Assert.Contains(nameof(viewModel.IsMapIsometricFloorLayerVisible), changedProperties);
        Assert.Contains(nameof(viewModel.IsMapIsometricFloorLayerEmpty), changedProperties);
        Assert.Contains(nameof(viewModel.IsMapIsometricObjectMarkerLayerVisible), changedProperties);
        Assert.Contains(nameof(viewModel.IsMapIsometricViewVisible), changedProperties);
    }

    [Fact]
    public void MapInspectorHexGridEmpty_UsesLayerElevationAndValidCoordinates()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        var inspector = viewModel.MapInspector;
        inspector.SetAvailableElevations([0, 1, 2], preferredElevationIndex: 0);
        inspector.SelectedMapDiagnosticLayerIndex = MapInspectorViewModel.ObjectHexesLayerIndex;
        Assert.False(inspector.IsMapHexGridEmpty);
        inspector.SetMapInspectorActive(true);
        Assert.True(inspector.IsMapHexGridEmpty);

        var otherElevation = CreateMarkerPlacement(1, elevation: 1);
        var outOfRangeCoordinates = CreateMarkerPlacement(2, elevation: 0) with
        {
            HexX = MapObjectPlacementDecoder.HexGridWidth
        };
        var missingCoordinate = CreateMarkerPlacement(3, elevation: 0) with
        {
            HexX = null
        };
        var outOfRangeY = CreateMarkerPlacement(4, elevation: 0) with
        {
            HexY = MapObjectPlacementDecoder.HexGridHeight
        };
        var missingY = CreateMarkerPlacement(5, elevation: 0) with
        {
            HexY = null
        };
        inspector.SetMapObjectPlacements(
            [otherElevation, outOfRangeCoordinates, missingCoordinate, outOfRangeY, missingY]);

        Assert.True(inspector.IsMapHexGridEmpty);

        inspector.SetMapObjectPlacements(
            [
                otherElevation,
                outOfRangeCoordinates,
                missingCoordinate,
                outOfRangeY,
                missingY,
                CreateMarkerPlacement(6, elevation: 0)
            ]);
        Assert.False(inspector.IsMapHexGridEmpty);

        inspector.SelectedMapElevationIndex = 2;
        Assert.True(inspector.IsMapHexGridEmpty);

        inspector.SelectedMapElevationIndex = 1;
        Assert.False(inspector.IsMapHexGridEmpty);

        inspector.SelectedMapDiagnosticLayerIndex = MapInspectorViewModel.SquareTilesLayerIndex;
        Assert.False(inspector.IsMapHexGridEmpty);
    }

    [Fact]
    public void MapInspectorHexGridEmpty_NotifiesWhenModeLayerElevationOrPlacementsChange()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        var inspector = viewModel.MapInspector;
        inspector.SetAvailableElevations([0, 1], preferredElevationIndex: 0);
        var changedProperties = new HashSet<string?>();
        inspector.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        inspector.SelectedMapDiagnosticLayerIndex = MapInspectorViewModel.ObjectHexesLayerIndex;
        Assert.Contains(nameof(inspector.IsMapHexGridEmpty), changedProperties);
        Assert.False(inspector.IsMapHexGridEmpty);

        changedProperties.Clear();
        inspector.SetMapInspectorActive(true);
        Assert.Contains(nameof(inspector.IsMapHexGridEmpty), changedProperties);
        Assert.True(inspector.IsMapHexGridEmpty);

        changedProperties.Clear();
        inspector.SelectedMapElevationIndex = 1;
        Assert.Contains(nameof(inspector.IsMapHexGridEmpty), changedProperties);

        changedProperties.Clear();
        inspector.SetMapObjectPlacements([CreateMarkerPlacement(1, elevation: 1)]);
        Assert.Contains(nameof(inspector.IsMapHexGridEmpty), changedProperties);
        Assert.False(inspector.IsMapHexGridEmpty);

        changedProperties.Clear();
        inspector.SetMapObjectPlacements([]);
        Assert.Contains(nameof(inspector.IsMapHexGridEmpty), changedProperties);
        Assert.True(inspector.IsMapHexGridEmpty);
    }

    [Fact]
    public void MapInspectorHexGridEmpty_TracksParentPlacementCollectionChanges()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        var inspector = viewModel.MapInspector;
        inspector.SetAvailableElevations([0], preferredElevationIndex: 0);
        inspector.SetMapInspectorActive(true);
        inspector.SelectedMapDiagnosticLayerIndex = MapInspectorViewModel.ObjectHexesLayerIndex;
        var changedProperties = new HashSet<string?>();
        inspector.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);
        var placement = CreateMarkerPlacement(1, elevation: 0);

        Assert.True(inspector.IsMapHexGridEmpty);

        viewModel.MapObjectPlacements.Add(placement);

        Assert.False(inspector.IsMapHexGridEmpty);
        Assert.Contains(nameof(inspector.IsMapHexGridEmpty), changedProperties);

        changedProperties.Clear();
        viewModel.MapObjectPlacements.Clear();

        Assert.True(inspector.IsMapHexGridEmpty);
        Assert.Contains(nameof(inspector.IsMapHexGridEmpty), changedProperties);
    }

    [Fact]
    public void ResettingMapState_ClearsMapOptionsAndSelectionButPreservesListPreferences()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        var placement = CreateMarkerPlacement(1, elevation: 0) with
        {
            PidTypeIdentifier = 2,
            PrototypeFileName = "proto/scenery/tree.pro",
            RawPid = 0x02000023
        };
        viewModel.MapObjectPlacements.Add(placement);
        viewModel.MapInspector.SetAvailableElevations([0, 1], preferredElevationIndex: 0);
        viewModel.MapInspector.SelectedMapDiagnosticLayerIndex =
            MapInspectorViewModel.FloorTilesLayerIndex;
        viewModel.MapInspector.SelectedMapObject = placement;
        viewModel.MapInspector.MapObjectSearchText = "tree";
        viewModel.MapInspector.SelectedMapObjectTypeFilter = viewModel.MapInspector.MapObjectTypeFilterOptions
            .Single(option => option.PidTypeIdentifier == 2);
        viewModel.MapInspector.SelectedMapObjectListSort = GetMapObjectSortOption(
            viewModel,
            MapInspectorViewModel.MapObjectListSortMode.ByteOffsetDescending);

        viewModel.MapObjectPlacements.Clear();
        viewModel.MapInspector.SetMapObjectPlacements(viewModel.MapObjectPlacements);
        viewModel.MapInspector.ResetMapState();

        Assert.Empty(viewModel.MapInspector.MapElevationOptions);
        Assert.Null(viewModel.MapInspector.SelectedMapElevationIndex);
        Assert.Equal(
            MapInspectorViewModel.SquareTilesLayerIndex,
            viewModel.MapInspector.SelectedMapDiagnosticLayerIndex);
        Assert.Null(viewModel.MapInspector.SelectedMapObject);
        Assert.Equal(
            "Select an Item or Scenery object to inspect its Fallout 2 prototype.",
            viewModel.MapObjectPrototypeDiagnosticStatus);
        Assert.Equal("tree", viewModel.MapInspector.MapObjectSearchText);
        Assert.Equal(2, viewModel.MapInspector.SelectedMapObjectTypeFilter.PidTypeIdentifier);
        Assert.Equal(
            MapInspectorViewModel.MapObjectListSortMode.ByteOffsetDescending,
            viewModel.MapInspector.SelectedMapObjectListSort.SortMode);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    public async Task PrepareMapFloorTiles_IsGatedBySelectedLayer(
        int layerIndex,
        bool shouldRequestTiles)
    {
        using var viewModel = new ArchiveBrowserViewModel();
        viewModel.MapInspector.SetMapInspectorActive(true);
        viewModel.MapInspector.SelectedMapDiagnosticLayerIndex = layerIndex;

        await viewModel.PrepareMapFloorTilesAsync();

        Assert.Equal(
            shouldRequestTiles
                ? "No decoded MAP tile grids are available."
                : "Select the floor-tile diagnostic layer to load this map's tiles.",
            viewModel.MapFloorTilesStatusDisplay);
    }

    [Fact]
    public void SelectingFloorMarker_UpdatesObjectDetailsWithoutChangingLayerOrElevation()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        viewModel.MapInspector.SetAvailableElevations([0, 1], preferredElevationIndex: 0);
        viewModel.MapInspector.SelectedMapDiagnosticLayerIndex =
            MapInspectorViewModel.FloorTilesLayerIndex;
        var placement = new MapObjectPlacement(
            4,
            128,
            0,
            0,
            20_099,
            100,
            100,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0x01000001,
            1,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            [],
            null,
            null);

        viewModel.MapInspector.SelectMapObjectFromFloorMarker(placement);

        Assert.Equal(
            MapInspectorViewModel.FloorTilesLayerIndex,
            viewModel.MapInspector.SelectedMapDiagnosticLayerIndex);
        Assert.Equal(0, viewModel.MapInspector.SelectedMapElevationIndex);
        Assert.Same(placement, viewModel.MapInspector.SelectedMapObject);
        Assert.Equal(placement.Details, viewModel.MapInspector.MapObjectDetailsDisplay);
    }

    [Fact]
    public void IsometricMarkerSelection_ClearsWhenElevationChanges()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        viewModel.MapInspector.SetAvailableElevations([0, 1], preferredElevationIndex: 0);
        var placement = CreateMarkerPlacement(21, elevation: 0);
        viewModel.MapObjectPlacements.Add(placement);

        viewModel.MapInspector.SelectMapObjectFromIsometricMarker(
            placement.RecordIndex,
            viewModel.MapInspector.SelectedMapElevationIndex);

        Assert.Same(placement, viewModel.MapInspector.SelectedMapObject);
        Assert.Equal(placement.RecordIndex, viewModel.MapInspector.SelectedMapObjectRecordIndex);

        viewModel.MapInspector.SelectedMapElevationIndex = 1;

        Assert.Null(viewModel.MapInspector.SelectedMapObject);
        Assert.Null(viewModel.MapInspector.SelectedMapObjectRecordIndex);
    }

    [Fact]
    public void IsometricMarkerSelection_ClearsWhenRecordLeavesCurrentMap()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        viewModel.MapInspector.SetAvailableElevations([0], preferredElevationIndex: 0);
        var placement = CreateMarkerPlacement(21, elevation: 0);
        viewModel.MapObjectPlacements.Add(placement);
        viewModel.MapInspector.SelectMapObjectFromIsometricMarker(
            placement.RecordIndex,
            viewModel.MapInspector.SelectedMapElevationIndex);

        viewModel.MapObjectPlacements.Remove(placement);

        Assert.Null(viewModel.MapInspector.SelectedMapObject);
        Assert.Null(viewModel.MapInspector.SelectedMapObjectRecordIndex);
    }

    [Fact]
    public void IsometricMarkerSelection_ClearsWhenEmptySpaceIsClicked()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        viewModel.MapInspector.SetAvailableElevations([0], preferredElevationIndex: 0);
        var placement = CreateMarkerPlacement(21, elevation: 0);
        viewModel.MapObjectPlacements.Add(placement);
        viewModel.MapInspector.SelectMapObjectFromIsometricMarker(
            placement.RecordIndex,
            viewModel.MapInspector.SelectedMapElevationIndex);

        viewModel.MapInspector.SelectMapObjectFromIsometricMarker(
            null,
            viewModel.MapInspector.SelectedMapElevationIndex);

        Assert.Null(viewModel.MapInspector.SelectedMapObject);
        Assert.Null(viewModel.MapInspector.SelectedMapObjectRecordIndex);
    }

    [Fact]
    public void ObjectSearch_MatchesCaseInsensitivePartialPrototypeFileNames()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        var scenery = CreateMarkerPlacement(31, elevation: 0) with
        {
            PrototypeFileName = "proto/scenery/00000345.pro"
        };
        var item = CreateMarkerPlacement(32, elevation: 0) with
        {
            PrototypeFileName = "proto/items/example.pro"
        };
        viewModel.MapObjectPlacements.Add(scenery);
        viewModel.MapObjectPlacements.Add(item);

        viewModel.MapInspector.MapObjectSearchText = "SCENERY/00000345";

        Assert.Equal([scenery], viewModel.MapInspector.FilteredMapObjectPlacements);

        viewModel.MapInspector.MapObjectSearchText = "AMPLE.PR";

        Assert.Equal([item], viewModel.MapInspector.FilteredMapObjectPlacements);
    }

    [Fact]
    public void ObjectSearch_MatchesMetadataWhenPrototypeFileNameIsMissing()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        var named = CreateMarkerPlacement(31, elevation: 0) with
        {
            PrototypeFileName = "proto/items/example.pro"
        };
        var unnamed = CreateMarkerPlacement(32, elevation: 0) with
        {
            RawPid = 0x02000002,
            PidTypeIdentifier = 2
        };
        var emptyName = CreateMarkerPlacement(33, elevation: 0) with
        {
            PrototypeFileName = string.Empty
        };
        viewModel.MapObjectPlacements.Add(named);
        viewModel.MapObjectPlacements.Add(unnamed);
        viewModel.MapObjectPlacements.Add(emptyName);

        viewModel.MapInspector.MapObjectSearchText = "example";

        Assert.Equal([named], viewModel.MapInspector.FilteredMapObjectPlacements);

        viewModel.MapInspector.MapObjectSearchText = "sCeNeRy";

        Assert.Equal([unnamed], viewModel.MapInspector.FilteredMapObjectPlacements);
    }

    [Fact]
    public void ObjectSearch_MatchesCaseInsensitiveObjectTypeNames()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        var scenery = CreateMarkerPlacement(31, elevation: 0) with
        {
            PidTypeIdentifier = 2
        };
        viewModel.MapObjectPlacements.Add(scenery);

        viewModel.MapInspector.MapObjectSearchText = "sCeNeRy";

        Assert.Equal([scenery], viewModel.MapInspector.FilteredMapObjectPlacements);
    }

    [Fact]
    public void ObjectSearch_MatchesDecimalRecordIndex()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        var placement = CreateMarkerPlacement(287, elevation: 0);
        viewModel.MapObjectPlacements.Add(placement);

        viewModel.MapInspector.MapObjectSearchText = "287";

        Assert.Equal([placement], viewModel.MapInspector.FilteredMapObjectPlacements);
    }

    [Fact]
    public void ObjectSearch_MatchesPidAndFidAsPrefixedAndUnprefixedHex()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        var placement = CreateMarkerPlacement(31, elevation: 0) with
        {
            RawPid = unchecked((int)0x0200039A),
            RawFid = unchecked((int)0xA1B2C3D4)
        };
        viewModel.MapObjectPlacements.Add(placement);

        foreach (var query in new[] { "0x0200039A", "0200039A", "0xA1B2C3D4", "A1B2C3D4" })
        {
            viewModel.MapInspector.MapObjectSearchText = query;
            Assert.Equal([placement], viewModel.MapInspector.FilteredMapObjectPlacements);
        }
    }

    [Fact]
    public void ObjectSearch_MatchesHexIndexAndCoordinatePair()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        var placement = CreateMarkerPlacement(31, elevation: 0) with
        {
            RawHexIndex = 12_345,
            HexX = 114,
            HexY = 103
        };
        viewModel.MapObjectPlacements.Add(placement);

        viewModel.MapInspector.MapObjectSearchText = "12345";
        Assert.Equal([placement], viewModel.MapInspector.FilteredMapObjectPlacements);

        viewModel.MapInspector.MapObjectSearchText = "114, 103";
        Assert.Equal([placement], viewModel.MapInspector.FilteredMapObjectPlacements);

        viewModel.MapInspector.MapObjectSearchText = "114,103";
        Assert.Equal([placement], viewModel.MapInspector.FilteredMapObjectPlacements);
    }

    [Fact]
    public void ObjectSearch_MatchesElevationAndByteOffsetWithInvariantLabels()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        var placement = CreateMarkerPlacement(31, elevation: 0) with
        {
            RecordOffset = 12_345,
            StoredElevation = 1
        };
        viewModel.MapObjectPlacements.Add(placement);

        viewModel.MapInspector.MapObjectSearchText = "elevation 0";
        Assert.Equal([placement], viewModel.MapInspector.FilteredMapObjectPlacements);

        viewModel.MapInspector.MapObjectSearchText = "stored elevation 1";
        Assert.Equal([placement], viewModel.MapInspector.FilteredMapObjectPlacements);

        viewModel.MapInspector.MapObjectSearchText = "byte offset 12345";
        Assert.Equal([placement], viewModel.MapInspector.FilteredMapObjectPlacements);
    }

    [Fact]
    public void ObjectSearch_EmptyAndWhitespaceQueriesRestoreOriginalCollection()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        var placement = CreateMarkerPlacement(31, elevation: 0) with
        {
            PrototypeFileName = "proto/items/example.pro"
        };
        viewModel.MapObjectPlacements.Add(placement);

        viewModel.MapInspector.MapObjectSearchText = "missing";
        Assert.Empty(viewModel.MapInspector.FilteredMapObjectPlacements);

        viewModel.MapInspector.MapObjectSearchText = " \t ";

        Assert.Same(viewModel.MapObjectPlacements, viewModel.MapInspector.FilteredMapObjectPlacements);
        viewModel.MapInspector.MapObjectSearchText = "example";
        viewModel.MapInspector.MapObjectSearchText = string.Empty;
        Assert.Same(viewModel.MapObjectPlacements, viewModel.MapInspector.FilteredMapObjectPlacements);
    }

    [Fact]
    public void ObjectSearchFeedback_TracksTotalAndMatchingObjectCounts()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        var scenery = CreateMarkerPlacement(31, elevation: 0) with
        {
            PrototypeFileName = "proto/scenery/tree.pro"
        };
        var item = CreateMarkerPlacement(32, elevation: 0) with
        {
            PrototypeFileName = "proto/items/armor.pro"
        };
        viewModel.MapObjectPlacements.Add(scenery);
        viewModel.MapObjectPlacements.Add(item);

        Assert.Equal("2 / 2 objects", viewModel.MapInspector.MapObjectSearchResultCountDisplay);
        Assert.False(viewModel.MapInspector.HasNoMatchingMapObjects);

        viewModel.MapInspector.MapObjectSearchText = "scenery";

        Assert.Equal("1 / 2 objects", viewModel.MapInspector.MapObjectSearchResultCountDisplay);
        Assert.False(viewModel.MapInspector.HasNoMatchingMapObjects);

        viewModel.MapInspector.MapObjectSearchText = "no-match";

        Assert.Equal("0 / 2 objects", viewModel.MapInspector.MapObjectSearchResultCountDisplay);
        Assert.True(viewModel.MapInspector.HasNoMatchingMapObjects);
    }

    [Fact]
    public void ObjectSearchFeedback_DoesNotShowEmptyStateForEmptyUnfilteredMap()
    {
        using var viewModel = new ArchiveBrowserViewModel();

        Assert.Equal("0 / 0 objects", viewModel.MapInspector.MapObjectSearchResultCountDisplay);
        Assert.False(viewModel.MapInspector.HasNoMatchingMapObjects);
    }

    [Fact]
    public void FilteringAndSorting_DoNotChangeArchiveOrFloorAssetOperationState()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        viewModel.MapObjectPlacements.Add(CreateMarkerPlacement(1, elevation: 0) with
        {
            PidTypeIdentifier = 2,
            PrototypeFileName = "proto/scenery/tree.pro"
        });
        viewModel.MapObjectPlacements.Add(CreateMarkerPlacement(2, elevation: 0) with
        {
            PidTypeIdentifier = 0,
            PrototypeFileName = "proto/items/armor.pro"
        });
        var archivePath = viewModel.ArchivePathDisplay;
        var operationStatus = viewModel.StatusMessage;
        var floorStatus = viewModel.MapFloorTilesStatusDisplay;

        viewModel.MapInspector.MapObjectSearchText = "tree";
        viewModel.MapInspector.SelectedMapObjectTypeFilter = viewModel.MapInspector.MapObjectTypeFilterOptions
            .Single(option => option.PidTypeIdentifier == 2);
        viewModel.MapInspector.SelectedMapObjectListSort = GetMapObjectSortOption(
            viewModel,
            MapInspectorViewModel.MapObjectListSortMode.ByteOffsetDescending);

        Assert.Equal(archivePath, viewModel.ArchivePathDisplay);
        Assert.Equal(operationStatus, viewModel.StatusMessage);
        Assert.Equal(floorStatus, viewModel.MapFloorTilesStatusDisplay);
        Assert.Equal(
            MapInspectorViewModel.SquareTilesLayerIndex,
            viewModel.MapInspector.SelectedMapDiagnosticLayerIndex);
    }

    [Fact]
    public void ObjectTypeFilter_MatchesDecodedPidTypeAndAllTypesPreservesUnknownTypes()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        Assert.Equal(
            new int?[] { null, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 },
            viewModel.MapInspector.MapObjectTypeFilterOptions.Select(option => option.PidTypeIdentifier));

        var item = CreateMarkerPlacement(31, elevation: 0) with { PidTypeIdentifier = 0 };
        var scenery = CreateMarkerPlacement(32, elevation: 0) with { PidTypeIdentifier = 2 };
        var unknown = CreateMarkerPlacement(33, elevation: 0) with { PidTypeIdentifier = 42 };
        viewModel.MapObjectPlacements.Add(item);
        viewModel.MapObjectPlacements.Add(scenery);
        viewModel.MapObjectPlacements.Add(unknown);

        viewModel.MapInspector.SelectedMapObjectTypeFilter = viewModel.MapInspector.MapObjectTypeFilterOptions
            .Single(option => option.PidTypeIdentifier == 2);

        Assert.Equal([scenery], viewModel.MapInspector.FilteredMapObjectPlacements);

        viewModel.MapInspector.SelectedMapObjectTypeFilter = viewModel.MapInspector.MapObjectTypeFilterOptions[0];

        Assert.Equal([item, scenery, unknown], viewModel.MapInspector.FilteredMapObjectPlacements);
    }

    [Fact]
    public void ObjectTypeFilter_ComposesWithTextSearch()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        var matchingScenery = CreateMarkerPlacement(31, elevation: 0) with
        {
            PidTypeIdentifier = 2,
            PrototypeFileName = "proto/scenery/oak.pro"
        };
        var otherScenery = CreateMarkerPlacement(32, elevation: 0) with
        {
            PidTypeIdentifier = 2,
            PrototypeFileName = "proto/scenery/pine.pro"
        };
        var matchingItem = CreateMarkerPlacement(33, elevation: 0) with
        {
            PidTypeIdentifier = 0,
            PrototypeFileName = "proto/items/oak-charm.pro"
        };
        viewModel.MapObjectPlacements.Add(matchingScenery);
        viewModel.MapObjectPlacements.Add(otherScenery);
        viewModel.MapObjectPlacements.Add(matchingItem);

        viewModel.MapInspector.SelectedMapObjectTypeFilter = viewModel.MapInspector.MapObjectTypeFilterOptions
            .Single(option => option.PidTypeIdentifier == 2);
        viewModel.MapInspector.MapObjectSearchText = "oak";

        Assert.Equal([matchingScenery], viewModel.MapInspector.FilteredMapObjectPlacements);
    }

    [Fact]
    public void ObjectTypeFilter_ClearsHiddenSelectionAndAllTypesRestoresRecords()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        var item = CreateMarkerPlacement(31, elevation: 0) with { PidTypeIdentifier = 0 };
        var scenery = CreateMarkerPlacement(32, elevation: 0) with { PidTypeIdentifier = 2 };
        viewModel.MapObjectPlacements.Add(item);
        viewModel.MapObjectPlacements.Add(scenery);
        viewModel.MapInspector.SelectedMapObject = item;

        viewModel.MapInspector.SelectedMapObjectTypeFilter = viewModel.MapInspector.MapObjectTypeFilterOptions
            .Single(option => option.PidTypeIdentifier == 2);

        Assert.Null(viewModel.MapInspector.SelectedMapObject);
        Assert.Null(viewModel.MapInspector.SelectedMapObjectRecordIndex);
        Assert.Equal("Select a parsed object to inspect its raw fields.", viewModel.MapInspector.MapObjectDetailsDisplay);
        Assert.Equal([scenery], viewModel.MapInspector.FilteredMapObjectPlacements);

        viewModel.MapInspector.SelectedMapObject = scenery;
        Assert.Same(scenery, viewModel.MapInspector.SelectedMapObject);

        viewModel.MapInspector.SelectedMapObjectTypeFilter = viewModel.MapInspector.MapObjectTypeFilterOptions[0];

        Assert.Equal([item, scenery], viewModel.MapInspector.FilteredMapObjectPlacements);
    }

    [Fact]
    public void MapObjectListSort_UsesNumericPidValuesAndDoesNotReorderMarkerSource()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        var highPid = CreateMarkerPlacement(3, elevation: 0) with
        {
            RawPid = unchecked((int)0xFF000001)
        };
        var lowPid = CreateMarkerPlacement(2, elevation: 0) with
        {
            RawPid = 0x01000010
        };
        var middlePid = CreateMarkerPlacement(1, elevation: 0) with
        {
            RawPid = 0x02000001
        };
        viewModel.MapObjectPlacements.Add(highPid);
        viewModel.MapObjectPlacements.Add(lowPid);
        viewModel.MapObjectPlacements.Add(middlePid);

        viewModel.MapInspector.SelectedMapObjectListSort = GetMapObjectSortOption(
            viewModel,
            MapInspectorViewModel.MapObjectListSortMode.PidAscending);

        Assert.Equal([lowPid, middlePid, highPid], viewModel.MapInspector.SortedMapObjectPlacements);

        viewModel.MapInspector.SelectedMapObjectListSort = GetMapObjectSortOption(
            viewModel,
            MapInspectorViewModel.MapObjectListSortMode.PidDescending);
        Assert.Equal([highPid, middlePid, lowPid], viewModel.MapInspector.SortedMapObjectPlacements);

        Assert.Equal([highPid, lowPid, middlePid], viewModel.MapInspector.FilteredMapObjectPlacements);
        Assert.Same(viewModel.MapObjectPlacements, viewModel.MapInspector.FilteredMapObjectPlacements);
    }

    [Fact]
    public void MapObjectListSort_SortsRecordIndexBothWaysAndRestoresNaturalOrder()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        var third = CreateMarkerPlacement(3, elevation: 0);
        var first = CreateMarkerPlacement(1, elevation: 0);
        var second = CreateMarkerPlacement(2, elevation: 0);
        viewModel.MapObjectPlacements.Add(third);
        viewModel.MapObjectPlacements.Add(first);
        viewModel.MapObjectPlacements.Add(second);

        Assert.Equal([first, second, third], viewModel.MapInspector.SortedMapObjectPlacements);

        viewModel.MapInspector.SelectedMapObjectListSort = GetMapObjectSortOption(
            viewModel,
            MapInspectorViewModel.MapObjectListSortMode.RecordIndexDescending);
        Assert.Equal([third, second, first], viewModel.MapInspector.SortedMapObjectPlacements);

        viewModel.MapInspector.SelectedMapObjectListSort = GetMapObjectSortOption(
            viewModel,
            MapInspectorViewModel.MapObjectListSortMode.RecordIndexAscending);
        Assert.Equal([first, second, third], viewModel.MapInspector.SortedMapObjectPlacements);
    }

    [Fact]
    public void MapObjectListSort_OrdersCoordinatesByYThenXAndPlacesMissingLast()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        var y4x9 = CreateMarkerPlacement(4, elevation: 0) with { HexX = 9, HexY = 4 };
        var y3x9 = CreateMarkerPlacement(3, elevation: 0) with { HexX = 9, HexY = 3 };
        var y4x1 = CreateMarkerPlacement(2, elevation: 0) with { HexX = 1, HexY = 4 };
        var missing = CreateMarkerPlacement(1, elevation: 0) with { HexX = null, HexY = null };
        viewModel.MapObjectPlacements.Add(y4x9);
        viewModel.MapObjectPlacements.Add(y3x9);
        viewModel.MapObjectPlacements.Add(y4x1);
        viewModel.MapObjectPlacements.Add(missing);

        viewModel.MapInspector.SelectedMapObjectListSort = GetMapObjectSortOption(
            viewModel,
            MapInspectorViewModel.MapObjectListSortMode.HexCoordinatesAscending);
        Assert.Equal([y3x9, y4x1, y4x9, missing], viewModel.MapInspector.SortedMapObjectPlacements);

        viewModel.MapInspector.SelectedMapObjectListSort = GetMapObjectSortOption(
            viewModel,
            MapInspectorViewModel.MapObjectListSortMode.HexCoordinatesDescending);
        Assert.Equal([y4x9, y4x1, y3x9, missing], viewModel.MapInspector.SortedMapObjectPlacements);
    }

    [Fact]
    public void MapObjectListSort_AppliesAfterTypeAndTextFilters()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        var oakLate = CreateMarkerPlacement(31, elevation: 0) with
        {
            PidTypeIdentifier = 2,
            PrototypeFileName = "proto/scenery/oak.pro",
            RecordOffset = 300
        };
        var pine = CreateMarkerPlacement(32, elevation: 0) with
        {
            PidTypeIdentifier = 2,
            PrototypeFileName = "proto/scenery/pine.pro",
            RecordOffset = 50
        };
        var oakEarlyOtherType = CreateMarkerPlacement(33, elevation: 0) with
        {
            PidTypeIdentifier = 0,
            PrototypeFileName = "proto/items/oak-charm.pro",
            RecordOffset = 1
        };
        var oakEarly = CreateMarkerPlacement(34, elevation: 0) with
        {
            PidTypeIdentifier = 2,
            PrototypeFileName = "proto/scenery/oak-small.pro",
            RecordOffset = 100
        };
        viewModel.MapObjectPlacements.Add(oakLate);
        viewModel.MapObjectPlacements.Add(pine);
        viewModel.MapObjectPlacements.Add(oakEarlyOtherType);
        viewModel.MapObjectPlacements.Add(oakEarly);
        viewModel.MapInspector.SelectedMapObjectTypeFilter = viewModel.MapInspector.MapObjectTypeFilterOptions
            .Single(option => option.PidTypeIdentifier == 2);
        viewModel.MapInspector.MapObjectSearchText = "oak";

        viewModel.MapInspector.SelectedMapObjectListSort = GetMapObjectSortOption(
            viewModel,
            MapInspectorViewModel.MapObjectListSortMode.ByteOffsetAscending);

        Assert.Equal([oakLate, oakEarly], viewModel.MapInspector.FilteredMapObjectPlacements);
        Assert.Equal([oakEarly, oakLate], viewModel.MapInspector.SortedMapObjectPlacements);
    }

    [Fact]
    public void MapObjectListSort_PreservesSelectedPlacementIdentity()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        var selected = CreateMarkerPlacement(2, elevation: 0) with
        {
            RawPid = 0x01000010
        };
        var other = CreateMarkerPlacement(1, elevation: 0) with
        {
            RawPid = 0x02000010
        };
        viewModel.MapObjectPlacements.Add(selected);
        viewModel.MapObjectPlacements.Add(other);
        viewModel.MapInspector.SelectedMapObject = selected;
        var moveEvents = 0;
        viewModel.MapInspector.SortedMapObjectPlacements.CollectionChanged += (_, args) =>
        {
            if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Move)
            {
                moveEvents++;
                viewModel.MapInspector.SelectedMapObject = null;
            }
        };

        viewModel.MapInspector.SelectedMapObjectListSort = GetMapObjectSortOption(
            viewModel,
            MapInspectorViewModel.MapObjectListSortMode.PidAscending);

        Assert.True(moveEvents > 0);
        Assert.Same(selected, viewModel.MapInspector.SelectedMapObject);
        Assert.Equal(selected.RecordIndex, viewModel.MapInspector.SelectedMapObjectRecordIndex);
        Assert.Same(selected, viewModel.MapInspector.SortedMapObjectPlacements[0]);
        Assert.Same(other, viewModel.MapInspector.SortedMapObjectPlacements[1]);
        Assert.Equal(selected.Details, viewModel.MapInspector.MapObjectDetailsDisplay);
    }

    [Fact]
    public void MapObjectListSort_SortsObjectTypeNamesAndByteOffsetsNumerically()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        var wall = CreateMarkerPlacement(3, elevation: 0) with
        {
            PidTypeIdentifier = 3,
            RecordOffset = 10
        };
        var item = CreateMarkerPlacement(2, elevation: 0) with
        {
            PidTypeIdentifier = 0,
            RecordOffset = 2
        };
        var scenery = CreateMarkerPlacement(1, elevation: 0) with
        {
            PidTypeIdentifier = 2,
            RecordOffset = 1
        };
        viewModel.MapObjectPlacements.Add(wall);
        viewModel.MapObjectPlacements.Add(item);
        viewModel.MapObjectPlacements.Add(scenery);

        viewModel.MapInspector.SelectedMapObjectListSort = GetMapObjectSortOption(
            viewModel,
            MapInspectorViewModel.MapObjectListSortMode.ObjectTypeAscending);
        Assert.Equal([item, scenery, wall], viewModel.MapInspector.SortedMapObjectPlacements);

        viewModel.MapInspector.SelectedMapObjectListSort = GetMapObjectSortOption(
            viewModel,
            MapInspectorViewModel.MapObjectListSortMode.ByteOffsetAscending);
        Assert.Equal([scenery, item, wall], viewModel.MapInspector.SortedMapObjectPlacements);

        viewModel.MapInspector.SelectedMapObjectListSort = GetMapObjectSortOption(
            viewModel,
            MapInspectorViewModel.MapObjectListSortMode.ByteOffsetDescending);
        Assert.Equal([wall, item, scenery], viewModel.MapInspector.SortedMapObjectPlacements);
    }

    [Fact]
    public void ObjectSearch_ClearsHiddenSelectionAndSelectsVisibleOriginalPlacement()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        var selected = CreateMarkerPlacement(31, elevation: 0) with
        {
            PrototypeFileName = "proto/scenery/tree.pro"
        };
        var visible = CreateMarkerPlacement(32, elevation: 0) with
        {
            PrototypeFileName = "proto/items/armor.pro"
        };
        viewModel.MapObjectPlacements.Add(selected);
        viewModel.MapObjectPlacements.Add(visible);
        viewModel.MapInspector.SelectedMapObject = selected;

        viewModel.MapInspector.MapObjectSearchText = "armor";

        Assert.Null(viewModel.MapInspector.SelectedMapObject);
        Assert.Null(viewModel.MapInspector.SelectedMapObjectRecordIndex);
        Assert.Equal("Select a parsed object to inspect its raw fields.", viewModel.MapInspector.MapObjectDetailsDisplay);
        Assert.Equal([visible], viewModel.MapInspector.FilteredMapObjectPlacements);
        Assert.Equal(2, viewModel.MapObjectPlacements.Count);

        viewModel.MapInspector.SelectMapObjectFromFloorMarker(selected);

        Assert.Null(viewModel.MapInspector.SelectedMapObject);
        Assert.Equal("Select a parsed object to inspect its raw fields.", viewModel.MapInspector.MapObjectDetailsDisplay);

        viewModel.MapInspector.SelectedMapObject = visible;
        Assert.Same(visible, viewModel.MapInspector.SelectedMapObject);
        Assert.Equal(visible.RecordIndex, viewModel.MapInspector.SelectedMapObjectRecordIndex);

        viewModel.MapInspector.MapObjectSearchText = string.Empty;

        Assert.Same(viewModel.MapObjectPlacements, viewModel.MapInspector.FilteredMapObjectPlacements);
        Assert.Equal(2, viewModel.MapInspector.FilteredMapObjectPlacements.Count);
    }

    private static MapObjectPlacement CreateMarkerPlacement(int recordIndex, int elevation) =>
        new(
            RecordIndex: recordIndex,
            RecordOffset: 0,
            ElevationGroup: elevation,
            RawObjectId: 0,
            RawHexIndex: 20_099,
            HexX: 100,
            HexY: 100,
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
            RawPayloadWords: [],
            Warning: null,
            UnsupportedReason: null);

    private static MapInspectorViewModel.MapObjectListSortOption GetMapObjectSortOption(
        ArchiveBrowserViewModel viewModel,
        MapInspectorViewModel.MapObjectListSortMode sortMode) =>
        viewModel.MapInspector.MapObjectListSortOptions.Single(option => option.SortMode == sortMode);
}
