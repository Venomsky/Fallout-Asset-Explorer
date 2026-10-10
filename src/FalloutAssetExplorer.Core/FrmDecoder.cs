using System.Buffers.Binary;

namespace FalloutAssetExplorer.Core;

public static class FrmDecoder
{
    public const int HeaderSize = 62;
    public const int MaximumFrameCount = 256;
    public const int MaximumDimension = 4096;
    public const long MaximumTotalPixels = 8 * 1024 * 1024;
    public const int MaximumFileSize = 32 * 1024 * 1024;

    private const int DirectionCount = 6;
    private const int FrameHeaderSize = 12;

    private static readonly string[] DirectionNames = ["NE", "E", "SE", "SW", "W", "NW"];

    public static FrmDocument Decode(ReadOnlyMemory<byte> data)
    {
        if (data.Length < HeaderSize)
        {
            throw new InvalidDataException("The FRM file is shorter than its 62-byte header.");
        }

        if (data.Length > MaximumFileSize)
        {
            throw new InvalidDataException("The FRM file exceeds the 32 MiB preview limit.");
        }

        var bytes = data.Span;
        var version = ReadUInt32BigEndian(bytes, 0);
        if (version is not (3 or 4))
        {
            throw new InvalidDataException($"Unsupported FRM version {version}; expected version 3 or 4.");
        }

        var framesPerSecond = ReadInt16BigEndian(bytes, 4);
        if (framesPerSecond < 0 || framesPerSecond > 120)
        {
            throw new InvalidDataException($"Invalid FRM frame rate {framesPerSecond}.");
        }

        var actionFrame = ReadInt16BigEndian(bytes, 6);
        var frameCount = ReadInt16BigEndian(bytes, 8);
        if (frameCount <= 0 || frameCount > MaximumFrameCount)
        {
            throw new InvalidDataException(
                $"Invalid FRM frame count {frameCount}; expected 1 through {MaximumFrameCount}.");
        }

        var shiftsX = new short[DirectionCount];
        var shiftsY = new short[DirectionCount];
        var dataOffsets = new uint[DirectionCount];
        for (var direction = 0; direction < DirectionCount; direction++)
        {
            shiftsX[direction] = ReadInt16BigEndian(bytes, 10 + direction * sizeof(short));
            shiftsY[direction] = ReadInt16BigEndian(bytes, 22 + direction * sizeof(short));
            dataOffsets[direction] = ReadUInt32BigEndian(bytes, 34 + direction * sizeof(uint));
        }

        var declaredDataSize = ReadUInt32BigEndian(bytes, 58);
        if (declaredDataSize != data.Length - HeaderSize)
        {
            throw new InvalidDataException(
                $"FRM data-size field declares {declaredDataSize} bytes, but the file contains {data.Length - HeaderSize}.");
        }

        var directions = new FrmDirection[DirectionCount];
        var position = HeaderSize;
        long totalPixels = 0;

        for (var direction = 0; direction < DirectionCount; direction++)
        {
            var directionOffset = dataOffsets[direction];
            if (directionOffset > declaredDataSize)
            {
                throw new InvalidDataException(
                    $"FRM direction {DirectionNames[direction]} starts outside the frame data.");
            }

            if (direction > 0 && directionOffset == dataOffsets[direction - 1])
            {
                directions[direction] = directions[direction - 1] with
                {
                    Index = direction,
                    Name = DirectionNames[direction],
                    ShiftX = shiftsX[direction],
                    ShiftY = shiftsY[direction]
                };
                continue;
            }

            var expectedOffset = position - HeaderSize;
            if (directionOffset != expectedOffset)
            {
                throw new InvalidDataException(
                    $"FRM direction {DirectionNames[direction]} offset {directionOffset} does not match the next frame block at {expectedOffset}.");
            }

            var frames = new FrmFrame[frameCount];
            for (var frameIndex = 0; frameIndex < frames.Length; frameIndex++)
            {
                if (bytes.Length - position < FrameHeaderSize)
                {
                    throw new InvalidDataException(
                        $"FRM direction {DirectionNames[direction]} frame {frameIndex} has a truncated frame header.");
                }

                var width = ReadInt16BigEndian(bytes, position);
                var height = ReadInt16BigEndian(bytes, position + 2);
                var pixelLength = ReadUInt32BigEndian(bytes, position + 4);
                var offsetX = ReadInt16BigEndian(bytes, position + 8);
                var offsetY = ReadInt16BigEndian(bytes, position + 10);

                if (width <= 0 || height <= 0 || width > MaximumDimension || height > MaximumDimension)
                {
                    throw new InvalidDataException(
                        $"FRM direction {DirectionNames[direction]} frame {frameIndex} has invalid dimensions {width}x{height}.");
                }

                var expectedPixelLength = (long)width * height;
                if (pixelLength != expectedPixelLength)
                {
                    throw new InvalidDataException(
                        $"FRM direction {DirectionNames[direction]} frame {frameIndex} declares {pixelLength} pixels; {expectedPixelLength} are required by its dimensions.");
                }

                if (expectedPixelLength > MaximumTotalPixels - totalPixels)
                {
                    throw new InvalidDataException("The FRM exceeds the 8 Mi-pixel decoded preview limit.");
                }

                var pixelsStart = position + FrameHeaderSize;
                if (expectedPixelLength > bytes.Length - pixelsStart)
                {
                    throw new InvalidDataException(
                        $"FRM direction {DirectionNames[direction]} frame {frameIndex} pixel data is truncated.");
                }

                var pixels = bytes.Slice(pixelsStart, (int)expectedPixelLength).ToArray();
                frames[frameIndex] = new FrmFrame(width, height, offsetX, offsetY, pixels);
                totalPixels += expectedPixelLength;
                position = checked(pixelsStart + (int)expectedPixelLength);
            }

            directions[direction] = new FrmDirection(
                direction,
                DirectionNames[direction],
                shiftsX[direction],
                shiftsY[direction],
                frames);
        }

        if (position != bytes.Length)
        {
            throw new InvalidDataException(
                $"FRM contains {bytes.Length - position} unexpected trailing frame-data bytes.");
        }

        return new FrmDocument(
            version,
            framesPerSecond == 0 ? 10 : framesPerSecond,
            actionFrame,
            frameCount,
            directions);
    }

    private static short ReadInt16BigEndian(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadInt16BigEndian(bytes.Slice(offset, sizeof(short)));

    private static uint ReadUInt32BigEndian(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, sizeof(uint)));
}
