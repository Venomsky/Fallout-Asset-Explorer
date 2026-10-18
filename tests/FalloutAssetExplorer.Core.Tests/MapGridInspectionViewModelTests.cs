using System.Text;
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
    public void EditSessionUsesExistingSelectionAndShowsOnlyConfirmedPlacementFields()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        var inspector = viewModel.MapInspector;
        var placement = CreateMarkerPlacement(7, elevation: 0);
        var sourceData = new MapSourceData(new byte[] { 1, 2, 3 });
        inspector.SetMapObjectPlacements([placement]);
        inspector.SetMapEditSession(new MapEditSession(sourceData, [placement]));

        Assert.Null(inspector.SelectedEditObject);
        Assert.Equal(
            "Select a parsed object to inspect its confirmed placement fields.",
            inspector.MapObjectDetailsDisplay);
        Assert.True(inspector.HasEditSession);

        var workingPlacement = Assert.Single(inspector.MapObjectPlacements);
        inspector.SelectedMapObject = workingPlacement;

        Assert.Same(workingPlacement, inspector.SelectedMapObject);
        Assert.Same(workingPlacement, inspector.SelectedEditObject!.Placement);
        Assert.NotSame(placement, workingPlacement);
        Assert.Equal(
            new MapEditObjectId(inspector.EditSession!.SessionId, placement.RecordIndex),
            inspector.SelectedEditObject.Id);
        Assert.Equal(
            "Session-local object ID: 7"
                + Environment.NewLine
                + "Elevation group: 0"
                + Environment.NewLine
                + "Hex-grid coordinates: (100, 100)",
            inspector.MapObjectDetailsDisplay);
        Assert.DoesNotContain("FID", inspector.MapObjectDetailsDisplay);
        Assert.DoesNotContain("Raw", inspector.MapObjectDetailsDisplay);

        inspector.ClearSelectedMapObject();
        inspector.SelectMapObjectFromFloorMarker(workingPlacement);
        Assert.Equal(placement.RecordIndex, inspector.SelectedEditObject?.Id.RecordIndex);

        inspector.ClearSelectedMapObject();
        inspector.SelectMapObjectFromIsometricMarker(placement.RecordIndex, placement.ElevationGroup);
        Assert.Equal(placement.RecordIndex, inspector.SelectedEditObject?.Id.RecordIndex);
    }

    [Fact]
    public void ReplacingEditSessionForNewMapClearsSelectionEvenWhenRecordIndexIsReused()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        var inspector = viewModel.MapInspector;
        var oldPlacement = CreateMarkerPlacement(7, elevation: 0);
        var newPlacement = oldPlacement with { HexX = 99, HexY = 100 };
        var oldSource = new MapSourceData(new byte[] { 1, 2, 3 });
        var newSource = new MapSourceData(new byte[] { 4, 5, 6 });
        inspector.SetMapObjectPlacements([oldPlacement]);
        inspector.SetMapEditSession(new MapEditSession(oldSource, [oldPlacement]));
        var oldWorkingPlacement = Assert.Single(inspector.MapObjectPlacements);
        inspector.SelectedMapObject = oldWorkingPlacement;

        var staleId = inspector.SelectedEditObject!.Id;
        inspector.SetMapObjectPlacements([newPlacement]);
        var newSession = new MapEditSession(newSource, [newPlacement]);
        inspector.SetMapEditSession(newSession);

        Assert.Null(inspector.SelectedMapObject);
        Assert.Null(inspector.SelectedEditObject);
        Assert.Null(newSession.GetObject(staleId));
        Assert.NotSame(newSession.SourceData, oldSource);
        Assert.Equal(
            "Select a parsed object to inspect its confirmed placement fields.",
            inspector.MapObjectDetailsDisplay);

        inspector.SelectedMapObject = oldWorkingPlacement;
        Assert.Null(inspector.SelectedMapObject);
        Assert.Null(inspector.SelectedEditObject);

        inspector.SetMapEditSession(null);
        Assert.Equal(
            "Select a parsed object to inspect its raw fields.",
            inspector.MapObjectDetailsDisplay);
    }

    [Fact]
    public void InventoryQuantityChangesRefreshSessionBackedListAndHistoryWithoutChangingSources()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        var inspector = viewModel.MapInspector;
        var parent = CreateMarkerPlacement(7, elevation: 0);
        var sourceChild = CreateMarkerPlacement(8, elevation: 0) with
        {
            ParentRecordIndex = parent.RecordIndex,
            InventoryQuantity = 2
        };
        var sourceBytes = new byte[] { 1, 2, 3 };
        var sourceData = new MapSourceData(sourceBytes);
        inspector.SetMapObjectPlacements([parent, sourceChild]);
        var session = new MapEditSession(sourceData, [parent, sourceChild]);
        inspector.SetMapEditSession(session);

        var workingChild = Assert.Single(
            inspector.MapObjectPlacements,
            placement => placement.RecordIndex == sourceChild.RecordIndex);
        var id = new MapEditObjectId(session.SessionId, sourceChild.RecordIndex);
        inspector.SelectedMapObject = workingChild;
        var refreshCount = 0;
        inspector.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MapInspectorViewModel.MapObjectPlacements))
            {
                refreshCount++;
            }
        };

        Assert.Contains("Quantity 2", workingChild.DisplayText);
        Assert.True(session.TrySetInventoryQuantity(id, 2, out var noOpError), noOpError);
        Assert.False(session.TrySetInventoryQuantity(id, 0, out var invalidError));
        Assert.NotNull(invalidError);
        Assert.Equal(0, refreshCount);

        Assert.True(session.TrySetInventoryQuantity(id, 5, out var editError), editError);
        var editedChild = Assert.Single(
            inspector.MapObjectPlacements,
            placement => placement.RecordIndex == sourceChild.RecordIndex);
        Assert.Equal(5, editedChild.InventoryQuantity);
        Assert.Contains("Quantity 5", editedChild.DisplayText);
        Assert.Same(editedChild, inspector.SelectedMapObject);
        Assert.Equal(5, inspector.SelectedEditObject?.Placement.InventoryQuantity);
        Assert.Equal(1, refreshCount);

        Assert.True(session.Undo(out var undoError), undoError);
        Assert.Equal(2, inspector.SelectedEditObject?.Placement.InventoryQuantity);
        Assert.Contains("Quantity 2", Assert.Single(
            inspector.MapObjectPlacements,
            placement => placement.RecordIndex == sourceChild.RecordIndex).DisplayText);
        Assert.Equal(2, refreshCount);

        Assert.True(session.Redo(out var redoError), redoError);
        Assert.Equal(5, inspector.SelectedEditObject?.Placement.InventoryQuantity);
        Assert.Equal(3, refreshCount);
        Assert.Equal(2, sourceChild.InventoryQuantity);
        Assert.Equal(sourceBytes, sourceData.GetRange(0, sourceData.Length).ToArray());
    }

    [Fact]
    public void InventoryQuantityEditorValidatesAndSynchronizesSessionBackedViewsAndHistory()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        var inspector = viewModel.MapInspector;
        var parent = CreateMarkerPlacement(7, elevation: 0);
        var sourceChild = CreateMarkerPlacement(8, elevation: 0) with
        {
            ParentRecordIndex = parent.RecordIndex,
            InventoryQuantity = 2
        };
        var unsupportedChild = CreateMarkerPlacement(9, elevation: 0) with
        {
            ParentRecordIndex = parent.RecordIndex
        };
        var sourceBytes = new byte[] { 7, 8, 9 };
        var sourceData = new MapSourceData(sourceBytes);
        inspector.SetMapObjectPlacements([parent, sourceChild, unsupportedChild]);
        inspector.SelectedMapObject = sourceChild;
        Assert.False(inspector.CanEditSelectedInventoryQuantity);

        var session = new MapEditSession(sourceData, [parent, sourceChild, unsupportedChild]);
        inspector.SetMapEditSession(session);
        var changedPlacementCount = 0;
        inspector.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MapInspectorViewModel.MapObjectPlacements))
            {
                changedPlacementCount++;
            }
        };

        inspector.SelectedMapObject = Assert.Single(
            inspector.MapObjectPlacements,
            placement => placement.RecordIndex == parent.RecordIndex);
        Assert.False(inspector.CanEditSelectedInventoryQuantity);

        inspector.SelectedMapObject = Assert.Single(
            inspector.MapObjectPlacements,
            placement => placement.RecordIndex == unsupportedChild.RecordIndex);
        Assert.False(inspector.CanEditSelectedInventoryQuantity);

        inspector.SelectedMapObject = Assert.Single(
            inspector.MapObjectPlacements,
            placement => placement.RecordIndex == sourceChild.RecordIndex);
        var selectedId = inspector.SelectedEditObject!.Id;
        Assert.True(inspector.CanEditSelectedInventoryQuantity);
        Assert.Equal("2", inspector.InventoryQuantityInput);
        Assert.Contains("Inventory quantity: 2", inspector.MapObjectDetailsDisplay);

        inspector.InventoryQuantityInput = "not a number";
        Assert.False(inspector.ApplyInventoryQuantity());
        Assert.Contains("whole-number", inspector.InventoryQuantityValidationError);
        Assert.False(session.CanUndo);
        Assert.Equal(0, changedPlacementCount);

        inspector.InventoryQuantityInput = "0";
        Assert.False(inspector.ApplyInventoryQuantity());
        Assert.Equal("Inventory quantity must be at least 1.", inspector.InventoryQuantityValidationError);
        Assert.False(session.CanUndo);
        Assert.Equal(0, changedPlacementCount);

        inspector.InventoryQuantityInput = "2";
        Assert.True(inspector.ApplyInventoryQuantity());
        Assert.False(session.CanUndo);
        Assert.False(inspector.IsMapEditSessionDirty);
        Assert.Equal(0, changedPlacementCount);

        inspector.InventoryQuantityInput = "5";
        Assert.True(inspector.ApplyInventoryQuantity());
        Assert.Equal(1, changedPlacementCount);
        Assert.True(inspector.IsMapEditSessionDirty);
        Assert.True(inspector.CanUndoMapEdit);
        Assert.False(inspector.CanRedoMapEdit);
        Assert.Contains("Unsaved in-memory", inspector.EditSessionDirtyDisplay);
        Assert.Contains("not saved to a MAP file", inspector.EditSessionStatusDisplay);
        Assert.Equal("5", inspector.InventoryQuantityInput);
        Assert.Contains("Inventory quantity: 5", inspector.MapObjectDetailsDisplay);
        Assert.Contains(
            "Quantity 5",
            Assert.Single(
                inspector.MapObjectPlacements,
                placement => placement.RecordIndex == sourceChild.RecordIndex).DisplayText);
        Assert.Equal(selectedId, inspector.SelectedEditObject!.Id);
        Assert.Equal(5, inspector.SelectedEditObject.Placement.InventoryQuantity);
        Assert.Equal(2, sourceChild.InventoryQuantity);

        Assert.True(inspector.UndoMapEdit());
        Assert.Equal(2, inspector.SelectedEditObject!.Placement.InventoryQuantity);
        Assert.Equal("2", inspector.InventoryQuantityInput);
        Assert.Contains("Quantity 2", inspector.SelectedMapObject!.DisplayText);
        Assert.Equal(selectedId, inspector.SelectedEditObject.Id);
        Assert.False(inspector.IsMapEditSessionDirty);
        Assert.False(inspector.CanUndoMapEdit);
        Assert.True(inspector.CanRedoMapEdit);
        Assert.Equal(2, changedPlacementCount);

        Assert.True(inspector.RedoMapEdit());
        Assert.Equal(5, inspector.SelectedEditObject!.Placement.InventoryQuantity);
        Assert.Equal("5", inspector.InventoryQuantityInput);
        Assert.Contains("Quantity 5", inspector.SelectedMapObject!.DisplayText);
        Assert.Equal(selectedId, inspector.SelectedEditObject.Id);
        Assert.True(inspector.IsMapEditSessionDirty);
        Assert.Equal(3, changedPlacementCount);
        Assert.Equal(2, sourceChild.InventoryQuantity);
        Assert.Equal(sourceBytes, sourceData.GetRange(0, sourceData.Length).ToArray());
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

        Assert.Contains("0x00000023", viewModel.MapInspector.MapObjectPrototypeDiagnosticDetails);
        Assert.Equal(
            "Prototype diagnostics require an open Fallout 2 DAT2 archive.",
            viewModel.MapInspector.MapObjectPrototypeDiagnosticStatus);

        viewModel.MapInspector.SelectedMapObject = scenery;

        Assert.Contains("0x02000045", viewModel.MapInspector.MapObjectPrototypeDiagnosticDetails);
        Assert.DoesNotContain("0x00000023", viewModel.MapInspector.MapObjectPrototypeDiagnosticDetails);
        Assert.Empty(viewModel.MapInspector.MapObjectPrototypeRawByteLines);

        var inspector = viewModel.MapInspector;
        inspector.SetMapObjectPrototypeDiagnostic(
            "Loaded",
            "Resolved details",
            "Scenery fields",
            "Item fields",
            ["00000000"]);
        var changedProperties = new List<string?>();
        inspector.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        viewModel.MapInspector.SelectedMapObject = null;

        Assert.Equal(
            "Select an Item or Scenery object to inspect its Fallout 2 prototype.",
            inspector.MapObjectPrototypeDiagnosticStatus);
        Assert.Empty(inspector.MapObjectPrototypeDiagnosticDetails);
        Assert.Empty(inspector.MapObjectPrototypeSceneryFieldsDisplay);
        Assert.Empty(inspector.MapObjectPrototypeItemFieldsDisplay);
        Assert.Empty(inspector.MapObjectPrototypeRawByteLines);
        Assert.False(inspector.HasMapObjectPrototypeSceneryFields);
        Assert.False(inspector.HasMapObjectPrototypeItemFields);
        Assert.False(inspector.HasMapObjectPrototypeRawBytes);
        Assert.Contains(nameof(inspector.HasMapObjectPrototypeSceneryFields), changedProperties);
        Assert.Contains(nameof(inspector.HasMapObjectPrototypeItemFields), changedProperties);
        Assert.Contains(nameof(inspector.HasMapObjectPrototypeRawBytes), changedProperties);
    }

    [Fact]
    public void ClearingMapPlacements_ClearsChildSelectionAndPrototypeDiagnostic()
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
            viewModel.MapInspector.MapObjectPrototypeDiagnosticStatus);

        viewModel.MapObjectPlacements.Clear();

        Assert.Null(viewModel.MapInspector.SelectedMapObject);
        Assert.Empty(viewModel.MapInspector.SortedMapObjectPlacements);
        Assert.Equal("Select a parsed object to inspect its raw fields.", viewModel.MapInspector.MapObjectDetailsDisplay);
        Assert.Equal(
            "Select an Item or Scenery object to inspect its Fallout 2 prototype.",
            viewModel.MapInspector.MapObjectPrototypeDiagnosticStatus);
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
            viewModel.MapInspector.MapObjectPrototypeDiagnosticStatus);
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
    public async Task PrepareMapFloorTiles_StaleTileIoCannotMutateOrNotifyNewMap()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        var oldArchive = new ControlledMapTileArchive(
            "old.frm",
            floorId: 3,
            includeTileAsset: true,
            blockTileAssetRead: true,
            throwTileAssetIo: true);
        var newArchive = new ControlledMapTileArchive(
            "new.frm",
            floorId: 7,
            includeTileAsset: false,
            blockTileListRead: true);
        Task? oldLoad = null;
        Task? newLoad = null;
        Task? duplicateLoad = null;

        try
        {
            SetMapFloorTileState(viewModel, oldArchive, floorId: 3);
            oldLoad = viewModel.PrepareMapFloorTilesAsync();
            await oldArchive.TileAssetReadStarted.WaitAsync(TimeSpan.FromSeconds(10));

            InvokeClearPreview(viewModel);
            SetMapFloorTileState(viewModel, newArchive, floorId: 7);

            var changedProperties = new List<string?>();
            viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);
            newLoad = viewModel.PrepareMapFloorTilesAsync();
            await newArchive.TileListReadStarted.WaitAsync(TimeSpan.FromSeconds(10));

            var currentCancellation = GetPrivateField<CancellationTokenSource?>(
                viewModel,
                "_mapFloorTileCancellation");
            var currentRevision = viewModel.MapFloorTilesRevision;
            changedProperties.Clear();

            oldArchive.ReleaseTileAssetRead();
            await oldLoad.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(
                "Resolving floor tile IDs and loading unique FRM assets...",
                viewModel.MapFloorTilesStatusDisplay);
            Assert.Empty(viewModel.UnavailableMapFloorIds);
            Assert.False(GetPrivateField<bool>(viewModel, "_mapFloorTilesLoaded"));
            Assert.Same(
                currentCancellation,
                GetPrivateField<CancellationTokenSource?>(viewModel, "_mapFloorTileCancellation"));
            Assert.Equal(currentRevision, viewModel.MapFloorTilesRevision);
            Assert.DoesNotContain(nameof(viewModel.MapFloorTilesStatusDisplay), changedProperties);
            Assert.DoesNotContain(nameof(viewModel.MapFloorTileBitmaps), changedProperties);
            Assert.DoesNotContain(nameof(viewModel.UnavailableMapFloorIds), changedProperties);
            Assert.DoesNotContain(nameof(viewModel.MapFloorTilesRevision), changedProperties);

            duplicateLoad = viewModel.PrepareMapFloorTilesAsync();
            var newMapTileListReadCount = newArchive.TileListReadCount;
            newArchive.ReleaseTileListRead();
            await Task.WhenAll(newLoad, duplicateLoad);

            Assert.Equal(1, newMapTileListReadCount);
            Assert.Equal(1, newArchive.TileListReadCount);
            Assert.True(GetPrivateField<bool>(viewModel, "_mapFloorTilesLoaded"));
            Assert.Contains(7u, viewModel.UnavailableMapFloorIds);
            Assert.Contains("ID 7:", viewModel.MapFloorTilesStatusDisplay);
        }
        finally
        {
            oldArchive.ReleaseTileAssetRead();
            newArchive.ReleaseTileListRead();
            if (oldLoad is not null)
            {
                await oldLoad;
            }

            if (newLoad is not null)
            {
                await newLoad;
            }

            if (duplicateLoad is not null)
            {
                await duplicateLoad;
            }
        }
    }

    [Fact]
    public async Task PrepareMapFloorTiles_CurrentTileIoFailureStillUpdatesStatusAndUnavailableIds()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        var archive = new ControlledMapTileArchive(
            "broken.frm",
            floorId: 23,
            includeTileAsset: true,
            throwTileAssetIo: true);
        SetMapFloorTileState(viewModel, archive, floorId: 23);

        await viewModel.PrepareMapFloorTilesAsync();

        Assert.True(GetPrivateField<bool>(viewModel, "_mapFloorTilesLoaded"));
        Assert.Contains(23u, viewModel.UnavailableMapFloorIds);
        Assert.Contains(
            "Loaded 0 of 1 distinct drawable floor IDs; 1 unavailable.",
            viewModel.MapFloorTilesStatusDisplay);
        Assert.Contains("ID 23: controlled floor-tile I/O failure", viewModel.MapFloorTilesStatusDisplay);
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

    private static void SetMapFloorTileState(
        ArchiveBrowserViewModel viewModel,
        IGameArchive archive,
        int floorId)
    {
        var mapBytes = new byte[MapHeaderDecoder.HeaderSize + MapHeaderDecoder.BytesPerElevation];
        mapBytes[3] = 20;
        mapBytes[0x2B] = 0x0C;
        var header = MapHeaderDecoder.Decode(mapBytes, FalloutGame.Fallout2);
        var values = new uint[MapTileGridDecoder.ValuesPerGrid];
        Array.Fill(values, checked((uint)floorId));
        var gridData = new MapTileGridData(header, [new MapTileGrid(0, values)]);

        SetPrivateField(viewModel, "_archive", archive);
        SetPrivateField(viewModel, "_mapTileGridData", gridData);
        SetPrivateField(viewModel, "_isMapGridDataLoaded", true);
        viewModel.MapInspector.SetAvailableElevations([0], preferredElevationIndex: 0);
        viewModel.MapInspector.SetMapInspectorActive(true);
        viewModel.MapInspector.SelectedMapDiagnosticLayerIndex =
            MapInspectorViewModel.FloorTilesLayerIndex;
    }

    private static void InvokeClearPreview(ArchiveBrowserViewModel viewModel)
    {
        var method = typeof(ArchiveBrowserViewModel).GetMethod(
            "ClearPreview",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(viewModel, null);
    }

    private static T GetPrivateField<T>(ArchiveBrowserViewModel viewModel, string fieldName)
    {
        var field = typeof(ArchiveBrowserViewModel).GetField(
            fieldName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (T)field.GetValue(viewModel)!;
    }

    private static void SetPrivateField<T>(
        ArchiveBrowserViewModel viewModel,
        string fieldName,
        T value)
    {
        var field = typeof(ArchiveBrowserViewModel).GetField(
            fieldName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(viewModel, value);
    }

    private sealed class ControlledMapTileArchive : IGameArchive
    {
        private const string TileListPath = "art/tiles/tiles.lst";
        private const string PalettePath = "COLOR.PAL";
        private readonly Dictionary<string, (ArchiveEntry Entry, byte[] Bytes)> _entries =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly byte[] _tileListBytes;
        private readonly string _tileAssetPath;
        private readonly byte[] _paletteBytes = new byte[FalloutPalette.MinimalFileSize];
        private readonly bool _includeTileAsset;
        private readonly bool _blockTileListRead;
        private readonly bool _blockTileAssetRead;
        private readonly bool _throwTileAssetIo;
        private readonly TaskCompletionSource<bool> _tileListReadStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _tileAssetReadStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _releaseTileListRead =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _releaseTileAssetRead =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _tileListReadCount;

        public ControlledMapTileArchive(
            string tileName,
            int floorId,
            bool includeTileAsset,
            bool blockTileListRead = false,
            bool blockTileAssetRead = false,
            bool throwTileAssetIo = false)
        {
            Path = typeof(MapGridInspectionViewModelTests).Assembly.Location;
            var tileNames = Enumerable.Repeat("unused.frm", floorId + 1).ToArray();
            tileNames[floorId] = tileName;
            _tileListBytes = Encoding.ASCII.GetBytes($"{string.Join('\n', tileNames)}\n");
            _tileAssetPath = $"art/tiles/{tileName}";
            _includeTileAsset = includeTileAsset;
            _blockTileListRead = blockTileListRead;
            _blockTileAssetRead = blockTileAssetRead;
            _throwTileAssetIo = throwTileAssetIo;
            AddEntry(TileListPath, _tileListBytes);
            AddEntry(PalettePath, _paletteBytes);
            if (_includeTileAsset)
            {
                AddEntry(_tileAssetPath, [0]);
            }

            BrowserEntries = _entries.Values.Select(value => value.Entry).ToArray();
        }

        public string Path { get; }

        public FalloutGame Game => FalloutGame.Fallout2;

        public ArchiveFormat Format => ArchiveFormat.Dat2;

        public IReadOnlyList<ArchiveEntry> BrowserEntries { get; }

        public Task TileListReadStarted => _tileListReadStarted.Task;

        public Task TileAssetReadStarted => _tileAssetReadStarted.Task;

        public int TileListReadCount => Volatile.Read(ref _tileListReadCount);

        public void ReleaseTileListRead() => _releaseTileListRead.TrySetResult(true);

        public void ReleaseTileAssetRead() => _releaseTileAssetRead.TrySetResult(true);

        public async Task CopyEntryToAsync(
            ArchiveEntry entry,
            Stream destination,
            CancellationToken cancellationToken = default)
        {
            var path = entry.Path.Replace('\\', '/');
            if (string.Equals(path, TileListPath, StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref _tileListReadCount);
                if (_blockTileListRead)
                {
                    _tileListReadStarted.TrySetResult(true);
                    await _releaseTileListRead.Task.ConfigureAwait(false);
                }
            }
            else if (string.Equals(path, _tileAssetPath, StringComparison.OrdinalIgnoreCase))
            {
                if (_blockTileAssetRead)
                {
                    _tileAssetReadStarted.TrySetResult(true);
                    await _releaseTileAssetRead.Task.ConfigureAwait(false);
                }

                if (_throwTileAssetIo)
                {
                    throw new IOException("controlled floor-tile I/O failure");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!_entries.TryGetValue(path, out var content))
            {
                throw new IOException($"Unexpected archive entry request: {path}");
            }

            await destination.WriteAsync(content.Bytes, cancellationToken);
        }

        private void AddEntry(string path, byte[] bytes)
        {
            var entry = new ArchiveEntry(path, bytes.Length, null, Path);
            _entries.Add(path, (entry, bytes));
        }
    }
}
