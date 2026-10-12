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
        viewModel.MapElevationOptions.Add(0);
        viewModel.MapElevationOptions.Add(1);
        viewModel.SelectedMapElevationIndex = 0;
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

        viewModel.SelectedMapObject = placement;

        Assert.Equal(1, viewModel.SelectedMapElevationIndex);
        Assert.Equal(1, viewModel.SelectedMapDiagnosticLayerIndex);
        Assert.Equal(placement.Details, viewModel.MapObjectDetailsDisplay);
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
            viewModel.MapDiagnosticLayerOptions);

        viewModel.SelectedMapDiagnosticLayerIndex = 2;

        Assert.Equal(2, viewModel.SelectedMapDiagnosticLayerIndex);
        viewModel.SelectedMapDiagnosticLayerIndex = 3;
        Assert.Equal(3, viewModel.SelectedMapDiagnosticLayerIndex);
        viewModel.SelectedMapDiagnosticLayerIndex = 4;
        Assert.Equal(4, viewModel.SelectedMapDiagnosticLayerIndex);
    }

    [Fact]
    public void SelectingFloorMarker_UpdatesObjectDetailsWithoutChangingLayerOrElevation()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        viewModel.MapElevationOptions.Add(0);
        viewModel.MapElevationOptions.Add(1);
        viewModel.SelectedMapElevationIndex = 0;
        viewModel.SelectedMapDiagnosticLayerIndex = 2;
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

        viewModel.SelectMapObjectFromFloorMarker(placement);

        Assert.Equal(2, viewModel.SelectedMapDiagnosticLayerIndex);
        Assert.Equal(0, viewModel.SelectedMapElevationIndex);
        Assert.Same(placement, viewModel.SelectedMapObject);
        Assert.Equal(placement.Details, viewModel.MapObjectDetailsDisplay);
    }

    [Fact]
    public void IsometricMarkerSelection_ClearsWhenElevationChanges()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        viewModel.MapElevationOptions.Add(0);
        viewModel.MapElevationOptions.Add(1);
        viewModel.SelectedMapElevationIndex = 0;
        var placement = CreateMarkerPlacement(21, elevation: 0);
        viewModel.MapObjectPlacements.Add(placement);

        viewModel.SelectMapObjectFromIsometricMarker(placement.RecordIndex);

        Assert.Same(placement, viewModel.SelectedMapObject);
        Assert.Equal(placement.RecordIndex, viewModel.SelectedMapObjectRecordIndex);

        viewModel.SelectedMapElevationIndex = 1;

        Assert.Null(viewModel.SelectedMapObject);
        Assert.Null(viewModel.SelectedMapObjectRecordIndex);
    }

    [Fact]
    public void IsometricMarkerSelection_ClearsWhenRecordLeavesCurrentMap()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        viewModel.MapElevationOptions.Add(0);
        viewModel.SelectedMapElevationIndex = 0;
        var placement = CreateMarkerPlacement(21, elevation: 0);
        viewModel.MapObjectPlacements.Add(placement);
        viewModel.SelectMapObjectFromIsometricMarker(placement.RecordIndex);

        viewModel.MapObjectPlacements.Remove(placement);

        Assert.Null(viewModel.SelectedMapObject);
        Assert.Null(viewModel.SelectedMapObjectRecordIndex);
    }

    [Fact]
    public void IsometricMarkerSelection_ClearsWhenEmptySpaceIsClicked()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        viewModel.MapElevationOptions.Add(0);
        viewModel.SelectedMapElevationIndex = 0;
        var placement = CreateMarkerPlacement(21, elevation: 0);
        viewModel.MapObjectPlacements.Add(placement);
        viewModel.SelectMapObjectFromIsometricMarker(placement.RecordIndex);

        viewModel.SelectMapObjectFromIsometricMarker(null);

        Assert.Null(viewModel.SelectedMapObject);
        Assert.Null(viewModel.SelectedMapObjectRecordIndex);
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

        viewModel.MapObjectSearchText = "SCENERY/00000345";

        Assert.Equal([scenery], viewModel.FilteredMapObjectPlacements);

        viewModel.MapObjectSearchText = "AMPLE.PR";

        Assert.Equal([item], viewModel.FilteredMapObjectPlacements);
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

        viewModel.MapObjectSearchText = "example";

        Assert.Equal([named], viewModel.FilteredMapObjectPlacements);

        viewModel.MapObjectSearchText = "sCeNeRy";

        Assert.Equal([unnamed], viewModel.FilteredMapObjectPlacements);
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

        viewModel.MapObjectSearchText = "sCeNeRy";

        Assert.Equal([scenery], viewModel.FilteredMapObjectPlacements);
    }

    [Fact]
    public void ObjectSearch_MatchesDecimalRecordIndex()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        var placement = CreateMarkerPlacement(287, elevation: 0);
        viewModel.MapObjectPlacements.Add(placement);

        viewModel.MapObjectSearchText = "287";

        Assert.Equal([placement], viewModel.FilteredMapObjectPlacements);
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
            viewModel.MapObjectSearchText = query;
            Assert.Equal([placement], viewModel.FilteredMapObjectPlacements);
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

        viewModel.MapObjectSearchText = "12345";
        Assert.Equal([placement], viewModel.FilteredMapObjectPlacements);

        viewModel.MapObjectSearchText = "114, 103";
        Assert.Equal([placement], viewModel.FilteredMapObjectPlacements);

        viewModel.MapObjectSearchText = "114,103";
        Assert.Equal([placement], viewModel.FilteredMapObjectPlacements);
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

        viewModel.MapObjectSearchText = "elevation 0";
        Assert.Equal([placement], viewModel.FilteredMapObjectPlacements);

        viewModel.MapObjectSearchText = "stored elevation 1";
        Assert.Equal([placement], viewModel.FilteredMapObjectPlacements);

        viewModel.MapObjectSearchText = "byte offset 12345";
        Assert.Equal([placement], viewModel.FilteredMapObjectPlacements);
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

        viewModel.MapObjectSearchText = "missing";
        Assert.Empty(viewModel.FilteredMapObjectPlacements);

        viewModel.MapObjectSearchText = " \t ";

        Assert.Same(viewModel.MapObjectPlacements, viewModel.FilteredMapObjectPlacements);
        viewModel.MapObjectSearchText = "example";
        viewModel.MapObjectSearchText = string.Empty;
        Assert.Same(viewModel.MapObjectPlacements, viewModel.FilteredMapObjectPlacements);
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

        Assert.Equal("2 / 2 objects", viewModel.MapObjectSearchResultCountDisplay);
        Assert.False(viewModel.HasNoMatchingMapObjects);

        viewModel.MapObjectSearchText = "scenery";

        Assert.Equal("1 / 2 objects", viewModel.MapObjectSearchResultCountDisplay);
        Assert.False(viewModel.HasNoMatchingMapObjects);

        viewModel.MapObjectSearchText = "no-match";

        Assert.Equal("0 / 2 objects", viewModel.MapObjectSearchResultCountDisplay);
        Assert.True(viewModel.HasNoMatchingMapObjects);
    }

    [Fact]
    public void ObjectSearchFeedback_DoesNotShowEmptyStateForEmptyUnfilteredMap()
    {
        using var viewModel = new ArchiveBrowserViewModel();

        Assert.Equal("0 / 0 objects", viewModel.MapObjectSearchResultCountDisplay);
        Assert.False(viewModel.HasNoMatchingMapObjects);
    }

    [Fact]
    public void ObjectTypeFilter_MatchesDecodedPidTypeAndAllTypesPreservesUnknownTypes()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        Assert.Equal(
            new int?[] { null, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 },
            viewModel.MapObjectTypeFilterOptions.Select(option => option.PidTypeIdentifier));

        var item = CreateMarkerPlacement(31, elevation: 0) with { PidTypeIdentifier = 0 };
        var scenery = CreateMarkerPlacement(32, elevation: 0) with { PidTypeIdentifier = 2 };
        var unknown = CreateMarkerPlacement(33, elevation: 0) with { PidTypeIdentifier = 42 };
        viewModel.MapObjectPlacements.Add(item);
        viewModel.MapObjectPlacements.Add(scenery);
        viewModel.MapObjectPlacements.Add(unknown);

        viewModel.SelectedMapObjectTypeFilter = viewModel.MapObjectTypeFilterOptions
            .Single(option => option.PidTypeIdentifier == 2);

        Assert.Equal([scenery], viewModel.FilteredMapObjectPlacements);

        viewModel.SelectedMapObjectTypeFilter = viewModel.MapObjectTypeFilterOptions[0];

        Assert.Equal([item, scenery, unknown], viewModel.FilteredMapObjectPlacements);
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

        viewModel.SelectedMapObjectTypeFilter = viewModel.MapObjectTypeFilterOptions
            .Single(option => option.PidTypeIdentifier == 2);
        viewModel.MapObjectSearchText = "oak";

        Assert.Equal([matchingScenery], viewModel.FilteredMapObjectPlacements);
    }

    [Fact]
    public void ObjectTypeFilter_ClearsHiddenSelectionAndAllTypesRestoresRecords()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        var item = CreateMarkerPlacement(31, elevation: 0) with { PidTypeIdentifier = 0 };
        var scenery = CreateMarkerPlacement(32, elevation: 0) with { PidTypeIdentifier = 2 };
        viewModel.MapObjectPlacements.Add(item);
        viewModel.MapObjectPlacements.Add(scenery);
        viewModel.SelectedMapObject = item;

        viewModel.SelectedMapObjectTypeFilter = viewModel.MapObjectTypeFilterOptions
            .Single(option => option.PidTypeIdentifier == 2);

        Assert.Null(viewModel.SelectedMapObject);
        Assert.Null(viewModel.SelectedMapObjectRecordIndex);
        Assert.Equal("Select a parsed object to inspect its raw fields.", viewModel.MapObjectDetailsDisplay);
        Assert.Equal([scenery], viewModel.FilteredMapObjectPlacements);

        viewModel.SelectedMapObject = scenery;
        Assert.Same(scenery, viewModel.SelectedMapObject);

        viewModel.SelectedMapObjectTypeFilter = viewModel.MapObjectTypeFilterOptions[0];

        Assert.Equal([item, scenery], viewModel.FilteredMapObjectPlacements);
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

        viewModel.SelectedMapObjectListSort = GetMapObjectSortOption(
            viewModel,
            ArchiveBrowserViewModel.MapObjectListSortMode.PidAscending);

        Assert.Equal([lowPid, middlePid, highPid], viewModel.SortedMapObjectPlacements);

        viewModel.SelectedMapObjectListSort = GetMapObjectSortOption(
            viewModel,
            ArchiveBrowserViewModel.MapObjectListSortMode.PidDescending);
        Assert.Equal([highPid, middlePid, lowPid], viewModel.SortedMapObjectPlacements);

        Assert.Equal([highPid, lowPid, middlePid], viewModel.FilteredMapObjectPlacements);
        Assert.Same(viewModel.MapObjectPlacements, viewModel.FilteredMapObjectPlacements);
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

        Assert.Equal([first, second, third], viewModel.SortedMapObjectPlacements);

        viewModel.SelectedMapObjectListSort = GetMapObjectSortOption(
            viewModel,
            ArchiveBrowserViewModel.MapObjectListSortMode.RecordIndexDescending);
        Assert.Equal([third, second, first], viewModel.SortedMapObjectPlacements);

        viewModel.SelectedMapObjectListSort = GetMapObjectSortOption(
            viewModel,
            ArchiveBrowserViewModel.MapObjectListSortMode.RecordIndexAscending);
        Assert.Equal([first, second, third], viewModel.SortedMapObjectPlacements);
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

        viewModel.SelectedMapObjectListSort = GetMapObjectSortOption(
            viewModel,
            ArchiveBrowserViewModel.MapObjectListSortMode.HexCoordinatesAscending);
        Assert.Equal([y3x9, y4x1, y4x9, missing], viewModel.SortedMapObjectPlacements);

        viewModel.SelectedMapObjectListSort = GetMapObjectSortOption(
            viewModel,
            ArchiveBrowserViewModel.MapObjectListSortMode.HexCoordinatesDescending);
        Assert.Equal([y4x9, y4x1, y3x9, missing], viewModel.SortedMapObjectPlacements);
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
        viewModel.SelectedMapObjectTypeFilter = viewModel.MapObjectTypeFilterOptions
            .Single(option => option.PidTypeIdentifier == 2);
        viewModel.MapObjectSearchText = "oak";

        viewModel.SelectedMapObjectListSort = GetMapObjectSortOption(
            viewModel,
            ArchiveBrowserViewModel.MapObjectListSortMode.ByteOffsetAscending);

        Assert.Equal([oakLate, oakEarly], viewModel.FilteredMapObjectPlacements);
        Assert.Equal([oakEarly, oakLate], viewModel.SortedMapObjectPlacements);
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
        viewModel.SelectedMapObject = selected;
        var moveEvents = 0;
        viewModel.SortedMapObjectPlacements.CollectionChanged += (_, args) =>
        {
            if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Move)
            {
                moveEvents++;
                viewModel.SelectedMapObject = null;
            }
        };

        viewModel.SelectedMapObjectListSort = GetMapObjectSortOption(
            viewModel,
            ArchiveBrowserViewModel.MapObjectListSortMode.PidAscending);

        Assert.True(moveEvents > 0);
        Assert.Same(selected, viewModel.SelectedMapObject);
        Assert.Equal(selected.RecordIndex, viewModel.SelectedMapObjectRecordIndex);
        Assert.Same(selected, viewModel.SortedMapObjectPlacements[0]);
        Assert.Same(other, viewModel.SortedMapObjectPlacements[1]);
        Assert.Equal(selected.Details, viewModel.MapObjectDetailsDisplay);
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

        viewModel.SelectedMapObjectListSort = GetMapObjectSortOption(
            viewModel,
            ArchiveBrowserViewModel.MapObjectListSortMode.ObjectTypeAscending);
        Assert.Equal([item, scenery, wall], viewModel.SortedMapObjectPlacements);

        viewModel.SelectedMapObjectListSort = GetMapObjectSortOption(
            viewModel,
            ArchiveBrowserViewModel.MapObjectListSortMode.ByteOffsetAscending);
        Assert.Equal([scenery, item, wall], viewModel.SortedMapObjectPlacements);

        viewModel.SelectedMapObjectListSort = GetMapObjectSortOption(
            viewModel,
            ArchiveBrowserViewModel.MapObjectListSortMode.ByteOffsetDescending);
        Assert.Equal([wall, item, scenery], viewModel.SortedMapObjectPlacements);
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
        viewModel.SelectedMapObject = selected;

        viewModel.MapObjectSearchText = "armor";

        Assert.Null(viewModel.SelectedMapObject);
        Assert.Null(viewModel.SelectedMapObjectRecordIndex);
        Assert.Equal("Select a parsed object to inspect its raw fields.", viewModel.MapObjectDetailsDisplay);
        Assert.Equal([visible], viewModel.FilteredMapObjectPlacements);
        Assert.Equal(2, viewModel.MapObjectPlacements.Count);

        viewModel.SelectMapObjectFromFloorMarker(selected);

        Assert.Null(viewModel.SelectedMapObject);
        Assert.Equal("Select a parsed object to inspect its raw fields.", viewModel.MapObjectDetailsDisplay);

        viewModel.SelectedMapObject = visible;
        Assert.Same(visible, viewModel.SelectedMapObject);
        Assert.Equal(visible.RecordIndex, viewModel.SelectedMapObjectRecordIndex);

        viewModel.MapObjectSearchText = string.Empty;

        Assert.Same(viewModel.MapObjectPlacements, viewModel.FilteredMapObjectPlacements);
        Assert.Equal(2, viewModel.FilteredMapObjectPlacements.Count);
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

    private static ArchiveBrowserViewModel.MapObjectListSortOption GetMapObjectSortOption(
        ArchiveBrowserViewModel viewModel,
        ArchiveBrowserViewModel.MapObjectListSortMode sortMode) =>
        viewModel.MapObjectListSortOptions.Single(option => option.SortMode == sortMode);
}
