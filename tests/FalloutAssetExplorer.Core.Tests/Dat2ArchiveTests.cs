using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class Dat2ArchiveTests
{
    [Fact]
    public void Open_ParsesEmptyArchive()
    {
        using var archiveFile = new TemporaryArchive(Dat2Fixture.Create());

        var archive = Dat2Archive.Open(archiveFile.Path);

        Assert.Empty(archive.Entries);
        Assert.Equal(FalloutGame.Fallout2, archive.Game);
        Assert.Equal(ArchiveFormat.Dat2, archive.Format);
    }

    [Fact]
    public async Task Open_ParsesAndReadsUncompressedNestedEntry()
    {
        byte[] expected = "Fallout 2 raw payload"u8.ToArray();
        using var archiveFile = new TemporaryArchive(Dat2Fixture.Create(
            new Dat2FixtureEntry(@"art\critters\hero.frm", false, expected)));

        var archive = Dat2Archive.Open(archiveFile.Path);
        var entry = Assert.Single(archive.Entries);
        using var output = new MemoryStream();

        await archive.CopyEntryToAsync(entry, output);

        Assert.Equal(@"art\critters\hero.frm", entry.StoredPath);
        Assert.Equal("art/critters/hero.frm", entry.Path);
        Assert.Equal(expected.Length, entry.UncompressedSize);
        Assert.Null(entry.CompressedSize);
        Assert.Equal(expected, output.ToArray());
    }

    [Fact]
    public async Task Open_ParsesAndReadsZlibCompressedEntry()
    {
        byte[] expected = "Fallout 2 zlib payload"u8.ToArray();
        var compressed = Compress(expected);
        using var archiveFile = new TemporaryArchive(Dat2Fixture.Create(
            new Dat2FixtureEntry("art/hero.frm", true, compressed, expected.Length)));

        var archive = Dat2Archive.Open(archiveFile.Path);
        var entry = Assert.Single(archive.Entries);
        using var output = new MemoryStream();

        await archive.CopyEntryToAsync(entry, output);

        Assert.Equal(compressed.Length, entry.CompressedSize);
        Assert.Equal(compressed.Length, entry.StoredSize);
        Assert.Equal(expected, output.ToArray());
    }

    [Fact]
    public async Task Open_ParsesValidEmptyUncompressedEntry()
    {
        using var archiveFile = new TemporaryArchive(Dat2Fixture.Create(
            new Dat2FixtureEntry("empty.dat", false, [])));
        var archive = Dat2Archive.Open(archiveFile.Path);
        using var output = new MemoryStream();

        await archive.CopyEntryToAsync(Assert.Single(archive.Entries), output);

        Assert.Empty(output.ToArray());
    }

    [Fact]
    public async Task Open_ParsesValidEmptyCompressedEntry()
    {
        using var archiveFile = new TemporaryArchive(Dat2Fixture.Create(
            new Dat2FixtureEntry("empty.bin", true, Compress([]), 0)));
        var archive = Dat2Archive.Open(archiveFile.Path);
        using var output = new MemoryStream();

        await archive.CopyEntryToAsync(Assert.Single(archive.Entries), output);

        Assert.Empty(output.ToArray());
    }

    [Fact]
    public async Task Open_ParsesMultipleEntriesAndReadsThroughSharedArchiveContract()
    {
        byte[] first = [0x01, 0x02];
        byte[] second = "second"u8.ToArray();
        using var archiveFile = new TemporaryArchive(
            Dat2Fixture.Create(
                new Dat2FixtureEntry("art/first.frm", false, first),
                new Dat2FixtureEntry("text/second.msg", false, second)),
            ".bin");

        IGameArchive archive = FalloutArchive.Open(archiveFile.Path);
        var entries = archive.BrowserEntries;
        using var output = new MemoryStream();

        await archive.CopyEntryToAsync(entries[1], output);

        Assert.Equal(FalloutGame.Fallout2, archive.Game);
        Assert.Equal(ArchiveFormat.Dat2, archive.Format);
        Assert.Equal(2, entries.Count);
        Assert.Equal(
            "art/first.frm",
            Assert.Single(ArchiveEntryFilter.Apply(entries, "FIRST", ".FRM")).Path);
        Assert.Equal(second, output.ToArray());
    }

    [Fact]
    public void Open_PreservesPermittedSpecialAsciiPathCharacters()
    {
        using var archiveFile = new TemporaryArchive(Dat2Fixture.Create(
            new Dat2FixtureEntry("text/dialogue test+1.msg", false, [])));

        var entry = Assert.Single(Dat2Archive.Open(archiveFile.Path).Entries);

        Assert.Equal("text/dialogue test+1.msg", entry.Path);
    }

    [Fact]
    public async Task Open_UsesOffsetsRelativeToDataBase()
    {
        byte[] prefix = [0xaa, 0xbb, 0xcc];
        byte[] expected = "prefixed data"u8.ToArray();
        using var archiveFile = new TemporaryArchive(Dat2Fixture.Create(
            [new Dat2FixtureEntry("relative.bin", false, expected)],
            prefix));
        var archive = Dat2Archive.Open(archiveFile.Path);
        using var output = new MemoryStream();

        await archive.CopyEntryToAsync(Assert.Single(archive.Entries), output);

        Assert.Equal(expected, output.ToArray());
    }

    [Fact]
    public void Open_RejectsTruncatedFooter()
    {
        using var archiveFile = new TemporaryArchive(new byte[7]);

        Assert.Throws<InvalidDataException>(() => Dat2Archive.Open(archiveFile.Path));
    }

    [Fact]
    public void Open_RejectsInvalidFooterSizes()
    {
        byte[] fixture = Dat2Fixture.Create();
        BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(fixture.Length - 8), uint.MaxValue);
        using var archiveFile = new TemporaryArchive(fixture);

        Assert.Throws<InvalidDataException>(() => Dat2Archive.Open(archiveFile.Path));
    }

    [Fact]
    public void Open_RejectsDataSizeBeyondPhysicalFileLength()
    {
        byte[] fixture = Dat2Fixture.Create();
        BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(fixture.Length - 4), uint.MaxValue);
        using var archiveFile = new TemporaryArchive(fixture);

        Assert.Throws<InvalidDataException>(() => Dat2Archive.Open(archiveFile.Path));
    }

    [Fact]
    public void Open_RejectsTreeOutsideDeclaredDataRegion()
    {
        byte[] fixture = Dat2Fixture.Create();
        BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(fixture.Length - 4), 8);
        using var archiveFile = new TemporaryArchive(fixture);

        Assert.Throws<InvalidDataException>(() => Dat2Archive.Open(archiveFile.Path));
    }

    [Fact]
    public void Open_RejectsFileCountThatCannotFitTheDirectory()
    {
        byte[] fixture = Dat2Fixture.Create();
        var directoryOffset = fixture.Length - 12;
        BinaryPrimitives.WriteUInt32LittleEndian(fixture.AsSpan(directoryOffset), uint.MaxValue);
        using var archiveFile = new TemporaryArchive(fixture);

        Assert.Throws<InvalidDataException>(() => Dat2Archive.Open(archiveFile.Path));
    }

    [Fact]
    public void Open_RejectsPathLengthBeyondDirectoryBounds()
    {
        byte[] fixture = Dat2Fixture.Create(
            new Dat2FixtureEntry("raw.bin", false, [0x41]));
        BinaryPrimitives.WriteUInt32LittleEndian(
            fixture.AsSpan(FindDirectoryEntry(fixture)),
            uint.MaxValue);
        using var archiveFile = new TemporaryArchive(fixture);

        Assert.Throws<InvalidDataException>(() => Dat2Archive.Open(archiveFile.Path));
    }

    [Fact]
    public void Open_RejectsUnsupportedCompressionFlag()
    {
        byte[] fixture = Dat2Fixture.Create(
            new Dat2FixtureEntry("unknown.bin", false, [0x41]));
        var entryStart = FindDirectoryEntry(fixture);
        fixture[entryStart + 4 + Encoding.ASCII.GetByteCount("unknown.bin")] = 2;
        using var archiveFile = new TemporaryArchive(fixture);

        Assert.Throws<InvalidDataException>(() => Dat2Archive.Open(archiveFile.Path));
    }

    [Fact]
    public void Open_RejectsUncompressedSizeMismatch()
    {
        byte[] fixture = Dat2Fixture.Create(
            new Dat2FixtureEntry("raw.bin", false, [0x41]));
        var entryStart = FindDirectoryEntry(fixture);
        BinaryPrimitives.WriteUInt32LittleEndian(
            fixture.AsSpan(entryStart + 4 + Encoding.ASCII.GetByteCount("raw.bin") + 1),
            2);
        using var archiveFile = new TemporaryArchive(fixture);

        Assert.Throws<InvalidDataException>(() => Dat2Archive.Open(archiveFile.Path));
    }

    [Fact]
    public void Open_RejectsEntryWhoseDataOverlapsDirectory()
    {
        byte[] fixture = Dat2Fixture.Create(
            new Dat2FixtureEntry("raw.bin", false, [0x41], Offset: uint.MaxValue));
        using var archiveFile = new TemporaryArchive(fixture);

        Assert.Throws<InvalidDataException>(() => Dat2Archive.Open(archiveFile.Path));
    }

    [Fact]
    public void Open_RejectsEntryAboveConfiguredSizeLimit()
    {
        byte[] fixture = Dat2Fixture.Create(
            new Dat2FixtureEntry("large.bin", true, [0x78], 512L * 1024 * 1024 + 1));
        using var archiveFile = new TemporaryArchive(fixture);

        Assert.Throws<InvalidDataException>(() => Dat2Archive.Open(archiveFile.Path));
    }

    [Theory]
    [InlineData("../outside.bin")]
    [InlineData(@"..\outside.bin")]
    [InlineData(@"\rooted.bin")]
    [InlineData("C:/rooted.bin")]
    [InlineData("art//empty-component.bin")]
    public void Open_RejectsUnsafeOrMalformedPaths(string entryPath)
    {
        using var archiveFile = new TemporaryArchive(Dat2Fixture.Create(
            new Dat2FixtureEntry(entryPath, false, [])));

        Assert.Throws<InvalidDataException>(() => Dat2Archive.Open(archiveFile.Path));
    }

    [Fact]
    public void Open_RejectsNonAsciiPathBytes()
    {
        byte[] fixture = Dat2Fixture.Create(
            new Dat2FixtureEntry("raw.bin", false, [0x41]));
        fixture[FindDirectoryEntry(fixture) + sizeof(uint)] = 0xff;
        using var archiveFile = new TemporaryArchive(fixture);

        Assert.Throws<InvalidDataException>(() => Dat2Archive.Open(archiveFile.Path));
    }

    [Fact]
    public void Open_RejectsCaseInsensitiveDuplicatePaths()
    {
        using var archiveFile = new TemporaryArchive(Dat2Fixture.Create(
            new Dat2FixtureEntry("art/hero.frm", false, []),
            new Dat2FixtureEntry("ART/HERO.FRM", false, [])));

        Assert.Throws<InvalidDataException>(() => Dat2Archive.Open(archiveFile.Path));
    }

    [Fact]
    public async Task CopyEntryToAsync_RejectsMalformedZlibData()
    {
        using var archiveFile = new TemporaryArchive(Dat2Fixture.Create(
            new Dat2FixtureEntry("broken.bin", true, [0x78, 0x9c, 0x01], 8)));
        var archive = Dat2Archive.Open(archiveFile.Path);
        using var output = new MemoryStream();

        await Assert.ThrowsAsync<InvalidDataException>(
            () => archive.CopyEntryToAsync(Assert.Single(archive.Entries), output));
    }

    [Fact]
    public async Task CopyEntryToAsync_RejectsZlibOutputAboveDeclaredSize()
    {
        using var archiveFile = new TemporaryArchive(Dat2Fixture.Create(
            new Dat2FixtureEntry("bomb.bin", true, Compress(new byte[256]), 8)));
        var archive = Dat2Archive.Open(archiveFile.Path);
        using var output = new MemoryStream();

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => archive.CopyEntryToAsync(Assert.Single(archive.Entries), output));

        Assert.Contains("beyond its declared size", exception.Message);
    }

    [Fact]
    public async Task CopyEntryToAsync_RejectsZlibOutputBelowDeclaredSize()
    {
        using var archiveFile = new TemporaryArchive(Dat2Fixture.Create(
            new Dat2FixtureEntry("short.bin", true, Compress([0x41, 0x42]), 3)));
        var archive = Dat2Archive.Open(archiveFile.Path);
        using var output = new MemoryStream();

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => archive.CopyEntryToAsync(Assert.Single(archive.Entries), output));

        Assert.Contains("expected 3", exception.Message);
    }

    [Fact]
    public async Task CopyEntryToAsync_RejectsEntryFromAnotherArchive()
    {
        using var firstFile = new TemporaryArchive(Dat2Fixture.Create(
            new Dat2FixtureEntry("same.bin", false, [0x41])));
        using var secondFile = new TemporaryArchive(Dat2Fixture.Create(
            new Dat2FixtureEntry("same.bin", false, [0x42])));
        var firstArchive = Dat2Archive.Open(firstFile.Path);
        var secondArchive = Dat2Archive.Open(secondFile.Path);
        using var output = new MemoryStream();

        await Assert.ThrowsAsync<ArgumentException>(
            () => firstArchive.CopyEntryToAsync(Assert.Single(secondArchive.Entries), output));
    }

    [Fact]
    public void FalloutArchive_OpenRejectsUnrecognizedDataWithBothFormatDiagnostics()
    {
        using var archiveFile = new TemporaryArchive([0x01, 0x02, 0x03, 0x04]);

        var exception = Assert.Throws<InvalidDataException>(() => FalloutArchive.Open(archiveFile.Path));

        Assert.Contains("DAT1 validation failed", exception.Message);
        Assert.Contains("DAT2 validation failed", exception.Message);
    }

    private static byte[] Compress(byte[] value)
    {
        using var output = new MemoryStream();
        using (var compressor = new ZLibStream(output, CompressionMode.Compress, leaveOpen: true))
        {
            compressor.Write(value);
        }

        return output.ToArray();
    }

    private static int FindDirectoryEntry(byte[] archive)
    {
        var treeSize = BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(archive.Length - 8));
        return archive.Length - checked((int)treeSize) - 8 + sizeof(uint);
    }

    private sealed class TemporaryArchive : IDisposable
    {
        public TemporaryArchive(byte[] bytes, string extension = ".dat")
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"fae-{Guid.NewGuid():N}{extension}");
            File.WriteAllBytes(Path, bytes);
        }

        public string Path { get; }

        public void Dispose() => File.Delete(Path);
    }

    private sealed record Dat2FixtureEntry(
        string Path,
        bool Compressed,
        byte[] StoredBytes,
        long? UncompressedSize = null,
        uint? Offset = null);

    private static class Dat2Fixture
    {
        public static byte[] Create(params Dat2FixtureEntry[] entries) =>
            Create(entries, []);

        public static byte[] Create(Dat2FixtureEntry[] entries, byte[] prefix)
        {
            using var data = new MemoryStream();
            data.Write(prefix);
            var offsets = new uint[entries.Length];
            for (var index = 0; index < entries.Length; index++)
            {
                offsets[index] = checked((uint)(data.Position - prefix.Length));
                data.Write(entries[index].StoredBytes);
            }

            using var directory = new MemoryStream();
            WriteUInt32(directory, checked((uint)entries.Length));
            for (var index = 0; index < entries.Length; index++)
            {
                var entry = entries[index];
                var pathBytes = Encoding.ASCII.GetBytes(entry.Path);
                WriteUInt32(directory, checked((uint)pathBytes.Length));
                directory.Write(pathBytes);
                directory.WriteByte(entry.Compressed ? (byte)1 : (byte)0);
                WriteUInt32(directory, checked((uint)(entry.UncompressedSize ?? entry.StoredBytes.Length)));
                WriteUInt32(directory, checked((uint)entry.StoredBytes.Length));
                WriteUInt32(directory, entry.Offset ?? offsets[index]);
            }

            using var archive = new MemoryStream();
            data.Position = 0;
            data.CopyTo(archive);
            directory.Position = 0;
            directory.CopyTo(archive);
            WriteUInt32(archive, checked((uint)directory.Length));
            WriteUInt32(
                archive,
                checked((uint)(data.Length - prefix.Length + directory.Length + FooterSize)));
            return archive.ToArray();
        }

        private static void WriteUInt32(Stream stream, uint value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
            stream.Write(buffer);
        }

        private const int FooterSize = 8;
    }
}
