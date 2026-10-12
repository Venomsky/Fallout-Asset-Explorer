using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using FalloutAssetExplorer.Core;

namespace FalloutAssetExplorer.App.Views;

public sealed class MapFloorTileDiagnosticView : Control
{
    private const double ZoomStep = 1.2;

    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.Parse("#171A1F"));
    private static readonly IBrush CellBrush = new SolidColorBrush(Color.Parse("#303640"));
    private static readonly IBrush AlternateCellBrush = new SolidColorBrush(Color.Parse("#343B46"));
    private static readonly IBrush MissingBrush = new SolidColorBrush(Color.Parse("#713E42"));
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.Parse("#68717F")), 0.75);
    private static readonly IPen MissingPen = new Pen(new SolidColorBrush(Color.Parse("#F08080")), 1.25);
    private static readonly IBrush HoverBrush = new SolidColorBrush(Color.FromArgb(72, 91, 192, 235));
    private static readonly IPen HoverPen = new Pen(new SolidColorBrush(Color.Parse("#5BC0EB")), 1.5);
    private static readonly IPen SelectionPen = new Pen(new SolidColorBrush(Color.Parse("#F2C14E")), 2);
    private static readonly IBrush ObjectSelectionBrush = new SolidColorBrush(Color.Parse("#F2C14E"));
    private static readonly IPen ObjectSelectionPen = new Pen(ObjectSelectionBrush, 1.5);
    private static readonly IBrush CritterBrush = new SolidColorBrush(Color.Parse("#F28E2B"));
    private static readonly IBrush SceneryBrush = new SolidColorBrush(Color.Parse("#59A14F"));
    private static readonly IBrush WallBrush = new SolidColorBrush(Color.Parse("#B8A98A"));
    private static readonly IBrush TileBrush = new SolidColorBrush(Color.Parse("#76B7B2"));
    private static readonly IBrush MiscBrush = new SolidColorBrush(Color.FromArgb(180, 91, 192, 235));
    private static readonly IBrush ItemBrush = new SolidColorBrush(Color.Parse("#EDC948"));
    private static readonly IBrush DefaultObjectBrush = new SolidColorBrush(Color.Parse("#5BC0EB"));

    public static readonly StyledProperty<uint[]?> ValuesProperty =
        AvaloniaProperty.Register<MapFloorTileDiagnosticView, uint[]?>(nameof(Values));

    public static readonly StyledProperty<IReadOnlyDictionary<uint, Bitmap>?> TileBitmapsProperty =
        AvaloniaProperty.Register<MapFloorTileDiagnosticView, IReadOnlyDictionary<uint, Bitmap>?>(
            nameof(TileBitmaps));

    public static readonly StyledProperty<IReadOnlySet<uint>?> UnavailableFloorIdsProperty =
        AvaloniaProperty.Register<MapFloorTileDiagnosticView, IReadOnlySet<uint>?>(
            nameof(UnavailableFloorIds));

    public static readonly StyledProperty<int?> SelectedIndexProperty =
        AvaloniaProperty.Register<MapFloorTileDiagnosticView, int?>(nameof(SelectedIndex));

    public static readonly StyledProperty<IReadOnlyList<MapObjectPlacement>?> PlacementsProperty =
        AvaloniaProperty.Register<MapFloorTileDiagnosticView, IReadOnlyList<MapObjectPlacement>?>(
            nameof(Placements));

    public static readonly StyledProperty<int?> ElevationProperty =
        AvaloniaProperty.Register<MapFloorTileDiagnosticView, int?>(nameof(Elevation));

    public static readonly StyledProperty<MapObjectPlacement?> SelectedObjectProperty =
        AvaloniaProperty.Register<MapFloorTileDiagnosticView, MapObjectPlacement?>(
            nameof(SelectedObject));

    private int? _hoveredIndex;
    private MapFloorGridViewTransform? _viewTransform;
    private Point? _panLastPosition;
    private bool _isFitToView = true;
    private IReadOnlyList<MapObjectFloorSample> _floorSamples = [];

    public event EventHandler<MapGridCellInspectedEventArgs>? CellInspected;

    public event EventHandler<MapGridCellInspectedEventArgs>? CellSelected;

    public event EventHandler<MapObjectPlacementSelectedEventArgs>? ObjectSelected;

    public uint[]? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public IReadOnlyDictionary<uint, Bitmap>? TileBitmaps
    {
        get => GetValue(TileBitmapsProperty);
        set => SetValue(TileBitmapsProperty, value);
    }

    public IReadOnlySet<uint>? UnavailableFloorIds
    {
        get => GetValue(UnavailableFloorIdsProperty);
        set => SetValue(UnavailableFloorIdsProperty, value);
    }

    public int? SelectedIndex
    {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    public IReadOnlyList<MapObjectPlacement>? Placements
    {
        get => GetValue(PlacementsProperty);
        set => SetValue(PlacementsProperty, value);
    }

    public int? Elevation
    {
        get => GetValue(ElevationProperty);
        set => SetValue(ElevationProperty, value);
    }

    public MapObjectPlacement? SelectedObject
    {
        get => GetValue(SelectedObjectProperty);
        set => SetValue(SelectedObjectProperty, value);
    }

    public void FitToView()
    {
        _isFitToView = true;
        _viewTransform = MapFloorTileDiagnosticGeometry.TryGetFitTransform(
            Bounds.Width,
            Bounds.Height,
            out var fit)
            ? fit
            : null;
        InvalidateVisual();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_isFitToView)
        {
            FitToView();
            return;
        }

        if (_viewTransform is MapFloorGridViewTransform current
            && MapFloorTileDiagnosticGeometry.TryGetResizedTransform(
                current,
                e.PreviousSize.Width,
                e.PreviousSize.Height,
                e.NewSize.Width,
                e.NewSize.Height,
                out var resized))
        {
            _viewTransform = resized;
        }
        else
        {
            FitToView();
        }

        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PlacementsProperty)
        {
            if (change.OldValue is INotifyCollectionChanged oldPlacements)
            {
                oldPlacements.CollectionChanged -= Placements_CollectionChanged;
            }

            if (change.NewValue is INotifyCollectionChanged newPlacements)
            {
                newPlacements.CollectionChanged += Placements_CollectionChanged;
            }

            UpdateFloorSamples();
        }
        else if (change.Property == ElevationProperty)
        {
            UpdateFloorSamples();
        }

        if (change.Property == ValuesProperty)
        {
            _hoveredIndex = null;
            _isFitToView = true;
            _viewTransform = null;
            CellInspected?.Invoke(this, MapGridCellInspectedEventArgs.None);
        }

        if (change.Property == ValuesProperty
            || change.Property == TileBitmapsProperty
            || change.Property == UnavailableFloorIdsProperty
            || change.Property == SelectedIndexProperty
            || change.Property == SelectedObjectProperty
            || change.Property == PlacementsProperty
            || change.Property == ElevationProperty)
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

        if (!TryGetCurrentTransform(out var transform)
            || !MapFloorTileDiagnosticGeometry.TryGetVisibleCells(
                transform,
                Bounds.Width,
                Bounds.Height,
                out var visibleCells))
        {
            return;
        }

        using var clip = context.PushClip(new Rect(Bounds.Size));
        var values = Values;
        var tileBitmaps = TileBitmaps;
        var unavailableFloorIds = UnavailableFloorIds;
        for (var y = visibleCells.StartY; y <= visibleCells.EndY; y++)
        {
            for (var x = visibleCells.StartX; x <= visibleCells.EndX; x++)
            {
                var index = MapTileGridDecoder.GetSerializedIndex(x, y);
                var cell = MapFloorTileDiagnosticGeometry.GetCellInfo(index, values[index]);
                var bounds = MapFloorTileDiagnosticGeometry.GetCellBounds(transform, x, y);
                context.FillRectangle((x + y) % 2 == 0 ? CellBrush : AlternateCellBrush, bounds);

                if (!cell.IsRenderSkipped
                    && cell.FloorArtId != MapFloorTileDiagnosticGeometry.CeEmptyFloorArtId)
                {
                    if (tileBitmaps is not null && tileBitmaps.TryGetValue(cell.FloorArtId, out var bitmap))
                    {
                        var imageBounds = MapFloorTileDiagnosticGeometry.ContainImage(
                            bounds,
                            bitmap.PixelSize.Width,
                            bitmap.PixelSize.Height);
                        context.DrawImage(
                            bitmap,
                            new Rect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height),
                            imageBounds);
                    }
                    else if (unavailableFloorIds?.Contains(cell.FloorArtId) == true)
                    {
                        context.FillRectangle(MissingBrush, bounds);
                        context.DrawLine(MissingPen, bounds.TopLeft, bounds.BottomRight);
                        context.DrawLine(MissingPen, bounds.TopRight, bounds.BottomLeft);
                    }
                }

                if ((x % 10 == 0) || (y % 10 == 0))
                {
                    context.DrawRectangle(GridPen, bounds);
                }
            }
        }

        if (_hoveredIndex is int hoveredIndex)
        {
            var (x, y) = MapTileGridDecoder.GetSquareGridCoordinates(hoveredIndex);
            if (visibleCells.Contains(x, y))
            {
                var hoverBounds = MapFloorTileDiagnosticGeometry.GetCellBounds(transform, x, y);
                context.FillRectangle(HoverBrush, hoverBounds);
                context.DrawRectangle(HoverPen, hoverBounds);
            }
        }

        if (SelectedIndex is int selectedIndex
            && (uint)selectedIndex < MapTileGridDecoder.ValuesPerGrid)
        {
            var (x, y) = MapTileGridDecoder.GetSquareGridCoordinates(selectedIndex);
            if (visibleCells.Contains(x, y))
            {
                context.DrawRectangle(
                    SelectionPen,
                    MapFloorTileDiagnosticGeometry.GetCellBounds(transform, x, y));
            }
        }

        foreach (var cellGroup in _floorSamples
                         .Where(sample => visibleCells.Contains(sample.X, sample.Y))
                         .GroupBy(sample => (sample.X, sample.Y)))
        {
            var center = transform.ToScreen(new Point(cellGroup.Key.X + 0.5, cellGroup.Key.Y + 0.5));
            var samples = cellGroup.OrderBy(sample => sample.Placement.RecordIndex).ToArray();
            var spread = Math.Min(transform.Scale * 0.18, 3);
            var radius = Math.Clamp(transform.Scale * 0.3, 1.5, 5);
            for (var index = 0; index < samples.Length; index++)
            {
                    var sample = samples[index];
                    var angle = samples.Length == 1 ? 0 : 2 * Math.PI * index / samples.Length;
                    var markerCenter = new Point(
                        center.X + Math.Cos(angle) * spread,
                        center.Y + Math.Sin(angle) * spread);
                    var isSelected = sample.Placement.RecordIndex == SelectedObject?.RecordIndex;
                    context.DrawEllipse(
                        isSelected ? ObjectSelectionBrush : GetObjectBrush(sample.Placement.PidTypeIdentifier),
                        null,
                        markerCenter,
                        radius,
                        radius);
                    if (isSelected)
                    {
                        context.DrawEllipse(
                            null,
                            ObjectSelectionPen,
                            markerCenter,
                            radius + 2,
                            radius + 2);
                    }
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_panLastPosition is Point last && _viewTransform is MapFloorGridViewTransform current)
        {
            var position = e.GetPosition(this);
            _viewTransform = MapFloorTileDiagnosticGeometry.Pan(
                current,
                position.X - last.X,
                position.Y - last.Y,
                Bounds.Width,
                Bounds.Height);
            _panLastPosition = position;
            _isFitToView = false;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

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
        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsRightButtonPressed || properties.IsMiddleButtonPressed)
        {
            if (TryGetCurrentTransform(out var current))
            {
                _viewTransform = current;
                _panLastPosition = e.GetPosition(this);
                _isFitToView = false;
                e.Pointer.Capture(this);
                e.Handled = true;
            }

            return;
        }

        if (!properties.IsLeftButtonPressed)
        {
            return;
        }

        var position = e.GetPosition(this);
        if (TryGetCurrentTransform(out var transform)
            && MapObjectFloorSampleGeometry.TrySelectAtScreenPosition(
                _floorSamples,
                SelectedObject?.RecordIndex,
                position.X,
                position.Y,
                transform,
                Bounds.Width,
                Bounds.Height,
                out var selectedObject))
        {
            ObjectSelected?.Invoke(
                this,
                new MapObjectPlacementSelectedEventArgs(selectedObject!));
            e.Handled = true;
            return;
        }

        if (Values is not { Length: MapTileGridDecoder.ValuesPerGrid } values
            || !TryGetSerializedIndex(
                position.X,
                position.Y,
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

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_panLastPosition is not null)
        {
            _panLastPosition = null;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _panLastPosition = null;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.Delta.Y == 0 || !TryGetCurrentTransform(out var current))
        {
            return;
        }

        var position = e.GetPosition(this);
        var zoomed = MapFloorTileDiagnosticGeometry.ZoomAt(
            current,
            position.X,
            position.Y,
            Math.Pow(ZoomStep, e.Delta.Y),
            Bounds.Width,
            Bounds.Height);
        if (zoomed != current)
        {
            _viewTransform = zoomed;
            _isFitToView = false;
            InvalidateVisual();
        }

        e.Handled = true;
    }

    private bool TryGetCurrentTransform(out MapFloorGridViewTransform transform)
    {
        if (_viewTransform is MapFloorGridViewTransform stored && stored.IsValid)
        {
            transform = stored;
            return true;
        }

        return MapFloorTileDiagnosticGeometry.TryGetFitTransform(
            Bounds.Width,
            Bounds.Height,
            out transform);
    }

    private bool TryGetSerializedIndex(double positionX, double positionY, out int index)
    {
        if (TryGetCurrentTransform(out var transform))
        {
            return MapFloorTileDiagnosticGeometry.TryGetSerializedIndex(
                positionX,
                positionY,
                transform,
                Bounds.Width,
                Bounds.Height,
                out index);
        }

        index = default;
        return false;
    }

    private void InspectCell(Point position)
    {
        if (Values is not { Length: MapTileGridDecoder.ValuesPerGrid } values
            || !TryGetSerializedIndex(position.X, position.Y, out var index))
        {
            SetHoveredIndex(null);
            return;
        }

        var (x, y) = MapTileGridDecoder.GetSquareGridCoordinates(index);
        SetHoveredIndex(index, x, y, values[index]);
    }

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

    private void Placements_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        UpdateFloorSamples();

    private void UpdateFloorSamples()
    {
        _floorSamples = MapObjectFloorSampleGeometry.GetSamples(Placements ?? [], Elevation);
        InvalidateVisual();
    }

    private static IBrush GetObjectBrush(int pidTypeIdentifier) => pidTypeIdentifier switch
    {
        1 => CritterBrush,
        2 => SceneryBrush,
        3 => WallBrush,
        4 => TileBrush,
        5 => MiscBrush,
        0 => ItemBrush,
        _ => DefaultObjectBrush
    };
}

