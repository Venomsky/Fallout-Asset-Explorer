using FalloutAssetExplorer.Core;

namespace FalloutAssetExplorer.App.Views;

public static class MapIsometricObjectSpriteDiagnostics
{
    public static int CountNonZeroPaletteIndexPixels(FrmFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(frame.Pixels);
        return frame.Pixels.Count(static paletteIndex => paletteIndex != 0);
    }
}
