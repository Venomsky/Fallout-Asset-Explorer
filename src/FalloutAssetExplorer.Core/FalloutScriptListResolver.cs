using System.Text;

namespace FalloutAssetExplorer.Core;

public sealed record FalloutScriptListResolution(
    bool Success,
    int Slot,
    string? ScriptName,
    string? IntPath,
    string? MessagePath,
    FalloutScriptListDiagnostic? Diagnostic);

public enum FalloutScriptListDiagnosticCode
{
    InvalidSlot,
    UnsupportedArchive,
    ScriptListMissing,
    ScriptListAmbiguous,
    ScriptListTooLarge,
    ScriptListLengthMismatch,
    ScriptListReadFailed,
    SlotOutOfRange,
    InvalidScriptName
}

public sealed record FalloutScriptListDiagnostic(
    FalloutScriptListDiagnosticCode Code,
    string Message);

public static class FalloutScriptListResolver
{
    public const string ScriptListPath = "scripts/scripts.lst";

    private const int MaximumListSize = 1024 * 1024;
    private const int ScriptListReadBufferSize = 260;
    private const int Fallout1EffectiveScriptNameLength = 15;
    private const int Fallout2MaximumScriptNameLength = 13;

    /// <summary>
    /// Maps one explicit slot to a safe name and candidate paths. Success is a local mapping only;
    /// it does not validate every Fallout 2 list entry or guarantee that CE accepts the whole list.
    /// </summary>
    public static async Task<FalloutScriptListResolution> ResolveAsync(
        IGameArchive archive,
        int slot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archive);

        if (slot < 0)
        {
            return Failure(
                slot,
                FalloutScriptListDiagnosticCode.InvalidSlot,
                "The script-list slot cannot be negative.");
        }

        if (!((archive.Game == FalloutGame.Fallout1 && archive.Format == ArchiveFormat.Dat1)
            || (archive.Game == FalloutGame.Fallout2 && archive.Format == ArchiveFormat.Dat2)))
        {
            return Failure(
                slot,
                FalloutScriptListDiagnosticCode.UnsupportedArchive,
                "A matching Fallout 1 DAT1 or Fallout 2 DAT2 archive is required.");
        }