public readonly record struct MapFloorGridViewTransform(double Scale, double OffsetX, double OffsetY)
{
    public bool IsValid =>
        double.IsFinite(Scale) && Scale > 0
        && double.IsFinite(OffsetX) && double.IsFinite(OffsetY);

    public Point ToScreen(Point gridPoint) =>
        new(gridPoint.X * Scale + OffsetX, gridPoint.Y * Scale + OffsetY);

    public Point ToGrid(Point screenPoint) =>
        new((screenPoint.X - OffsetX) / Scale, (screenPoint.Y - OffsetY) / Scale);
}

public readonly record struct MapFloorVisibleCells(int StartX, int StartY, int EndX, int EndY)
{
    public bool Contains(int x, int y) =>
        x >= StartX && x <= EndX && y >= StartY && y <= EndY;
}

public readonly record struct MapFloorTileCellInfo(
    int SerializedIndex,
    int X,
    int Y,
    uint FloorArtId,
    bool IsRenderSkipped);

public static class MapFloorTileDiagnosticGeometry
{
    private const double Margin = 12;
    public const uint CeEmptyFloorArtId = 1;
    public const double MinZoom = 0.25;
    public const double MaxZoom = 32;

    public static bool TryGetFitTransform(
        double viewWidth,
        double viewHeight,
        out MapFloorGridViewTransform transform)
    {
        transform = default;
        if (!IsValidViewSize(viewWidth, viewHeight)
            || viewWidth <= Margin * 2
            || viewHeight <= Margin * 2)
        {
            return false;
        }

        var scale = Math.Min(
            (viewWidth - Margin * 2) / MapTileGridDecoder.GridWidth,
            (viewHeight - Margin * 2) / MapTileGridDecoder.GridHeight);
        if (!double.IsFinite(scale) || scale <= 0)
        {
            return false;
        }

        transform = new MapFloorGridViewTransform(
            scale,
            (viewWidth - MapTileGridDecoder.GridWidth * scale) / 2,
            (viewHeight - MapTileGridDecoder.GridHeight * scale) / 2);
        return true;
    }

