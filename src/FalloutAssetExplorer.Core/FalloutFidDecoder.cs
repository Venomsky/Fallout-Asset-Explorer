namespace FalloutAssetExplorer.Core;

public static class FalloutFidDecoder
{
    public static FalloutFid Decode(int rawValue)
    {
        var value = unchecked((uint)rawValue);
        return new FalloutFid(
            rawValue,
            (int)(value & 0x00000FFF),
            (int)((value & 0x0000F000) >> 12),
            (int)((value & 0x00FF0000) >> 16),
            (int)((value & 0x0F000000) >> 24),
            (int)((value & 0x70000000) >> 28));
    }
}

public sealed record FalloutFid(
    int RawValue,
    int ArtListIndex,
    int WeaponCode,
    int AnimationType,
    int ArtType,
    int UpperBits);
