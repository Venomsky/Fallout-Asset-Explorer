using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using FalloutAssetExplorer.Core;

namespace FalloutAssetExplorer.App.Views;

public sealed class MapHexGridDiagnosticView : Control
{
    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.Parse("#171A1F"));
    private static readonly IBrush GuideBrush = new SolidColorBrush(Color.Parse("#39414B"));
    private static readonly IPen GuidePen = new Pen(GuideBrush, 0.75);
    private static readonly IBrush SelectionBrush = new SolidColorBrush(Color.Parse("#F2C14E"));
    private static readonly IPen SelectionPen = new Pen(SelectionBrush, 2);
    private static readonly IBrush DefaultObjectBrush = new SolidColorBrush(Color.Parse("#5BC0EB"));
    private static readonly IBrush WallBrush = new SolidColorBrush(Color.Parse("#B8A98A"));
    // Misc objects (type 5) often form long dense lines; keep them translucent so they read as background.
    private static readonly IBrush MiscBrush = new SolidColorBrush(Color.FromArgb(120, 91, 192, 235));

    private const double ZoomStep = 1.2;

    private MapHexViewTransform? _viewTransform;
    private Point? _panLastPosition;

    public static readonly StyledProperty<IReadOnlyList<MapObjectPlacement>?> PlacementsProperty =
        AvaloniaProperty.Register<MapHexGridDiagnosticView, IReadOnlyList<MapObjectPlacement>?>(
            nameof(Placements));

    public static readonly StyledProperty<int?> ElevationProperty =
        AvaloniaProperty.Register<MapHexGridDiagnosticView, int?>(nameof(Elevation));

    public static readonly StyledProperty<MapObjectPlacement?> SelectedObjectProperty =
        AvaloniaProperty.Register<MapHexGridDiagnosticView, MapObjectPlacement?>(
            nameof(SelectedObject),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

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

            _viewTransform = null;
            InvalidateVisual();
        }
        else if (change.Property == ElevationProperty)
        {
            FitToView();
        }
        else if (change.Property == SelectedObjectProperty)
        {
            InvalidateVisual();
        }
    }

    // Fits the placed objects of the current elevation; an empty elevation restores the whole grid.
    public void FitToView()
    {
        _viewTransform = MapHexGridDiagnosticGeometry.TryGetFitTransform(
            Placements ?? [],
            Elevation,
            Bounds.Width,
            Bounds.Height,
            out var fit)
            ? fit
            : null;
        InvalidateVisual();
    }

    // Centers the selected object at the current zoom; does nothing when it is not drawable here.
    public void CenterOnSelection()
    {
        if (SelectedObject is not { HexX: int hexX, HexY: int hexY } selected
            || selected.ElevationGroup != Elevation
            || !TryGetCurrentTransform(out var current)
            || !MapHexGridDiagnosticGeometry.TryGetCenteredTransform(
                current,
                hexX,
                hexY,
                Bounds.Width,
                Bounds.Height,
                out var centered))
        {
            return;
        }

        _viewTransform = centered;
        InvalidateVisual();
    }

    private bool TryGetCurrentTransform(out MapHexViewTransform transform)
    {
        if (_viewTransform is MapHexViewTransform stored)
        {
            transform = stored;
            return true;
        }

        return MapHexGridDiagnosticGeometry.TryGetBaseTransform(
            Bounds.Width,
            Bounds.Height,
            out transform);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(BackgroundBrush, new Rect(Bounds.Size));
        if (!TryGetCurrentTransform(out var transform))
        {
            return;
        }

        using var clip = context.PushClip(new Rect(Bounds.Size));
        DrawGridGuides(context, transform);
        var visiblePlacements = MapHexGridDiagnosticGeometry.GetPlacedPlacements(
            Placements ?? [],
            Elevation);
        foreach (var group in visiblePlacements.GroupBy(
                     placement => (X: placement.HexX!.Value, Y: placement.HexY!.Value)))
        {
            if (!MapHexGridDiagnosticGeometry.TryGetScreenPosition(
                    group.Key.X,
                    group.Key.Y,
                    transform,
                    out var center))
            {
                continue;
            }

            var groupPlacements = group.ToArray();
            foreach (var placement in groupPlacements
                         .OrderBy(placement => placement.RecordIndex == SelectedObject?.RecordIndex
                             ? 2
                             : MapHexGridDiagnosticGeometry.GetMarkerDrawOrder(placement.PidTypeIdentifier)))
            {
                var isSelected = placement.RecordIndex == SelectedObject?.RecordIndex;
                var radius = MapHexGridDiagnosticGeometry.GetMarkerRadius(
                    isSelected ? -1 : placement.PidTypeIdentifier,
                    transform.Scale);
                context.DrawEllipse(
                    isSelected ? SelectionBrush : GetObjectBrush(placement.PidTypeIdentifier),
                    null,
                    center,
                    radius,
                    radius);
            }

            if (groupPlacements.Any(placement => placement.RecordIndex == SelectedObject?.RecordIndex))
            {
                context.DrawEllipse(null, SelectionPen, center, 7, 7);
            }
        }
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
                e.Pointer.Capture(this);
                e.Handled = true;
            }

            return;
        }

        if (!properties.IsLeftButtonPressed
            || !TryGetCurrentTransform(out var transform)
            || !MapHexGridDiagnosticGeometry.TrySelectPlacementAtPosition(
                Placements ?? [],
                Elevation,
                SelectedObject?.RecordIndex,
                e.GetPosition(this).X,
                e.GetPosition(this).Y,
                transform,
                out var placement))
        {
            return;
        }

        SetCurrentValue(SelectedObjectProperty, placement);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_panLastPosition is not Point last || _viewTransform is not MapHexViewTransform current)
        {
            return;
        }

        var position = e.GetPosition(this);
        _viewTransform = MapHexGridDiagnosticGeometry.Pan(
            current,
            position.X - last.X,
            position.Y - last.Y,
            Bounds.Width,
            Bounds.Height);
        _panLastPosition = position;
        InvalidateVisual();
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
        _viewTransform = MapHexGridDiagnosticGeometry.ZoomAt(
            current,
            position.X,
            position.Y,
            Math.Pow(ZoomStep, e.Delta.Y),
            Bounds.Width,
            Bounds.Height);
        InvalidateVisual();
        e.Handled = true;
    }

    private void DrawGridGuides(DrawingContext context, MapHexViewTransform transform)
    {
        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            for (var x = 0; x < MapHexGridDiagnosticGeometry.GridWidth; x += 20)
            {
                if (!MapHexGridDiagnosticGeometry.TryGetScreenPosition(
                        x,
                        0,
                        transform,
                        out var start)
                    || !MapHexGridDiagnosticGeometry.TryGetScreenPosition(
                        x,
                        MapHexGridDiagnosticGeometry.GridHeight - 1,
                        transform,
                        out var end))
                {
                    return;
                }

                geometryContext.BeginFigure(start, false);
                geometryContext.LineTo(end);
                geometryContext.EndFigure(false);
            }

            for (var y = 0; y < MapHexGridDiagnosticGeometry.GridHeight; y += 20)
            {
                if (!MapHexGridDiagnosticGeometry.TryGetScreenPosition(
                        0,
                        y,
                        Bounds.Width,
                        Bounds.Height,
                        out var first))
                {
                    return;
                }

                geometryContext.BeginFigure(first, false);
                for (var x = 1; x < MapHexGridDiagnosticGeometry.GridWidth; x++)
                {
                    if (!MapHexGridDiagnosticGeometry.TryGetScreenPosition(
                            x,
                            y,
                            Bounds.Width,
                            Bounds.Height,
                            out var point))
                    {
                        return;
                    }

                    geometryContext.LineTo(point);
                }

                geometryContext.EndFigure(false);
            }
        }

        context.DrawGeometry(null, GuidePen, geometry);
    }

    private void Placements_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _viewTransform = null;
        }

        InvalidateVisual();
    }

    private static IBrush GetObjectBrush(int pidType) =>
        pidType switch
        {
            0 => Brushes.LightGreen,
            1 => Brushes.Orange,
            2 => Brushes.MediumPurple,
            3 => WallBrush,
            5 => MiscBrush,
            _ => DefaultObjectBrush
        };
}