    public static MapFloorGridViewTransform ZoomAt(
        MapFloorGridViewTransform current,
        double cursorX,
        double cursorY,
        double factor,
        double viewWidth,
        double viewHeight)
    {
        if (!current.IsValid
            || !double.IsFinite(cursorX)
            || !double.IsFinite(cursorY)
            || !double.IsFinite(factor)
            || factor <= 0
            || !TryGetFitTransform(viewWidth, viewHeight, out var fit))
        {
            return current;
        }

        var anchor = current.ToGrid(new Point(cursorX, cursorY));
        var scale = Math.Clamp(
            current.Scale * factor,
            fit.Scale * MinZoom,
            fit.Scale * MaxZoom);
        var zoomed = new MapFloorGridViewTransform(
            scale,
            cursorX - anchor.X * scale,
            cursorY - anchor.Y * scale);
        return ClampToView(zoomed, viewWidth, viewHeight);
    }

    public static MapFloorGridViewTransform Pan(
        MapFloorGridViewTransform current,
        double deltaX,
        double deltaY,
        double viewWidth,
        double viewHeight)
    {
        if (!current.IsValid
            || !double.IsFinite(deltaX)
            || !double.IsFinite(deltaY)
            || !IsValidViewSize(viewWidth, viewHeight))
        {
            return current;
        }

        return ClampToView(
            current with
            {
                OffsetX = current.OffsetX + deltaX,
                OffsetY = current.OffsetY + deltaY
            },
            viewWidth,
            viewHeight);
    }

