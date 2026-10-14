using FalloutAssetExplorer.Core;

namespace FalloutAssetExplorer.App.Views;

public static class MapIsometricObjectSpriteFrameSelector
{
    public static bool TrySelectFrame(
        MapObjectPlacement placement,
        FrmDocument document,
        out int directionIndex,
        out FrmFrame? frame,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(document);
        directionIndex = placement.RawRotation;
        frame = null;
        error = null;

        if ((uint)directionIndex >= 6)
        {
            error = $"Direction index {directionIndex} is outside the supported FRM range 0-5.";
            return false;
        }

        if (directionIndex >= document.Directions.Count)
        {
            error = $"FRM has no direction {directionIndex}.";
            return false;
        }

        var direction = document.Directions[directionIndex];
        if ((uint)placement.RawFrame >= (uint)direction.Frames.Count)
        {
            error = $"Frame index {placement.RawFrame} is outside direction {directionIndex} ({direction.Frames.Count} frames).";
            return false;
        }

        frame = direction.Frames[placement.RawFrame];
        return true;
    }
}
