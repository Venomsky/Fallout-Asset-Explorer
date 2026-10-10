namespace FalloutAssetExplorer.Core;

public sealed record FrmFrame(
    int Width,
    int Height,
    short OffsetX,
    short OffsetY,
    byte[] Pixels);