        var entriesByPath = new Dictionary<string, ArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        var duplicatePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.BrowserEntries)
        {
            var normalizedPath = NormalizeArchivePath(entry.Path);
            if (!entriesByPath.TryAdd(normalizedPath, entry))
            {
                duplicatePaths.Add(normalizedPath);
            }
        }

        var normalizedListPath = NormalizeArchivePath(ScriptListPath);
        if (duplicatePaths.Contains(normalizedListPath))
        {
            return Failure(
                slot,
                FalloutScriptListDiagnosticCode.ScriptListAmbiguous,
                $"The archive contains multiple entries matching '{ScriptListPath}'.");
        }

        if (!entriesByPath.TryGetValue(normalizedListPath, out var listEntry))
        {
            return Failure(
                slot,
                FalloutScriptListDiagnosticCode.ScriptListMissing,
                $"The archive entry '{ScriptListPath}' is missing.");
        }

        if (listEntry.UncompressedSize is < 0 or > MaximumListSize)
        {
            return Failure(
                slot,
                FalloutScriptListDiagnosticCode.ScriptListTooLarge,
                $"The script list exceeds the {MaximumListSize}-byte size limit.");
        }

        byte[] listBytes;
        try
        {
            await using var output = new MemoryStream(checked((int)listEntry.UncompressedSize));
            await using var boundedOutput = new SizeLimitedWriteStream(output, MaximumListSize);
            await archive.CopyEntryToAsync(listEntry, boundedOutput, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (output.Length != listEntry.UncompressedSize)
            {
                return Failure(
                    slot,
                    FalloutScriptListDiagnosticCode.ScriptListLengthMismatch,
                    $"The script list declares {listEntry.UncompressedSize} bytes but {output.Length} bytes were read.");
            }

            listBytes = output.ToArray();
        }
        catch (ListSizeLimitExceededException)
        {
            return Failure(
                slot,
                FalloutScriptListDiagnosticCode.ScriptListTooLarge,
                $"The script list exceeds the {MaximumListSize}-byte size limit.");
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return Failure(
                slot,
                FalloutScriptListDiagnosticCode.ScriptListReadFailed,
                $"The script list could not be read: {exception.Message}");
        }

        if (!TryGetSlot(listBytes, slot, out var line))
        {
            return Failure(
                slot,
                FalloutScriptListDiagnosticCode.SlotOutOfRange,
                $"Script-list slot {slot} is outside the list.");
        }

        if (!TryGetScriptName(archive.Game, line, out var scriptName))
        {
            return Failure(
                slot,
                FalloutScriptListDiagnosticCode.InvalidScriptName,
                $"Script-list slot {slot} does not contain a safe script name for {archive.Game}.");
        }

        return new FalloutScriptListResolution(
            true,
            slot,
            scriptName,
            $"scripts/{scriptName}.int",
            $"dialog/{scriptName}.msg",
            null);
    }

    private static FalloutScriptListResolution Failure(
        int slot,
        FalloutScriptListDiagnosticCode code,
        string message) =>
        new(false, slot, null, null, null, new FalloutScriptListDiagnostic(code, message));

    private static string NormalizeArchivePath(string path) =>
        path.Replace('\\', '/');

    private static bool TryGetSlot(
        byte[] listBytes,
        int requestedSlot,
        out byte[] line)
    {
        var offset = 0;
        var slot = 0;
        var chunkLength = 0;
        var chunk = new byte[ScriptListReadBufferSize - 1];

        // Match CE's 259-character reads; CRLF is one newline in its text-mode readers.
        while (offset < listBytes.Length)
        {
            var value = listBytes[offset++];
            if (value == (byte)'\r' && offset < listBytes.Length && listBytes[offset] == (byte)'\n')
            {
                value = listBytes[offset++];
            }

            chunk[chunkLength++] = value;
            if (value == (byte)'\n' || chunkLength == chunk.Length)
            {
                if (slot == requestedSlot)
                {
                    line = chunk[..chunkLength];
                    return true;
                }

                slot++;
                chunkLength = 0;
            }
        }

        if (chunkLength > 0 && slot == requestedSlot)
        {
            line = chunk[..chunkLength];
            return true;
        }

        line = [];
        return false;
    }

    private static bool TryGetScriptName(
        FalloutGame game,
        byte[] lineBytes,
        out string? scriptName)
    {
        var line = lineBytes.AsSpan();
        var delimiter = game switch
        {
            FalloutGame.Fallout1 => line.IndexOf((byte)'.'),
            FalloutGame.Fallout2 => line.IndexOf(".int"u8),
            _ => -1
        };
        var maximumLength = game switch
        {
            FalloutGame.Fallout1 => Fallout1EffectiveScriptNameLength,
            FalloutGame.Fallout2 => Fallout2MaximumScriptNameLength,
            _ => 0
        };

        if (delimiter <= 0 || (game == FalloutGame.Fallout2 && delimiter > maximumLength))
        {
            scriptName = null;
            return false;
        }

        var effectiveNameLength = game == FalloutGame.Fallout1
            ? Math.Min(delimiter, maximumLength)
            : delimiter;
        var name = line[..effectiveNameLength];
        if (name[^1] == (byte)'.')
        {
            scriptName = null;
            return false;
        }

        foreach (var value in name)
        {
            if (!IsSafeScriptNameByte(value, game))
            {
                scriptName = null;
                return false;
            }
        }

        scriptName = Encoding.ASCII.GetString(name);
        return true;
    }

    private static bool IsSafeScriptNameByte(byte value, FalloutGame game)
    {
        if (value is >= (byte)'A' and <= (byte)'Z'
            or >= (byte)'a' and <= (byte)'z'
            or >= (byte)'0' and <= (byte)'9'
            or (byte)'_'
            or (byte)'-')
        {
            return true;
        }

        return game == FalloutGame.Fallout2 && value == (byte)'.';
    }

    private sealed class SizeLimitedWriteStream(Stream inner, long maximumLength) : Stream
    {
        private void EnsureWithinLimit(int writeLength)
        {
            if (writeLength < 0 || inner.Length > maximumLength - writeLength)
            {
                throw new ListSizeLimitExceededException();
            }
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureWithinLimit(count);
            inner.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureWithinLimit(buffer.Length);
            inner.Write(buffer);
        }

        public override void WriteByte(byte value)
        {
            EnsureWithinLimit(1);
            inner.WriteByte(value);
        }

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            EnsureWithinLimit(count);
            return inner.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            EnsureWithinLimit(buffer.Length);
            return inner.WriteAsync(buffer, cancellationToken);
        }
    }

    private sealed class ListSizeLimitExceededException : Exception
    {
    }
}
