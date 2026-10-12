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
}
