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
}