public readonly record struct MapHexViewTransform(double Scale, double OffsetX, double OffsetY)
{
    public bool IsValid =>
        double.IsFinite(Scale) && Scale > 0 && double.IsFinite(OffsetX) && double.IsFinite(OffsetY);

    public Point ToScreen(Point projected) =>
        new(projected.X * Scale + OffsetX, projected.Y * Scale + OffsetY);

    public Point ToProjected(Point screen) =>
        new((screen.X - OffsetX) / Scale, (screen.Y - OffsetY) / Scale);
}

public static class MapHexGridDiagnosticGeometry
{
    private const double Margin = 16;
    private const double HitRadius = 9;
    public const double MinZoom = 0.5;
    public const double MaxZoom = 32;
    private const double SinglePointZoom = 4;
    private const int CenterColumn = 100;
    private const int CenterRow = 100;

    private static readonly (double MinX, double MinY, double MaxX, double MaxY) ProjectedBounds =
        GetProjectedBounds();

    public const int GridWidth = MapObjectPlacementDecoder.HexGridWidth;
    public const int GridHeight = MapObjectPlacementDecoder.HexGridHeight;

    public static Point GetRelativeScreenPosition(int hexX, int hexY)
    {
        ValidateCoordinates(hexX, hexY);

        var columnFromCenter = hexX - CenterColumn;
        var rowFromCenter = hexY - CenterRow;
        var screenX = 48 * (columnFromCenter / 2) + 16 * rowFromCenter;
        var screenY = 12 * (columnFromCenter / -2) + 12 * rowFromCenter;

        if ((hexX & 1) != 0)
        {
            if (hexX <= CenterColumn)
            {
                screenX -= 16;
                screenY += 12;
            }
            else
            {
                screenX += 32;
            }
        }

        return new Point(screenX, screenY);
    }

