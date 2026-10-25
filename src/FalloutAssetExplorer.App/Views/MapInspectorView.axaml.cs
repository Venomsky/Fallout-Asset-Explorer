using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using FalloutAssetExplorer.App.ViewModels;

namespace FalloutAssetExplorer.App.Views;

public partial class MapInspectorView : UserControl
{
    private GridLength? _recordsHeightBeforeCollapse;
    private GridLength? _recordsSplitterHeightBeforeCollapse;
    private bool _isRecordsCollapsed;

    public MapInspectorView()
    {
        InitializeComponent();
    }

    public event EventHandler<RoutedEventArgs>? WorkspaceToggleRequested;

    public event EventHandler<RoutedEventArgs>? RecordsToggleRequested;

    public event EventHandler<RoutedEventArgs>? MapSaveAsRequested;

    public (GridLength Workspace, GridLength Splitter) GetMapWorkspaceRows() =>
        (
            MapInspectorLayoutGrid.RowDefinitions[2].Height,
            MapInspectorLayoutGrid.RowDefinitions[1].Height);

    public void SetMapWorkspaceRows(GridLength workspace, GridLength splitter)
    {
        MapInspectorLayoutGrid.RowDefinitions[2].Height = workspace;
        MapInspectorLayoutGrid.RowDefinitions[1].Height = splitter;
    }

    public void UpdateShellControls(bool isWorkspaceCollapsed) =>
        MapWorkspaceToggleButton.Content = isWorkspaceCollapsed ? "Show workspace" : "Hide workspace";

    public void ToggleRecords()
    {
        var recordsRow = RecordPrototypeGrid.RowDefinitions[0];
        var splitterRow = RecordPrototypeGrid.RowDefinitions[1];
        if (_isRecordsCollapsed)
        {
            recordsRow.Height = _recordsHeightBeforeCollapse
                ?? new GridLength(1, GridUnitType.Star);
            splitterRow.Height = _recordsSplitterHeightBeforeCollapse
                ?? new GridLength(6);
            _recordsHeightBeforeCollapse = null;
            _recordsSplitterHeightBeforeCollapse = null;
            MapRecordsToggleButton.Content = "Hide records";
            _isRecordsCollapsed = false;
            return;
        }

        _recordsHeightBeforeCollapse = recordsRow.Height;
        _recordsSplitterHeightBeforeCollapse = splitterRow.Height;
        recordsRow.Height = new GridLength(0);
        splitterRow.Height = new GridLength(0);
        MapRecordsToggleButton.Content = "Show records";
        _isRecordsCollapsed = true;
    }

    private ArchiveBrowserViewModel? ViewModel => DataContext as ArchiveBrowserViewModel;

    private void MapGridView_CellInspected(object? sender, MapGridCellInspectedEventArgs e) =>
        ViewModel?.InspectMapGridCell(e.X, e.Y, e.SerializedIndex, e.RawValue);

    private void MapGridView_CellSelected(object? sender, MapGridCellInspectedEventArgs e) =>
        ViewModel?.SelectMapGridCell(e.SerializedIndex);

    private void MapFloorTileView_ObjectSelected(
        object? sender,
        MapObjectPlacementSelectedEventArgs e) =>
        ViewModel?.MapInspector.SelectMapObjectFromFloorMarker(e.Placement);

    private async void MapIsometricFloorView_ObjectSelectionChanged(
        object? sender,
        MapObjectMarkerSelectionChangedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.MapInspector.SelectMapObjectFromIsometricMarker(
                e.RecordIndex,
                viewModel.MapInspector.SelectedMapElevationIndex);
            await viewModel.PrepareSelectedMapObjectSpriteAsync();
        }
    }

    private void ClearMapObjectSearch_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.MapInspector.MapObjectSearchText = string.Empty;
        }
    }

    private void ApplyInventoryQuantity_Click(object? sender, RoutedEventArgs e) =>
        ViewModel?.MapInspector.ApplyInventoryQuantity();

    private void SaveMapAs_Click(object? sender, RoutedEventArgs e) =>
        MapSaveAsRequested?.Invoke(this, e);

    private void UndoMapEdit_Click(object? sender, RoutedEventArgs e) =>
        ViewModel?.MapInspector.UndoMapEdit();

    private void RedoMapEdit_Click(object? sender, RoutedEventArgs e) =>
        ViewModel?.MapInspector.RedoMapEdit();

    private async void MapDiagnosticLayer_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo
            && combo.SelectedIndex is 2 or 3 or 4
            && ViewModel is { } viewModel)
        {
            viewModel.MapInspector.SelectedMapDiagnosticLayerIndex = combo.SelectedIndex;
            var floorTask = viewModel.PrepareMapFloorTilesAsync();
            await floorTask;
        }
    }

    private void MapHexFit_Click(object? sender, RoutedEventArgs e) => MapHexGridView.FitToView();

    private void MapHexCenter_Click(object? sender, RoutedEventArgs e) => MapHexGridView.CenterOnSelection();

    private void MapFloorFit_Click(object? sender, RoutedEventArgs e) => MapFloorTileView.FitToView();

    private void MapIsometricFloorFit_Click(object? sender, RoutedEventArgs e) =>
        MapIsometricFloorView.FitToView();

    private void ToggleMapRegistrationDiagnostics_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggleButton)
        {
            MapIsometricFloorView.ShowRegistrationDiagnostics = toggleButton.IsChecked == true;
        }
    }

    private void ToggleMapWorkspace_Click(object? sender, RoutedEventArgs e) =>
        WorkspaceToggleRequested?.Invoke(this, e);

    private void ToggleMapRecords_Click(object? sender, RoutedEventArgs e) =>
        RecordsToggleRequested?.Invoke(this, e);
}
