using System.Buffers.Binary;
using System.Text;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class Dat1ArchiveTests
{
    [Fact]
    public void Open_ParsesEmptyArchive()
    {
        using var archiveFile = new TemporaryArchive(
            Dat1Fixture.Create(string.Empty, null, null, [], 0));

        var archive = Dat1Archive.Open(archiveFile.Path);

        Assert.Empty(archive.Entries);
    }

    [Fact]
    public void FalloutArchive_OpenDetectsDat1WithoutUsingTheExtension()
    {
        using var archiveFile = new TemporaryArchive(Dat1Fixture.Create(string.Empty, null, null, [], 0));

        var archive = FalloutArchive.Open(archiveFile.Path);

        Assert.IsType<Dat1Archive>(archive);
        Assert.Equal(ArchiveFormat.Dat1, archive.Format);
        Assert.Equal(FalloutGame.Fallout1, archive.Game);
    }

    [Fact]
    public async Task Open_ParsesAndReadsRawEntry()
    {
        var expected = Encoding.ASCII.GetBytes("Fallout DAT1 raw payload");
        using var archiveFile = new TemporaryArchive(Dat1Fixture.Create(
            ".",
            "art\\hero.FRM",
            Dat1Archive.RawEntryType,
            expected,
            expected.Length));

        var archive = Dat1Archive.Open(archiveFile.Path);
        var entry = Assert.Single(archive.Entries);
        using var output = new MemoryStream();

        Assert.IsAssignableFrom<IGameArchive>(archive);
        Assert.Equal(FalloutGame.Fallout1, archive.Game);
        Assert.Equal(ArchiveFormat.Dat1, archive.Format);
        Assert.Same(entry, Assert.Single(archive.BrowserEntries));
        await archive.CopyEntryToAsync(entry, output);

        Assert.Equal("art/hero.FRM", entry.Path);
        Assert.Equal(expected.Length, entry.UncompressedSize);
        Assert.Equal(expected.Length, entry.PackedSize);
        Assert.Equal(expected, output.ToArray());
    }

    [Fact]
    public async Task Open_ReadsRawEntryWhenPackedSizeIsZero()
    {
        byte[] expected = [0x10, 0x20, 0x30, 0x40];
        using var archiveFile = new TemporaryArchive(Dat1Fixture.Create(
            "ART\\SCENERY",
            "TEMP.REF",
            Dat1Archive.RawEntryType,
            expected,
            expected.Length,
            packedSize: 0));

        var archive = Dat1Archive.Open(archiveFile.Path);
        var entry = Assert.Single(archive.Entries);
        using var output = new MemoryStream();

        await archive.CopyEntryToAsync(entry, output);

        Assert.Equal("ART/SCENERY/TEMP.REF", entry.Path);
        Assert.Equal(0, entry.PackedSize);
        Assert.Equal(expected.Length, entry.UncompressedSize);
        Assert.Equal(expected.Length, entry.StoredDataSize);
        Assert.Null(entry.ToBrowserEntry().CompressedSize);
        Assert.Equal(expected, output.ToArray());
    }

    [Fact]
    public async Task Open_ParsesAndReadsCompressedLzssEntryWithBackReference()
    {
        byte[] expected = "ABCABC"u8.ToArray();
        byte[] compressed =
        [
            0x00, 0x06, // Compressed block length.
            0x07, (byte)'A', (byte)'B', (byte)'C', 0xee, 0xf0, // Literals, then a three-byte back-reference.
            0x00, 0x00 // End marker.
        ];
        using var archiveFile = new TemporaryArchive(Dat1Fixture.Create(
            "art",
            "repeat.frm",
            Dat1Archive.LzssEntryType,
            compressed,
            expected.Length));

        var archive = Dat1Archive.Open(archiveFile.Path);
        using var output = new MemoryStream();

        await archive.CopyEntryToAsync(Assert.Single(archive.Entries), output);

        Assert.Equal(expected, output.ToArray());
    }

    [Fact]
    public async Task Open_ReadsRawLzssBlock()
    {
        byte[] expected = "raw"u8.ToArray();
        byte[] compressed = [0xff, 0xfd, (byte)'r', (byte)'a', (byte)'w'];
        using var archiveFile = new TemporaryArchive(Dat1Fixture.Create(
            "art",
            "raw.frm",
            Dat1Archive.LzssEntryType,
            compressed,
            expected.Length));

        var archive = Dat1Archive.Open(archiveFile.Path);
        using var output = new MemoryStream();

        await archive.CopyEntryToAsync(Assert.Single(archive.Entries), output);

        Assert.Equal(expected, output.ToArray());
    }

    [Fact]
    public void Open_PreservesUnusualLegacyNameBytes()
    {
        using var archiveFile = new TemporaryArchive(Dat1Fixture.Create(
            "art",
            "caf\u00e9 ?.frm",
            Dat1Archive.RawEntryType,
            [0x41],
            1));

        var archive = Dat1Archive.Open(archiveFile.Path);

        Assert.Equal("art/caf\u00e9 ?.frm", Assert.Single(archive.Entries).Path);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(15)]
    public void Open_RejectsTruncatedHeader(int length)
    {
        using var archiveFile = new TemporaryArchive(new byte[length]);

        Assert.Throws<InvalidDataException>(() => Dat1Archive.Open(archiveFile.Path));
    }

    [Fact]
    public void Open_RejectsFolderCountImpossibleForArchive()
    {
        byte[] fixture = Dat1Fixture.Create(string.Empty, null, null, [], 0);
        BinaryPrimitives.WriteUInt32BigEndian(fixture, uint.MaxValue);
        using var archiveFile = new TemporaryArchive(fixture);

        Assert.Throws<InvalidDataException>(() => Dat1Archive.Open(archiveFile.Path));
    }

    [Fact]
    public void Open_RejectsTruncatedFolderName()
    {
        byte[] fixture = new byte[18];
        BinaryPrimitives.WriteUInt32BigEndian(fixture, 1);
        fixture[16] = 3;
        fixture[17] = (byte)'a';
        using var archiveFile = new TemporaryArchive(fixture);

        Assert.Throws<InvalidDataException>(() => Dat1Archive.Open(archiveFile.Path));
    }

    [Fact]
    public void Open_RejectsFileCountImpossibleForArchive()
    {
        byte[] fixture = Dat1Fixture.Create(".", null, null, [], 0);
        BinaryPrimitives.WriteUInt32BigEndian(fixture.AsSpan(18), 1);
        using var archiveFile = new TemporaryArchive(fixture);

        Assert.Throws<InvalidDataException>(() => Dat1Archive.Open(archiveFile.Path));
    }

    [Fact]
    public void Open_RejectsFileCountAboveLimitBeforeReadingRecords()
    {
        byte[] fixture = Dat1Fixture.Create(".", null, null, [], 0);
        BinaryPrimitives.WriteUInt32BigEndian(fixture.AsSpan(18), 250_001);
        using var archiveFile = new TemporaryArchive(fixture);

        Assert.Throws<InvalidDataException>(() => Dat1Archive.Open(archiveFile.Path));
    }

    [Fact]
    public void Open_RejectsOutOfBoundsEntryData()
    {
        byte[] fixture = Dat1Fixture.Create(
            ".",
            "x",
            Dat1Archive.RawEntryType,
            [0x41],
            1);
        BinaryPrimitives.WriteUInt32BigEndian(fixture.AsSpan(40), uint.MaxValue);
        using var archiveFile = new TemporaryArchive(fixture);

        Assert.Throws<InvalidDataException>(() => Dat1Archive.Open(archiveFile.Path));
    }

    [Fact]
    public void Open_RejectsEntryOverlappingDirectory()
    {
        byte[] fixture = Dat1Fixture.Create(
            ".",
            "x",
            Dat1Archive.RawEntryType,
            [0x41],
            1);
        BinaryPrimitives.WriteUInt32BigEndian(fixture.AsSpan(40), 0);
        using var archiveFile = new TemporaryArchive(fixture);

        Assert.Throws<InvalidDataException>(() => Dat1Archive.Open(archiveFile.Path));
    }

    [Fact]
    public void Open_UsesUnpackedSizeForRawEntryDataBounds()
    {
        byte[] fixture = Dat1Fixture.Create(
            ".",
            "x",
            Dat1Archive.RawEntryType,
            [0x41],
            1,
            packedSize: 2);
        BinaryPrimitives.WriteUInt32BigEndian(fixture.AsSpan(48), 2);
        using var archiveFile = new TemporaryArchive(fixture);

        var entry = Assert.Single(Dat1Archive.Open(archiveFile.Path).Entries);

        Assert.Equal(2, entry.PackedSize);
        Assert.Equal(1, entry.StoredDataSize);
    }

    [Fact]
    public void Open_RejectsEntryAboveConfiguredSizeLimit()
    {
        byte[] fixture = Dat1Fixture.Create(
            ".",
            "x",
            Dat1Archive.LzssEntryType,
            [],
            512L * 1024 * 1024 + 1);
        using var archiveFile = new TemporaryArchive(fixture);

        Assert.Throws<InvalidDataException>(() => Dat1Archive.Open(archiveFile.Path));
    }

    [Fact]
    public async Task CopyEntryToAsync_RejectsUnsupportedEntryType()
    {
        using var archiveFile = new TemporaryArchive(Dat1Fixture.Create(
            ".",
            "unknown.bin",
            0x99,
            [0x41],
            1));
        var archive = Dat1Archive.Open(archiveFile.Path);
        using var output = new MemoryStream();

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => archive.CopyEntryToAsync(Assert.Single(archive.Entries), output));

        Assert.Contains("unsupported type", exception.Message);
    }

    [Fact]
    public async Task CopyEntryToAsync_RejectsTruncatedLzssBackReference()
    {
        byte[] compressed = [0x00, 0x02, 0x00, 0x01, 0x00, 0x00];
        using var archiveFile = new TemporaryArchive(Dat1Fixture.Create(
            ".",
            "broken.frm",
            Dat1Archive.LzssEntryType,
            compressed,
            3));
        var archive = Dat1Archive.Open(archiveFile.Path);
        using var output = new MemoryStream();

        await Assert.ThrowsAsync<InvalidDataException>(
            () => archive.CopyEntryToAsync(Assert.Single(archive.Entries), output));
    }

    [Fact]
    public async Task CopyEntryToAsync_RejectsEntryFromAnotherArchive()
    {
        using var firstFile = new TemporaryArchive(Dat1Fixture.Create(
            ".",
            "same",
            Dat1Archive.RawEntryType,
            [0x41],
            1));
        using var secondFile = new TemporaryArchive(Dat1Fixture.Create(
            ".",
            "same",
            Dat1Archive.RawEntryType,
            [0x42],
            1));
        var firstArchive = Dat1Archive.Open(firstFile.Path);
        var secondArchive = Dat1Archive.Open(secondFile.Path);
        using var output = new MemoryStream();

        await Assert.ThrowsAsync<ArgumentException>(
            () => firstArchive.CopyEntryToAsync(Assert.Single(secondArchive.Entries), output));
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

    private static class Dat1Fixture
    {
        public static byte[] Create(
            string folder,
            string? fileName,
            uint? type,
            byte[] payload,
            long unpackedSize,
            uint? packedSize = null)
        {
            using var stream = new MemoryStream();
            WriteUInt32(stream, folder.Length == 0 ? 0u : 1u);
            WriteUInt32(stream, 0);
            WriteUInt32(stream, 0);
            WriteUInt32(stream, 0);

            if (folder.Length == 0)
            {
                return stream.ToArray();
            }

            WriteName(stream, folder);
            WriteUInt32(stream, fileName is null ? 0u : 1u);
            WriteUInt32(stream, 0);
            WriteUInt32(stream, 0);
            WriteUInt32(stream, 0);

            if (fileName is not null)
            {
                WriteName(stream, fileName);
                WriteUInt32(stream, type!.Value);
                var offsetPosition = stream.Position;
                WriteUInt32(stream, 0);
                WriteUInt32(stream, checked((uint)unpackedSize));
                WriteUInt32(stream, packedSize ?? checked((uint)payload.Length));

                var dataOffset = checked((uint)stream.Position);
                stream.Position = offsetPosition;
                WriteUInt32(stream, dataOffset);
                stream.Position = stream.Length;
                stream.Write(payload);
            }

            return stream.ToArray();
        }

        private static void WriteName(Stream stream, string value)
        {
            var bytes = Encoding.Latin1.GetBytes(value);
            if (bytes.Length > byte.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            stream.WriteByte((byte)bytes.Length);
            stream.Write(bytes);
        }

        private static void WriteUInt32(Stream stream, uint value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
            stream.Write(buffer);
        }
    }
}
