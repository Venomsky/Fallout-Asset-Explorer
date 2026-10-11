using System.Buffers.Binary;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class FrmDecoderTests
{
    [Fact]
    public void Decode_ParsesMinimalFrameAndSharesBlockAcrossDirections()
    {
        byte[] pixels = [0, 1, 127, 255];
        var data = FrmFixture.Create([[new FrmFixtureFrame(2, 2, -3, 4, pixels)]]);

        var document = FrmDecoder.Decode(data);

        Assert.Equal(3u, document.Version);
        Assert.Equal(10, document.FramesPerSecond);
        Assert.Equal(0, document.ActionFrame);
        Assert.Equal(1, document.FramesPerDirection);
        Assert.Equal(6, document.Directions.Count);
        Assert.Equal(pixels, document.Directions[0].Frames[0].Pixels);
        Assert.Equal(pixels, document.Directions[5].Frames[0].Pixels);
        Assert.Equal("NW", document.Directions[5].Name);
        Assert.Equal(-3, document.Directions[0].Frames[0].OffsetX);
        Assert.Equal(4, document.Directions[0].Frames[0].OffsetY);
    }

    [Fact]
    public void Decode_ParsesMultipleFramesAndDistinctDirectionBlocks()
    {
        var data = FrmFixture.Create(
        [
            [
                new FrmFixtureFrame(2, 1, 1, -1, [10, 20]),
                new FrmFixtureFrame(1, 2, 2, -2, [30, 40])
            ],
            [
                new FrmFixtureFrame(1, 1, 3, -3, [50]),
                new FrmFixtureFrame(1, 1, 4, -4, [60])
            ]
        ]);

        var document = FrmDecoder.Decode(data);

        Assert.Equal(2, document.FramesPerDirection);
        Assert.Equal([10, 20], document.Directions[0].Frames[0].Pixels);
        Assert.Equal([30, 40], document.Directions[0].Frames[1].Pixels);
        Assert.Equal(1, document.Directions[0].Frames[1].Width);
        Assert.Equal(2, document.Directions[0].Frames[1].Height);
        Assert.Equal("E", document.Directions[1].Name);
        Assert.Equal([50], document.Directions[1].Frames[0].Pixels);
        Assert.Equal([50], document.Directions[5].Frames[0].Pixels);
    }

    [Fact]
    public void Decode_UsesNonzeroFrameRate()
    {
        var data = FrmFixture.Create(
            [[new FrmFixtureFrame(1, 1, 0, 0, [1])]],
            framesPerSecond: 12,
            actionFrame: 1);

        var document = FrmDecoder.Decode(data);

        Assert.Equal(12, document.FramesPerSecond);
        Assert.Equal((short)1, document.ActionFrame);
    }

    [Fact]
    public void Decode_ParsesFalloutTwoVersionFourFrame()
    {
        var data = FrmFixture.Create([[new FrmFixtureFrame(1, 1, 0, 0, [42])]]);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0, 4), 4);

        var document = FrmDecoder.Decode(data);

        Assert.Equal(4u, document.Version);
        Assert.Equal([42], document.Directions[0].Frames[0].Pixels);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(61)]
    public void Decode_RejectsTruncatedHeader(int length)
    {
        Assert.Throws<InvalidDataException>(() => FrmDecoder.Decode(new byte[length]));
    }

    [Fact]
    public void Decode_RejectsTruncatedFrameHeader()
    {
        var data = FrmFixture.CreateHeaderOnly();

        Assert.Throws<InvalidDataException>(() => FrmDecoder.Decode(data));
    }

    [Fact]
    public void Decode_RejectsInvalidDirectionOffset()
    {
        var data = FrmFixture.Create([[new FrmFixtureFrame(1, 1, 0, 0, [1])]]);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(34, 4), 1);

        Assert.Throws<InvalidDataException>(() => FrmDecoder.Decode(data));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(4097, 1)]
    public void Decode_RejectsInvalidDimensions(int width, int height)
    {
        var data = FrmFixture.Create([[new FrmFixtureFrame(1, 1, 0, 0, [1])]]);
        BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(62, 2), checked((short)width));
        BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(64, 2), checked((short)height));

        Assert.Throws<InvalidDataException>(() => FrmDecoder.Decode(data));
    }

    [Fact]
    public void Decode_RejectsExcessiveFrameCount()
    {
        var data = FrmFixture.CreateHeaderOnly();
        BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(8, 2), FrmDecoder.MaximumFrameCount + 1);

        Assert.Throws<InvalidDataException>(() => FrmDecoder.Decode(data));
    }

    [Fact]
    public void Decode_RejectsPixelLengthThatDoesNotMatchDimensions()
    {
        var data = FrmFixture.Create([[new FrmFixtureFrame(1, 1, 0, 0, [1])]]);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(66, 4), uint.MaxValue);

        Assert.Throws<InvalidDataException>(() => FrmDecoder.Decode(data));
    }

    [Fact]
    public void Decode_RejectsExcessivePixelCountBeforeReadingPixelPayload()
    {
        var data = FrmFixture.Create([[new FrmFixtureFrame(1, 1, 0, 0, [1])]]);
        BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(62, 2), FrmDecoder.MaximumDimension);
        BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(64, 2), FrmDecoder.MaximumDimension);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(66, 4), (uint)FrmDecoder.MaximumDimension * FrmDecoder.MaximumDimension);

        Assert.Throws<InvalidDataException>(() => FrmDecoder.Decode(data));
    }

    [Fact]
    public void Decode_RejectsTruncatedPixelData()
    {
        var data = FrmFixture.Create([[new FrmFixtureFrame(2, 2, 0, 0, [1, 2, 3, 4])]]);
        Array.Resize(ref data, data.Length - 1);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(58, 4), (uint)(data.Length - FrmDecoder.HeaderSize));

        Assert.Throws<InvalidDataException>(() => FrmDecoder.Decode(data));
    }

    [Fact]
    public void Decode_RejectsInconsistentDeclaredDataSize()
    {
        var data = FrmFixture.Create([[new FrmFixtureFrame(1, 1, 0, 0, [1])]]);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(58, 4), 0);

        Assert.Throws<InvalidDataException>(() => FrmDecoder.Decode(data));
    }

    [Fact]
    public void Decode_RejectsTrailingData()
    {
        var data = FrmFixture.Create([[new FrmFixtureFrame(1, 1, 0, 0, [1])]]);
        Array.Resize(ref data, data.Length + 1);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(58, 4), (uint)(data.Length - FrmDecoder.HeaderSize));

        Assert.Throws<InvalidDataException>(() => FrmDecoder.Decode(data));
    }

    [Fact]
    public void Decode_RejectsUnsupportedVersion()
    {
        var data = FrmFixture.Create([[new FrmFixtureFrame(1, 1, 0, 0, [1])]]);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0, 4), 5);

        Assert.Throws<InvalidDataException>(() => FrmDecoder.Decode(data));
    }

    [Fact]
    public void Decode_RejectsNegativeFrameRate()
    {
        var data = FrmFixture.Create([[new FrmFixtureFrame(1, 1, 0, 0, [1])]]);
        BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(4, 2), -1);

        Assert.Throws<InvalidDataException>(() => FrmDecoder.Decode(data));
    }
}

