using System.Buffers.Binary;
using System.IO.Compression;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class FrmPngExporterTests
{
    [Fact]
    public void Write_EncodesSelectedDirectionAndFrameAsNativeSizeRgbaPng()
    {
        var palette = CreatePalette();
        var selectedFrame = new FrmFrame(2, 1, 0, 0, [0, 2]);
        var document = CreateDocument(
            new FrmDirection(0, "NE", 0, 0, [new FrmFrame(1, 1, 0, 0, [1])]),
            new FrmDirection(
                1,
                "E",
                0,
                0,
                [
                    new FrmFrame(1, 1, 0, 0, [1]),
                    selectedFrame
                ]));

        using var output = new MemoryStream();
        FrmPngExporter.Write(document, 1, 1, palette, output);
        var decoded = DecodePng(output.ToArray());

        Assert.Equal(2, decoded.Width);
        Assert.Equal(1, decoded.Height);
        Assert.Equal(
            new byte[]
            {
                0, 0, 0, 0,
                252, 4, 0, 255
            },
            decoded.Rgba);
    }

    [Fact]
    public void Write_ProducesFullyTransparentPixelsForAnAllZeroFrame()
    {
        var document = CreateDocument(
            new FrmDirection(0, "NE", 0, 0, [new FrmFrame(2, 2, 0, 0, [0, 0, 0, 0])]));

        using var output = new MemoryStream();
        FrmPngExporter.Write(document, 0, 0, CreatePalette(), output);
        var decoded = DecodePng(output.ToArray());

        Assert.Equal(2, decoded.Width);
        Assert.Equal(2, decoded.Height);
        Assert.Equal(new byte[16], decoded.Rgba);
    }

    [Fact]
    public void Write_RejectsInvalidDimensionsAndMismatchedPixels()
    {
        var palette = CreatePalette();
        var invalidDimensions = CreateDocument(
            new FrmDirection(0, "NE", 0, 0, [new FrmFrame(0, 1, 0, 0, [])]));
        var mismatchedPixels = CreateDocument(
            new FrmDirection(0, "NE", 0, 0, [new FrmFrame(2, 1, 0, 0, [1])]));

        Assert.Throws<InvalidDataException>(() =>
            FrmPngExporter.Write(invalidDimensions, 0, 0, palette, new MemoryStream()));
        Assert.Throws<InvalidDataException>(() =>
            FrmPngExporter.Write(mismatchedPixels, 0, 0, palette, new MemoryStream()));
    }

    [Fact]
    public void Write_RejectsMissingPaletteBeforeWriting()
    {
        var document = CreateDocument(
            new FrmDirection(0, "NE", 0, 0, [new FrmFrame(1, 1, 0, 0, [1])]));
        using var output = new MemoryStream();

        Assert.Throws<ArgumentNullException>(() =>
            FrmPngExporter.Write(document, 0, 0, null!, output));
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public void Write_RejectsOutOfRangeSelectionAndNonWritableDestination()
    {
        var document = CreateDocument(
            new FrmDirection(0, "NE", 0, 0, [new FrmFrame(1, 1, 0, 0, [1])]));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FrmPngExporter.Write(document, 1, 0, CreatePalette(), new MemoryStream()));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FrmPngExporter.Write(document, 0, 1, CreatePalette(), new MemoryStream()));
        Assert.Throws<ArgumentException>(() =>
            FrmPngExporter.Write(
                document,
                0,
                0,
                CreatePalette(),
                new MemoryStream([0], writable: false)));
    }

    [Fact]
    public void Write_ExportsNativePixelsWithoutPreviewScalingOrCheckerboard()
    {
        var document = CreateDocument(
            new FrmDirection(0, "NE", 0, 0, [new FrmFrame(1, 2, 0, 0, [0, 1])]));
        using var output = new MemoryStream();

        FrmPngExporter.Write(document, 0, 0, CreatePalette(), output);
        var decoded = DecodePng(output.ToArray());

        Assert.Equal(1, decoded.Width);
        Assert.Equal(2, decoded.Height);
        Assert.Equal(
            new byte[] { 0, 0, 0, 0, 40, 80, 120, 255 },
            decoded.Rgba);
    }

    [Fact]
    public void Write_StreamsCompressedDataAcrossMultipleValidIdatChunks()
    {
        var paletteBytes = new byte[FalloutPalette.MinimalFileSize];
        var random = new Random(173);
        for (var index = 0; index < FalloutPalette.RgbDataSize; index++)
        {
            paletteBytes[index] = (byte)random.Next(64);
        }

        var palette = FalloutPalette.Parse(paletteBytes);
        var pixels = new byte[256 * 256];
        random.NextBytes(pixels);
        var frame = new FrmFrame(256, 256, 0, 0, pixels);
        var document = CreateDocument(new FrmDirection(0, "NE", 0, 0, [frame]));
        using var output = new MemoryStream();

        FrmPngExporter.Write(document, 0, 0, palette, output);
        var decoded = DecodePng(output.ToArray());
        var expectedRgba = new byte[pixels.Length * 4];
        palette.CopyRgba(frame, expectedRgba);

        Assert.True(decoded.IdatChunkCount > 1);
        Assert.Equal(256, decoded.Width);
        Assert.Equal(256, decoded.Height);
        Assert.Equal(expectedRgba, decoded.Rgba);
    }

    private static FalloutPalette CreatePalette()
    {
        var bytes = new byte[FalloutPalette.MinimalFileSize];
        bytes[3] = 10;
        bytes[4] = 20;
        bytes[5] = 30;
        bytes[6] = 63;
        bytes[7] = 1;
        bytes[8] = 0;
        return FalloutPalette.Parse(bytes);
    }

    private static FrmDocument CreateDocument(params FrmDirection[] directions) =>
        new(4, 10, 0, directions.Max(direction => direction.Frames.Count), directions);

    private static DecodedPng DecodePng(byte[] bytes)
    {
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes.AsSpan(0, 8).ToArray());

        var offset = 8;
        var width = 0;
        var height = 0;
        var idatChunkCount = 0;
        using var compressedData = new MemoryStream();
        while (offset < bytes.Length)
        {
            var chunkLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4)));
            offset += 4;
            var chunkStart = offset;
            var type = bytes.AsSpan(offset, 4);
            offset += 4;
            var data = bytes.AsSpan(offset, chunkLength);
            offset += chunkLength;
            var expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, sizeof(uint)));
            Assert.Equal(expectedCrc, CalculateCrc(bytes.AsSpan(chunkStart, sizeof(uint) + chunkLength)));
            offset += sizeof(uint);

            if (type.SequenceEqual("IHDR"u8))
            {
                width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data));
                height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data[4..]));
                Assert.Equal(8, data[8]);
                Assert.Equal(6, data[9]);
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                idatChunkCount++;
                compressedData.Write(data);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                break;
            }
        }

        Assert.Equal(bytes.Length, offset);
        Assert.True(width > 0);
        Assert.True(height > 0);

        compressedData.Position = 0;
        using var decompressor = new ZLibStream(compressedData, CompressionMode.Decompress);
        using var scanlines = new MemoryStream();
        decompressor.CopyTo(scanlines);

        var bytesPerRow = checked(width * 4 + 1);
        Assert.Equal(checked(bytesPerRow * height), checked((int)scanlines.Length));
        var raw = scanlines.ToArray();
        var rgba = new byte[checked(width * height * 4)];
        for (var row = 0; row < height; row++)
        {
            var sourceOffset = row * bytesPerRow;
            Assert.Equal(0, raw[sourceOffset]);
            raw.AsSpan(sourceOffset + 1, width * 4).CopyTo(rgba.AsSpan(row * width * 4));
        }

        return new DecodedPng(width, height, rgba, idatChunkCount);
    }

    private static uint CalculateCrc(ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0
                    ? 0xEDB88320 ^ (crc >> 1)
                    : crc >> 1;
            }
        }

        return ~crc;
    }

    private sealed record DecodedPng(int Width, int Height, byte[] Rgba, int IdatChunkCount);
}
