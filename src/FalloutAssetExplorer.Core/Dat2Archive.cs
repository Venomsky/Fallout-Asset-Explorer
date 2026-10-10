using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace FalloutAssetExplorer.Core;

public sealed class Dat2Archive : IGameArchive
{
    private const int FooterSize = 8;
    private const int MinimumTreeSize = sizeof(uint);
    private const int MinimumEntrySize = sizeof(uint) + 1 + 3 * sizeof(uint) + 1;
    private const int MaximumEntryCount = 250_000;
    private const uint MaximumDirectorySize = 128 * 1024 * 1024;
    private const int MaximumPathLength = 4096;
    private const long MaximumEntrySize = 512L * 1024 * 1024;
    private const int CopyBufferSize = 64 * 1024;

    private readonly long _archiveLength;
    private readonly long _dataBaseOffset;
    private readonly long _directoryOffset;
    private readonly HashSet<Dat2ArchiveEntry> _entrySet;

    private Dat2Archive(
        string path,
        long archiveLength,
        long dataBaseOffset,
        long directoryOffset,
        IReadOnlyList<Dat2ArchiveEntry> entries)
    {
        Path = path;
        _archiveLength = archiveLength;
        _dataBaseOffset = dataBaseOffset;
        _directoryOffset = directoryOffset;
        Entries = entries;
        _entrySet = new HashSet<Dat2ArchiveEntry>(entries);
    }

    public string Path { get; }

    public FalloutGame Game => FalloutGame.Fallout2;

    public ArchiveFormat Format => ArchiveFormat.Dat2;

    public IReadOnlyList<Dat2ArchiveEntry> Entries { get; }

    public IReadOnlyList<ArchiveEntry> BrowserEntries => Entries;

    public static Dat2Archive Open(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            CopyBufferSize,
            FileOptions.RandomAccess);

        var archiveLength = stream.Length;
        if (archiveLength < FooterSize)
        {
            throw new InvalidDataException("The archive is shorter than a DAT2 footer.");
        }

        stream.Position = archiveLength - FooterSize;
        var treeSize = ReadUInt32LittleEndian(stream);
        var dataSize = ReadUInt32LittleEndian(stream);
        if (dataSize > archiveLength)
        {
            throw new InvalidDataException("The DAT2 data size exceeds the physical archive length.");
        }

        if (treeSize < MinimumTreeSize
            || treeSize > MaximumDirectorySize
            || (long)treeSize + FooterSize > dataSize)
        {
            throw new InvalidDataException("The DAT2 directory size is invalid.");
        }

        var dataBaseOffset = archiveLength - dataSize;
        var directoryOffset = archiveLength - treeSize - FooterSize;
        if (directoryOffset < dataBaseOffset)
        {
            throw new InvalidDataException("The DAT2 directory begins before the archive data region.");
        }

        var directoryEnd = directoryOffset + treeSize;
        stream.Position = directoryOffset;
        var entryCount = ReadUInt32LittleEndian(stream);
        if (entryCount > MaximumEntryCount
            || (long)entryCount * MinimumEntrySize > treeSize - MinimumTreeSize)
        {
            throw new InvalidDataException("The DAT2 file count is invalid for its directory size.");
        }

        var entries = new List<Dat2ArchiveEntry>((int)entryCount);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dataRegionSize = directoryOffset - dataBaseOffset;
        for (var index = 0; index < entryCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (directoryEnd - stream.Position < MinimumEntrySize)
            {
                throw new InvalidDataException("The DAT2 directory contains a truncated file record.");
            }

            var pathLength = ReadUInt32LittleEndian(stream);
            if (pathLength == 0 || pathLength > MaximumPathLength
                || (long)pathLength + sizeof(byte) + 3 * sizeof(uint) > directoryEnd - stream.Position)
            {
                throw new InvalidDataException("A DAT2 entry path length is invalid for the remaining directory.");
            }

            var pathBytes = new byte[(int)pathLength];
            ReadExactly(stream, pathBytes, "A DAT2 entry path is truncated.");
            if (pathBytes.Any(value => value > 0x7f))
            {
                throw new InvalidDataException("A DAT2 entry path contains bytes outside the documented ASCII encoding.");
            }

            var storedPath = Encoding.ASCII.GetString(pathBytes);
            var normalizedPath = NormalizeEntryPath(storedPath);
            var compressionFlag = stream.ReadByte();
            if (compressionFlag is not (0 or 1))
            {
                throw new InvalidDataException($"DAT2 entry '{storedPath}' uses unsupported compression flag {compressionFlag}.");
            }

            var uncompressedSize = ReadUInt32LittleEndian(stream);
            var storedSize = ReadUInt32LittleEndian(stream);
            var offset = ReadUInt32LittleEndian(stream);
            var isCompressed = compressionFlag == 1;

            if (uncompressedSize > MaximumEntrySize || storedSize > MaximumEntrySize)
            {
                throw new InvalidDataException("A DAT2 entry exceeds the 512 MiB stored or uncompressed size limit.");
            }

            if (!isCompressed && storedSize != uncompressedSize)
            {
                throw new InvalidDataException(
                    $"Uncompressed DAT2 entry '{storedPath}' has inconsistent stored and uncompressed sizes.");
            }

            if (isCompressed && storedSize == 0 && uncompressedSize != 0)
            {
                throw new InvalidDataException($"Compressed DAT2 entry '{storedPath}' has no stored data.");
            }

            if (offset > dataRegionSize || storedSize > dataRegionSize - offset)
            {
                throw new InvalidDataException($"DAT2 entry '{storedPath}' extends into or beyond the directory.");
            }

            if (!paths.Add(normalizedPath))
            {
                throw new InvalidDataException(
                    $"The DAT2 directory contains duplicate entry path '{normalizedPath}' (case-insensitive).");
            }

            entries.Add(new Dat2ArchiveEntry(
                storedPath,
                normalizedPath,
                isCompressed,
                uncompressedSize,
                storedSize,
                offset,
                path));
        }

