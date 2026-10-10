namespace FalloutAssetExplorer.Core;

public sealed record FrmDocument(
    uint Version,
    int FramesPerSecond,
    short ActionFrame,
    int FramesPerDirection,
    IReadOnlyList<FrmDirection> Directions);
