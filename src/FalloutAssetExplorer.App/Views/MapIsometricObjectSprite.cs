using Avalonia.Media.Imaging;

namespace FalloutAssetExplorer.App.Views;

public sealed record MapIsometricObjectSprite(
    int RecordIndex,
    Bitmap Bitmap,
    string ResourcePath,
    int DirectionIndex,
    int FrameIndex,
    short ArtShiftX,
    short ArtShiftY,
    short FrameOffsetX,
    short FrameOffsetY,
    int NonZeroPaletteIndexPixelCount);
