using FalloutAssetExplorer.App.Views;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class MapObjectSearchHighlighterTests
{
    [Fact]
    public void CreateSegments_HighlightsCaseInsensitivePartialMatches()
    {
        var segments = MapObjectSearchHighlighter.CreateSegments(
            "Prototype file: 00000923.pro",
            "00000923");

        Assert.Equal(
            [
                new MapObjectSearchHighlightSegment("Prototype file: ", IsMatch: false),
                new MapObjectSearchHighlightSegment("00000923", IsMatch: true),
                new MapObjectSearchHighlightSegment(".pro", IsMatch: false)
            ],
            segments);
    }

    [Fact]
    public void CreateSegments_HighlightsMultipleOccurrences()
    {
        var segments = MapObjectSearchHighlighter.CreateSegments("PID abc | FID ABC", "aBc");

        Assert.Equal(
            [
                new MapObjectSearchHighlightSegment("PID ", IsMatch: false),
                new MapObjectSearchHighlightSegment("abc", IsMatch: true),
                new MapObjectSearchHighlightSegment(" | FID ", IsMatch: false),
                new MapObjectSearchHighlightSegment("ABC", IsMatch: true)
            ],
            segments);
    }

    [Fact]
    public void CreateSegments_LeavesMetadataOnlyMatchesUnhighlightedInVisibleText()
    {
        var segments = MapObjectSearchHighlighter.CreateSegments("Record #31 | Elev 0", "scenery");

        Assert.Equal(
            [new MapObjectSearchHighlightSegment("Record #31 | Elev 0", IsMatch: false)],
            segments);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t ")]
    public void CreateSegments_DoesNotHighlightForEmptyOrWhitespaceQuery(string query)
    {
        var segments = MapObjectSearchHighlighter.CreateSegments("Record #31", query);

        Assert.Equal(
            [new MapObjectSearchHighlightSegment("Record #31", IsMatch: false)],
            segments);
    }
}
