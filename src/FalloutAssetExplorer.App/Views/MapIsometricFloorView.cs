using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using FalloutAssetExplorer.Core;

namespace FalloutAssetExplorer.App.Views;

public sealed class MapIsometricFloorView : Control
{
    private const int ProjectionCenterHexX = 100;
    private const int ProjectionCenterHexY = 100;
    private const int ProjectionViewportWidth = 640;
    private const int ProjectionViewportHeight = 480;
    private const double ZoomStep = 1.2;

    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.Parse("#171A1F"));
    private static readonly IPen ObjectMarkerOutlinePen =
        new Pen(new SolidColorBrush(Color.Parse("#171A1F")), 1);
    private static readonly IPen SelectedObjectMarkerPen =
        new Pen(new SolidColorBrush(Color.Parse("#F2C14E")), 2);
    private static readonly IBrush ItemMarkerBrush = new SolidColorBrush(Color.Parse("#F2C14E"));
    private static readonly IBrush CritterMarkerBrush = new SolidColorBrush(Color.Parse("#6FCF97"));
    private static readonly IBrush SceneryMarkerBrush = new SolidColorBrush(Color.Parse("#57CC99"));
    private static readonly IBrush WallMarkerBrush = new SolidColorBrush(Color.Parse("#B8A98A"));
    private static readonly IBrush TileMarkerBrush = new SolidColorBrush(Color.Parse("#C792EA"));
    private static readonly IBrush MiscMarkerBrush = new SolidColorBrush(Color.Parse("#5BC0EB"));
    private static readonly IBrush InterfaceMarkerBrush = new SolidColorBrush(Color.Parse("#F78C6B"));
    private static readonly IBrush InventoryMarkerBrush = new SolidColorBrush(Color.Parse("#E76F9A"));
    private static readonly IBrush HeadMarkerBrush = new SolidColorBrush(Color.Parse("#A7C957"));
    private static readonly IBrush BackgroundMarkerBrush = new SolidColorBrush(Color.Parse("#8892A0"));
    private static readonly IBrush SkilldexMarkerBrush = new SolidColorBrush(Color.Parse("#FFD166"));
    private static readonly IBrush DefaultObjectMarkerBrush = new SolidColorBrush(Color.Parse("#E7EAF0"));

    public static readonly StyledProperty<uint[]?> ValuesProperty =
        AvaloniaProperty.Register<MapIsometricFloorView, uint[]?>(nameof(Values));

    public static readonly StyledProperty<IReadOnlyDictionary<uint, Bitmap>?> TileBitmapsProperty =
        AvaloniaProperty.Register<MapIsometricFloorView, IReadOnlyDictionary<uint, Bitmap>?>(nameof(TileBitmaps));

    public static readonly StyledProperty<int> AssetRevisionProperty =
        AvaloniaProperty.Register<MapIsometricFloorView, int>(nameof(AssetRevision));

    public static readonly StyledProperty<IReadOnlyList<MapObjectPlacement>?> PlacementsProperty =
        AvaloniaProperty.Register<MapIsometricFloorView, IReadOnlyList<MapObjectPlacement>?>(
            nameof(Placements));

    public static readonly StyledProperty<int?> ElevationProperty =
        AvaloniaProperty.Register<MapIsometricFloorView, int?>(nameof(Elevation));

    public static readonly StyledProperty<bool> ShowObjectMarkersProperty =
        AvaloniaProperty.Register<MapIsometricFloorView, bool>(nameof(ShowObjectMarkers));

    public static readonly StyledProperty<int?> SelectedRecordIndexProperty =
        AvaloniaProperty.Register<MapIsometricFloorView, int?>(nameof(SelectedRecordIndex));

    public static readonly StyledProperty<MapIsometricObjectSprite?> SelectedObjectSpriteProperty =
        AvaloniaProperty.Register<MapIsometricFloorView, MapIsometricObjectSprite?>(
            nameof(SelectedObjectSprite));

    public static readonly StyledProperty<string?> SpriteRenderDiagnosticDisplayProperty =
        AvaloniaProperty.Register<MapIsometricFloorView, string?>(nameof(SpriteRenderDiagnosticDisplay));

    private MapIsometricFloorCameraTransform? _cameraTransform;
    private IReadOnlyList<MapIsometricObjectMarker>? _objectMarkers;
    private MapIsometricFloorViewTransform? _objectMarkersProjection;
    private int? _objectMarkersElevation;
    private Point? _panLastPosition;
    private Point? _pointerPosition;
    private string? _hoveredPrototypeFileName;
    private bool _isFitToView = true;

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

    public int AssetRevision
    {
        get => GetValue(AssetRevisionProperty);
        set => SetValue(AssetRevisionProperty, value);
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

    public bool ShowObjectMarkers
    {
        get => GetValue(ShowObjectMarkersProperty);
        set => SetValue(ShowObjectMarkersProperty, value);
    }

    public int? SelectedRecordIndex
    {
        get => GetValue(SelectedRecordIndexProperty);
        set => SetValue(SelectedRecordIndexProperty, value);
    }

    public MapIsometricObjectSprite? SelectedObjectSprite
    {
        get => GetValue(SelectedObjectSpriteProperty);
        set => SetValue(SelectedObjectSpriteProperty, value);
    }

    public string? SpriteRenderDiagnosticDisplay
    {
        get => GetValue(SpriteRenderDiagnosticDisplayProperty);
        private set => SetValue(SpriteRenderDiagnosticDisplayProperty, value);
    }

    public event EventHandler<MapObjectMarkerSelectionChangedEventArgs>? ObjectSelectionChanged;

    public void FitToView()
    {
        UpdateFitCamera();
        RefreshHoverToolTip();
        InvalidateVisual();
    }

    private void UpdateFitCamera()
    {
        _isFitToView = true;
        _cameraTransform = TryGetContentBounds(out var contentBounds)
            && MapIsometricFloorViewportGeometry.TryGetFitTransform(
                contentBounds,
                Bounds.Width,
                Bounds.Height,
                out var fit)
                ? fit
                : null;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_isFitToView)
        {
            UpdateFitCamera();
            RefreshHoverToolTip();
            InvalidateVisual();
            return;
        }

        if (_cameraTransform is MapIsometricFloorCameraTransform current
            && TryGetContentBounds(out var contentBounds)
            && MapIsometricFloorViewportGeometry.TryGetResizedTransform(
                current,
                e.PreviousSize.Width,
                e.PreviousSize.Height,
                e.NewSize.Width,
                e.NewSize.Height,
                contentBounds,
                out var resized))
        {
            _cameraTransform = resized;
        }
        else
        {
            UpdateFitCamera();
        }

        RefreshHoverToolTip();
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValuesProperty)
        {
            UpdateFitCamera();
        }
        else if (change.Property == TileBitmapsProperty || change.Property == AssetRevisionProperty)
        {
            UpdateCameraForAssetChange();
        }
        else if (change.Property == PlacementsProperty)
        {
            if (change.OldValue is INotifyCollectionChanged oldPlacements)
            {
                oldPlacements.CollectionChanged -= Placements_CollectionChanged;
            }

            if (change.NewValue is INotifyCollectionChanged newPlacements)
            {
                newPlacements.CollectionChanged += Placements_CollectionChanged;
            }

            InvalidateObjectMarkers();
            PrepareCameraForMarkersIfNeeded();
            ValidateSelectedRecord();
        }
        else if (change.Property == ElevationProperty)
        {
            InvalidateObjectMarkers();
            PrepareCameraForMarkersIfNeeded();
            ValidateSelectedRecord();
        }
        else if (change.Property == ShowObjectMarkersProperty)
        {
            PrepareCameraForMarkersIfNeeded();
        }
        else if (change.Property == SelectedRecordIndexProperty)
        {
            ValidateSelectedRecord();
        }
        else if (change.Property == SelectedObjectSpriteProperty && _isFitToView)
        {
            UpdateFitCamera();
        }

        if (change.Property == ValuesProperty
            || change.Property == TileBitmapsProperty
            || change.Property == AssetRevisionProperty
            || change.Property == PlacementsProperty
            || change.Property == ElevationProperty
            || change.Property == ShowObjectMarkersProperty
            || change.Property == SelectedRecordIndexProperty
            || change.Property == SelectedObjectSpriteProperty)
        {
            InvalidateVisual();
            RefreshHoverToolTip();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(BackgroundBrush, new Rect(Bounds.Size));

        if (SelectedObjectSprite is null)
        {
            SetSpriteRenderDiagnostic(null);
        }
        else if (!ShowObjectMarkers)
        {
            SetSpriteRenderDiagnostic(
                "Prepared sprite not drawn: the object-marker layer is disabled. " +
                "Map-space bounds unavailable (the marker layer is disabled).");
        }

        if (!TryGetProjectionTransform(out var projection)
            || !TryGetCurrentCamera(out var camera))
        {
            if (SelectedObjectSprite is not null && ShowObjectMarkers)
            {
                SetSpriteRenderDiagnostic(
                    "Prepared sprite not drawn: projection or camera geometry is unavailable. " +
                    "Map-space bounds unavailable (projection or camera is unavailable).");
            }

            return;
        }

        using var clip = context.PushClip(new Rect(Bounds.Size));
        if (Values is { Length: MapTileGridDecoder.ValuesPerGrid } values
            && TileBitmaps is { Count: > 0 } tileBitmaps)
        {
            // CE iterates y first and then the serialized square-grid column, i.e. increasing MAP index.
            for (var serializedIndex = 0; serializedIndex < values.Length; serializedIndex++)
            {
                var cell = MapFloorTileDiagnosticGeometry.GetCellInfo(serializedIndex, values[serializedIndex]);
                if (cell.IsRenderSkipped
                    || cell.FloorArtId == MapFloorTileDiagnosticGeometry.CeEmptyFloorArtId
                    || !tileBitmaps.TryGetValue(cell.FloorArtId, out var bitmap))
                {
                    continue;
                }

                var (x, y) = MapTileGridDecoder.GetSquareGridCoordinates(serializedIndex);
                if (!MapIsometricFloorGeometry.TryProjectSquareCoordinates(
                        x,
                        y,
                        projection,
                        out var worldAnchor))
                {
                    continue;
                }

                var imageBounds = MapIsometricFloorViewportGeometry.GetImageBounds(
                    camera,
                    worldAnchor,
                    bitmap.PixelSize.Width,
                    bitmap.PixelSize.Height);
                if (!MapIsometricFloorViewportGeometry.IntersectsViewport(
                        imageBounds,
                        Bounds.Width,
                        Bounds.Height))
                {
                    continue;
                }

                context.DrawImage(
                    bitmap,
                    new Rect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height),
                    imageBounds);
            }
        }

        if (ShowObjectMarkers)
        {
            var markerRadius = MapIsometricObjectMarkerHitTest.GetMarkerRadius(camera.Scale);
            var markers = GetObjectMarkers(projection);
            if (SelectedObjectSprite is { } sprite)
            {
                if (sprite.RecordIndex != SelectedRecordIndex)
                {
                    SetSpriteRenderDiagnostic(
                        "Prepared sprite not drawn: its record does not match the selected record. " +
                        "Map-space bounds unavailable (record mismatch prevents marker lookup).");
                }
                else if (markers.FirstOrDefault(
                             marker => marker.Placement.RecordIndex == sprite.RecordIndex)
                         is not { Placement: not null } spriteMarker)
                {
                    SetSpriteRenderDiagnostic(
                        "Prepared sprite not drawn: no matching object marker is available. " +
                        "Map-space bounds unavailable (no marker position is available).");
                }
                else if (!MapIsometricObjectSpriteGeometry.TryGetImageBounds(
                             spriteMarker.WorldPosition,
                             camera,
                             sprite.Bitmap.PixelSize.Width,
                             sprite.Bitmap.PixelSize.Height,
                             sprite.ArtShiftX,
                             sprite.ArtShiftY,
                             spriteMarker.Placement.RawOffsetX,
                             spriteMarker.Placement.RawOffsetY,
                             out var spriteBounds))
                {
                    SetSpriteRenderDiagnostic(
                        "Prepared sprite not drawn: image bounds could not be calculated. " +
                        "Map-space bounds unavailable (geometry calculation failed).");
                }
                else
                {
                    var mapSpaceBounds = GetMapSpaceBounds(spriteBounds, camera);
                    var mapBoundsDisplay = FormatMapSpaceBounds(mapSpaceBounds);
                    if (!MapIsometricFloorViewportGeometry.IntersectsViewport(
                            spriteBounds,
                            Bounds.Width,
                            Bounds.Height))
                    {
                        SetSpriteRenderDiagnostic(
                            $"Prepared sprite not drawn: image bounds are outside the viewport. " +
                            $"Map-space bounds: {mapBoundsDisplay}.");
                    }
                    else
                    {
                        context.DrawImage(
                            sprite.Bitmap,
                            new Rect(0, 0, sprite.Bitmap.PixelSize.Width, sprite.Bitmap.PixelSize.Height),
                            spriteBounds);
                        SetSpriteRenderDiagnostic(
                            $"DrawImage command issued; actual pixel visibility is not verified. " +
                            $"Map-space bounds: {mapBoundsDisplay}.");
                    }
                }
            }

            foreach (var marker in markers)
            {
                var center = camera.ToScreen(marker.WorldPosition);
                var markerBounds = new Rect(
                    center.X - markerRadius,
                    center.Y - markerRadius,
                    markerRadius * 2,
                    markerRadius * 2);
                if (!MapIsometricFloorViewportGeometry.IntersectsViewport(
                        markerBounds,
                        Bounds.Width,
                        Bounds.Height))
                {
                    continue;
                }

                context.DrawEllipse(
                    GetObjectMarkerBrush(marker.Placement.PidTypeIdentifier),
                    ObjectMarkerOutlinePen,
                    center,
                    markerRadius,
                    markerRadius);

                if (marker.Placement.RecordIndex == SelectedRecordIndex)
                {
                    context.DrawEllipse(
                        null,
                        SelectedObjectMarkerPen,
                        center,
                        markerRadius + 2,
                        markerRadius + 2);
                }
            }

        }
    }

    private void SetSpriteRenderDiagnostic(string? message)
    {
        if (!string.Equals(SpriteRenderDiagnosticDisplay, message, StringComparison.Ordinal))
        {
            SpriteRenderDiagnosticDisplay = message;
        }
    }

    private static Rect GetMapSpaceBounds(
        Rect screenBounds,
        MapIsometricFloorCameraTransform camera)
    {
        var mapTopLeft = camera.ToWorld(new Point(screenBounds.X, screenBounds.Y));
        var mapBottomRight = camera.ToWorld(new Point(screenBounds.Right, screenBounds.Bottom));
        return new Rect(
            mapTopLeft.X,
            mapTopLeft.Y,
            mapBottomRight.X - mapTopLeft.X,
            mapBottomRight.Y - mapTopLeft.Y);
    }

    private static string FormatMapSpaceBounds(Rect bounds) =>
        FormattableString.Invariant(
            $"x={bounds.X:G17}, y={bounds.Y:G17}, width={bounds.Width:G17}, height={bounds.Height:G17}");

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        if (_panLastPosition is Point last
            && TryGetCurrentCamera(out var current)
            && TryGetContentBounds(out var contentBounds))
        {
            _cameraTransform = MapIsometricFloorViewportGeometry.Pan(
                current,
                position.X - last.X,
                position.Y - last.Y,
                contentBounds,
                Bounds.Width,
                Bounds.Height);
            _panLastPosition = position;
            _isFitToView = false;
            UpdateHoverToolTip(position);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        UpdateHoverToolTip(position);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _pointerPosition = null;
        SetHoveredPrototypeFileName(null);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsLeftButtonPressed && ShowObjectMarkers)
        {
            int? recordIndex = null;
            if (TryGetProjectionTransform(out var projection)
                && TryGetCurrentCamera(out var camera))
            {
                recordIndex = MapIsometricObjectMarkerHitTest.GetRecordIndexAtPosition(
                    GetObjectMarkers(projection),
                    camera,
                    e.GetPosition(this),
                    Bounds.Width,
                    Bounds.Height);
            }

            ObjectSelectionChanged?.Invoke(
                this,
                new MapObjectMarkerSelectionChangedEventArgs(recordIndex));
            e.Handled = true;
            return;
        }

        if (!properties.IsRightButtonPressed && !properties.IsMiddleButtonPressed)
        {
            return;
        }

        if (!TryGetCurrentCamera(out var current))
        {
            return;
        }

        _cameraTransform = current;
        _panLastPosition = e.GetPosition(this);
        _isFitToView = false;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_panLastPosition is null)
        {
            return;
        }

        _panLastPosition = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _panLastPosition = null;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.Delta.Y == 0
            || !TryGetCurrentCamera(out var current)
            || !TryGetContentBounds(out var contentBounds))
        {
            return;
        }

        var position = e.GetPosition(this);
        _cameraTransform = MapIsometricFloorViewportGeometry.ZoomAt(
            current,
            position.X,
            position.Y,
            Math.Pow(ZoomStep, e.Delta.Y),
            contentBounds,
            Bounds.Width,
            Bounds.Height);
        _isFitToView = false;
        UpdateHoverToolTip(position);
        InvalidateVisual();
        e.Handled = true;
    }

    private void UpdateCameraForAssetChange()
    {
        if (_isFitToView)
        {
            FitToView();
            return;
        }

        if (_cameraTransform is MapIsometricFloorCameraTransform current
            && TryGetContentBounds(out var contentBounds)
            && MapIsometricFloorViewportGeometry.TryGetResizedTransform(
                current,
                Bounds.Width,
                Bounds.Height,
                Bounds.Width,
                Bounds.Height,
                contentBounds,
                out var adjusted))
        {
            _cameraTransform = adjusted;
        }
    }

    private void PrepareCameraForMarkersIfNeeded()
    {
        if (ShowObjectMarkers && !TryGetCurrentCamera(out _))
        {
            UpdateFitCamera();
        }
    }

    private IReadOnlyList<MapIsometricObjectMarker> GetObjectMarkers(
        MapIsometricFloorViewTransform projection)
    {
        if (_objectMarkers is not null
            && _objectMarkersProjection == projection
            && _objectMarkersElevation == Elevation)
        {
            return _objectMarkers;
        }

        _objectMarkers = MapIsometricObjectMarkerFilter.GetMarkers(
            Placements,
            Elevation,
            projection);
        _objectMarkersProjection = projection;
        _objectMarkersElevation = Elevation;
        return _objectMarkers;
    }

    private void Placements_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateObjectMarkers();
        PrepareCameraForMarkersIfNeeded();
        ValidateSelectedRecord();
        RefreshHoverToolTip();
        InvalidateVisual();
    }

    private void RefreshHoverToolTip()
    {
        if (_pointerPosition is Point position)
        {
            UpdateHoverToolTip(position);
        }
    }

    private void UpdateHoverToolTip(Point position)
    {
        _pointerPosition = position;
        string? prototypeFileName = null;
        if (ShowObjectMarkers
            && TryGetProjectionTransform(out var projection)
            && TryGetCurrentCamera(out var camera))
        {
            prototypeFileName = MapIsometricObjectMarkerHitTest.GetPrototypeFileNameAtPosition(
                GetObjectMarkers(projection),
                camera,
                position,
                Bounds.Width,
                Bounds.Height);
        }

        SetHoveredPrototypeFileName(prototypeFileName);
    }

    private void SetHoveredPrototypeFileName(string? prototypeFileName)
    {
        if (string.Equals(_hoveredPrototypeFileName, prototypeFileName, StringComparison.Ordinal))
        {
            return;
        }

        _hoveredPrototypeFileName = prototypeFileName;
        var toolTip = MapIsometricObjectMarkerHitTest.FormatPrototypeFileToolTip(prototypeFileName);
        ToolTip.SetIsOpen(this, false);
        ToolTip.SetTip(this, toolTip);
        ToolTip.SetIsOpen(this, toolTip is not null);
    }

    private void ValidateSelectedRecord()
    {
        if (SelectedRecordIndex is not int selectedRecordIndex
            || !TryGetProjectionTransform(out var projection)
            || GetObjectMarkers(projection).Any(
                marker => marker.Placement.RecordIndex == selectedRecordIndex))
        {
            return;
        }

        ObjectSelectionChanged?.Invoke(
            this,
            new MapObjectMarkerSelectionChangedEventArgs(null));
    }

    private void InvalidateObjectMarkers()
    {
        _objectMarkers = null;
        _objectMarkersProjection = null;
        _objectMarkersElevation = null;
    }

    private static IBrush GetObjectMarkerBrush(int pidType) =>
        pidType switch
        {
            0 => ItemMarkerBrush,
            1 => CritterMarkerBrush,
            2 => SceneryMarkerBrush,
            3 => WallMarkerBrush,
            4 => TileMarkerBrush,
            5 => MiscMarkerBrush,
            6 => InterfaceMarkerBrush,
            7 => InventoryMarkerBrush,
            8 => HeadMarkerBrush,
            9 => BackgroundMarkerBrush,
            10 => SkilldexMarkerBrush,
            _ => DefaultObjectMarkerBrush
        };

    private bool TryGetCurrentCamera(out MapIsometricFloorCameraTransform camera)
    {
        if (_cameraTransform is MapIsometricFloorCameraTransform stored && stored.IsValid)
        {
            camera = stored;
            return true;
        }

        camera = default;
        return false;
    }

    // A fixed CE viewport keeps projected world coordinates stable; the camera fits them to the control.
    private bool TryGetProjectionTransform(out MapIsometricFloorViewTransform projection) =>
        MapIsometricFloorGeometry.TryCreateViewTransform(
            ProjectionCenterHexX,
            ProjectionCenterHexY,
            ProjectionViewportWidth,
            ProjectionViewportHeight,
            0,
            0,
            out projection);

    private bool TryGetContentBounds(out Rect contentBounds)
    {
        contentBounds = default;
        if (!TryGetProjectionTransform(out var projection)
            || !MapIsometricFloorGeometry.TryProjectSquareCoordinates(0, 0, projection, out var topLeft)
            || !MapIsometricFloorGeometry.TryProjectSquareCoordinates(99, 0, projection, out var topRight)
            || !MapIsometricFloorGeometry.TryProjectSquareCoordinates(0, 99, projection, out var bottomLeft)
            || !MapIsometricFloorGeometry.TryProjectSquareCoordinates(99, 99, projection, out var bottomRight))
        {
            return false;
        }

        var minX = Math.Min(Math.Min(topLeft.X, topRight.X), Math.Min(bottomLeft.X, bottomRight.X));
        var minY = Math.Min(Math.Min(topLeft.Y, topRight.Y), Math.Min(bottomLeft.Y, bottomRight.Y));
        var maxX = Math.Max(Math.Max(topLeft.X, topRight.X), Math.Max(bottomLeft.X, bottomRight.X));
        var maxY = Math.Max(Math.Max(topLeft.Y, topRight.Y), Math.Max(bottomLeft.Y, bottomRight.Y));
        var maxBitmapWidth = 0;
        var maxBitmapHeight = 0;
        if (TileBitmaps is not null)
        {
            foreach (var bitmap in TileBitmaps.Values)
            {
                maxBitmapWidth = Math.Max(maxBitmapWidth, bitmap.PixelSize.Width);
                maxBitmapHeight = Math.Max(maxBitmapHeight, bitmap.PixelSize.Height);
            }
        }

        maxX += maxBitmapWidth;
        maxY += maxBitmapHeight;
        var width = maxX - minX;
        var height = maxY - minY;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
        {
            return false;
        }

        contentBounds = new Rect(minX, minY, width, height);
        if (SelectedObjectSprite is { } sprite
            && MapIsometricObjectSpriteGeometry.TryGetSelectedSpriteWorldBounds(
                sprite.RecordIndex,
                SelectedRecordIndex,
                ShowObjectMarkers,
                GetObjectMarkers(projection),
                sprite.Bitmap.PixelSize.Width,
                sprite.Bitmap.PixelSize.Height,
                sprite.ArtShiftX,
                sprite.ArtShiftY,
                out var spriteBounds))
        {
            if (MapIsometricFloorViewportGeometry.IncludeBounds(
                contentBounds,
                spriteBounds) is not Rect expandedBounds)
            {
                return false;
            }

            contentBounds = expandedBounds;
        }

        return true;
    }
}
