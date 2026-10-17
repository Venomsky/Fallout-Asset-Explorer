using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FalloutAssetExplorer.App.ViewModels;

public sealed class SpriteZoomModel : INotifyPropertyChanged
{
    public const double MinimumZoom = 0.01;
    public const double MaximumZoom = 32;
    private const double Epsilon = 0.000001;

    private double _viewportWidth;
    private double _viewportHeight;
    private double _imageWidth;
    private double _imageHeight;
    private double _zoom = 1;
    private double _offsetX;
    private double _offsetY;
    private bool _isFit = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public double Zoom => _zoom;

    public double OffsetX => _offsetX;

    public double OffsetY => _offsetY;

    public bool IsFit => _isFit;

    public bool HasImage => _imageWidth > 0 && _imageHeight > 0;

    public bool CanPan =>
        HasImage
        && (_imageWidth * _zoom > _viewportWidth + Epsilon
            || _imageHeight * _zoom > _viewportHeight + Epsilon);

    public string ZoomDisplay => $"{_zoom:P0}";

    public static double CalculateFitScale(
        double viewportWidth,
        double viewportHeight,
        double imageWidth,
        double imageHeight)
    {
        if (!IsPositiveFinite(viewportWidth)
            || !IsPositiveFinite(viewportHeight)
            || !IsPositiveFinite(imageWidth)
            || !IsPositiveFinite(imageHeight))
        {
            return 1;
        }

        var scale = Math.Min(viewportWidth / imageWidth, viewportHeight / imageHeight);
        return Math.Clamp(scale, MinimumZoom, MaximumZoom);
    }

    public void SetViewportSize(double width, double height)
    {
        _viewportWidth = NormalizeDimension(width);
        _viewportHeight = NormalizeDimension(height);
        if (_isFit)
        {
            ApplyFit();
        }
        else
        {
            ClampOffsets();
            NotifyStateChanged();
        }
    }

    public void SetImageSize(double width, double height)
    {
        _imageWidth = NormalizeDimension(width);
        _imageHeight = NormalizeDimension(height);
        if (_isFit)
        {
            ApplyFit();
        }
        else
        {
            ClampOffsets();
            NotifyStateChanged();
        }
    }

    public void ResetForResource()
    {
        _imageWidth = 0;
        _imageHeight = 0;
        _zoom = 1;
        _offsetX = 0;
        _offsetY = 0;
        _isFit = true;
        NotifyStateChanged();
    }

    public void Fit()
    {
        _isFit = true;
        ApplyFit();
    }

    public void Set100Percent()
    {
        _isFit = false;
        _zoom = 1;
        CenterImage();
        ClampOffsets();
        NotifyStateChanged();
    }

    public void ZoomBy(double factor, double anchorX, double anchorY)
    {
        if (!HasImage || !IsPositiveFinite(factor))
        {
            return;
        }

        var newZoom = Math.Clamp(_zoom * factor, MinimumZoom, MaximumZoom);
        if (Math.Abs(newZoom - _zoom) < Epsilon)
        {
            return;
        }

        _isFit = false;
        var ratio = newZoom / _zoom;
        _offsetX = anchorX - ((anchorX - _offsetX) * ratio);
        _offsetY = anchorY - ((anchorY - _offsetY) * ratio);
        _zoom = newZoom;
        ClampOffsets();
        NotifyStateChanged();
    }

    public void PanBy(double deltaX, double deltaY)
    {
        if (!CanPan || !double.IsFinite(deltaX) || !double.IsFinite(deltaY))
        {
            return;
        }

        _offsetX += deltaX;
        _offsetY += deltaY;
        ClampOffsets();
        NotifyStateChanged();
    }

    private void ApplyFit()
    {
        if (!HasImage
            || !IsPositiveFinite(_viewportWidth)
            || !IsPositiveFinite(_viewportHeight))
        {
            _zoom = 1;
            _offsetX = 0;
            _offsetY = 0;
        }
        else
        {
            _zoom = CalculateFitScale(
                _viewportWidth,
                _viewportHeight,
                _imageWidth,
                _imageHeight);
            CenterImage();
        }

        NotifyStateChanged();
    }

    private void CenterImage()
    {
        _offsetX = (_viewportWidth - (_imageWidth * _zoom)) / 2;
        _offsetY = (_viewportHeight - (_imageHeight * _zoom)) / 2;
    }

    private void ClampOffsets()
    {
        if (!HasImage || _viewportWidth <= 0 || _viewportHeight <= 0)
        {
            _offsetX = 0;
            _offsetY = 0;
            return;
        }

        _offsetX = ClampAxis(_offsetX, _imageWidth * _zoom, _viewportWidth);
        _offsetY = ClampAxis(_offsetY, _imageHeight * _zoom, _viewportHeight);
    }

    private static double ClampAxis(double offset, double scaledSize, double viewportSize) =>
        scaledSize <= viewportSize
            ? (viewportSize - scaledSize) / 2
            : Math.Clamp(offset, viewportSize - scaledSize, 0);

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(Zoom));
        OnPropertyChanged(nameof(OffsetX));
        OnPropertyChanged(nameof(OffsetY));
        OnPropertyChanged(nameof(IsFit));
        OnPropertyChanged(nameof(HasImage));
        OnPropertyChanged(nameof(CanPan));
        OnPropertyChanged(nameof(ZoomDisplay));
    }

    private static double NormalizeDimension(double value) =>
        IsPositiveFinite(value) ? value : 0;

    private static bool IsPositiveFinite(double value) =>
        double.IsFinite(value) && value > 0;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