    public static bool TryGetScreenPosition(
        int hexX,
        int hexY,
        double viewWidth,
        double viewHeight,
        out Point screenPosition)
    {
        screenPosition = default;
        return TryGetBaseTransform(viewWidth, viewHeight, out var transform)
            && TryGetScreenPosition(hexX, hexY, transform, out screenPosition);
    }

    // Base projected coordinates -> screen, using the view transform.
    public static bool TryGetScreenPosition(
        int hexX,
        int hexY,
        MapHexViewTransform transform,
        out Point screenPosition)
    {
        screenPosition = default;
        if ((uint)hexX >= GridWidth || (uint)hexY >= GridHeight || !transform.IsValid)
        {
            return false;
        }

        screenPosition = transform.ToScreen(GetRelativeScreenPosition(hexX, hexY));
        return true;
    }

    // Whole grid fitted and centered in the view; the unzoomed, unpanned state.
    public static bool TryGetBaseTransform(
        double viewWidth,
        double viewHeight,
        out MapHexViewTransform transform)
    {
        transform = default;
        var scale = GetScale(viewWidth, viewHeight);
        if (scale <= 0 || !double.IsFinite(scale))
        {
            return false;
        }

        var drawingWidth = (ProjectedBounds.MaxX - ProjectedBounds.MinX) * scale;
        var drawingHeight = (ProjectedBounds.MaxY - ProjectedBounds.MinY) * scale;
        transform = new MapHexViewTransform(
            scale,
            (viewWidth - drawingWidth) / 2 - ProjectedBounds.MinX * scale,
            (viewHeight - drawingHeight) / 2 - ProjectedBounds.MinY * scale);
        return true;
    }

