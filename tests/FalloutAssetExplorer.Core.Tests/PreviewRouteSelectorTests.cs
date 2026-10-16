using FalloutAssetExplorer.App.ViewModels;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class PreviewRouteSelectorTests
{
    [Theory]
    [InlineData(".FRM", PreviewRoute.Frm)]
    [InlineData(".fRm", PreviewRoute.Frm)]
    [InlineData(".MAP", PreviewRoute.Map)]
    [InlineData(".mAp", PreviewRoute.Map)]
    [InlineData(".PRO", PreviewRoute.Pro)]
    [InlineData(".pRo", PreviewRoute.Pro)]
    [InlineData(".MSG", PreviewRoute.Msg)]
    [InlineData(".mSg", PreviewRoute.Msg)]
    [InlineData(".INT", PreviewRoute.Int)]
    [InlineData(".iNt", PreviewRoute.Int)]
    public void Select_RecognizesSupportedExtensionsCaseInsensitively(
        string extension,
        PreviewRoute expectedRoute)
    {
        Assert.Equal(expectedRoute, PreviewRouteSelector.Select(extension));
    }

    [Theory]
    [InlineData(".LST")]
    [InlineData(".BIN")]
    [InlineData("")]
    [InlineData(null)]
    public void Select_ReturnsUnsupportedForOtherOrMissingExtensions(string? extension)
    {
        Assert.Equal(PreviewRoute.Unsupported, PreviewRouteSelector.Select(extension));
    }
}
