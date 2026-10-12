using Avalonia;

namespace FalloutAssetExplorer.App.Views;

public readonly record struct MapIsometricFloorCameraTransform(
    double Scale,
    double OffsetX,
    double OffsetY)
{
    public bool IsValid =>
        double.IsFinite(Scale) && Scale > 0
        && double.IsFinite(OffsetX)
        && double.IsFinite(OffsetY);

    public Point ToScreen(Point worldPoint) =>
        new(worldPoint.X * Scale + OffsetX, worldPoint.Y * Scale + OffsetY);

    public Point ToWorld(Point screenPoint) =>
        new((screenPoint.X - OffsetX) / Scale, (screenPoint.Y - OffsetY) / Scale);
}

public static class MapIsometricFloorViewportGeometry
{
    private const double Margin = 12;

    public const double MinZoom = 0.25;
    public const double MaxZoom = 32;

    public static bool TryGetFitTransform(
        Rect contentBounds,
        double viewWidth,
        double viewHeight,
        out MapIsometricFloorCameraTransform transform)
    {
        transform = default;
        if (!IsValidBounds(contentBounds)
            || !IsValidViewSize(viewWidth, viewHeight)
            || viewWidth <= Margin * 2
            || viewHeight <= Margin * 2)
        {
            return false;
        }

        var scale = Math.Min(
            (viewWidth - Margin * 2) / contentBounds.Width,
            (viewHeight - Margin * 2) / contentBounds.Height);
        if (!double.IsFinite(scale) || scale <= 0)
        {
            return false;
        }

        transform = new MapIsometricFloorCameraTransform(
            scale,
            (viewWidth - contentBounds.Width * scale) / 2 - contentBounds.X * scale,
            (viewHeight - contentBounds.Height * scale) / 2 - contentBounds.Y * scale);
        return transform.IsValid;
    }

    public static MapIsometricFloorCameraTransform ZoomAt(
        MapIsometricFloorCameraTransform current,
        double cursorX,
        double cursorY,
        double factor,
        Rect contentBounds,
        double viewWidth,
        double viewHeight)
    {
        if (!current.IsValid
            || !double.IsFinite(cursorX)
            || !double.IsFinite(cursorY)
            || !double.IsFinite(factor)
            || factor <= 0
            || !TryGetFitTransform(contentBounds, viewWidth, viewHeight, out var fit))
        {
            return current;
        }

        var anchor = current.ToWorld(new Point(cursorX, cursorY));
        var scale = Math.Clamp(current.Scale * factor, fit.Scale * MinZoom, fit.Scale * MaxZoom);
        var zoomed = new MapIsometricFloorCameraTransform(
            scale,
            cursorX - anchor.X * scale,
            cursorY - anchor.Y * scale);
        return ClampToView(zoomed, contentBounds, viewWidth, viewHeight);
    }

    public static MapIsometricFloorCameraTransform Pan(
        MapIsometricFloorCameraTransform current,
        double deltaX,
        double deltaY,
        Rect contentBounds,
        double viewWidth,
        double viewHeight)
    {
        if (!current.IsValid
            || !double.IsFinite(deltaX)
            || !double.IsFinite(deltaY)
            || !IsValidBounds(contentBounds)
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
            contentBounds,
            viewWidth,
            viewHeight);
    }

    public static bool TryGetResizedTransform(
        MapIsometricFloorCameraTransform current,
        double previousWidth,
        double previousHeight,
        double newWidth,
        double newHeight,
        Rect contentBounds,
        out MapIsometricFloorCameraTransform resized)
    {
        resized = default;
        if (!current.IsValid
            || !IsValidViewSize(previousWidth, previousHeight)
            || !TryGetFitTransform(contentBounds, newWidth, newHeight, out var fit))
        {
            return false;
        }

        var worldCenter = current.ToWorld(new Point(previousWidth / 2, previousHeight / 2));
        var scale = Math.Clamp(current.Scale, fit.Scale * MinZoom, fit.Scale * MaxZoom);
        resized = ClampToView(
            new MapIsometricFloorCameraTransform(
                scale,
                newWidth / 2 - worldCenter.X * scale,
                newHeight / 2 - worldCenter.Y * scale),
            contentBounds,
            newWidth,
            newHeight);
        return true;
    }

    public static Rect GetImageBounds(
        MapIsometricFloorCameraTransform transform,
        Point worldAnchor,
        int imageWidth,
        int imageHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageHeight);
        if (!transform.IsValid
            || !double.IsFinite(worldAnchor.X)
            || !double.IsFinite(worldAnchor.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(transform));
        }

        var screenAnchor = transform.ToScreen(worldAnchor);
        return new Rect(
            screenAnchor.X,
            screenAnchor.Y,
            imageWidth * transform.Scale,
            imageHeight * transform.Scale);
    }

    public static bool IntersectsViewport(
        Rect imageBounds,
        double viewWidth,
        double viewHeight) =>
        IsValidBounds(imageBounds)
        && IsValidViewSize(viewWidth, viewHeight)
        && imageBounds.Right > 0
        && imageBounds.Bottom > 0
        && imageBounds.Left < viewWidth
        && imageBounds.Top < viewHeight;

    private static MapIsometricFloorCameraTransform ClampToView(
        MapIsometricFloorCameraTransform transform,
        Rect contentBounds,
        double viewWidth,
        double viewHeight)
    {
        if (!transform.IsValid
            || !IsValidBounds(contentBounds)
            || !IsValidViewSize(viewWidth, viewHeight))
        {
            return transform;
        }

        return transform with
        {
            OffsetX = Math.Clamp(
                transform.OffsetX,
                Margin - contentBounds.Right * transform.Scale,
                viewWidth - Margin - contentBounds.Left * transform.Scale),
            OffsetY = Math.Clamp(
                transform.OffsetY,
                Margin - contentBounds.Bottom * transform.Scale,
                viewHeight - Margin - contentBounds.Top * transform.Scale)
        };
    }

    private static bool IsValidBounds(Rect bounds) =>
        double.IsFinite(bounds.X)
        && double.IsFinite(bounds.Y)
        && double.IsFinite(bounds.Width)
        && double.IsFinite(bounds.Height)
        && bounds.Width > 0
        && bounds.Height > 0;

    private static bool IsValidViewSize(double width, double height) =>
        double.IsFinite(width)
        && double.IsFinite(height)
        && width > 0
        && height > 0;
}
