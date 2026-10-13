namespace FalloutAssetExplorer.Core;

public static class FrmSpritesheetPacker
{
    /// <summary>
    /// Packs frames in input order using shelf rows. Input keys must be strictly
    /// increasing by direction index, then frame index. The maximum width is also
    /// the row-wrap width; the returned sheet has no outer margin.
    /// </summary>
    public static FrmSpritesheetLayout Pack(
        IReadOnlyList<FrmSpritesheetFrame> frames,
        int spacing,
        FrmSpritesheetPackingLimits limits)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentOutOfRangeException.ThrowIfNegative(spacing);

        if (frames.Count == 0)
        {
            return new FrmSpritesheetLayout(
                0,
                0,
                new Dictionary<FrmSpritesheetFrameKey, FrmSpritesheetPlacement>());
        }

        var placements = new Dictionary<FrmSpritesheetFrameKey, FrmSpritesheetPlacement>(frames.Count);
        long currentX = 0;
        long currentY = 0;
        long rowHeight = 0;
        long maximumRight = 0;
        var hasFramesInRow = false;
        FrmSpritesheetFrameKey? previousKey = null;

        for (var index = 0; index < frames.Count; index++)
        {
            var frame = frames[index]
                ?? throw new ArgumentException("The frame list cannot contain null entries.", nameof(frames));
            ValidateFrame(frame, previousKey, index);
            previousKey = frame.Key;

            if (frame.Width > limits.MaximumWidth)
            {
                throw new InvalidOperationException(
                    $"Frame {frame.Key} is wider than the configured maximum width.");
            }

            var nextRight = checked(
                currentX + (hasFramesInRow ? spacing : 0L) + frame.Width);
            if (hasFramesInRow && nextRight > limits.MaximumWidth)
            {
                currentY = checked(currentY + rowHeight + spacing);
                currentX = 0;
                rowHeight = 0;
                hasFramesInRow = false;
            }

            var x = checked(currentX + (hasFramesInRow ? spacing : 0L));
            var right = checked(x + frame.Width);
            var bottom = checked(currentY + frame.Height);
            if (right > limits.MaximumWidth)
            {
                throw new InvalidOperationException(
                    $"Frame {frame.Key} exceeds the configured maximum width.");
            }

            if (bottom > limits.MaximumHeight)
            {
                throw new InvalidOperationException(
                    $"Frame {frame.Key} exceeds the configured maximum height.");
            }

            placements.Add(
                frame.Key,
                new FrmSpritesheetPlacement(
                    frame,
                    new FrmAtlasRectangle(
                        checked((int)x),
                        checked((int)currentY),
                        frame.Width,
                        frame.Height)));

            currentX = right;
            rowHeight = Math.Max(rowHeight, frame.Height);
            maximumRight = Math.Max(maximumRight, right);
            hasFramesInRow = true;
        }

        var sheetWidth = checked((int)maximumRight);
        var sheetHeight = checked((int)checked(currentY + rowHeight));
        var pixelCount = checked((long)sheetWidth * sheetHeight);
        if (pixelCount > limits.MaximumPixelCount)
        {
            throw new InvalidOperationException(
                "The packed spritesheet exceeds the configured maximum pixel count.");
        }

        return new FrmSpritesheetLayout(sheetWidth, sheetHeight, placements);
    }

    private static void ValidateFrame(
        FrmSpritesheetFrame frame,
        FrmSpritesheetFrameKey? previousKey,
        int inputIndex)
    {
        if (frame.Key.DirectionIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frame),
                $"Frame at input index {inputIndex} has a negative direction index.");
        }

        if (frame.Key.FrameIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frame),
                $"Frame at input index {inputIndex} has a negative frame index.");
        }

        if (frame.Width <= 0 || frame.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frame),
                $"Frame {frame.Key} must have positive dimensions.");
        }

        if (previousKey is { } previous
            && (frame.Key.DirectionIndex < previous.DirectionIndex
                || (frame.Key.DirectionIndex == previous.DirectionIndex
                    && frame.Key.FrameIndex <= previous.FrameIndex)))
        {
            throw new ArgumentException(
                "Frames must be ordered strictly by direction index, then frame index.",
                "frames");
        }
    }
}