    public static bool TryGetResizedTransform(
        MapFloorGridViewTransform current,
        double previousWidth,
        double previousHeight,
        double newWidth,
        double newHeight,
        out MapFloorGridViewTransform resized)
    {
        resized = default;
        if (!current.IsValid
            || !IsValidViewSize(previousWidth, previousHeight)
            || !TryGetFitTransform(newWidth, newHeight, out var fit))
        {
            return false;
        }

        var previousCenter = new Point(previousWidth / 2, previousHeight / 2);
        var gridCenter = current.ToGrid(previousCenter);
        var scale = Math.Clamp(
            current.Scale,
            fit.Scale * MinZoom,
            fit.Scale * MaxZoom);
        resized = ClampToView(
            new MapFloorGridViewTransform(
                scale,
                newWidth / 2 - gridCenter.X * scale,
                newHeight / 2 - gridCenter.Y * scale),
            newWidth,
            newHeight);
        return true;
    }

    public static bool TryGetSerializedIndex(
        double positionX,
        double positionY,
        MapFloorGridViewTransform transform,
        double viewWidth,
        double viewHeight,
        out int serializedIndex)
    {
        serializedIndex = default;
        if (!transform.IsValid
            || !IsValidViewSize(viewWidth, viewHeight)
            || !double.IsFinite(positionX)
            || !double.IsFinite(positionY)
            || positionX < 0 || positionX >= viewWidth
            || positionY < 0 || positionY >= viewHeight)
        {
            return false;
        }

        var gridPoint = transform.ToGrid(new Point(positionX, positionY));
        if (gridPoint.X < 0 || gridPoint.X >= MapTileGridDecoder.GridWidth
            || gridPoint.Y < 0 || gridPoint.Y >= MapTileGridDecoder.GridHeight)
        {
            return false;
        }

        serializedIndex = MapTileGridDecoder.GetSerializedIndex(
            (int)Math.Floor(gridPoint.X),
            (int)Math.Floor(gridPoint.Y));
        return true;
    }

