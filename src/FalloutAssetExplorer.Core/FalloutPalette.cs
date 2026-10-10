namespace FalloutAssetExplorer.Core;

public sealed class FalloutPalette
{
    public const int EntryCount = 256;
    public const int BytesPerEntry = 3;
    public const int RgbDataSize = EntryCount * BytesPerEntry;
    public const int Rgb555LookupSize = 32 * 32 * 32;
    public const int MinimalFileSize = RgbDataSize + Rgb555LookupSize;
    public const int ExtendedFileSize = MinimalFileSize + sizeof(uint) + 3 * 256 * 256;

    private const byte MaximumChannelValue = 63;
    private readonly byte[] _rgbComponents;

    private FalloutPalette(byte[] rgbComponents)
    {
        _rgbComponents = rgbComponents;
    }

    public static FalloutPalette Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length != MinimalFileSize && data.Length != ExtendedFileSize)
        {
            throw new InvalidDataException(
                $"A Fallout PAL must be {MinimalFileSize} bytes or a supported {ExtendedFileSize}-byte extended file; got {data.Length} bytes.");
        }

        if (data.Length == ExtendedFileSize
            && !data.Slice(MinimalFileSize, sizeof(uint)).SequenceEqual("NEWC"u8))
        {
            throw new InvalidDataException("The extended Fallout PAL is missing its NEWC color-table tag.");
        }

        var components = data[..RgbDataSize].ToArray();
        for (var index = 0; index < EntryCount; index++)
        {
            var componentOffset = index * BytesPerEntry;
            if (components[componentOffset] > MaximumChannelValue
                || components[componentOffset + 1] > MaximumChannelValue
                || components[componentOffset + 2] > MaximumChannelValue)
            {
                components[componentOffset] = 0;
                components[componentOffset + 1] = 0;
                components[componentOffset + 2] = 0;
            }
        }

        return new FalloutPalette(components);
    }

    public RgbaColor GetColor(byte paletteIndex)
    {
        if (paletteIndex == 0)
        {
            return new RgbaColor(0, 0, 0, 0);
        }

        var offset = paletteIndex * BytesPerEntry;
        return new RgbaColor(
            (byte)(_rgbComponents[offset] << 2),
            (byte)(_rgbComponents[offset + 1] << 2),
            (byte)(_rgbComponents[offset + 2] << 2),
            byte.MaxValue);
    }

    public void CopyRgba(FrmFrame frame, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(frame.Pixels);

        if (frame.Width <= 0 || frame.Height <= 0
            || frame.Width > FrmDecoder.MaximumDimension
            || frame.Height > FrmDecoder.MaximumDimension)
        {
            throw new ArgumentException("The frame has invalid dimensions.", nameof(frame));
        }

        var pixelCount = checked(frame.Width * frame.Height);
        if (pixelCount > FrmDecoder.MaximumTotalPixels || frame.Pixels.Length != pixelCount)
        {
            throw new ArgumentException("The frame pixel count does not match its dimensions.", nameof(frame));
        }

        var requiredLength = checked(pixelCount * 4);
        if (destination.Length < requiredLength)
        {
            throw new ArgumentException("The destination is too small for the rendered RGBA pixels.", nameof(destination));
        }

        for (var index = 0; index < pixelCount; index++)
        {
            var color = GetColor(frame.Pixels[index]);
            var outputOffset = index * 4;
            destination[outputOffset] = color.R;
            destination[outputOffset + 1] = color.G;
            destination[outputOffset + 2] = color.B;
            destination[outputOffset + 3] = color.A;
        }
    }
}

public readonly record struct RgbaColor(byte R, byte G, byte B, byte A);