        if (stream.Position != directoryEnd)
        {
            throw new InvalidDataException("The DAT2 directory contains unexpected trailing bytes.");
        }

        return new Dat2Archive(
            path,
            archiveLength,
            dataBaseOffset,
            directoryOffset,
            entries);
    }

    public Task CopyEntryToAsync(
        ArchiveEntry entry,
        Stream destination,
        CancellationToken cancellationToken = default) =>
        entry is Dat2ArchiveEntry dat2Entry
            ? CopyEntryToAsync(dat2Entry, destination, cancellationToken)
            : throw new ArgumentException("The entry is not a DAT2 entry.", nameof(entry));

    public async Task CopyEntryToAsync(
        Dat2ArchiveEntry entry,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(destination);
        if (!_entrySet.Contains(entry))
        {
            throw new ArgumentException("The entry does not belong to this archive.", nameof(entry));
        }

        if (!destination.CanWrite)
        {
            throw new ArgumentException("The destination stream must be writable.", nameof(destination));
        }

        if (entry.UncompressedSize > MaximumEntrySize || entry.StoredSize > MaximumEntrySize)
        {
            throw new InvalidDataException("The DAT2 entry exceeds the 512 MiB size limit.");
        }

        await using var source = new FileStream(
            Path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            CopyBufferSize,
            FileOptions.Asynchronous | FileOptions.RandomAccess);

        if (source.Length != _archiveLength)
        {
            throw new InvalidDataException("The DAT2 archive changed after its directory was read.");
        }

        var relativeDataEnd = _directoryOffset - _dataBaseOffset;
        if (entry.Offset > relativeDataEnd || entry.StoredSize > relativeDataEnd - entry.Offset)
        {
            throw new InvalidDataException($"DAT2 entry '{entry.Path}' changed or extends beyond its data region.");
        }

        source.Position = checked(_dataBaseOffset + entry.Offset);
        using var boundedInput = new BoundedReadStream(source, entry.StoredSize);
        if (!entry.IsCompressed)
        {
            await CopyExactlyAsync(boundedInput, destination, entry.UncompressedSize, cancellationToken);
            return;
        }

        await using var decompressor = new ZLibStream(
            boundedInput,
            CompressionMode.Decompress,
            leaveOpen: true);
        await CopyDecompressedAsync(
            decompressor,
            boundedInput,
            destination,
            entry.UncompressedSize,
            entry.Path,
            cancellationToken);
    }

    private static async Task CopyExactlyAsync(
        Stream input,
        Stream destination,
        long length,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[CopyBufferSize];
        var remaining = length;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await input.ReadAsync(
                buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)),
                cancellationToken);
            if (read == 0)
            {
                throw new InvalidDataException("An uncompressed DAT2 entry is truncated.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            remaining -= read;
        }
    }

    private static async Task CopyDecompressedAsync(
        Stream decompressor,
        BoundedReadStream boundedInput,
        Stream destination,
        long expectedSize,
        string entryPath,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[CopyBufferSize];
        long totalWritten = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await decompressor.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (read > expectedSize - totalWritten)
            {
                throw new InvalidDataException(
                    $"Compressed DAT2 entry '{entryPath}' expands beyond its declared size.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            totalWritten += read;
        }

        if (totalWritten != expectedSize)
        {
            throw new InvalidDataException(
                $"Compressed DAT2 entry '{entryPath}' expanded to {totalWritten} bytes; expected {expectedSize}.");
        }

        if (boundedInput.Remaining != 0)
        {
            throw new InvalidDataException(
                $"Compressed DAT2 entry '{entryPath}' did not consume its complete stored data range.");
        }
    }

    private static string NormalizeEntryPath(string storedPath)
    {
        if (storedPath.Any(char.IsControl))
        {
            throw new InvalidDataException("A DAT2 entry path contains a control character.");
        }

        var normalizedPath = storedPath.Replace('\\', '/');
        if (normalizedPath[0] == '/'
            || (normalizedPath.Length >= 2
                && char.IsLetter(normalizedPath[0])
                && normalizedPath[1] == ':'))
        {
            throw new InvalidDataException($"DAT2 entry path '{storedPath}' is absolute.");
        }

        var components = normalizedPath.Split('/');
        if (components.Any(component =>
                component.Length == 0
                || component is "." or ".."))
        {
            throw new InvalidDataException($"DAT2 entry path '{storedPath}' contains an invalid path component.");
        }

        return normalizedPath;
    }

    private static uint ReadUInt32LittleEndian(Stream stream)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        ReadExactly(stream, bytes, "The DAT2 archive contains a truncated integer field.");
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer, string errorMessage)
    {
        try
        {
            stream.ReadExactly(buffer);
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException(errorMessage, exception);
        }
    }

    private static void ReadExactly(Stream stream, byte[] buffer, string errorMessage) =>
        ReadExactly(stream, buffer.AsSpan(), errorMessage);

    private sealed class BoundedReadStream(Stream source, long length) : Stream
    {
        public long Remaining { get; private set; } = length;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = source.Read(buffer, offset, (int)Math.Min(count, Remaining));
            Remaining -= read;
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            var read = source.Read(buffer[..(int)Math.Min(buffer.Length, Remaining)]);
            Remaining -= read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var read = await source.ReadAsync(
                buffer[..(int)Math.Min(buffer.Length, Remaining)],
                cancellationToken);
            Remaining -= read;
            return read;
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
