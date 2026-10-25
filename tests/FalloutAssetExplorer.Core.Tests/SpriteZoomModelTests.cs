using FalloutAssetExplorer.App.ViewModels;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class SpriteZoomModelTests
{
    [Fact]
    public void CalculateFitScale_PreservesAspectRatioAndFitsBothDimensions()
    {
        var scale = SpriteZoomModel.CalculateFitScale(
            viewportWidth: 100,
            viewportHeight: 80,
            imageWidth: 400,
            imageHeight: 100);

        Assert.Equal(0.25, scale);
        Assert.True(400 * scale <= 100);
        Assert.True(100 * scale <= 80);
    }

    [Fact]
    public void Fit_EnlargesSmallSpriteAndCentersItWithinTheViewport()
    {
        var zoom = new SpriteZoomModel();
        zoom.SetViewportSize(640, 480);
        zoom.SetImageSize(16, 24);

        Assert.True(zoom.IsFit);
        Assert.Equal(20, zoom.Zoom);
        Assert.Equal(160, zoom.OffsetX);
        Assert.Equal(0, zoom.OffsetY);
        Assert.True(16 * zoom.Zoom <= 640);
        Assert.True(24 * zoom.Zoom <= 480);
    }

    [Fact]
    public void Fit_UpdatesWithViewportAndImageDimensions()
    {
        var zoom = new SpriteZoomModel();
        zoom.SetViewportSize(100, 100);
        zoom.SetImageSize(200, 100);

        Assert.True(zoom.IsFit);
        Assert.Equal(0.5, zoom.Zoom);
        Assert.Equal(0, zoom.OffsetX);
        Assert.Equal(25, zoom.OffsetY);

        zoom.SetViewportSize(80, 120);

        Assert.Equal(0.4, zoom.Zoom);
        Assert.Equal(0, zoom.OffsetX);
        Assert.Equal(40, zoom.OffsetY);
    }

    [Fact]
    public void ZoomBy_ClampsLimitsAndKeepsTheCursorAnchorWhenPossible()
    {
        var zoom = new SpriteZoomModel();
        zoom.SetViewportSize(100, 100);
        zoom.SetImageSize(100, 100);

        zoom.ZoomBy(2, 25, 40);

        Assert.Equal(2, zoom.Zoom);
        Assert.Equal(-25, zoom.OffsetX);
        Assert.Equal(-40, zoom.OffsetY);
        Assert.False(zoom.IsFit);

        zoom.ZoomBy(1000, 50, 50);
        Assert.Equal(SpriteZoomModel.MaximumZoom, zoom.Zoom);

        zoom.ZoomBy(0.000001, 50, 50);
        Assert.Equal(SpriteZoomModel.MinimumZoom, zoom.Zoom);
    }

    [Fact]
    public void PanBy_ClampsImageWithinViewportAndResetRestoresFitState()
    {
        var zoom = new SpriteZoomModel();
        zoom.SetViewportSize(100, 100);
        zoom.SetImageSize(200, 100);
        zoom.Set100Percent();

        Assert.True(zoom.CanPan);
        zoom.PanBy(-500, 500);
        Assert.Equal(-100, zoom.OffsetX);
        Assert.Equal(0, zoom.OffsetY);

        zoom.ResetForResource();

        Assert.True(zoom.IsFit);
        Assert.False(zoom.HasImage);
        Assert.Equal(1, zoom.Zoom);
        Assert.Equal(0, zoom.OffsetX);
        Assert.Equal(0, zoom.OffsetY);
    }
}
