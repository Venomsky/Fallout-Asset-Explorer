using System.Buffers.Binary;
using System.Text;

namespace FalloutAssetExplorer.Core;

public sealed class Dat1Archive : IGameArchive
{
    public const uint RawEntryType = 0x20;
    public const uint LzssEntryType = 0x40;

    private const int HeaderSize = 16;
    private const int FolderContentHeaderSize = 16;
    private const int FolderRecordMinimumSize = 17;
    private const int FileRecordMinimumSize = 17;
    private const int MaximumFolderCount = 65_536;
    private const int MaximumEntryCount = 250_000;
    private const long MaximumArchiveSize = uint.MaxValue;
    private const long MaximumEntrySize = 512L * 1024 * 1024;
    private const int CopyBufferSize = 64 * 1024;
    private const int LzssOutputBufferSize = 512 * 1024;
    private const int RingBufferSize = 4096;
    private const int MatchLengthUpperLimit = 18;
    private const int LzssThreshold = 2;

    private readonly HashSet<Dat1ArchiveEntry> _entrySet;

    private Dat1Archive(string path, IReadOnlyList<Dat1ArchiveEntry> entries)
    {
        Path = path;
        Entries = entries;
        _entrySet = new HashSet<Dat1ArchiveEntry>(entries);
    }

    public string Path { get; }

    public IReadOnlyList<Dat1ArchiveEntry> Entries { get; }

    public FalloutGame Game => FalloutGame.Fallout1;

    public ArchiveFormat Format => ArchiveFormat.Dat1;

    public IReadOnlyList<ArchiveEntry> BrowserEntries => Entries;

    public static Dat1Archive Open(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            CopyBufferSize,
            FileOptions.SequentialScan);

        if (stream.Length < HeaderSize)
        {
            throw new InvalidDataException("The archive is shorter than the DAT1 header.");
        }

        if (stream.Length > MaximumArchiveSize)
        {
            throw new InvalidDataException("The archive exceeds the DAT1 32-bit offset limit.");
        }

        var folderCount = ReadUInt32BigEndian(stream);
        SkipExactly(stream, 12);
        if (folderCount > MaximumFolderCount
            || folderCount > (ulong)((stream.Length - stream.Position) / FolderRecordMinimumSize))
        {
            throw new InvalidDataException("The DAT1 folder count is invalid for this archive.");
        }

        var folderNames = new string[(int)folderCount];
        for (var index = 0; index < folderNames.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            folderNames[index] = ReadLengthPrefixedName(stream);
            if (folderNames[index] == ".")
            {
                folderNames[index] = string.Empty;
            }
        }

        var entries = new List<Dat1ArchiveEntry>();
        for (var folderIndex = 0; folderIndex < folderNames.Length; folderIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileCount = ReadUInt32BigEndian(stream);
            SkipExactly(stream, FolderContentHeaderSize - sizeof(uint));

            if (fileCount > MaximumEntryCount - entries.Count
                || fileCount > (ulong)((stream.Length - stream.Position) / FileRecordMinimumSize))
            {
                throw new InvalidDataException("The DAT1 file count is invalid for this archive.");
            }

            for (var fileIndex = 0; fileIndex < fileCount; fileIndex++)
            {
                if ((fileIndex & 0x3ff) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var fileName = ReadLengthPrefixedName(stream);
                var type = ReadUInt32BigEndian(stream);
                var offset = ReadUInt32BigEndian(stream);
                var unpackedSize = ReadUInt32BigEndian(stream);
                var packedSize = ReadUInt32BigEndian(stream);

                if (unpackedSize > MaximumEntrySize || packedSize > MaximumEntrySize)
                {
                    throw new InvalidDataException("A DAT1 entry exceeds the 512 MiB per-entry limit.");
                }

                entries.Add(new Dat1ArchiveEntry(
                    CombinePath(folderNames[folderIndex], fileName),
                    type,
                    offset,
                    unpackedSize,
                    packedSize,
                    path));
            }
        }

        var directoryEnd = stream.Position;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Offset > stream.Length || entry.StoredDataSize > stream.Length - entry.Offset)
            {
                throw new InvalidDataException($"DAT1 entry '{entry.Path}' extends beyond the archive.");
            }

