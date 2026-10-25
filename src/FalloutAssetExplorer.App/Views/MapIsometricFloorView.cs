using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using FalloutAssetExplorer.Core;

namespace FalloutAssetExplorer.App.Views;

public sealed partial class MapIsometricFloorView : Control
{
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

    public static readonly StyledProperty<double> InitialObjectMarkersZoomFactorProperty =
        AvaloniaProperty.Register<MapIsometricFloorView, double>(
            nameof(InitialObjectMarkersZoomFactor),
            1.2);

    public static readonly StyledProperty<string?> MapIdentityProperty =
        AvaloniaProperty.Register<MapIsometricFloorView, string?>(nameof(MapIdentity));

    public static readonly StyledProperty<IReadOnlyList<MapObjectPlacement>?> PlacementsProperty =
        AvaloniaProperty.Register<MapIsometricFloorView, IReadOnlyList<MapObjectPlacement>?>(
            nameof(Placements));

    public static readonly StyledProperty<int?> ElevationProperty =
        AvaloniaProperty.Register<MapIsometricFloorView, int?>(nameof(Elevation));

    public static readonly StyledProperty<bool> ShowObjectMarkersProperty =
        AvaloniaProperty.Register<MapIsometricFloorView, bool>(nameof(ShowObjectMarkers));

    public static readonly StyledProperty<bool> ShowObjectSpritesProperty =
        AvaloniaProperty.Register<MapIsometricFloorView, bool>(nameof(ShowObjectSprites));

    public static readonly StyledProperty<int?> SelectedRecordIndexProperty =
        AvaloniaProperty.Register<MapIsometricFloorView, int?>(nameof(SelectedRecordIndex));

    public static readonly StyledProperty<MapIsometricObjectSprite?> SelectedObjectSpriteProperty =
        AvaloniaProperty.Register<MapIsometricFloorView, MapIsometricObjectSprite?>(
            nameof(SelectedObjectSprite));

    public static readonly StyledProperty<IReadOnlyList<MapIsometricObjectSprite>?> ObjectSpritesProperty =
        AvaloniaProperty.Register<MapIsometricFloorView, IReadOnlyList<MapIsometricObjectSprite>?>(
            nameof(ObjectSprites));

    public static readonly StyledProperty<string?> SpriteRenderDiagnosticDisplayProperty =
        AvaloniaProperty.Register<MapIsometricFloorView, string?>(nameof(SpriteRenderDiagnosticDisplay));

    private MapIsometricFloorCameraTransform? _cameraTransform;
    private Dictionary<int, MapIsometricObjectSprite> _objectSpritesByRecord = [];
    private IReadOnlyList<MapIsometricObjectMarker>? _objectMarkers;
    private MapIsometricFloorViewTransform? _objectMarkersProjection;
    private int? _objectMarkersElevation;
    private Point? _panLastPosition;
    private Point? _pointerPosition;
    private string? _hoveredPrototypeFileName;
    private bool _isFitToView = true;
    private bool _hasAppliedInitialObjectMarkersZoom;

    public uint[]? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public double InitialObjectMarkersZoomFactor
    {
        get => GetValue(InitialObjectMarkersZoomFactorProperty);
        set => SetValue(InitialObjectMarkersZoomFactorProperty, value);
    }

