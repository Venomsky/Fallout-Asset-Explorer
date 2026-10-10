namespace FalloutAssetExplorer.Core;

public sealed class FrmPlaybackController
{
    public const int MinimumFramesPerSecond = 1;
    public const int MaximumFramesPerSecond = 30;
    public const int DefaultFramesPerSecond = 8;

    private int[] _frameCounts = [];
    private int _framesPerSecond = DefaultFramesPerSecond;

    public int DirectionIndex { get; private set; }

    public int CurrentFrameIndex { get; private set; }

    public int CurrentDirectionFrameCount =>
        DirectionIndex < _frameCounts.Length ? _frameCounts[DirectionIndex] : 0;

    public bool IsPlaying { get; private set; }

    public bool IsLoopEnabled { get; set; } = true;

    public int FramesPerSecond
    {
        get => _framesPerSecond;
        set
        {
            if (value is < MinimumFramesPerSecond or > MaximumFramesPerSecond)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    $"Preview speed must be between {MinimumFramesPerSecond} and {MaximumFramesPerSecond} FPS.");
            }

            _framesPerSecond = value;
        }
    }

    public bool CanPlay => CurrentDirectionFrameCount > 1;

    public void SetFrameCounts(IReadOnlyList<int> frameCounts)
    {
        ArgumentNullException.ThrowIfNull(frameCounts);
        if (frameCounts.Any(count => count < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(frameCounts), "Direction frame counts cannot be negative.");
        }

        _frameCounts = frameCounts.ToArray();
        DirectionIndex = 0;
        CurrentFrameIndex = 0;
        IsPlaying = false;
    }

    public bool SelectDirection(int directionIndex)
    {
        if ((uint)directionIndex >= (uint)_frameCounts.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(directionIndex));
        }

        if (DirectionIndex == directionIndex)
        {
            return false;
        }

        DirectionIndex = directionIndex;
        CurrentFrameIndex = 0;
        IsPlaying = false;
        return true;
    }

    public bool SelectFrame(int frameIndex)
    {
        if ((uint)frameIndex >= (uint)CurrentDirectionFrameCount)
        {
            throw new ArgumentOutOfRangeException(nameof(frameIndex));
        }

        var changed = CurrentFrameIndex != frameIndex;
        CurrentFrameIndex = frameIndex;
        IsPlaying = false;
        return changed;
    }

    public bool Play()
    {
        if (!CanPlay)
        {
            IsPlaying = false;
            return false;
        }

        IsPlaying = true;
        return true;
    }

    public void Pause() => IsPlaying = false;

    public bool Advance()
    {
        if (!IsPlaying)
        {
            return false;
        }

        if (CurrentFrameIndex + 1 < CurrentDirectionFrameCount)
        {
            CurrentFrameIndex++;
            if (!IsLoopEnabled && CurrentFrameIndex == CurrentDirectionFrameCount - 1)
            {
                IsPlaying = false;
            }

            return true;
        }

        if (IsLoopEnabled)
        {
            CurrentFrameIndex = 0;
            return true;
        }

        IsPlaying = false;
        return false;
    }
}
