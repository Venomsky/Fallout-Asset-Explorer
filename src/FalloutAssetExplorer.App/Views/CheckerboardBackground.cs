using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace FalloutAssetExplorer.App.Views;

public sealed class CheckerboardBackground : Control
{
    private const double TileSize = 12;

    private static readonly IBrush LightBrush = new SolidColorBrush(Color.Parse("#343A44"));
    private static readonly IBrush DarkBrush = new SolidColorBrush(Color.Parse("#252A33"));

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(DarkBrush, bounds);

        var columns = (int)Math.Ceiling(bounds.Width / TileSize);
        var rows = (int)Math.Ceiling(bounds.Height / TileSize);
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                if ((row + column) % 2 == 0)
                {
                    context.FillRectangle(
                        LightBrush,
                        new Rect(column * TileSize, row * TileSize, TileSize, TileSize));
                }
            }
        }
    }
}
