using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using FalloutAssetExplorer.Core;

namespace FalloutAssetExplorer.App.Views;

public sealed class MapGridDiagnosticView : Control
{
    private const double GridSize = MapTileGridDecoder.GridWidth;

    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.Parse("#171A1F"));
    private static readonly IBrush CellBrush = new SolidColorBrush(Color.Parse("#303640"));
    private static readonly IBrush AlternateCellBrush = new SolidColorBrush(Color.Parse("#343B46"));
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.Parse("#68717F")), 0.75);
    private static readonly IBrush HoverBrush = new SolidColorBrush(Color.FromArgb(72, 91, 192, 235));
    private static readonly IPen HoverPen = new Pen(new SolidColorBrush(Color.Parse("#5BC0EB")), 1.5);
    private static readonly IPen SelectionPen = new Pen(new SolidColorBrush(Color.Parse("#F2C14E")), 2);

    public static readonly StyledProperty<uint[]?> ValuesProperty =
        AvaloniaProperty.Register<MapGridDiagnosticView, uint[]?>(nameof(Values));

    public static readonly StyledProperty<int?> SelectedIndexProperty =
        AvaloniaProperty.Register<MapGridDiagnosticView, int?>(nameof(SelectedIndex));

    private int? _hoveredIndex;
    private Rect _gridBounds;
    private double _cellSize;

    public event EventHandler<MapGridCellInspectedEventArgs>? CellInspected;

    public event EventHandler<MapGridCellInspectedEventArgs>? CellSelected;

    public uint[]? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public int? SelectedIndex
    {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValuesProperty)
        {
            _hoveredIndex = null;
            CellInspected?.Invoke(this, MapGridCellInspectedEventArgs.None);
            InvalidateVisual();
        }
        else if (change.Property == SelectedIndexProperty)
        {
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(BackgroundBrush, new Rect(Bounds.Size));

        if (Values is not { Length: MapTileGridDecoder.ValuesPerGrid })
        {
            return;
        }

        _cellSize = Math.Min(Bounds.Width / GridSize, Bounds.Height / GridSize);
        if (_cellSize <= 0)
        {
            return;
        }

        var width = _cellSize * GridSize;
        var height = _cellSize * GridSize;
        _gridBounds = new Rect(
            (Bounds.Width - width) / 2,
            (Bounds.Height - height) / 2,
            width,
            height);

        for (var y = 0; y < MapTileGridDecoder.GridHeight; y++)
        {
            for (var x = 0; x < MapTileGridDecoder.GridWidth; x++)
            {
                var cell = new Rect(
                    _gridBounds.X + x * _cellSize,
                    _gridBounds.Y + y * _cellSize,
                    _cellSize,
                    _cellSize);
                context.FillRectangle((x + y) % 2 == 0 ? CellBrush : AlternateCellBrush, cell);
                if ((x % 10 == 0) || (y % 10 == 0))
                {
                    context.DrawRectangle(GridPen, cell);
                }
            }
        }

        if (_hoveredIndex is int hoveredIndex)
        {
            var (x, y) = MapTileGridDecoder.GetSquareGridCoordinates(hoveredIndex);
            var hoverRect = GetCellBounds(x, y);
            context.FillRectangle(HoverBrush, hoverRect);
            context.DrawRectangle(HoverPen, hoverRect);
        }

        if (SelectedIndex is int selectedIndex
            && (uint)selectedIndex < MapTileGridDecoder.ValuesPerGrid)
        {
            var (x, y) = MapTileGridDecoder.GetSquareGridCoordinates(selectedIndex);
            context.DrawRectangle(SelectionPen, GetCellBounds(x, y));
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        InspectCell(e.GetPosition(this));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHoveredIndex(null);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var position = e.GetPosition(this);
        if (Values is not { Length: MapTileGridDecoder.ValuesPerGrid } values
            || !MapGridDiagnosticGeometry.TryGetSerializedIndex(
                position.X,
                position.Y,
                Bounds.Width,
                Bounds.Height,
                out var index))
        {
            return;
        }

        var (x, y) = MapTileGridDecoder.GetSquareGridCoordinates(index);
        SetCurrentValue(SelectedIndexProperty, index);
        CellSelected?.Invoke(
            this,
            new MapGridCellInspectedEventArgs(x, y, index, values[index]));
        e.Handled = true;
    }

    private void InspectCell(Point position)
    {
        if (Values is not { Length: MapTileGridDecoder.ValuesPerGrid } values
            || !MapGridDiagnosticGeometry.TryGetSerializedIndex(
                position.X,
                position.Y,
                Bounds.Width,
                Bounds.Height,
                out var index))
        {
            SetHoveredIndex(null);
            return;
        }

        var (x, y) = MapTileGridDecoder.GetSquareGridCoordinates(index);
        SetHoveredIndex(index, x, y, values[index]);
    }

    private Rect GetCellBounds(int x, int y) =>
        new(
            _gridBounds.X + x * _cellSize,
            _gridBounds.Y + y * _cellSize,
            _cellSize,
            _cellSize);

    private void SetHoveredIndex(int? index, int x = 0, int y = 0, uint value = 0)
    {
        if (_hoveredIndex == index)
        {
            return;
        }

        _hoveredIndex = index;
        InvalidateVisual();
        CellInspected?.Invoke(
            this,
            index is null
                ? MapGridCellInspectedEventArgs.None
                : new MapGridCellInspectedEventArgs(x, y, index.Value, value));
    }
}

