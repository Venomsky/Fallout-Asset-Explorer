using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class FrmSpritesheetPackerTests
{
    [Fact]
    public void Pack_PlacesSingleFrameAtOriginWithoutOuterMargin()
    {
        var frame = CreateFrame(0, 0, 3, 2);

        var layout = Pack([frame]);

        Assert.Equal(3, layout.Width);
        Assert.Equal(2, layout.Height);
        AssertPlacement(layout, frame, 0, 0);
    }

    [Fact]
    public void Pack_PlacesEqualSizedFramesInInputOrderWithSpacing()
    {
        var frames = new[]
        {
            CreateFrame(0, 0, 2, 2),
            CreateFrame(0, 1, 2, 2),
            CreateFrame(0, 2, 2, 2)
        };

        var layout = Pack(frames, spacing: 1);

        Assert.Equal(8, layout.Width);
        Assert.Equal(2, layout.Height);
        AssertPlacement(layout, frames[0], 0, 0);
        AssertPlacement(layout, frames[1], 3, 0);
        AssertPlacement(layout, frames[2], 6, 0);
    }

    [Fact]
    public void Pack_PacksDifferentSizedFramesIntoTightRows()
    {
        var frames = new[]
        {
            CreateFrame(0, 0, 3, 2),
            CreateFrame(0, 1, 2, 1),
            CreateFrame(0, 2, 4, 3)
        };

        var layout = Pack(frames, spacing: 1, maximumWidth: 6);

        Assert.Equal(6, layout.Width);
        Assert.Equal(6, layout.Height);
        AssertPlacement(layout, frames[0], 0, 0);
        AssertPlacement(layout, frames[1], 4, 0);
        AssertPlacement(layout, frames[2], 0, 3);
        AssertAllFramesPresentAndNonOverlapping(layout);
    }

    [Fact]
    public void Pack_IsDeterministicForSameInput()
    {
        var frames = new[]
        {
            CreateFrame(0, 0, 2, 3),
            CreateFrame(0, 1, 4, 1),
            CreateFrame(1, 0, 1, 2)
        };

        var first = Pack(frames, spacing: 2, maximumWidth: 7);
        var second = Pack(frames, spacing: 2, maximumWidth: 7);

        Assert.Equal(first.Width, second.Width);
        Assert.Equal(first.Height, second.Height);
        Assert.Equal(first.Placements.OrderBy(pair => pair.Key.DirectionIndex)
                .ThenBy(pair => pair.Key.FrameIndex),
            second.Placements.OrderBy(pair => pair.Key.DirectionIndex)
                .ThenBy(pair => pair.Key.FrameIndex));
    }

    [Fact]
    public void Pack_PreservesAllFramesWithoutOverlapsAndFitsEveryRectangle()
    {
        var frames = new[]
        {
            CreateFrame(0, 0, 4, 2),
            CreateFrame(0, 1, 2, 3),
            CreateFrame(1, 0, 3, 1),
            CreateFrame(1, 1, 1, 4)
        };

        var layout = Pack(frames, spacing: 1, maximumWidth: 6);

        Assert.Equal(frames.Length, layout.Placements.Count);
        Assert.Equal(
            frames.Select(frame => frame.Key).OrderBy(key => key.DirectionIndex).ThenBy(key => key.FrameIndex),
            layout.Placements.Keys.OrderBy(key => key.DirectionIndex).ThenBy(key => key.FrameIndex));
        AssertAllFramesPresentAndNonOverlapping(layout);
        Assert.Equal(layout.Placements.Values.Max(placement => placement.Rectangle.X + placement.Rectangle.Width),
            layout.Width);
        Assert.Equal(layout.Placements.Values.Max(placement => placement.Rectangle.Y + placement.Rectangle.Height),
            layout.Height);
    }

    [Fact]
    public void Pack_LeavesConfiguredSpacingBetweenRowsAndFrames()
    {
        var frames = new[]
        {
            CreateFrame(0, 0, 3, 2),
            CreateFrame(0, 1, 2, 2),
            CreateFrame(0, 2, 2, 1)
        };

        var layout = Pack(frames, spacing: 2, maximumWidth: 7);
        var first = layout.Placements[frames[0].Key].Rectangle;
        var second = layout.Placements[frames[1].Key].Rectangle;
        var third = layout.Placements[frames[2].Key].Rectangle;

        Assert.Equal(2, second.X - (first.X + first.Width));
        Assert.Equal(2, third.Y - (first.Y + first.Height));
        Assert.Equal(7, layout.Width);
        Assert.Equal(5, layout.Height);
    }

    [Fact]
    public void Pack_ReturnsZeroSizedLayoutForEmptyInput()
    {
        var layout = Pack([]);

        Assert.Equal(0, layout.Width);
        Assert.Equal(0, layout.Height);
        Assert.Empty(layout.Placements);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    public void Pack_RejectsNonPositiveFrameDimensions(int width, int height)
    {
        var frame = CreateFrame(0, 0, width, height);

        Assert.Throws<ArgumentOutOfRangeException>(() => Pack([frame]));
    }

    [Fact]
    public void Pack_RejectsNegativeSpacing()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FrmSpritesheetPacker.Pack(
                [CreateFrame(0, 0, 1, 1)],
                -1,
                CreateLimits()));
    }

    [Fact]
    public void Pack_RejectsCoordinatesThatWouldOverflowAnInt()
    {
        var frames = new[]
        {
            CreateFrame(0, 0, 1, 1),
            CreateFrame(0, 1, 1, 1)
        };

        Assert.Throws<InvalidOperationException>(() =>
            FrmSpritesheetPacker.Pack(
                frames,
                int.MaxValue,
                CreateLimits(int.MaxValue, int.MaxValue, long.MaxValue)));
    }

    [Fact]
    public void Pack_RejectsConfiguredWidthHeightAndPixelLimitViolations()
    {
        var frame = CreateFrame(0, 0, 2, 2);

        Assert.Throws<InvalidOperationException>(() =>
            FrmSpritesheetPacker.Pack(
                [frame],
                0,
                CreateLimits(maximumWidth: 1)));
        Assert.Throws<InvalidOperationException>(() =>
            FrmSpritesheetPacker.Pack(
                [frame],
                0,
                CreateLimits(maximumHeight: 1)));
        Assert.Throws<InvalidOperationException>(() =>
            FrmSpritesheetPacker.Pack(
                [frame],
                0,
                CreateLimits(maximumPixelCount: 3)));
    }

    [Fact]
    public void Pack_MapsSameSizedFramesFromDifferentDirectionsSeparately()
    {
        var firstDirection = CreateFrame(0, 0, 2, 2);
        var secondDirection = CreateFrame(1, 0, 2, 2);

        var layout = Pack([firstDirection, secondDirection]);

        Assert.Equal(2, layout.Placements.Count);
        AssertPlacement(layout, firstDirection, 0, 0);
        AssertPlacement(layout, secondDirection, 2, 0);
        Assert.NotEqual(
            layout.Placements[firstDirection.Key].Rectangle,
            layout.Placements[secondDirection.Key].Rectangle);
    }

    [Fact]
    public void Pack_RejectsFramesNotOrderedByDirectionThenIndex()
    {
        var directionOutOfOrder = new[]
        {
            CreateFrame(1, 0, 1, 1),
            CreateFrame(0, 0, 1, 1)
        };
        var frameOutOfOrder = new[]
        {
            CreateFrame(0, 1, 1, 1),
            CreateFrame(0, 0, 1, 1)
        };

        Assert.Throws<ArgumentException>(() => Pack(directionOutOfOrder));
        Assert.Throws<ArgumentException>(() => Pack(frameOutOfOrder));
        Assert.Throws<ArgumentException>(() =>
            Pack([CreateFrame(0, 0, 1, 1), CreateFrame(0, 0, 2, 2)]));
    }

    [Fact]
    public void FrameFactory_UsesExistingDirectionAndFrameMetadata()
    {
        var direction = new FrmDirection(
            2,
            "SE",
            -5,
            7,
            [new FrmFrame(3, 4, -2, 6, new byte[12])]);

        var frame = FrmSpritesheetFrame.From(direction, 0);

        Assert.Equal(new FrmSpritesheetFrameKey(2, 0), frame.Key);
        Assert.Equal(3, frame.Width);
        Assert.Equal(4, frame.Height);
        Assert.Equal((short)-2, frame.OffsetX);
        Assert.Equal((short)6, frame.OffsetY);
        Assert.Equal((short)-5, frame.ShiftX);
        Assert.Equal((short)7, frame.ShiftY);
    }

    private static FrmSpritesheetFrame CreateFrame(
        int directionIndex,
        int frameIndex,
        int width,
        int height) =>
        new(
            new FrmSpritesheetFrameKey(directionIndex, frameIndex),
            width,
            height,
            0,
            0,
            0,
            0);

    private static FrmSpritesheetLayout Pack(
        IReadOnlyList<FrmSpritesheetFrame> frames,
        int spacing = 0,
        int maximumWidth = 64) =>
        FrmSpritesheetPacker.Pack(
            frames,
            spacing,
            CreateLimits(maximumWidth: maximumWidth));

    private static FrmSpritesheetPackingLimits CreateLimits(
        int maximumWidth = 64,
        int maximumHeight = 64,
        long maximumPixelCount = 4096) =>
        new(maximumWidth, maximumHeight, maximumPixelCount);

    private static void AssertPlacement(
        FrmSpritesheetLayout layout,
        FrmSpritesheetFrame frame,
        int expectedX,
        int expectedY)
    {
        var placement = layout.Placements[frame.Key];
        Assert.Equal(frame, placement.Frame);
        Assert.Equal(
            new FrmAtlasRectangle(expectedX, expectedY, frame.Width, frame.Height),
            placement.Rectangle);
    }

    private static void AssertAllFramesPresentAndNonOverlapping(FrmSpritesheetLayout layout)
    {
        var placements = layout.Placements.Values.ToArray();
        for (var index = 0; index < placements.Length; index++)
        {
            var rectangle = placements[index].Rectangle;
            Assert.True(rectangle.X >= 0);
            Assert.True(rectangle.Y >= 0);
            Assert.True((long)rectangle.X + rectangle.Width <= layout.Width);
            Assert.True((long)rectangle.Y + rectangle.Height <= layout.Height);

            for (var otherIndex = index + 1; otherIndex < placements.Length; otherIndex++)
            {
                var other = placements[otherIndex].Rectangle;
                var overlaps = rectangle.X < (long)other.X + other.Width
                    && other.X < (long)rectangle.X + rectangle.Width
                    && rectangle.Y < (long)other.Y + other.Height
                    && other.Y < (long)rectangle.Y + rectangle.Height;
                Assert.False(overlaps);
            }
        }
    }
}
