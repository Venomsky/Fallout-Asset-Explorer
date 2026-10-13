using Avalonia.Controls;
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

    public event EventHandler<RoutedEventArgs>? MaximizeToggleRequested;

    public (GridLength Viewport, GridLength Splitter, GridLength Workspace) GetPreviewSplitRows() =>
        (
            MapInspectorLayoutGrid.RowDefinitions[0].Height,
            MapInspectorLayoutGrid.RowDefinitions[1].Height,
            MapInspectorLayoutGrid.RowDefinitions[2].Height);

    public void SetPreviewSplitRows(GridLength viewport, GridLength splitter, GridLength workspace)
    {
        MapInspectorLayoutGrid.RowDefinitions[0].Height = viewport;
        MapInspectorLayoutGrid.RowDefinitions[1].Height = splitter;
        MapInspectorLayoutGrid.RowDefinitions[2].Height = workspace;
    }

    public (GridLength Workspace, GridLength Splitter) GetMapWorkspaceRows() =>
        (
            MapInspectorLayoutGrid.RowDefinitions[2].Height,
            MapInspectorLayoutGrid.RowDefinitions[1].Height);

    public void SetMapWorkspaceRows(GridLength workspace, GridLength splitter)
    {
        MapInspectorLayoutGrid.RowDefinitions[2].Height = workspace;
        MapInspectorLayoutGrid.RowDefinitions[1].Height = splitter;
    }

    public void UpdateShellControls(bool isWorkspaceCollapsed, bool isMaximized)
    {
        MapWorkspaceToggleButton.Content = isWorkspaceCollapsed ? "Show workspace" : "Hide workspace";
        MapWorkspaceToggleButton.IsEnabled = !isMaximized;
        MapRecordsToggleButton.IsEnabled = !isMaximized;
        MapMaximizeButton.Content = isMaximized ? "Restore workspace" : "Maximize map";
    }

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
        ViewModel?.SelectMapObjectFromFloorMarker(e.Placement);

    private void MapIsometricFloorView_ObjectSelectionChanged(
        object? sender,
        MapObjectMarkerSelectionChangedEventArgs e) =>
        ViewModel?.SelectMapObjectFromIsometricMarker(e.RecordIndex);

    private void ClearMapObjectSearch_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.MapObjectSearchText = string.Empty;
        }
    }

    private async void MapDiagnosticLayer_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo
            && combo.SelectedIndex is 2 or 3 or 4
            && ViewModel is { } viewModel)
        {
            viewModel.SelectedMapDiagnosticLayerIndex = combo.SelectedIndex;
            await viewModel.PrepareMapFloorTilesAsync();
        }
    }

    private void MapHexFit_Click(object? sender, RoutedEventArgs e) => MapHexGridView.FitToView();

    private void MapHexCenter_Click(object? sender, RoutedEventArgs e) => MapHexGridView.CenterOnSelection();

    private void MapFloorFit_Click(object? sender, RoutedEventArgs e) => MapFloorTileView.FitToView();

    private void MapIsometricFloorFit_Click(object? sender, RoutedEventArgs e) =>
        MapIsometricFloorView.FitToView();

    private void ToggleMapWorkspace_Click(object? sender, RoutedEventArgs e) =>
        WorkspaceToggleRequested?.Invoke(this, e);

    private void ToggleMapRecords_Click(object? sender, RoutedEventArgs e) =>
        RecordsToggleRequested?.Invoke(this, e);

    private void ToggleMapMaximized_Click(object? sender, RoutedEventArgs e) =>
        MaximizeToggleRequested?.Invoke(this, e);
}