    public string? MapIdentity
    {
        get => GetValue(MapIdentityProperty);
        set => SetValue(MapIdentityProperty, value);
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

    public bool ShowObjectSprites
    {
        get => GetValue(ShowObjectSpritesProperty);
        set => SetValue(ShowObjectSpritesProperty, value);
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

    public IReadOnlyList<MapIsometricObjectSprite>? ObjectSprites
    {
        get => GetValue(ObjectSpritesProperty);
        set => SetValue(ObjectSpritesProperty, value);
    }

    public string? SpriteRenderDiagnosticDisplay
    {
        get => GetValue(SpriteRenderDiagnosticDisplayProperty);
        private set => SetValue(SpriteRenderDiagnosticDisplayProperty, value);
    }

    public event EventHandler<MapObjectMarkerSelectionChangedEventArgs>? ObjectSelectionChanged;

    public void FitToView()
    {
        var diagnosticPairId = BeginViewDiagnostic("Fit", "explicit Fit");
        UpdateFitCamera();
        if (ShowObjectMarkers)
        {
            _hasAppliedInitialObjectMarkersZoom = true;
        }

        EndViewDiagnostic("Fit", "explicit Fit", diagnosticPairId);
        RefreshHoverToolTip();
        InvalidateVisual();
    }

    private void UpdateFitCamera()
    {
        _isFitToView = true;
        var hasContentBounds = TryGetContentBounds(out var contentBounds);
        _cameraTransform = hasContentBounds
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
        var reason = FormattableString.Invariant(
            $"{e.PreviousSize.Width:R}x{e.PreviousSize.Height:R}->{e.NewSize.Width:R}x{e.NewSize.Height:R}");
        var diagnosticPairId = BeginViewDiagnostic("SizeChanged", reason);
        if (_isFitToView)
        {
            UpdateFitCamera();
            TryApplyInitialObjectMarkersZoom();
            EndViewDiagnostic("SizeChanged", reason, diagnosticPairId);
            RefreshHoverToolTip();
            InvalidateVisual();
            return;
        }

        var hasContentBounds = TryGetContentBounds(out var contentBounds);
        if (_cameraTransform is MapIsometricFloorCameraTransform current
            && hasContentBounds
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

        EndViewDiagnostic("SizeChanged", reason, diagnosticPairId);
        RefreshHoverToolTip();
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        var shouldDiagnose = IsViewDiagnosticsEnabled && IsViewDiagnosticProperty(change.Property);
        var diagnosticReason = shouldDiagnose
            ? $"{change.Property.Name}:{FormatDiagnosticValue(change.OldValue)}->{FormatDiagnosticValue(change.NewValue)}"
            : string.Empty;
        var diagnosticPairId = shouldDiagnose
            ? BeginViewDiagnostic("PropertyChanged", diagnosticReason)
            : 0;

        base.OnPropertyChanged(change);

        if (change.Property == ObjectSpritesProperty
            || change.Property == SelectedObjectSpriteProperty
            || change.Property == SelectedRecordIndexProperty)
        {
            UpdateObjectSpritesByRecord();
        }

        if (change.Property == ValuesProperty)
        {
            UpdateFitCamera();
            TryApplyInitialObjectMarkersZoom();
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
            PrepareCameraForObjectRenderingIfNeeded();
            ValidateSelectedRecord();
        }
        else if (change.Property == ElevationProperty)
        {
            InvalidateObjectMarkers();
            PrepareCameraForObjectRenderingIfNeeded();
            ValidateSelectedRecord();
        }
        else if (change.Property == ShowObjectMarkersProperty
            || change.Property == ShowObjectSpritesProperty)
        {
            PrepareCameraForObjectRenderingIfNeeded();
            if (_isFitToView)
            {
                UpdateFitCamera();
            }

            if (change.Property == ShowObjectMarkersProperty && ShowObjectMarkers)
            {
                TryApplyInitialObjectMarkersZoom();
            }
        }
        else if (change.Property == MapIdentityProperty)
        {
            _hasAppliedInitialObjectMarkersZoom = false;
        }
        else if (change.Property == InitialObjectMarkersZoomFactorProperty)
        {
            TryApplyInitialObjectMarkersZoom();
        }
        else if (change.Property == SelectedRecordIndexProperty)
        {
            ValidateSelectedRecord();
        }
        else if (change.Property == SelectedObjectSpriteProperty
            || change.Property == ObjectSpritesProperty)
        {
            UpdateCameraForAssetChange();
        }

        if (change.Property == ValuesProperty
            || change.Property == TileBitmapsProperty
            || change.Property == AssetRevisionProperty
            || change.Property == PlacementsProperty
            || change.Property == ElevationProperty
            || change.Property == ShowObjectMarkersProperty
            || change.Property == ShowObjectSpritesProperty
            || change.Property == ShowRegistrationDiagnosticsProperty
            || change.Property == SelectedRecordIndexProperty
            || change.Property == SelectedObjectSpriteProperty
            || change.Property == ObjectSpritesProperty)
        {
            InvalidateVisual();
            RefreshHoverToolTip();
        }

        if (shouldDiagnose)
        {
            EndViewDiagnostic("PropertyChanged", diagnosticReason, diagnosticPairId);
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
        else if (!ShowObjectSprites)
        {
            SetSpriteRenderDiagnostic(
                "Prepared sprite not drawn: object sprites are disabled. " +
                "Map-space bounds unavailable (sprite display is disabled).");
        }

        if (!TryGetProjectionTransform(out var projection)
            || !TryGetCurrentCamera(out var camera))
        {
            if (SelectedObjectSprite is not null && ShowObjectSprites)
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

        var markers = GetObjectMarkers(projection);
        if (ShowObjectSprites)
        {
            var renderedSelectedSprite = false;
            // The batch follows input placement order; this is provisional and not engine draw-order evidence.
            foreach (var marker in markers)
            {
                if (!_objectSpritesByRecord.TryGetValue(marker.Placement.RecordIndex, out var sprite))
                {
                    continue;
                }

                if (!MapIsometricObjectSpriteGeometry.TryGetImageBounds(
                        marker.WorldPosition,
                        camera,
                        sprite.Bitmap.PixelSize.Width,
                        sprite.Bitmap.PixelSize.Height,
                        sprite.ArtShiftX,
                        sprite.ArtShiftY,
                        marker.Placement.RawOffsetX,
                        marker.Placement.RawOffsetY,
                        out var spriteBounds))
                {
                    if (sprite.RecordIndex == SelectedRecordIndex)
                    {
                        SetSpriteRenderDiagnostic(
                            "Prepared sprite not drawn: image bounds could not be calculated. " +
                            "Map-space bounds unavailable (geometry calculation failed).");
                    }

                    continue;
                }

                if (!MapIsometricFloorViewportGeometry.IntersectsViewport(
                        spriteBounds,
                        Bounds.Width,
                        Bounds.Height))
                {
                    if (sprite.RecordIndex == SelectedRecordIndex)
                    {
                        var mapBoundsDisplay = FormatMapSpaceBounds(GetMapSpaceBounds(spriteBounds, camera));
                        SetSpriteRenderDiagnostic(
                            $"Prepared sprite not drawn: image bounds are outside the viewport. " +
                            $"Map-space bounds: {mapBoundsDisplay}.");
                    }

                    continue;
                }

                context.DrawImage(
                    sprite.Bitmap,
                    new Rect(0, 0, sprite.Bitmap.PixelSize.Width, sprite.Bitmap.PixelSize.Height),
                    spriteBounds);
                if (sprite.RecordIndex == SelectedRecordIndex)
                {
                    renderedSelectedSprite = true;
                    var mapBoundsDisplay = FormatMapSpaceBounds(GetMapSpaceBounds(spriteBounds, camera));
                    SetSpriteRenderDiagnostic(
                        $"DrawImage command issued; actual pixel visibility is not verified. " +
                        $"Map-space bounds: {mapBoundsDisplay}.");
                }
            }

            if (SelectedObjectSprite is { } preparedSprite && !renderedSelectedSprite)
            {
                if (preparedSprite.RecordIndex != SelectedRecordIndex)
                {
                    SetSpriteRenderDiagnostic(
                        "Prepared sprite not drawn: its record does not match the selected record. " +
                        "Map-space bounds unavailable (record mismatch prevents marker lookup).");
                }
                else if (!markers.Any(marker => marker.Placement.RecordIndex == preparedSprite.RecordIndex))
                {
                    SetSpriteRenderDiagnostic(
                        "Prepared sprite not drawn: no matching object marker is available. " +
                        "Map-space bounds unavailable (no marker position is available).");
                }
            }
        }

        if (ShowObjectMarkers)
        {
            var markerRadius = MapIsometricObjectMarkerHitTest.GetMarkerRadius(camera.Scale);
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

        if (ShowRegistrationDiagnostics)
        {
            DrawRegistrationDiagnostics(context, projection, camera, markers);
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
        if (properties.IsLeftButtonPressed
            && TryGetObjectMarkerSelection(e.GetPosition(this), out var recordIndex))
        {
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
        _viewDiagnosticPanPairId = BeginViewDiagnostic("Pan", "user pan");
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private bool TryGetObjectMarkerSelection(Point position, out int? recordIndex)
    {
        recordIndex = null;
        if (!ShowObjectMarkers)
        {
            return false;
        }

        if (TryGetProjectionTransform(out var projection)
            && TryGetCurrentCamera(out var camera))
        {
            recordIndex = MapIsometricObjectMarkerHitTest.GetRecordIndexAtPosition(
                GetObjectMarkers(projection),
                camera,
                position,
                Bounds.Width,
                Bounds.Height);
        }

        return true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_panLastPosition is null)
        {
            return;
        }

        _panLastPosition = null;
        EndViewDiagnostic("Pan", "user pan", _viewDiagnosticPanPairId);
        _viewDiagnosticPanPairId = 0;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _panLastPosition = null;
        EndViewDiagnostic("Pan", "pointer capture lost", _viewDiagnosticPanPairId);
        _viewDiagnosticPanPairId = 0;
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
        var diagnosticPairId = BeginViewDiagnostic(
            "Zoom",
            FormattableString.Invariant($"wheel delta={e.Delta.Y:R};at={position.X:R},{position.Y:R}"));
        _cameraTransform = MapIsometricFloorViewportGeometry.ZoomAt(
            current,
            position.X,
            position.Y,
            Math.Pow(ZoomStep, e.Delta.Y),
            contentBounds,
            Bounds.Width,
            Bounds.Height);
        _isFitToView = false;
        EndViewDiagnostic(
            "Zoom",
            FormattableString.Invariant($"wheel delta={e.Delta.Y:R};at={position.X:R},{position.Y:R}"),
            diagnosticPairId);
        UpdateHoverToolTip(position);
        InvalidateVisual();
        e.Handled = true;
    }

    private void UpdateCameraForAssetChange()
    {
        if (!TryGetCurrentCamera(out _))
        {
            UpdateFitCamera();
        }
    }

    private void PrepareCameraForObjectRenderingIfNeeded()
    {
        if ((ShowObjectMarkers || ShowObjectSprites) && !TryGetCurrentCamera(out _))
        {
            UpdateFitCamera();
            TryApplyInitialObjectMarkersZoom();
        }
    }

    private void TryApplyInitialObjectMarkersZoom()
    {
        if (_hasAppliedInitialObjectMarkersZoom
            || !ShowObjectMarkers
            || !TryGetCurrentCamera(out var current))
        {
            return;
        }

        if (!_isFitToView)
        {
            _hasAppliedInitialObjectMarkersZoom = true;
            return;
        }

        if (!double.IsFinite(InitialObjectMarkersZoomFactor)
            || InitialObjectMarkersZoomFactor <= 0
            || !TryGetContentBounds(out var contentBounds)
            || Bounds.Width <= 0
            || Bounds.Height <= 0)
        {
            return;
        }

        _hasAppliedInitialObjectMarkersZoom = true;
        var diagnosticPairId =
            BeginViewDiagnostic("InitialMarkerZoom", "first marker-layer initialization");
        _cameraTransform = MapIsometricFloorViewportGeometry.ZoomAt(
            current,
            Bounds.Width / 2,
            Bounds.Height / 2,
            InitialObjectMarkersZoomFactor,
            contentBounds,
            Bounds.Width,
            Bounds.Height);
        _isFitToView = false;
        EndViewDiagnostic(
            "InitialMarkerZoom",
            "first marker-layer initialization",
            diagnosticPairId);
        RefreshHoverToolTip();
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

    private void UpdateObjectSpritesByRecord()
    {
        var spritesByRecord = new Dictionary<int, MapIsometricObjectSprite>();
        if (ObjectSprites is { } objectSprites)
        {
            foreach (var sprite in objectSprites)
            {
                spritesByRecord.TryAdd(sprite.RecordIndex, sprite);
            }
        }

        if (SelectedObjectSprite is { } selectedSprite
            && selectedSprite.RecordIndex == SelectedRecordIndex)
        {
            spritesByRecord[selectedSprite.RecordIndex] = selectedSprite;
        }

        _objectSpritesByRecord = spritesByRecord;
    }

    private void Placements_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateObjectMarkers();
        PrepareCameraForObjectRenderingIfNeeded();
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
        MapIsometricFloorGeometry.TryCreateDefaultViewTransform(out projection);

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
        if (!ShowObjectSprites)
        {
            return true;
        }

        var markers = GetObjectMarkers(projection);
        var includedRecordIndices = new HashSet<int>();
        if (ObjectSprites is { } objectSprites)
        {
            foreach (var sprite in objectSprites)
            {
                if (!MapIsometricObjectSpriteGeometry.TryGetSpriteWorldBounds(
                        sprite,
                        markers,
                        out var spriteBounds))
                {
                    continue;
                }

                if (MapIsometricFloorViewportGeometry.IncludeBounds(
                        contentBounds,
                        spriteBounds) is not Rect expandedBounds)
                {
                    return false;
                }

                contentBounds = expandedBounds;
                includedRecordIndices.Add(sprite.RecordIndex);
            }
        }

        if (SelectedObjectSprite is { } selectedSprite
            && !includedRecordIndices.Contains(selectedSprite.RecordIndex)
            && MapIsometricObjectSpriteGeometry.TryGetSelectedSpriteWorldBounds(
                selectedSprite.RecordIndex,
                SelectedRecordIndex,
                ShowObjectSprites,
                markers,
                selectedSprite.Bitmap.PixelSize.Width,
                selectedSprite.Bitmap.PixelSize.Height,
                selectedSprite.ArtShiftX,
                selectedSprite.ArtShiftY,
                out var selectedSpriteBounds))
        {
            if (MapIsometricFloorViewportGeometry.IncludeBounds(
                contentBounds,
                selectedSpriteBounds) is not Rect expandedBounds)
            {
                return false;
            }

            contentBounds = expandedBounds;
        }

        return true;
    }

}