    public static bool TryGetFitTransform(
        IEnumerable<MapObjectPlacement> placements,
        int? elevation,
        double viewWidth,
        double viewHeight,
        out MapHexViewTransform transform)
    {
        if (!TryGetBaseTransform(viewWidth, viewHeight, out var baseTransform))
        {
            transform = default;
            return false;
        }

        var placed = GetPlacedPlacements(placements, elevation);
        if (placed.Count == 0)
        {
            transform = baseTransform;
            return true;
        }

        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        foreach (var placement in placed)
        {
            var point = GetRelativeScreenPosition(placement.HexX!.Value, placement.HexY!.Value);
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        var extentX = maxX - minX;
        var extentY = maxY - minY;
        var availableWidth = viewWidth - Margin * 2;
        var availableHeight = viewHeight - Margin * 2;
        var scale = Math.Min(
            extentX > 0 ? availableWidth / extentX : double.PositiveInfinity,
            extentY > 0 ? availableHeight / extentY : double.PositiveInfinity);
        if (double.IsInfinity(scale))
        {
            scale = baseTransform.Scale * SinglePointZoom;
        }

        scale = ClampScale(scale, baseTransform.Scale);
        transform = new MapHexViewTransform(
            scale,
            viewWidth / 2 - (minX + maxX) / 2 * scale,
            viewHeight / 2 - (minY + maxY) / 2 * scale);
        return true;
    }

    // Keeps the current zoom and moves the hex to the center of the view.
    public static bool TryGetCenteredTransform(
        MapHexViewTransform current,
        int hexX,
        int hexY,
        double viewWidth,
        double viewHeight,
        out MapHexViewTransform transform)
    {
        transform = default;
        if ((uint)hexX >= GridWidth
            || (uint)hexY >= GridHeight
            || !current.IsValid
            || !double.IsFinite(viewWidth)
            || !double.IsFinite(viewHeight)
            || viewWidth <= 0
            || viewHeight <= 0)
        {
            return false;
        }

        var projected = GetRelativeScreenPosition(hexX, hexY);
        transform = new MapHexViewTransform(
            current.Scale,
            viewWidth / 2 - projected.X * current.Scale,
            viewHeight / 2 - projected.Y * current.Scale);
        return true;
    }

    // Zooms by factor while keeping the projected point under the cursor fixed.
    public static MapHexViewTransform ZoomAt(
        MapHexViewTransform current,
        double cursorX,
        double cursorY,
        double factor,
        double viewWidth,
        double viewHeight)
    {
        if (!current.IsValid
            || !double.IsFinite(factor)
            || factor <= 0
            || !TryGetBaseTransform(viewWidth, viewHeight, out var baseTransform))
        {
            return current;
        }

        var anchor = current.ToProjected(new Point(cursorX, cursorY));
        var scale = ClampScale(current.Scale * factor, baseTransform.Scale);
        var zoomed = new MapHexViewTransform(
            scale,
            cursorX - anchor.X * scale,
            cursorY - anchor.Y * scale);
        return ClampToView(zoomed, viewWidth, viewHeight);
    }

    public static MapHexViewTransform Pan(
        MapHexViewTransform current,
        double deltaX,
        double deltaY,
        double viewWidth,
        double viewHeight) =>
        !current.IsValid || !double.IsFinite(deltaX) || !double.IsFinite(deltaY)
            ? current
            : ClampToView(
                new MapHexViewTransform(current.Scale, current.OffsetX + deltaX, current.OffsetY + deltaY),
                viewWidth,
                viewHeight);

    public static double ClampScale(double scale, double baseScale) =>
        Math.Clamp(scale, baseScale * MinZoom, baseScale * MaxZoom);

    // Keeps at least a margin-wide strip of the grid inside the view so it cannot be panned away.
    private static MapHexViewTransform ClampToView(
        MapHexViewTransform transform,
        double viewWidth,
        double viewHeight)
    {
        if (!double.IsFinite(viewWidth) || !double.IsFinite(viewHeight))
        {
            return transform;
        }

        var offsetX = Math.Clamp(
            transform.OffsetX,
            Margin - ProjectedBounds.MaxX * transform.Scale,
            viewWidth - Margin - ProjectedBounds.MinX * transform.Scale);
        var offsetY = Math.Clamp(
            transform.OffsetY,
            Margin - ProjectedBounds.MaxY * transform.Scale,
            viewHeight - Margin - ProjectedBounds.MinY * transform.Scale);
        return transform with { OffsetX = offsetX, OffsetY = offsetY };
    }

    public static double GetMarkerRadius(int pidType, double scale)
    {
        var radius = Math.Clamp(scale * 0.4, 2, 4.5);
        return pidType switch
        {
            3 => radius * 0.75,
            5 => radius * 0.6,
            _ => radius
        };
    }

    // Walls and misc markers are drawn first so critters, items and scenery stay on top.
    public static int GetMarkerDrawOrder(int pidType) =>
        pidType is 3 or 5 ? 0 : 1;

    public static double GetScale(double viewWidth, double viewHeight)
    {
        if (!double.IsFinite(viewWidth)
            || !double.IsFinite(viewHeight)
            || viewWidth <= Margin * 2
            || viewHeight <= Margin * 2)
        {
            return 0;
        }

        return Math.Min(
            (viewWidth - Margin * 2) / (ProjectedBounds.MaxX - ProjectedBounds.MinX),
            (viewHeight - Margin * 2) / (ProjectedBounds.MaxY - ProjectedBounds.MinY));
    }

    public static IReadOnlyList<MapObjectPlacement> GetPlacedPlacements(
        IEnumerable<MapObjectPlacement> placements,
        int? elevation) =>
        elevation is int selectedElevation
            ? placements
                .Where(placement => placement.ElevationGroup == selectedElevation
                    && placement.HexX is int x
                    && (uint)x < GridWidth
                    && placement.HexY is int y
                    && (uint)y < GridHeight)
                .ToArray()
            : [];

    public static bool TrySelectPlacementAtPosition(
        IReadOnlyList<MapObjectPlacement> placements,
        int? elevation,
        int? currentRecordIndex,
        double positionX,
        double positionY,
        double viewWidth,
        double viewHeight,
        out MapObjectPlacement? selectedPlacement)
    {
        if (!TryGetBaseTransform(viewWidth, viewHeight, out var transform))
        {
            selectedPlacement = null;
            return false;
        }

        return TrySelectPlacementAtPosition(
            placements,
            elevation,
            currentRecordIndex,
            positionX,
            positionY,
            transform,
            out selectedPlacement);
    }

    public static bool TrySelectPlacementAtPosition(
        IReadOnlyList<MapObjectPlacement> placements,
        int? elevation,
        int? currentRecordIndex,
        double positionX,
        double positionY,
        MapHexViewTransform transform,
        out MapObjectPlacement? selectedPlacement)
    {
        selectedPlacement = null;
        if (!double.IsFinite(positionX) || !double.IsFinite(positionY) || !transform.IsValid)
        {
            return false;
        }

        var visible = GetPlacedPlacements(placements, elevation);
        var nearestDistanceSquared = HitRadius * HitRadius;
        MapObjectPlacement? nearest = null;
        foreach (var placement in visible)
        {
            if (!TryGetScreenPosition(
                    placement.HexX!.Value,
                    placement.HexY!.Value,
                    transform,
                    out var point))
            {
                continue;
            }

            var distanceX = point.X - positionX;
            var distanceY = point.Y - positionY;
            var distanceSquared = distanceX * distanceX + distanceY * distanceY;
            if (distanceSquared < nearestDistanceSquared)
            {
                nearest = placement;
                nearestDistanceSquared = distanceSquared;
            }
        }

        if (nearest is null)
        {
            return false;
        }

        var sameHex = visible
            .Where(placement => placement.HexX == nearest.HexX && placement.HexY == nearest.HexY)
            .OrderBy(placement => placement.RecordIndex)
            .ToArray();
        var currentIndex = Array.FindIndex(
            sameHex,
            placement => placement.RecordIndex == currentRecordIndex);
        selectedPlacement = sameHex[(currentIndex + 1) % sameHex.Length];
        return true;
    }

    private static (double MinX, double MinY, double MaxX, double MaxY) GetProjectedBounds()
    {
        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        for (var y = 0; y < GridHeight; y++)
        {
            for (var x = 0; x < GridWidth; x++)
            {
                var point = GetRelativeScreenPosition(x, y);
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
            }
        }

        return (minX, minY, maxX, maxY);
    }

    private static void ValidateCoordinates(int hexX, int hexY)
    {
        if ((uint)hexX >= GridWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(hexX));
        }

        if ((uint)hexY >= GridHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(hexY));
        }
    }
}