    public static bool TryGetVisibleCells(
        MapFloorGridViewTransform transform,
        double viewWidth,
        double viewHeight,
        out MapFloorVisibleCells cells)
    {
        cells = default;
        if (!transform.IsValid || !IsValidViewSize(viewWidth, viewHeight))
        {
            return false;
        }

        var left = Math.Max(0, (0 - transform.OffsetX) / transform.Scale);
        var top = Math.Max(0, (0 - transform.OffsetY) / transform.Scale);
        var right = Math.Min(
            MapTileGridDecoder.GridWidth,
            (viewWidth - transform.OffsetX) / transform.Scale);
        var bottom = Math.Min(
            MapTileGridDecoder.GridHeight,
            (viewHeight - transform.OffsetY) / transform.Scale);
        if (!double.IsFinite(left)
            || !double.IsFinite(top)
            || !double.IsFinite(right)
            || !double.IsFinite(bottom)
            || right <= left
            || bottom <= top)
        {
            return false;
        }

        cells = new MapFloorVisibleCells(
            Math.Clamp((int)Math.Floor(left), 0, MapTileGridDecoder.GridWidth - 1),
            Math.Clamp((int)Math.Floor(top), 0, MapTileGridDecoder.GridHeight - 1),
            Math.Clamp((int)Math.Ceiling(right) - 1, 0, MapTileGridDecoder.GridWidth - 1),
            Math.Clamp((int)Math.Ceiling(bottom) - 1, 0, MapTileGridDecoder.GridHeight - 1));
        return true;
    }

