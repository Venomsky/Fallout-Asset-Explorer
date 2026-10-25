using System.Buffers.Binary;
using System.Security.Cryptography;
using Avalonia.Controls;
using FalloutAssetExplorer.App.Views;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class ProSampleStreamReaderTests
{
    private const int MaximumBytes = Fallout2PrototypeSampleVerifier.MaximumSampleFileSizeBytes;

    [Fact]
    public async Task ReadBoundedAsync_AcceptsExactlyOneMiBAndVerifiesCompleteBytes()
    {
        var source = CreateValidSample(MaximumBytes);
        await using var stream = new SyntheticReadStream(source, maximumReadSize: 8191);

        var accepted = await ProEditorView.ReadBoundedAsync(stream, MaximumBytes);
        var verification = Fallout2PrototypeSampleVerifier.Verify("sample.PRO", accepted);

        Assert.Equal(MaximumBytes, accepted.Length);
        Assert.Equal(source, accepted);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(source)), verification.Sha256);
        Assert.Equal(Fallout2PrototypeSampleStructureStatus.PlausibleHeader, verification.StructureStatus);
    }

    [Fact]
    public async Task ReadBoundedAsync_RejectsOneMiBPlusOneAndConsumesOnlyOverflowSentinel()
    {
        var source = new byte[MaximumBytes + 8192];
        await using var stream = new SyntheticReadStream(source);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => ProEditorView.ReadBoundedAsync(stream, MaximumBytes));

        Assert.Equal(MaximumBytes + 1, stream.BytesRead);
    }

    [Fact]
    public async Task ReadBoundedAsync_HandlesRepeatedShortReadsWithoutLosingBytes()
    {
        var source = Enumerable.Range(0, 4097)
            .Select(value => (byte)(value % 256))
            .ToArray();
        await using var stream = new SyntheticReadStream(source, maximumReadSize: 13);

        var accepted = await ProEditorView.ReadBoundedAsync(stream, MaximumBytes);

        Assert.Equal(source, accepted);
        Assert.Equal(source.Length, stream.BytesRead);
    }

    [Fact]
    public async Task ReadBoundedAsync_RejectsDataThatGrowsAfterInitialEndOfStream()
    {
        var initialData = new byte[MaximumBytes];
        await using var stream = new SyntheticReadStream(
            initialData,
            maximumReadSize: 4093,
            appendOneByteAtEnd: true);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => ProEditorView.ReadBoundedAsync(stream, MaximumBytes));

        Assert.True(stream.GrowthByteRead);
        Assert.Equal(MaximumBytes + 1, stream.BytesRead);
    }

    [Fact]
    public async Task ReadBoundedAsync_DoesNotReturnPartialSampleWhenStreamThrows()
    {
        var source = new byte[4096];
        await using var stream = new SyntheticReadStream(
            source,
            maximumReadSize: 257,
            throwAfterBytes: 1024);

        await Assert.ThrowsAsync<IOException>(
            () => ProEditorView.ReadBoundedAsync(stream, MaximumBytes));

        Assert.Equal(1024, stream.BytesRead);
    }

    private static byte[] CreateValidSample(int length)
    {
        var bytes = new byte[length];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0, sizeof(uint)), 0x00000002);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(0x20, sizeof(int)), 0);
        return bytes;
    }

    private sealed class SyntheticReadStream : Stream
    {
        private readonly byte[] _initialData;
        private readonly int _maximumReadSize;
        private readonly int? _throwAfterBytes;
        private readonly bool _appendOneByteAtEnd;
        private long _position;
        private bool _growthByteReturned;

        public SyntheticReadStream(
            byte[] initialData,
            int maximumReadSize = int.MaxValue,
            int? throwAfterBytes = null,
            bool appendOneByteAtEnd = false)
        {
            _initialData = initialData;
            _maximumReadSize = maximumReadSize;
            _throwAfterBytes = throwAfterBytes;
            _appendOneByteAtEnd = appendOneByteAtEnd;
        }

        public int BytesRead { get; private set; }

        public bool GrowthByteRead => _growthByteReturned;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _initialData.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_throwAfterBytes is int throwAfter && _position >= throwAfter)
            {
                throw new IOException("Synthetic stream read failure.");
            }

            if (_position >= _initialData.Length)
            {
                if (_appendOneByteAtEnd && !_growthByteReturned)
                {
                    buffer.Span[0] = 0xA5;
                    _growthByteReturned = true;
                    _position++;
                    BytesRead++;
                    return ValueTask.FromResult(1);
                }

                return ValueTask.FromResult(0);
            }

            var available = _initialData.Length - (int)_position;
            var count = Math.Min(
                Math.Min(buffer.Length, available),
                _maximumReadSize);
            if (_throwAfterBytes is int threshold)
            {
                count = Math.Min(count, threshold - (int)_position);
            }

            _initialData.AsMemory((int)_position, count).CopyTo(buffer);
            _position += count;
            BytesRead += count;
            return ValueTask.FromResult(count);
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
