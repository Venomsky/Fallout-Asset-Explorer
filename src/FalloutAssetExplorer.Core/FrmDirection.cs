namespace FalloutAssetExplorer.Core;

public sealed record FrmDirection(
    int Index,
    string Name,
    short ShiftX,
    short ShiftY,
    IReadOnlyList<FrmFrame> Frames);
