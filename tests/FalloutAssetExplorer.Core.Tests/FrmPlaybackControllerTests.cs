using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class FrmPlaybackControllerTests
{
    [Fact]
    public void Advance_MovesToTheNextFrame()
    {
        var player = CreatePlayer(3);

        Assert.True(player.Play());
        Assert.True(player.Advance());
        Assert.Equal(1, player.CurrentFrameIndex);
    }

    [Fact]
    public void Advance_WrapsToFirstFrameWhenLooping()
    {
        var player = CreatePlayer(2);
        player.Play();
        player.Advance();

        Assert.True(player.Advance());
        Assert.Equal(0, player.CurrentFrameIndex);
        Assert.True(player.IsPlaying);
    }

    [Fact]
    public void Advance_StopsOnFinalFrameWhenLoopingIsDisabled()
    {
        var player = CreatePlayer(2);
        player.IsLoopEnabled = false;
        player.Play();

        Assert.True(player.Advance());
        Assert.Equal(1, player.CurrentFrameIndex);
        Assert.False(player.IsPlaying);
        Assert.False(player.Advance());
    }

    [Fact]
    public void PauseAndResume_PreservesTheCurrentFrame()
    {
        var player = CreatePlayer(3);
        player.Play();
        player.Advance();
        player.Pause();

        Assert.False(player.Advance());
        Assert.Equal(1, player.CurrentFrameIndex);

        Assert.True(player.Play());
        Assert.True(player.Advance());
        Assert.Equal(2, player.CurrentFrameIndex);
    }

    [Fact]
    public void SelectFrame_WhilePlayingPausesAndDisplaysSelectedFrame()
    {
        var player = CreatePlayer(4);
        player.Play();

        player.SelectFrame(2);

        Assert.Equal(2, player.CurrentFrameIndex);
        Assert.False(player.IsPlaying);
    }

    [Fact]
    public void SelectDirection_StopsAndResetsForDifferentFrameCount()
    {
        var player = new FrmPlaybackController();
        player.SetFrameCounts([4, 2]);
        player.SelectFrame(3);
        player.Play();

        Assert.True(player.SelectDirection(1));

        Assert.Equal(1, player.DirectionIndex);
        Assert.Equal(2, player.CurrentDirectionFrameCount);
        Assert.Equal(0, player.CurrentFrameIndex);
        Assert.False(player.IsPlaying);
    }

    [Fact]
    public void SetFrameCounts_WhenFrmChangesStopsAndResetsThePlayer()
    {
        var player = new FrmPlaybackController();
        player.SetFrameCounts([3, 3]);
        player.SelectDirection(1);
        player.SelectFrame(2);
        player.Play();

        player.SetFrameCounts([1, 1]);

        Assert.Equal(0, player.DirectionIndex);
        Assert.Equal(0, player.CurrentFrameIndex);
        Assert.Equal(1, player.CurrentDirectionFrameCount);
        Assert.False(player.IsPlaying);
        Assert.False(player.CanPlay);
    }

    [Fact]
    public void Play_IsHarmlessForEmptyAndSingleFrameDirections()
    {
        var player = new FrmPlaybackController();
        player.SetFrameCounts([]);

        Assert.False(player.Play());
        Assert.False(player.Advance());
        Assert.False(player.IsPlaying);

        player.SetFrameCounts([1]);

        Assert.False(player.Play());
        Assert.False(player.Advance());
        Assert.Equal(0, player.CurrentFrameIndex);
        Assert.False(player.IsPlaying);
    }

    [Fact]
    public void ChangingSpeed_ValidatesAndStoresSubsequentPreviewRate()
    {
        var player = CreatePlayer(2);
        player.Play();

        player.FramesPerSecond = 15;

        Assert.Equal(15, player.FramesPerSecond);
        Assert.True(player.IsPlaying);
        Assert.Throws<ArgumentOutOfRangeException>(() => player.FramesPerSecond = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => player.FramesPerSecond = 31);
    }

    [Fact]
    public void StoppedPlaybackCannotAdvance()
    {
        var player = CreatePlayer(3);

        Assert.False(player.Advance());
        Assert.Equal(0, player.CurrentFrameIndex);

        player.Play();
        player.Pause();

        Assert.False(player.Advance());
        Assert.Equal(0, player.CurrentFrameIndex);
    }

    private static FrmPlaybackController CreatePlayer(int frameCount)
    {
        var player = new FrmPlaybackController();
        player.SetFrameCounts([frameCount]);
        return player;
    }
}
