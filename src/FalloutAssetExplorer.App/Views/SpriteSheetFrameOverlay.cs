using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FalloutAssetExplorer.Core;

namespace FalloutAssetExplorer.App.Views;

public sealed class SpriteSheetFrameOverlay : Control
{
    private static readonly Pen FramePen = new(
        new SolidColorBrush(Color.Parse("#90D7E3FC")),
        1);
    private static readonly Pen SelectedFramePen = new(
        new SolidColorBrush(Color.Parse("#FFFFD54F")),
        2);
    private FrmSpritesheetLayout? _layout;
    private FrmSpritesheetFrameKey? _selectedFrame;

    public void SetLayout(
        FrmSpritesheetLayout? layout,
        FrmSpritesheetFrameKey? selectedFrame)
    {
        _layout = layout;
        _selectedFrame = selectedFrame;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_layout is null)
        {
            return;
        }

        foreach (var placement in _layout.Placements.Values)
        {
            var rectangle = placement.Rectangle;
            var bounds = new Rect(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
            var pen = placement.Frame.Key == _selectedFrame ? SelectedFramePen : FramePen;
            context.DrawRectangle(null, pen, bounds);
        }
    }
}