public readonly record struct MapGridCellValueFields(uint RawValue)
{
    public uint FloorArtId => RawValue & 0x0FFFu;

    public uint FloorFlagNibble => (RawValue >> 12) & 0x0Fu;

    public uint RoofArtId => (RawValue >> 16) & 0x0FFFu;

    public uint RoofFlagNibble => (RawValue >> 28) & 0x0Fu;

    public bool FloorRenderSuppressionBitIsSet => (FloorFlagNibble & 0x01u) != 0;

    public bool RoofRenderSuppressionBitIsSet => (RoofFlagNibble & 0x01u) != 0;
}

public static class MapGridDiagnosticGeometry
{
    public static bool TryGetSerializedIndex(
        double positionX,
        double positionY,
        double controlWidth,
        double controlHeight,
        out int serializedIndex)
    {
        serializedIndex = -1;
        if (!double.IsFinite(positionX)
            || !double.IsFinite(positionY)
            || !double.IsFinite(controlWidth)
            || !double.IsFinite(controlHeight)
            || controlWidth <= 0
            || controlHeight <= 0)
        {
            return false;
        }

        var cellSize = Math.Min(
            controlWidth / MapTileGridDecoder.GridWidth,
            controlHeight / MapTileGridDecoder.GridHeight);
        if (cellSize <= 0)
        {
            return false;
        }

        var gridWidth = cellSize * MapTileGridDecoder.GridWidth;
        var gridHeight = cellSize * MapTileGridDecoder.GridHeight;
        var left = (controlWidth - gridWidth) / 2;
        var top = (controlHeight - gridHeight) / 2;
        var right = left + gridWidth;
        var bottom = top + gridHeight;
        if (positionX < left || positionX >= right || positionY < top || positionY >= bottom)
        {
            return false;
        }

        var x = (int)((positionX - left) / cellSize);
        var y = (int)((positionY - top) / cellSize);
        serializedIndex = MapTileGridDecoder.GetSerializedIndex(x, y);
        return true;
    }
}

public sealed class MapGridCellInspectedEventArgs : EventArgs
{
    public static MapGridCellInspectedEventArgs None { get; } = new(null, null, null, null);

    public MapGridCellInspectedEventArgs(int? x, int? y, int? serializedIndex, uint? rawValue)
    {
        X = x;
        Y = y;
        SerializedIndex = serializedIndex;
        RawValue = rawValue;
    }

    public int? X { get; }

    public int? Y { get; }

    public int? SerializedIndex { get; }

    public uint? RawValue { get; }
}