internal sealed record FrmFixtureFrame(short Width, short Height, short OffsetX, short OffsetY, byte[] Pixels);

internal static class FrmFixture
{
    public static byte[] Create(
        IReadOnlyList<FrmFixtureFrame[]> directionBlocks,
        short framesPerSecond = 0,
        short actionFrame = 0)
    {
        if (directionBlocks.Count is < 1 or > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(directionBlocks));
        }

        var framesPerDirection = directionBlocks[0].Length;
        if (framesPerDirection is < 1 or > short.MaxValue
            || directionBlocks.Any(block => block.Length != framesPerDirection))
        {
            throw new ArgumentException("All frame blocks must have the same nonzero frame count.", nameof(directionBlocks));
        }

        using var data = new MemoryStream();
        var offsets = new uint[6];
        for (var direction = 0; direction < directionBlocks.Count; direction++)
        {
            offsets[direction] = checked((uint)data.Length);
            var frames = directionBlocks[direction];
            foreach (var frame in frames)
            {
                if (frame.Pixels.Length != frame.Width * frame.Height)
                {
                    throw new ArgumentException("Fixture pixel count must equal frame dimensions.", nameof(directionBlocks));
                }

                WriteInt16(data, frame.Width);
                WriteInt16(data, frame.Height);
                WriteUInt32(data, (uint)frame.Pixels.Length);
                WriteInt16(data, frame.OffsetX);
                WriteInt16(data, frame.OffsetY);
                data.Write(frame.Pixels);
            }
        }

        for (var direction = directionBlocks.Count; direction < offsets.Length; direction++)
        {
            offsets[direction] = offsets[direction - 1];
        }

        var frameData = data.ToArray();
        var result = new byte[FrmDecoder.HeaderSize + frameData.Length];
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(0, 4), 3);
        BinaryPrimitives.WriteInt16BigEndian(result.AsSpan(4, 2), framesPerSecond);
        BinaryPrimitives.WriteInt16BigEndian(result.AsSpan(6, 2), actionFrame);
        BinaryPrimitives.WriteInt16BigEndian(result.AsSpan(8, 2), (short)framesPerDirection);
        for (var direction = 0; direction < 6; direction++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(34 + direction * 4, 4), offsets[direction]);
        }

        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(58, 4), (uint)frameData.Length);
        frameData.CopyTo(result, FrmDecoder.HeaderSize);
        return result;
    }

    public static byte[] CreateHeaderOnly()
    {
        var result = new byte[FrmDecoder.HeaderSize];
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(0, 4), 3);
        BinaryPrimitives.WriteInt16BigEndian(result.AsSpan(8, 2), 1);
        return result;
    }

    private static void WriteInt16(Stream stream, short value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(short)];
        BinaryPrimitives.WriteInt16BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }
}
