using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FalloutAssetExplorer.Core;

public static class FrmSpritesheetPngExporter
{
    private const string MetadataKeyword = "FrmSpritesheet";
    private const int RowBufferSize = 64 * 1024;
    private static readonly byte[] MetadataKeywordBytes = Encoding.ASCII.GetBytes(MetadataKeyword);
    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void Write(
        FrmDocument document,
        FalloutPalette? palette,
        Stream destination,
        FrmSpritesheetPackingLimits limits,
        int spacing = 0,
        string? assetName = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(limits);
        if (palette is null)
        {
            throw new ArgumentNullException(
                nameof(palette),
                "A resolved Fallout palette is required for spritesheet export.");
        }

        if (!destination.CanWrite)
        {
            throw new ArgumentException("The PNG destination stream must be writable.", nameof(destination));
        }

        if (document.FramesPerSecond <= 0 || document.FramesPerSecond > 120)
        {
            throw new InvalidDataException("The FRM document has an invalid effective frame rate.");
        }

        if (document.Directions is null)
        {
            throw new InvalidDataException("The FRM document has no direction list.");
        }

        var directions = GetOrderedDirections(document.Directions);
        var layoutFrames = new List<FrmSpritesheetFrame>();
        var sourceFrames = new Dictionary<FrmSpritesheetFrameKey, FrmFrame>();
        foreach (var direction in directions)
        {
            if (direction.Frames is null)
            {
                throw new InvalidDataException($"FRM direction {direction.Index} has no frame list.");
            }

            for (var frameIndex = 0; frameIndex < direction.Frames.Count; frameIndex++)
            {
                var frame = direction.Frames[frameIndex];
                if (frame is null || frame.Pixels is null)
                {
                    throw new InvalidDataException(
                        $"FRM direction {direction.Index} frame {frameIndex} has no pixel data.");
                }

                if (frame.Width <= 0 || frame.Height <= 0)
                {
                    throw new InvalidDataException(
                        $"FRM direction {direction.Index} frame {frameIndex} has invalid dimensions.");
                }

                var expectedPixelCount = checked((long)frame.Width * frame.Height);
                if (frame.Pixels.LongLength != expectedPixelCount)
                {
                    throw new InvalidDataException(
                        $"FRM direction {direction.Index} frame {frameIndex} pixel count does not match its dimensions.");
                }

                var layoutFrame = FrmSpritesheetFrame.From(direction, frameIndex);
                layoutFrames.Add(layoutFrame);
                sourceFrames.Add(layoutFrame.Key, frame);
            }
        }

        if (layoutFrames.Count == 0)
        {
            throw new InvalidDataException("The FRM document contains no animation frames to export.");
        }

        var layout = FrmSpritesheetPacker.Pack(layoutFrames, spacing, limits);
        var metadata = CreateMetadata(document, assetName, directions, layout);
        var metadataJson = JsonSerializer.SerializeToUtf8Bytes(metadata, MetadataJsonOptions);
        var internationalText = CreateInternationalTextChunk(metadataJson);
        var rowBuffer = new byte[RowBufferSize];
        var placements = layout.Placements.Values
            .OrderBy(placement => placement.Rectangle.Y)
            .ThenBy(placement => placement.Rectangle.X)
            .ToArray();

        WritePngHeader(destination, layout.Width, layout.Height);
        FrmPngExporter.WriteChunk(destination, "iTXt"u8, internationalText);

        using (var idatChunks = new FrmPngExporter.IdatChunkStream(destination))
        {
            using (var compressor = new ZLibStream(idatChunks, CompressionLevel.Optimal, leaveOpen: true))
            {
                WriteImageData(compressor, layout, placements, sourceFrames, palette, rowBuffer);
            }
        }

        FrmPngExporter.WriteChunk(destination, "IEND"u8, ReadOnlySpan<byte>.Empty);
    }

    private static FrmDirection[] GetOrderedDirections(IReadOnlyList<FrmDirection> sourceDirections)
    {
        var directions = new FrmDirection[sourceDirections.Count];
        var indices = new HashSet<int>();
        for (var index = 0; index < sourceDirections.Count; index++)
        {
            var direction = sourceDirections[index];
            if (direction is null)
            {
                throw new InvalidDataException($"FRM direction at list index {index} is null.");
            }

            if (direction.Index < 0 || !indices.Add(direction.Index))
            {
                throw new InvalidDataException($"FRM direction index {direction.Index} is invalid or duplicated.");
            }

            if (string.IsNullOrWhiteSpace(direction.Name))
            {
                throw new InvalidDataException($"FRM direction {direction.Index} has no name.");
            }

            directions[index] = direction;
        }

        Array.Sort(directions, static (left, right) => left.Index.CompareTo(right.Index));
        return directions;
    }

