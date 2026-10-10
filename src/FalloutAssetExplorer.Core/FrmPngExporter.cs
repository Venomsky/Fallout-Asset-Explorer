using System.Buffers.Binary;
using System.IO.Compression;

namespace FalloutAssetExplorer.Core;

public static class FrmPngExporter
{
    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    private static readonly uint[] CrcTable = CreateCrcTable();

    public static void Write(
        FrmDocument document,
        int directionIndex,
        int frameIndex,
        FalloutPalette palette,
        Stream destination)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(destination);

        if (!destination.CanWrite)
        {
            throw new ArgumentException("The PNG destination stream must be writable.", nameof(destination));
        }

        if (document.Directions is null
            || (uint)directionIndex >= (uint)document.Directions.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(directionIndex));
        }

        var direction = document.Directions[directionIndex];
        if (direction?.Frames is null
            || (uint)frameIndex >= (uint)direction.Frames.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(frameIndex));
        }

        var frame = direction.Frames[frameIndex];
        if (frame is null || frame.Pixels is null)
        {
            throw new InvalidDataException("The selected FRM frame has no pixel data.");
        }

        if (frame.Width <= 0 || frame.Height <= 0
            || frame.Width > FrmDecoder.MaximumDimension
            || frame.Height > FrmDecoder.MaximumDimension)
        {
            throw new InvalidDataException("The selected FRM frame has invalid dimensions.");
        }

        var pixelCount = (long)frame.Width * frame.Height;
        if (pixelCount > FrmDecoder.MaximumTotalPixels || frame.Pixels.Length != pixelCount)
        {
            throw new InvalidDataException("The selected FRM frame pixel count does not match its dimensions.");
        }

        destination.Write(Signature);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)frame.Width));
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], checked((uint)frame.Height));
        header[8] = 8;
        header[9] = 6;
        header[10] = 0;
        header[11] = 0;
        header[12] = 0;
        WriteChunk(destination, "IHDR"u8, header);

        using (var idatChunks = new IdatChunkStream(destination))
        {
            using (var compressor = new ZLibStream(idatChunks, CompressionLevel.Optimal, leaveOpen: true))
            {
                var row = new byte[checked(frame.Width * 4 + 1)];
                for (var y = 0; y < frame.Height; y++)
                {
                    row[0] = 0;
                    var sourceOffset = y * frame.Width;
                    for (var x = 0; x < frame.Width; x++)
                    {
                        var color = palette.GetColor(frame.Pixels[sourceOffset + x]);
                        var targetOffset = 1 + x * 4;
                        row[targetOffset] = color.R;
                        row[targetOffset + 1] = color.G;
                        row[targetOffset + 2] = color.B;
                        row[targetOffset + 3] = color.A;
                    }

                    compressor.Write(row);
                }
            }
        }

        WriteChunk(destination, "IEND"u8, ReadOnlySpan<byte>.Empty);
    }

    private static void WriteChunk(Stream destination, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> lengthBytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(lengthBytes, checked((uint)data.Length));
        destination.Write(lengthBytes);
        destination.Write(type);
        destination.Write(data);

        var checksum = UpdateCrc(uint.MaxValue, type);
        checksum = ~UpdateCrc(checksum, data);
        Span<byte> checksumBytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(checksumBytes, checksum);
        destination.Write(checksumBytes);
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (var index = 0; index < table.Length; index++)
        {
            var value = (uint)index;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0
                    ? 0xEDB88320 ^ (value >> 1)
                    : value >> 1;
            }

            table[index] = value;
        }

        return table;
    }

    private sealed class IdatChunkStream(Stream destination) : Stream
    {
        private const int ChunkBufferSize = 64 * 1024;
        private readonly byte[] _buffer = new byte[ChunkBufferSize];
        private int _bufferLength;
        private bool _disposed;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => !_disposed;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
            ThrowIfDisposed();
            WriteBufferedChunk();
            destination.Flush();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            Write(buffer.AsSpan(offset, count));
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ThrowIfDisposed();
            while (!buffer.IsEmpty)
            {
                var count = Math.Min(ChunkBufferSize - _bufferLength, buffer.Length);
                buffer[..count].CopyTo(_buffer.AsSpan(_bufferLength));
                _bufferLength += count;
                buffer = buffer[count..];

                if (_bufferLength == ChunkBufferSize)
                {
                    WriteBufferedChunk();
                }
            }
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                WriteBufferedChunk();
                _disposed = true;
            }

            base.Dispose(disposing);
        }

        private void WriteBufferedChunk()
        {
            if (_bufferLength == 0)
            {
                return;
            }

            WriteChunk(destination, "IDAT"u8, _buffer.AsSpan(0, _bufferLength));
            _bufferLength = 0;
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }
    }
}