    public static Rect GetCellBounds(
        MapFloorGridViewTransform transform,
        int x,
        int y)
    {
        ValidateGridCoordinates(x, y);
        if (!transform.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(transform));
        }

        var topLeft = transform.ToScreen(new Point(x, y));
        return new Rect(topLeft.X, topLeft.Y, transform.Scale, transform.Scale);
    }

    public static MapFloorTileCellInfo GetCellInfo(int serializedIndex, uint rawValue)
    {
        var (x, y) = MapTileGridDecoder.GetSquareGridCoordinates(serializedIndex);
        var fields = new MapGridCellValueFields(rawValue);
        return new MapFloorTileCellInfo(
            serializedIndex,
            x,
            y,
            fields.FloorArtId,
            fields.FloorRenderSuppressionBitIsSet);
    }

    public static IReadOnlyList<int> GetDrawableFloorIds(IEnumerable<uint> rawValues) =>
        rawValues
            .Select(rawValue => new MapGridCellValueFields(rawValue))
            .Where(fields =>
                !fields.FloorRenderSuppressionBitIsSet
                && fields.FloorArtId != CeEmptyFloorArtId)
            .Select(fields => checked((int)fields.FloorArtId))
            .Distinct()
            .ToArray();

    public static Rect GetCellBounds(Rect gridBounds, double cellSize, int x, int y)
    {
        if ((uint)x >= MapTileGridDecoder.GridWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        if ((uint)y >= MapTileGridDecoder.GridHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }

        if (!double.IsFinite(cellSize) || cellSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize));
        }

        return new Rect(
            gridBounds.X + x * cellSize,
            gridBounds.Y + y * cellSize,
            cellSize,
            cellSize);
    }

    public static Rect ContainImage(Rect cellBounds, int imageWidth, int imageHeight)
    {
        if (imageWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(imageWidth));
        }

        if (imageHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(imageHeight));
        }

        var scale = Math.Min(cellBounds.Width / imageWidth, cellBounds.Height / imageHeight);
        var width = imageWidth * scale;
        var height = imageHeight * scale;
        return new Rect(
            cellBounds.X + (cellBounds.Width - width) / 2,
            cellBounds.Y + (cellBounds.Height - height) / 2,
            width,
            height);
    }

    private static MapFloorGridViewTransform ClampToView(
        MapFloorGridViewTransform transform,
        double viewWidth,
        double viewHeight)
    {
        if (!transform.IsValid
            || !IsValidViewSize(viewWidth, viewHeight))
        {
            return transform;
        }

        var mapWidth = MapTileGridDecoder.GridWidth * transform.Scale;
        var mapHeight = MapTileGridDecoder.GridHeight * transform.Scale;
        return transform with
        {
            OffsetX = Math.Clamp(transform.OffsetX, Margin - mapWidth, viewWidth - Margin),
            OffsetY = Math.Clamp(transform.OffsetY, Margin - mapHeight, viewHeight - Margin)
        };
    }

    private static bool IsValidViewSize(double width, double height) =>
        double.IsFinite(width)
        && double.IsFinite(height)
        && width > 0
        && height > 0;

    private static void ValidateGridCoordinates(int x, int y)
    {
        if ((uint)x >= MapTileGridDecoder.GridWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        if ((uint)y >= MapTileGridDecoder.GridHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }
    }
}
