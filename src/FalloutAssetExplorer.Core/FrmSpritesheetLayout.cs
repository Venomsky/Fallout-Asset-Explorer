using System.Collections.ObjectModel;

namespace FalloutAssetExplorer.Core;

public readonly record struct FrmSpritesheetFrameKey(int DirectionIndex, int FrameIndex);

public sealed record FrmSpritesheetFrame(
    FrmSpritesheetFrameKey Key,
    int Width,
    int Height,
    short OffsetX,
    short OffsetY,
    short ShiftX,
    short ShiftY)
{
    public static FrmSpritesheetFrame From(FrmDirection direction, int frameIndex)
    {
        ArgumentNullException.ThrowIfNull(direction);
        if ((uint)frameIndex >= (uint)direction.Frames.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(frameIndex));
        }

        var frame = direction.Frames[frameIndex];
        return new FrmSpritesheetFrame(
            new FrmSpritesheetFrameKey(direction.Index, frameIndex),
            frame.Width,
            frame.Height,
            frame.OffsetX,
            frame.OffsetY,
            direction.ShiftX,
            direction.ShiftY);
    }
}

public readonly record struct FrmAtlasRectangle(int X, int Y, int Width, int Height);

public sealed record FrmSpritesheetPlacement(FrmSpritesheetFrame Frame, FrmAtlasRectangle Rectangle);

public sealed class FrmSpritesheetLayout
{
    internal FrmSpritesheetLayout(
        int width,
        int height,
        IDictionary<FrmSpritesheetFrameKey, FrmSpritesheetPlacement> placements)
    {
        Width = width;
        Height = height;
        Placements = new ReadOnlyDictionary<FrmSpritesheetFrameKey, FrmSpritesheetPlacement>(
            new Dictionary<FrmSpritesheetFrameKey, FrmSpritesheetPlacement>(placements));
    }

    public int Width { get; }

    public int Height { get; }

    public IReadOnlyDictionary<FrmSpritesheetFrameKey, FrmSpritesheetPlacement> Placements { get; }
}

public sealed record FrmSpritesheetPackingLimits
{
    public FrmSpritesheetPackingLimits(int maximumWidth, int maximumHeight, long maximumPixelCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPixelCount);

        MaximumWidth = maximumWidth;
        MaximumHeight = maximumHeight;
        MaximumPixelCount = maximumPixelCount;
    }

    public int MaximumWidth { get; }

    public int MaximumHeight { get; }

    public long MaximumPixelCount { get; }
}
