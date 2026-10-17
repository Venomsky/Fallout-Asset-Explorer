using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class FrmSpritesheetPngExporterTests
{
    [Fact]
    public void Write_EncodesAllLogicalDirectionsAndFramesWithUtf8Metadata()
    {
        FrmFrame[] sharedFrames =
        [
            new FrmFrame(1, 1, -3, 4, [1]),
            new FrmFrame(2, 1, 7, -8, [2, 3])
        ];
        string[] names = ["NE", "E", "SE", "SW", "W", "NW"];
        var directions = Enumerable.Range(0, 6)
            .Reverse()
            .Select(index => new FrmDirection(
                index,
                names[index],
                checked((short)(index - 3)),
                checked((short)(4 - index)),
                sharedFrames))
            .ToArray();
        var document = new FrmDocument(4, 12, 1, 2, directions);
        using var output = new MemoryStream();

        FrmSpritesheetPngExporter.Write(
            document,
            CreatePalette(),
            output,
            CreateLimits(maximumWidth: 4),
            spacing: 1,
            assetName: "герой-🐾.frm");

        var png = DecodePng(output.ToArray());
        Assert.Equal(4, png.Width);
        Assert.Equal(11, png.Height);
        AssertValidPngChunkOrderAndMetadata(png);
        Assert.Equal("FrmSpritesheet", png.MetadataKeyword);
        Assert.Equal(0, png.CompressionFlag);
        Assert.Equal(0, png.CompressionMethod);
        Assert.Equal(string.Empty, png.LanguageTag);
        Assert.Equal(string.Empty, png.TranslatedKeyword);

        using var metadata = JsonDocument.Parse(png.MetadataJson);
        var root = metadata.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("герой-🐾.frm", root.GetProperty("assetName").GetString());
        Assert.Equal(4, root.GetProperty("sheetWidth").GetInt32());
        Assert.Equal(11, root.GetProperty("sheetHeight").GetInt32());
        Assert.Equal(12, root.GetProperty("framesPerSecond").GetInt32());
        Assert.Equal(1, root.GetProperty("actionFrame").GetInt16());

        var metadataDirections = root.GetProperty("directions").EnumerateArray().ToArray();
        Assert.Equal(6, metadataDirections.Length);
        Assert.Equal(Enumerable.Range(0, 6), metadataDirections.Select(item => item.GetProperty("index").GetInt32()));
        foreach (var direction in metadataDirections)
        {
            Assert.Equal(2, direction.GetProperty("frames").GetArrayLength());
        }

        var firstDirection = metadataDirections[0];
        var firstFrame = firstDirection.GetProperty("frames")[0];
        Assert.Equal(0, firstFrame.GetProperty("directionIndex").GetInt32());
        Assert.Equal(0, firstFrame.GetProperty("frameIndex").GetInt32());
        Assert.Equal(-3, firstFrame.GetProperty("offsetX").GetInt16());
        Assert.Equal(4, firstFrame.GetProperty("offsetY").GetInt16());
        Assert.Equal(-3, firstFrame.GetProperty("shiftX").GetInt16());
        Assert.Equal(4, firstFrame.GetProperty("shiftY").GetInt16());
        Assert.Equal(0, firstFrame.GetProperty("atlas").GetProperty("x").GetInt32());
        Assert.Equal(0, firstFrame.GetProperty("atlas").GetProperty("y").GetInt32());
        Assert.Equal(1, firstFrame.GetProperty("originalWidth").GetInt32());
        Assert.Equal(1, firstFrame.GetProperty("originalHeight").GetInt32());

        AssertRgba(png, 0, 0, 40, 80, 120, 255);
        AssertRgba(png, 1, 0, 0, 0, 0, 0);
        AssertRgba(png, 2, 0, 252, 4, 0, 255);
        AssertRgba(png, 3, 0, 0, 0, 252, 255);
        AssertRgba(png, 0, 2, 40, 80, 120, 255);
    }

    [Fact]
    public void Write_PacksDifferentFrameDimensionsAndPreservesRawOffsetsWithoutMovingPixels()
    {
        var first = new FrmFrame(2, 1, -20, 30, [0, 1]);
        var second = new FrmFrame(1, 2, 40, -50, [2, 3]);
        var document = CreateDocument(
            new FrmDirection(0, "NE", 100, -200, [first, second]));
        using var output = new MemoryStream();

        FrmSpritesheetPngExporter.Write(
            document,
            CreatePalette(),
            output,
            CreateLimits(maximumWidth: 3),
            spacing: 1);

        var png = DecodePng(output.ToArray());
        Assert.Equal(2, png.Width);
        Assert.Equal(4, png.Height);
        AssertRgba(png, 0, 0, 0, 0, 0, 0);
        AssertRgba(png, 1, 0, 40, 80, 120, 255);
        AssertRgba(png, 0, 2, 252, 4, 0, 255);
        AssertRgba(png, 0, 3, 0, 0, 252, 255);

        using var metadata = JsonDocument.Parse(png.MetadataJson);
        var frames = metadata.RootElement.GetProperty("directions")[0].GetProperty("frames");
        Assert.Equal(-20, frames[0].GetProperty("offsetX").GetInt16());
        Assert.Equal(30, frames[0].GetProperty("offsetY").GetInt16());
        Assert.Equal(100, frames[0].GetProperty("shiftX").GetInt16());
        Assert.Equal(-200, frames[0].GetProperty("shiftY").GetInt16());
        Assert.Equal(0, frames[0].GetProperty("atlas").GetProperty("x").GetInt32());
        Assert.Equal(0, frames[0].GetProperty("atlas").GetProperty("y").GetInt32());
        Assert.Equal(0, frames[1].GetProperty("atlas").GetProperty("x").GetInt32());
        Assert.Equal(2, frames[1].GetProperty("atlas").GetProperty("y").GetInt32());
        Assert.Equal(1, frames[1].GetProperty("originalWidth").GetInt32());
        Assert.Equal(2, frames[1].GetProperty("originalHeight").GetInt32());
    }

    [Fact]
    public void WriteDirection_ExportsOnlyTheRequestedDirection()
    {
        var document = CreateDocument(
            new FrmDirection(0, "NE", 0, 0, [new FrmFrame(1, 1, 0, 0, [1])]),
            new FrmDirection(1, "E", 12, -7,
            [
                new FrmFrame(1, 1, -2, 3, [2]),
                new FrmFrame(2, 1, 4, -5, [3, 0])
            ]));
        using var output = new MemoryStream();

        FrmSpritesheetPngExporter.WriteDirection(
            document,
            directionIndex: 1,
            CreatePalette(),
            output,
            CreateLimits(maximumWidth: 4),
            spacing: 1);

        var png = DecodePng(output.ToArray());
        Assert.Equal(4, png.Width);
        Assert.Equal(1, png.Height);
        AssertRgba(png, 0, 0, 252, 4, 0, 255);
        AssertRgba(png, 1, 0, 0, 0, 0, 0);
        AssertRgba(png, 2, 0, 0, 0, 252, 255);
        AssertRgba(png, 3, 0, 0, 0, 0, 0);

        using var metadata = JsonDocument.Parse(png.MetadataJson);
        var direction = Assert.Single(metadata.RootElement.GetProperty("directions").EnumerateArray());
        Assert.Equal(1, direction.GetProperty("index").GetInt32());
        Assert.Equal("E", direction.GetProperty("name").GetString());
        var frames = direction.GetProperty("frames");
        Assert.Equal(2, frames.GetArrayLength());
        Assert.Equal(-2, frames[0].GetProperty("offsetX").GetInt16());
        Assert.Equal(12, frames[0].GetProperty("shiftX").GetInt16());
    }

    [Fact]
    public void WriteAllDirectionsSeparated_StacksVariableSizedDirectionBlocksWithTransparentGaps()
    {
        var document = CreateDocument(
            new FrmDirection(0, "NE", 0, 0,
            [
                new FrmFrame(2, 1, -1, 2, [1, 0]),
                new FrmFrame(1, 2, 3, -4, [2, 3])
            ]),
            new FrmDirection(1, "E", 8, 9, [new FrmFrame(1, 1, 10, 11, [3])]));
        using var output = new MemoryStream();

        FrmSpritesheetPngExporter.WriteAllDirectionsSeparated(
            document,
            CreatePalette(),
            output,
            CreateLimits(maximumWidth: 4),
            directionSpacing: 2);

        var png = DecodePng(output.ToArray());
        Assert.Equal(3, png.Width);
        Assert.Equal(5, png.Height);
        AssertRgba(png, 0, 0, 40, 80, 120, 255);
        AssertRgba(png, 1, 0, 0, 0, 0, 0);
        AssertRgba(png, 2, 0, 252, 4, 0, 255);
        AssertRgba(png, 2, 1, 0, 0, 252, 255);
        for (var y = 2; y <= 3; y++)
        {
            for (var x = 0; x < png.Width; x++)
            {
                AssertRgba(png, x, y, 0, 0, 0, 0);
            }
        }

        AssertRgba(png, 0, 4, 0, 0, 252, 255);
        using var metadata = JsonDocument.Parse(png.MetadataJson);
        var directions = metadata.RootElement.GetProperty("directions").EnumerateArray().ToArray();
        Assert.Equal(2, directions.Length);
        Assert.Equal(0, directions[0].GetProperty("frames")[0]
            .GetProperty("atlas").GetProperty("y").GetInt32());
        Assert.Equal(4, directions[1].GetProperty("frames")[0]
            .GetProperty("atlas").GetProperty("y").GetInt32());
        Assert.Equal(10, directions[1].GetProperty("frames")[0].GetProperty("offsetX").GetInt16());
    }

    [Fact]
    public void WriteAndGetLayout_ReturnsTheExactDirectionSeparatedPngPlacements()
    {
        var document = CreateDocument(
            new FrmDirection(0, "NE", 0, 0,
            [
                new FrmFrame(2, 1, -1, 2, [1, 0]),
                new FrmFrame(1, 2, 3, -4, [2, 3])
            ]),
            new FrmDirection(1, "E", 8, 9, [new FrmFrame(1, 1, 10, 11, [3])]));
        using var output = new MemoryStream();

        var layout = FrmSpritesheetPngExporter.WriteAndGetLayout(
            document,
            CreatePalette(),
            output,
            CreateLimits(maximumWidth: 4),
            separateDirections: true,
            frameSpacing: 1,
            directionSpacing: 24);

        var png = DecodePng(output.ToArray());
        Assert.Equal((layout.Width, layout.Height), (png.Width, png.Height));
        Assert.Equal(new FrmAtlasRectangle(0, 0, 2, 1), layout.Placements[new(0, 0)].Rectangle);
        Assert.Equal(new FrmAtlasRectangle(3, 0, 1, 2), layout.Placements[new(0, 1)].Rectangle);
        Assert.Equal(new FrmAtlasRectangle(0, 26, 1, 1), layout.Placements[new(1, 0)].Rectangle);
        AssertRgba(png, 0, 0, 40, 80, 120, 255);
        AssertRgba(png, 1, 0, 0, 0, 0, 0);
        AssertRgba(png, 3, 0, 252, 4, 0, 255);
        AssertRgba(png, 0, 26, 0, 0, 252, 255);
    }

    [Fact]
    public void Write_OmitsAssetNameWhenUnavailable()
    {
        var document = CreateDocument(
            new FrmDirection(0, "NE", 0, 0, [new FrmFrame(1, 1, 0, 0, [1])]));
        using var output = new MemoryStream();

        FrmSpritesheetPngExporter.Write(document, CreatePalette(), output, CreateLimits());

        var png = DecodePng(output.ToArray());
        using var metadata = JsonDocument.Parse(png.MetadataJson);
        Assert.False(metadata.RootElement.TryGetProperty("assetName", out _));
    }

    [Fact]
    public void Write_RejectsAnEmptyAnimationBeforeWritingPngBytes()
    {
        var document = CreateDocument(new FrmDirection(0, "NE", 0, 0, []));
        using var output = new MemoryStream();

        Assert.Throws<InvalidDataException>(() =>
            FrmSpritesheetPngExporter.Write(document, CreatePalette(), output, CreateLimits()));

        Assert.Empty(output.ToArray());
    }

    [Fact]
    public void Write_CancelsDuringIdatEncodingAndDoesNotWriteIend()
    {
        const int width = 512;
        const int height = 512;
        var pixels = new byte[width * height];
        new Random(1729).NextBytes(pixels);
        var document = CreateDocument(
            new FrmDirection(0, "NE", 0, 0, [new FrmFrame(width, height, 0, 0, pixels)]));
        using var cancellation = new CancellationTokenSource();
        using var output = new CancelOnIdatWriteStream(cancellation);

        Assert.ThrowsAny<OperationCanceledException>(() =>
            FrmSpritesheetPngExporter.Write(
                document,
                CreateHighEntropyPalette(),
                output,
                CreateLimits(
                    maximumWidth: width,
                    maximumHeight: height,
                    maximumPixelCount: width * height),
                cancellationToken: cancellation.Token));

        Assert.True(output.SawIdat);
        Assert.False(output.SawIend);
        Assert.True(cancellation.IsCancellationRequested);
    }

    [Fact]
    public void Write_RejectsMissingPaletteBeforeWritingPngBytes()
    {
        var document = CreateDocument(
            new FrmDirection(0, "NE", 0, 0, [new FrmFrame(1, 1, 0, 0, [1])]));
        using var output = new MemoryStream();

        var exception = Assert.Throws<ArgumentNullException>(() =>
            FrmSpritesheetPngExporter.Write(document, null, output, CreateLimits()));

        Assert.Contains("resolved Fallout palette", exception.Message);
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public void Write_RejectsExceededPackingLimitsBeforeWritingImage()
    {
        var document = CreateDocument(
            new FrmDirection(0, "NE", 0, 0, [new FrmFrame(2, 2, 0, 0, [1, 1, 1, 1])]));
        using var output = new MemoryStream();

        Assert.Throws<InvalidOperationException>(() =>
            FrmSpritesheetPngExporter.Write(
                document,
                CreatePalette(),
                output,
                CreateLimits(maximumWidth: 1)));

        Assert.Empty(output.ToArray());
    }

    [Fact]
    public void Write_RejectsExceededPixelLimitBeforeWritingImage()
    {
        var document = CreateDocument(
            new FrmDirection(0, "NE", 0, 0, [new FrmFrame(2, 2, 0, 0, [1, 1, 1, 1])]));
        using var output = new MemoryStream();

        Assert.Throws<InvalidOperationException>(() =>
            FrmSpritesheetPngExporter.Write(
                document,
                CreatePalette(),
                output,
                CreateLimits(maximumPixelCount: 3)));

        Assert.Empty(output.ToArray());
    }

    [Fact]
    public void Write_RejectsMalformedPixelsBeforeWritingPngBytes()
    {
        var document = CreateDocument(
            new FrmDirection(0, "NE", 0, 0, [new FrmFrame(2, 2, 0, 0, [1, 2, 3])]));
        using var output = new MemoryStream();

        Assert.Throws<InvalidDataException>(() =>
            FrmSpritesheetPngExporter.Write(document, CreatePalette(), output, CreateLimits()));

        Assert.Empty(output.ToArray());
    }

    [Fact]
    public void Write_RejectsDuplicateDirectionIndicesBeforeWritingPngBytes()
    {
        var document = CreateDocument(
            new FrmDirection(0, "NE", 0, 0, [new FrmFrame(1, 1, 0, 0, [1])]),
            new FrmDirection(0, "E", 0, 0, [new FrmFrame(1, 1, 0, 0, [2])]));
        using var output = new MemoryStream();

        Assert.Throws<InvalidDataException>(() =>
            FrmSpritesheetPngExporter.Write(document, CreatePalette(), output, CreateLimits()));

        Assert.Empty(output.ToArray());
    }

    [Fact]
    public void Write_PropagatesDestinationWriteFailures()
    {
        var document = CreateDocument(
            new FrmDirection(0, "NE", 0, 0, [new FrmFrame(1, 1, 0, 0, [1])]));

        Assert.Throws<IOException>(() =>
            FrmSpritesheetPngExporter.Write(
                document,
                CreatePalette(),
                new ThrowingWriteStream(),
                CreateLimits()));
    }

    [Fact]
    public void Write_LeavesDestinationStreamOpenForTheCaller()
    {
        var document = CreateDocument(
            new FrmDirection(0, "NE", 0, 0, [new FrmFrame(1, 1, 0, 0, [1])]));
        using var output = new MemoryStream();

        FrmSpritesheetPngExporter.Write(document, CreatePalette(), output, CreateLimits());

        Assert.True(output.CanWrite);
        output.WriteByte(0x42);
    }

    private static FrmDocument CreateDocument(params FrmDirection[] directions) =>
        new(4, 10, 0, directions.Length == 0 ? 0 : directions.Max(direction => direction.Frames.Count), directions);

    private static FrmSpritesheetPackingLimits CreateLimits(
        int maximumWidth = 64,
        int maximumHeight = 64,
        long maximumPixelCount = 4096) =>
        new(maximumWidth, maximumHeight, maximumPixelCount);

    private static FalloutPalette CreatePalette()
    {
        var bytes = new byte[FalloutPalette.MinimalFileSize];
        bytes[3] = 10;
        bytes[4] = 20;
        bytes[5] = 30;
        bytes[6] = 63;
        bytes[7] = 1;
        bytes[8] = 0;
        bytes[9] = 0;
        bytes[10] = 0;
        bytes[11] = 63;
        return FalloutPalette.Parse(bytes);
    }

    private static FalloutPalette CreateHighEntropyPalette()
    {
        var bytes = new byte[FalloutPalette.MinimalFileSize];
        for (var index = 0; index < 256; index++)
        {
            var offset = index * 3;
            bytes[offset] = (byte)(index % 64);
            bytes[offset + 1] = (byte)((index * 17) % 64);
            bytes[offset + 2] = (byte)((index * 37) % 64);
        }

        return FalloutPalette.Parse(bytes);
    }

    private static void AssertValidPngChunkOrderAndMetadata(DecodedPng png)
    {
        Assert.Equal("IHDR", png.ChunkTypes[0]);
        var textIndex = png.ChunkTypes.IndexOf("iTXt");
        var idatIndex = png.ChunkTypes.IndexOf("IDAT");
        Assert.True(textIndex > 0);
        Assert.True(textIndex < idatIndex);
        Assert.Equal("IEND", png.ChunkTypes[^1]);
    }

    private static void AssertRgba(DecodedPng png, int x, int y, byte r, byte g, byte b, byte a)
    {
        var offset = checked((y * png.Width + x) * 4);
        Assert.Equal(new byte[] { r, g, b, a }, png.Rgba.AsSpan(offset, 4).ToArray());
    }

    private static DecodedPng DecodePng(byte[] bytes)
    {
        Assert.Equal([137, 80, 78, 71, 13, 10, 26, 10], bytes.AsSpan(0, 8).ToArray());
        var offset = 8;
        var width = 0;
        var height = 0;
        var metadataKeyword = string.Empty;
        var metadataJson = string.Empty;
        var compressionFlag = -1;
        var compressionMethod = -1;
        var languageTag = string.Empty;
        var translatedKeyword = string.Empty;
        using var compressedData = new MemoryStream();
        var chunkTypes = new List<string>();

        while (offset < bytes.Length)
        {
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, sizeof(uint))));
            offset += sizeof(uint);
            var chunkStart = offset;
            var type = bytes.AsSpan(offset, 4);
            var typeName = Encoding.ASCII.GetString(type);
            chunkTypes.Add(typeName);
            offset += 4;
            var data = bytes.AsSpan(offset, length);
            offset += length;
            var expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, sizeof(uint)));
            Assert.Equal(expectedCrc, CalculateCrc(bytes.AsSpan(chunkStart, sizeof(uint) + length)));
            offset += sizeof(uint);

            if (type.SequenceEqual("IHDR"u8))
            {
                Assert.Equal(13, length);
                width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data));
                height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data[4..]));
                Assert.Equal(8, data[8]);
                Assert.Equal(6, data[9]);
            }
            else if (type.SequenceEqual("iTXt"u8))
            {
                var keywordEnd = data.IndexOf((byte)0);
                Assert.True(keywordEnd > 0);
                metadataKeyword = Encoding.ASCII.GetString(data[..keywordEnd]);
                var textOffset = keywordEnd + 1;
                compressionFlag = data[textOffset++];
                compressionMethod = data[textOffset++];
                var languageEnd = data[textOffset..].IndexOf((byte)0);
                Assert.True(languageEnd >= 0);
                languageTag = Encoding.UTF8.GetString(data.Slice(textOffset, languageEnd));
                textOffset += languageEnd + 1;
                var translatedEnd = data[textOffset..].IndexOf((byte)0);
                Assert.True(translatedEnd >= 0);
                translatedKeyword = Encoding.UTF8.GetString(data.Slice(textOffset, translatedEnd));
                textOffset += translatedEnd + 1;
                metadataJson = new UTF8Encoding(false, true).GetString(data[textOffset..]);
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                compressedData.Write(data);
            }
        }

        Assert.Equal(bytes.Length, offset);
        Assert.True(width > 0);
        Assert.True(height > 0);
        compressedData.Position = 0;
        using var decompressor = new ZLibStream(compressedData, CompressionMode.Decompress);
        using var scanlines = new MemoryStream();
        decompressor.CopyTo(scanlines);
        var rowLength = checked(width * 4 + 1);
        Assert.Equal(checked(rowLength * height), checked((int)scanlines.Length));
        var raw = scanlines.ToArray();
        var rgba = new byte[checked(width * height * 4)];
        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * rowLength;
            Assert.Equal(0, raw[rowOffset]);
            raw.AsSpan(rowOffset + 1, width * 4).CopyTo(rgba.AsSpan(y * width * 4));
        }

        return new DecodedPng(
            width,
            height,
            rgba,
            chunkTypes,
            metadataKeyword,
            metadataJson,
            compressionFlag,
            compressionMethod,
            languageTag,
            translatedKeyword);
    }

    private static uint CalculateCrc(ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
            }
        }

        return ~crc;
    }

    private sealed record DecodedPng(
        int Width,
        int Height,
        byte[] Rgba,
        List<string> ChunkTypes,
        string MetadataKeyword,
        string MetadataJson,
        int CompressionFlag,
        int CompressionMethod,
        string LanguageTag,
        string TranslatedKeyword);

    private sealed class ThrowingWriteStream : MemoryStream
    {
        public override void Write(ReadOnlySpan<byte> buffer) =>
            throw new IOException("Simulated destination write failure.");

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new IOException("Simulated destination write failure.");
    }

    private sealed class CancelOnIdatWriteStream(CancellationTokenSource cancellation) : MemoryStream
    {
        public bool SawIdat { get; private set; }

        public bool SawIend { get; private set; }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            base.Write(buffer);
            ObserveChunkType(buffer);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            base.Write(buffer, offset, count);
            ObserveChunkType(buffer.AsSpan(offset, count));
        }

        private void ObserveChunkType(ReadOnlySpan<byte> bytes)
        {
            if (bytes.SequenceEqual("IDAT"u8))
            {
                SawIdat = true;
                cancellation.Cancel();
            }
            else if (bytes.SequenceEqual("IEND"u8))
            {
                SawIend = true;
            }
        }
    }
}