            if (entry.StoredDataSize > 0 && entry.Offset < directoryEnd)
            {
                throw new InvalidDataException($"DAT1 entry '{entry.Path}' overlaps the directory.");
            }
        }

        return new Dat1Archive(path, entries);
    }

    public async Task CopyEntryToAsync(
        Dat1ArchiveEntry entry,
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

        if (entry.UncompressedSize > MaximumEntrySize)
        {
            throw new InvalidDataException("The DAT1 entry exceeds the 512 MiB per-entry limit.");
        }

        await using var source = new FileStream(
            Path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            CopyBufferSize,
            FileOptions.Asynchronous | FileOptions.RandomAccess);

        if (entry.Offset > source.Length || entry.StoredDataSize > source.Length - entry.Offset)
        {
            throw new InvalidDataException($"DAT1 entry '{entry.Path}' changed or extends beyond the archive.");
        }

        source.Position = entry.Offset;
        var boundedInput = new BoundedInput(source, entry.StoredDataSize);

        switch (entry.Type)
        {
            case RawEntryType:
                boundedInput = new BoundedInput(source, entry.UncompressedSize);
                await CopyRawAsync(boundedInput, destination, cancellationToken);
                break;

            case LzssEntryType:
                await DecodeLzssAsync(
                    boundedInput,
                    destination,
                    entry.UncompressedSize,
                    cancellationToken);
                break;

            default:
                throw new InvalidDataException(
                    $"DAT1 entry '{entry.Path}' uses unsupported type 0x{entry.Type:X8}.");
        }
    }

    public Task CopyEntryToAsync(
        ArchiveEntry entry,
        Stream destination,
        CancellationToken cancellationToken = default) =>
        entry is Dat1ArchiveEntry dat1Entry
            ? CopyEntryToAsync(dat1Entry, destination, cancellationToken)
            : throw new ArgumentException("The entry is not a DAT1 entry.", nameof(entry));

    private static async Task CopyRawAsync(
        BoundedInput input,
        Stream destination,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[CopyBufferSize];
        while (input.Remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await input.ReadAsync(buffer.AsMemory(
                0,
                (int)Math.Min(buffer.Length, input.Remaining)), cancellationToken);
            if (read == 0)
            {
                throw new InvalidDataException("A raw DAT1 entry is truncated.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static async Task DecodeLzssAsync(
        BoundedInput input,
        Stream destination,
        long expectedOutputSize,
        CancellationToken cancellationToken)
    {
        var ring = new byte[RingBufferSize];
        Array.Fill(ring, (byte)' ', 0, RingBufferSize - MatchLengthUpperLimit);
        var ringPosition = RingBufferSize - MatchLengthUpperLimit;
        var outputBuffer = new byte[LzssOutputBufferSize];
        var outputBufferLength = 0;
        long outputCount = 0;

        while (input.Remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (input.Remaining < sizeof(ushort))
            {
                throw new InvalidDataException("A DAT1 LZSS block header is truncated.");
            }

            var headerBytes = new byte[sizeof(ushort)];
            await input.ReadExactlyAsync(headerBytes, cancellationToken);
            var blockLength = (short)BinaryPrimitives.ReadUInt16BigEndian(headerBytes);
            if (blockLength == 0)
            {
                if (outputCount != expectedOutputSize || input.Remaining != 0)
                {
                    throw new InvalidDataException("The DAT1 LZSS stream has an invalid end marker.");
                }

                break;
            }

            if (blockLength < 0)
            {
                var rawBlockLength = -(int)blockLength;
                EnsureOutputFits(outputCount, rawBlockLength, expectedOutputSize);
                if (rawBlockLength > input.Remaining)
                {
                    throw new InvalidDataException("A raw DAT1 LZSS block extends beyond its entry.");
                }

                var remaining = rawBlockLength;
                while (remaining > 0)
                {
                    var read = await input.ReadAsync(outputBuffer.AsMemory(
                        outputBufferLength,
                        Math.Min(remaining, outputBuffer.Length - outputBufferLength)), cancellationToken);
                    if (read == 0)
                    {
                        throw new InvalidDataException("A raw DAT1 LZSS block is truncated.");
                    }

                    for (var index = 0; index < read; index++)
                    {
                        ring[ringPosition] = outputBuffer[outputBufferLength + index];
                        ringPosition = (ringPosition + 1) & (RingBufferSize - 1);
                    }

                    outputBufferLength += read;
                    outputCount += read;
                    remaining -= read;
                    if (outputBufferLength == outputBuffer.Length)
                    {
                        await destination.WriteAsync(outputBuffer, cancellationToken);
                        outputBufferLength = 0;
                    }
                }

                if (outputBufferLength > 0)
                {
                    await destination.WriteAsync(outputBuffer.AsMemory(0, outputBufferLength), cancellationToken);
                    outputBufferLength = 0;
                }

                continue;
            }

            if (blockLength > input.Remaining)
            {
                throw new InvalidDataException("A compressed DAT1 LZSS block extends beyond its entry.");
            }

            var compressedBlock = new byte[blockLength];
            await input.ReadExactlyAsync(compressedBlock, cancellationToken);
            DecodeCompressedBlock(
                compressedBlock,
                ring,
                outputBuffer,
                ref outputBufferLength,
                ref ringPosition,
                ref outputCount,
                expectedOutputSize);

            if (outputBufferLength > 0)
            {
                await destination.WriteAsync(outputBuffer.AsMemory(0, outputBufferLength), cancellationToken);
                outputBufferLength = 0;
            }
        }

        if (outputCount != expectedOutputSize || input.Remaining != 0)
        {
            throw new InvalidDataException("The DAT1 LZSS stream produced an unexpected output size.");
        }

        if (outputBufferLength > 0)
        {
            await destination.WriteAsync(outputBuffer.AsMemory(0, outputBufferLength), cancellationToken);
        }
    }

    private static void DecodeCompressedBlock(
        ReadOnlySpan<byte> input,
        byte[] ring,
        byte[] outputBuffer,
        ref int outputBufferLength,
        ref int ringPosition,
        ref long outputCount,
        long expectedOutputSize)
    {
        var inputPosition = 0;
        uint flags = 0;

        while (inputPosition < input.Length)
        {
            flags >>= 1;
            if ((flags & 0x100) == 0)
            {
                flags = (uint)(input[inputPosition++] | 0xff00);
            }

            if ((flags & 1) != 0)
            {
                if (inputPosition >= input.Length)
                {
                    throw new InvalidDataException("A DAT1 LZSS literal is truncated.");
                }

                EmitLzssByte(
                    input[inputPosition++],
                    ring,
                    outputBuffer,
                    ref outputBufferLength,
                    ref ringPosition,
                    ref outputCount,
                    expectedOutputSize);
                continue;
            }

            if (input.Length - inputPosition < 2)
            {
                throw new InvalidDataException("A DAT1 LZSS back-reference is truncated.");
            }

            var sourcePosition = (int)input[inputPosition++];
            var lengthByte = (int)input[inputPosition++];
            sourcePosition |= (lengthByte & 0xf0) << 4;
            var length = (lengthByte & 0x0f) + LzssThreshold + 1;
            EnsureOutputFits(outputCount, length, expectedOutputSize);

            for (var index = 0; index < length; index++)
            {
                EmitLzssByte(
                    ring[(sourcePosition + index) & (RingBufferSize - 1)],
                    ring,
                    outputBuffer,
                    ref outputBufferLength,
                    ref ringPosition,
                    ref outputCount,
                    expectedOutputSize);
            }
        }
    }

    private static void EmitLzssByte(
        byte value,
        byte[] ring,
        byte[] outputBuffer,
        ref int outputBufferLength,
        ref int ringPosition,
        ref long outputCount,
        long expectedOutputSize)
    {
        EnsureOutputFits(outputCount, 1, expectedOutputSize);
        outputBuffer[outputBufferLength++] = value;
        outputCount++;
        ring[ringPosition] = value;
        ringPosition = (ringPosition + 1) & (RingBufferSize - 1);
    }

    private static void EnsureOutputFits(long currentSize, int additionalSize, long expectedSize)
    {
        if (additionalSize > expectedSize - currentSize)
        {
            throw new InvalidDataException("The DAT1 LZSS stream exceeds its declared output size.");
        }
    }

    private static string CombinePath(string folder, string fileName)
    {
        var normalizedFolder = folder.Replace('\\', '/').Trim('/');
        var normalizedFileName = fileName.Replace('\\', '/').TrimStart('/');
        return normalizedFolder.Length == 0
            ? normalizedFileName
            : $"{normalizedFolder}/{normalizedFileName}";
    }

    private static string ReadLengthPrefixedName(Stream stream)
    {
        var length = stream.ReadByte();
        if (length < 0)
        {
            throw new InvalidDataException("A DAT1 name length is truncated.");
        }

        var nameBytes = new byte[length];
        ReadExactly(stream, nameBytes);
        return Encoding.Latin1.GetString(nameBytes);
    }

    private static uint ReadUInt32BigEndian(Stream stream)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        ReadExactly(stream, bytes);
        return BinaryPrimitives.ReadUInt32BigEndian(bytes);
    }

    private static void SkipExactly(Stream stream, int count)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        while (count > 0)
        {
            var chunkLength = Math.Min(count, bytes.Length);
            ReadExactly(stream, bytes[..chunkLength]);
            count -= chunkLength;
        }
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        try
        {
            stream.ReadExactly(buffer);
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("The DAT1 archive contains a truncated record.", exception);
        }
    }

    private sealed class BoundedInput(Stream stream, long length)
    {
        public long Remaining { get; private set; } = length;

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            if (Remaining == 0)
            {
                return 0;
            }

            var boundedBuffer = buffer[..(int)Math.Min(buffer.Length, Remaining)];
            var read = await stream.ReadAsync(boundedBuffer, cancellationToken);
            Remaining -= read;
            return read;
        }

        public async ValueTask ReadExactlyAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken)
        {
            while (!buffer.IsEmpty)
            {
                var read = await ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    throw new InvalidDataException("A DAT1 entry is truncated.");
                }

                buffer = buffer[read..];
            }
        }
    }
}
