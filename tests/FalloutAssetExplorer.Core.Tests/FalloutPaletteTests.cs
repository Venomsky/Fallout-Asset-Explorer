using System.Buffers.Binary;
using System.Text;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class FalloutPaletteTests
{
    [Fact]
    public void Parse_ReadsSixBitRgbTriplesAndConvertsToEightBitChannels()
    {
        var bytes = PaletteFixture.Create();
        bytes[3] = 1;
        bytes[4] = 31;
        bytes[5] = 63;

        var palette = FalloutPalette.Parse(bytes);

        Assert.Equal(new RgbaColor(4, 124, 252, 255), palette.GetColor(1));
    }

    [Fact]
    public void GetColor_MakesPaletteIndexZeroTransparent()
    {
        var bytes = PaletteFixture.Create();
        bytes[0] = 63;
        bytes[1] = 42;
        bytes[2] = 1;

        var color = FalloutPalette.Parse(bytes).GetColor(0);

        Assert.Equal(new RgbaColor(0, 0, 0, 0), color);
    }

    [Fact]
    public void CopyRgba_RendersFrmPixelsThroughPaletteAndKeepsZeroTransparent()
    {
        var bytes = PaletteFixture.Create();
        bytes[3] = 10;
        bytes[4] = 20;
        bytes[5] = 30;
        bytes[6] = 63;
        bytes[7] = 1;
        bytes[8] = 0;
        var palette = FalloutPalette.Parse(bytes);
        var frame = new FrmFrame(3, 1, 0, 0, [0, 1, 2]);
        var rendered = new byte[12];

        palette.CopyRgba(frame, rendered);

        Assert.Equal(
            new byte[] { 0, 0, 0, 0, 40, 80, 120, 255, 252, 4, 0, 255 },
            rendered);
    }

    [Fact]
    public void Parse_NormalizesInvalidSixBitEntryLikeFalloutOneEngine()
    {
        var bytes = PaletteFixture.Create();
        bytes[3] = 10;
        bytes[4] = 64;
        bytes[5] = 20;

        var palette = FalloutPalette.Parse(bytes);

        Assert.Equal(new RgbaColor(0, 0, 0, 255), palette.GetColor(1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(767)]
    [InlineData(FalloutPalette.MinimalFileSize - 1)]
    [InlineData(FalloutPalette.MinimalFileSize + 1)]
    public void Parse_RejectsUnsupportedOrTruncatedLengths(int length)
    {
        Assert.Throws<InvalidDataException>(() => FalloutPalette.Parse(new byte[length]));
    }

    [Fact]
    public void Parse_RejectsExtendedPaletteWithoutNewcTag()
    {
        var bytes = PaletteFixture.Create(extended: true);

        Assert.Throws<InvalidDataException>(() => FalloutPalette.Parse(bytes));
    }

    [Fact]
    public void Parse_AcceptsExtendedPaletteWithNewcTag()
    {
        var bytes = PaletteFixture.Create(extended: true);
        "NEWC"u8.CopyTo(bytes.AsSpan(FalloutPalette.MinimalFileSize, 4));
        bytes[3] = 63;

        var palette = FalloutPalette.Parse(bytes);

        Assert.Equal(new RgbaColor(252, 0, 0, 255), palette.GetColor(1));
    }

    [Fact]
    public async Task ResolveAsync_FindsRootPaletteCaseInsensitivelyInOpenArchive()
    {
        var paletteBytes = PaletteFixture.Create();
        paletteBytes[3] = 20;
        using var archiveFile = new TemporaryArchive(
            DatArchiveFixture.Create("color.PaL", paletteBytes));

        var result = await new FalloutPaletteResolver().ResolveAsync(Dat1Archive.Open(archiveFile.Path));

        Assert.NotNull(result.Palette);
        Assert.Equal(new RgbaColor(80, 0, 0, 255), result.Palette.GetColor(1));
        Assert.Contains(FalloutPaletteResolver.DefaultPaletteFileName, result.Source);
    }

    [Fact]
    public async Task ResolveAsync_FindsLoosePaletteCaseInsensitivelyInConfiguredDirectory()
    {
        using var gameDirectory = new TemporaryDirectory();
        var paletteBytes = PaletteFixture.Create();
        paletteBytes[3] = 5;
        File.WriteAllBytes(Path.Combine(gameDirectory.Path, "COLOR.PAL"), paletteBytes);

        var result = await new FalloutPaletteResolver().ResolveAsync(null, gameDirectory.Path);

        Assert.NotNull(result.Palette);
        Assert.Equal(new RgbaColor(20, 0, 0, 255), result.Palette.GetColor(1));
    }

    [Fact]
    public async Task ResolveAsync_FindsRootPaletteInConfiguredMasterArchive()
    {
        using var gameDirectory = new TemporaryDirectory();
        var paletteBytes = PaletteFixture.Create();
        paletteBytes[3] = 15;
        File.WriteAllBytes(
            Path.Combine(gameDirectory.Path, "master.dat"),
            DatArchiveFixture.Create("COLOR.PAL", paletteBytes));

        var result = await new FalloutPaletteResolver().ResolveAsync(null, gameDirectory.Path);

        Assert.NotNull(result.Palette);
        Assert.Equal(new RgbaColor(60, 0, 0, 255), result.Palette.GetColor(1));
        Assert.Contains("master.dat", result.Source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveAsync_FindsAdjacentMasterPaletteWhenCritterArchiveIsOpenedDirectly()
    {
        using var gameDirectory = new TemporaryDirectory();
        var paletteBytes = PaletteFixture.Create();
        paletteBytes[3] = 18;
        WriteArchive(Path.Combine(gameDirectory.Path, "MASTER.DAT"), "COLOR.PAL", paletteBytes);
        WriteArchive(Path.Combine(gameDirectory.Path, "CRITTER.DAT"), "ART/CRITTERS/HERO.FRM", [1, 2, 3]);

        var critter = Dat1Archive.Open(Path.Combine(gameDirectory.Path, "CRITTER.DAT"));
        var result = await new FalloutPaletteResolver().ResolveAsync(critter);

        Assert.NotNull(result.Palette);
        Assert.Equal(new RgbaColor(72, 0, 0, 255), result.Palette.GetColor(1));
        Assert.Equal("COLOR.PAL in MASTER.DAT", result.Source);
    }

    [Fact]
    public async Task ResolveAsync_ReusesOpenedMasterPaletteWhenSwitchingToCritterArchive()
    {
        using var gameDirectory = new TemporaryDirectory();
        var paletteBytes = PaletteFixture.Create();
        paletteBytes[3] = 25;
        WriteArchive(Path.Combine(gameDirectory.Path, "MASTER.DAT"), "COLOR.PAL", paletteBytes);
        WriteArchive(Path.Combine(gameDirectory.Path, "CRITTER.DAT"), "ART/CRITTERS/HERO.FRM", [1, 2, 3]);
        var master = Dat1Archive.Open(Path.Combine(gameDirectory.Path, "MASTER.DAT"));
        var critter = Dat1Archive.Open(Path.Combine(gameDirectory.Path, "CRITTER.DAT"));
        var resolver = new FalloutPaletteResolver();

        var fromMaster = await resolver.ResolveAsync(master);
        var fromCritter = await resolver.ResolveAsync(critter);

        Assert.NotNull(fromMaster.Palette);
        Assert.Same(fromMaster.Palette, fromCritter.Palette);
        Assert.Equal("COLOR.PAL in MASTER.DAT", fromCritter.Source);
    }

    [Fact]
    public async Task ResolveAsync_ConfiguredDirectoryTakesPrecedenceOverAdjacentMaster()
    {
        using var critterDirectory = new TemporaryDirectory();
        using var configuredDirectory = new TemporaryDirectory();
        var adjacentBytes = PaletteFixture.Create();
        adjacentBytes[3] = 10;
        var configuredBytes = PaletteFixture.Create();
        configuredBytes[3] = 30;
        WriteArchive(Path.Combine(critterDirectory.Path, "MASTER.DAT"), "COLOR.PAL", adjacentBytes);
        WriteArchive(Path.Combine(critterDirectory.Path, "CRITTER.DAT"), "ART/CRITTERS/HERO.FRM", [1]);
        WriteArchive(Path.Combine(configuredDirectory.Path, "MASTER.DAT"), "COLOR.PAL", configuredBytes);
        var critter = Dat1Archive.Open(Path.Combine(critterDirectory.Path, "CRITTER.DAT"));
        var resolver = new FalloutPaletteResolver();

        var result = await resolver.ResolveAsync(critter, configuredDirectory.Path);

        Assert.Equal(new RgbaColor(120, 0, 0, 255), result.Palette!.GetColor(1));
        Assert.Equal("COLOR.PAL in MASTER.DAT", result.Source);
    }

    [Fact]
    public async Task ResolveAsync_ChangingConfiguredDirectoryDoesNotReusePreviousPalette()
    {
        using var firstDirectory = new TemporaryDirectory();
        using var secondDirectory = new TemporaryDirectory();
        var firstBytes = PaletteFixture.Create();
        firstBytes[3] = 11;
        var secondBytes = PaletteFixture.Create();
        secondBytes[3] = 37;
        WriteArchive(Path.Combine(firstDirectory.Path, "MASTER.DAT"), "COLOR.PAL", firstBytes);
        WriteArchive(Path.Combine(secondDirectory.Path, "MASTER.DAT"), "COLOR.PAL", secondBytes);
        var resolver = new FalloutPaletteResolver();

        var first = await resolver.ResolveAsync(null, firstDirectory.Path);
        var second = await resolver.ResolveAsync(null, secondDirectory.Path);

        Assert.Equal(new RgbaColor(44, 0, 0, 255), first.Palette!.GetColor(1));
        Assert.Equal(new RgbaColor(148, 0, 0, 255), second.Palette!.GetColor(1));
        Assert.NotSame(first.Palette, second.Palette);
    }

    [Fact]
    public async Task ResolveAsync_ReportsMissingPaletteInsteadOfSelectingArbitraryPal()
    {
        using var gameDirectory = new TemporaryDirectory();
        File.WriteAllBytes(Path.Combine(gameDirectory.Path, "unrelated.pal"), PaletteFixture.Create());

        var result = await new FalloutPaletteResolver().ResolveAsync(null, gameDirectory.Path);

        Assert.Null(result.Palette);
        Assert.Null(result.Source);
        Assert.Contains("No root COLOR.PAL", result.Diagnostic);
    }

    [Fact]
    public async Task ResolveAsync_CritterWithoutSupportedPaletteSourceUsesDiagnosticFallback()
    {
        using var gameDirectory = new TemporaryDirectory();
        File.WriteAllBytes(
            Path.Combine(gameDirectory.Path, "CRITTER.DAT"),
            DatArchiveFixture.Create("ART/CRITTERS/HERO.FRM", [1, 2, 3]));
        File.WriteAllBytes(Path.Combine(gameDirectory.Path, "unrelated.pal"), PaletteFixture.Create());
        var critter = Dat1Archive.Open(Path.Combine(gameDirectory.Path, "CRITTER.DAT"));

        var result = await new FalloutPaletteResolver().ResolveAsync(critter);

        Assert.Null(result.Palette);
        Assert.Null(result.Source);
        Assert.Contains("No root COLOR.PAL", result.Diagnostic);
        Assert.Contains("diagnostic grayscale", result.Diagnostic);
    }

    [Fact]
    public async Task ResolveAsync_UsesDiagnosticFallbackForInvalidPaletteInConfiguredDirectory()
    {
        using var gameDirectory = new TemporaryDirectory();
        File.WriteAllBytes(Path.Combine(gameDirectory.Path, "color.pal"), new byte[768]);

        var result = await new FalloutPaletteResolver().ResolveAsync(null, gameDirectory.Path);

        Assert.Null(result.Palette);
        Assert.Null(result.Source);
        Assert.Contains("Could not load the Fallout palette", result.Diagnostic);
        Assert.Contains("diagnostic grayscale", result.Diagnostic);
    }

    private sealed class TemporaryArchive : IDisposable
    {
        public TemporaryArchive(byte[] bytes)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"fae-{Guid.NewGuid():N}.dat");
            File.WriteAllBytes(Path, bytes);
        }

        public string Path { get; }

        public void Dispose() => File.Delete(Path);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"fae-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private static class PaletteFixture
    {
        public static byte[] Create(bool extended = false)
        {
            var bytes = new byte[extended ? FalloutPalette.ExtendedFileSize : FalloutPalette.MinimalFileSize];
            if (extended)
            {
                bytes[FalloutPalette.MinimalFileSize] = (byte)'X';
            }

            return bytes;
        }
    }

    private static class DatArchiveFixture
    {
        public static byte[] Create(string entryPath, byte[] payload)
        {
            using var stream = new MemoryStream();
            WriteUInt32(stream, 1);
            WriteUInt32(stream, 0);
            WriteUInt32(stream, 0);
            WriteUInt32(stream, 0);
            WriteName(stream, ".");
            WriteUInt32(stream, 1);
            WriteUInt32(stream, 0);
            WriteUInt32(stream, 0);
            WriteUInt32(stream, 0);
            WriteName(stream, entryPath);
            WriteUInt32(stream, Dat1Archive.RawEntryType);
            var offsetPosition = stream.Position;
            WriteUInt32(stream, 0);
            WriteUInt32(stream, (uint)payload.Length);
            WriteUInt32(stream, (uint)payload.Length);
            var dataOffset = (uint)stream.Position;
            stream.Position = offsetPosition;
            WriteUInt32(stream, dataOffset);
            stream.Position = stream.Length;
            stream.Write(payload);
            return stream.ToArray();
        }

        private static void WriteName(Stream stream, string value)
        {
            var bytes = Encoding.Latin1.GetBytes(value);
            stream.WriteByte((byte)bytes.Length);
            stream.Write(bytes);
        }

        private static void WriteUInt32(Stream stream, uint value)
        {
            Span<byte> bytes = stackalloc byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
            stream.Write(bytes);
        }

    }

    private static void WriteArchive(string path, string entryPath, byte[] payload) =>
        File.WriteAllBytes(path, DatArchiveFixture.Create(entryPath, payload));
}