    private static SpritesheetMetadata CreateMetadata(
        FrmDocument document,
        string? assetName,
        IReadOnlyList<FrmDirection> directions,
        FrmSpritesheetLayout layout)
    {
        var metadataDirections = new DirectionMetadata[directions.Count];
        for (var directionIndex = 0; directionIndex < directions.Count; directionIndex++)
        {
            var direction = directions[directionIndex];
            var frameMetadata = new FrameMetadata[direction.Frames.Count];
            for (var frameIndex = 0; frameIndex < direction.Frames.Count; frameIndex++)
            {
                var key = new FrmSpritesheetFrameKey(direction.Index, frameIndex);
                var frame = layout.Placements[key].Frame;
                frameMetadata[frameIndex] = new FrameMetadata(
                    key.DirectionIndex,
                    key.FrameIndex,
                    layout.Placements[key].Rectangle,
                    frame.Width,
                    frame.Height,
                    frame.OffsetX,
                    frame.OffsetY,
                    frame.ShiftX,
                    frame.ShiftY);
            }

            metadataDirections[directionIndex] = new DirectionMetadata(
                direction.Index,
                direction.Name,
                frameMetadata);
        }

        return new SpritesheetMetadata(
            1,
            assetName,
            layout.Width,
            layout.Height,
            document.FramesPerSecond,
            document.ActionFrame,
            metadataDirections);
    }

    private static byte[] CreateInternationalTextChunk(ReadOnlySpan<byte> json)
    {
        var data = new byte[checked(MetadataKeywordBytes.Length + 5 + json.Length)];
        var offset = 0;
        MetadataKeywordBytes.CopyTo(data, offset);
        offset += MetadataKeywordBytes.Length;
        data[offset++] = 0;
        data[offset++] = 0;
        data[offset++] = 0;
        data[offset++] = 0;
        data[offset++] = 0;
        json.CopyTo(data.AsSpan(offset));
        return data;
    }

    private static void WritePngHeader(Stream destination, int width, int height)
    {
        destination.Write(FrmPngExporter.Signature);

        Span<byte> header = stackalloc byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)width));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header[4..], checked((uint)height));
        header[8] = 8;
        header[9] = 6;
        header[10] = 0;
        header[11] = 0;
        header[12] = 0;
        FrmPngExporter.WriteChunk(destination, "IHDR"u8, header);
    }

    private static void WriteImageData(
        Stream compressor,
        FrmSpritesheetLayout layout,
        IReadOnlyList<FrmSpritesheetPlacement> placements,
        IReadOnlyDictionary<FrmSpritesheetFrameKey, FrmFrame> sourceFrames,
        FalloutPalette palette,
        byte[] rowBuffer)
    {
        var activePlacements = new List<FrmSpritesheetPlacement>();
        var nextPlacementIndex = 0;

        for (var y = 0; y < layout.Height; y++)
        {
            while (nextPlacementIndex < placements.Count
                && placements[nextPlacementIndex].Rectangle.Y <= y)
            {
                activePlacements.Add(placements[nextPlacementIndex]);
                nextPlacementIndex++;
            }

            activePlacements.RemoveAll(placement =>
                (long)placement.Rectangle.Y + placement.Rectangle.Height <= y);

            compressor.WriteByte(0);
            var rowPixelOffset = 0;
            while (rowPixelOffset < layout.Width)
            {
                var pixelCount = Math.Min(rowBuffer.Length / 4, layout.Width - rowPixelOffset);
                var pixelData = rowBuffer.AsSpan(0, checked(pixelCount * 4));
                pixelData.Clear();
                DrawRowSegment(
                    pixelData,
                    y,
                    rowPixelOffset,
                    pixelCount,
                    activePlacements,
                    sourceFrames,
                    palette);
                compressor.Write(pixelData);
                rowPixelOffset += pixelCount;
            }
        }
    }

    private static void DrawRowSegment(
        Span<byte> pixelData,
        int y,
        int rowPixelOffset,
        int pixelCount,
        IReadOnlyList<FrmSpritesheetPlacement> placements,
        IReadOnlyDictionary<FrmSpritesheetFrameKey, FrmFrame> sourceFrames,
        FalloutPalette palette)
    {
        var segmentRight = checked(rowPixelOffset + pixelCount);
        foreach (var placement in placements)
        {
            var rectangle = placement.Rectangle;
            var rectangleRight = checked(rectangle.X + rectangle.Width);
            var startX = Math.Max(rowPixelOffset, rectangle.X);
            var endX = Math.Min(segmentRight, rectangleRight);
            if (startX >= endX)
            {
                continue;
            }

            var frame = sourceFrames[placement.Frame.Key];
            var sourceY = y - rectangle.Y;
            for (var x = startX; x < endX; x++)
            {
                var sourceOffset = checked(sourceY * frame.Width + x - rectangle.X);
                var color = palette.GetColor(frame.Pixels[sourceOffset]);
                var destinationOffset = checked((x - rowPixelOffset) * 4);
                pixelData[destinationOffset] = color.R;
                pixelData[destinationOffset + 1] = color.G;
                pixelData[destinationOffset + 2] = color.B;
                pixelData[destinationOffset + 3] = color.A;
            }
        }
    }

    private sealed record SpritesheetMetadata(
        int SchemaVersion,
        string? AssetName,
        int SheetWidth,
        int SheetHeight,
        int FramesPerSecond,
        short ActionFrame,
        IReadOnlyList<DirectionMetadata> Directions);

    private sealed record DirectionMetadata(
        int Index,
        string Name,
        IReadOnlyList<FrameMetadata> Frames);

    private sealed record FrameMetadata(
        int DirectionIndex,
        int FrameIndex,
        FrmAtlasRectangle Atlas,
        int OriginalWidth,
        int OriginalHeight,
        short OffsetX,
        short OffsetY,
        short ShiftX,
        short ShiftY);
}
