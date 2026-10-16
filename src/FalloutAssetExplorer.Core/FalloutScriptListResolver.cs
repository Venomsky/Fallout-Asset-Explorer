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

public enum FalloutScriptListSlotStatus
{
    Candidate,
    Empty,
    InvalidName
}

public sealed class FalloutScriptListSlot
{
    internal FalloutScriptListSlot(
        int slot,
        ReadOnlyMemory<byte> rawLineBytes,
        FalloutScriptListSlotStatus status,
        string? scriptName,
        string? intPathCandidate)
    {
        Slot = slot;
        RawLineBytes = rawLineBytes;
        Status = status;
        ScriptName = scriptName;
        IntPathCandidate = intPathCandidate;
    }

    public int Slot { get; }

    public ReadOnlyMemory<byte> RawLineBytes { get; }

    public FalloutScriptListSlotStatus Status { get; }

    public string? ScriptName { get; }

    public string? IntPathCandidate { get; }
}

public sealed class FalloutScriptListReadResult
{
    internal FalloutScriptListReadResult(
        bool success,
        IReadOnlyList<FalloutScriptListSlot> slots,
        FalloutScriptListDiagnostic? diagnostic)
    {
        Success = success;
        Slots = slots;
        Diagnostic = diagnostic;
    }

    public bool Success { get; }

    public IReadOnlyList<FalloutScriptListSlot> Slots { get; }

    public FalloutScriptListDiagnostic? Diagnostic { get; }
}

public enum FalloutScriptListIntTargetStatus
{
    Found,
    Missing,
    Ambiguous,
    Invalid
}

public sealed class FalloutScriptListIntTargetResolution
{
    internal FalloutScriptListIntTargetResolution(
        FalloutScriptListIntTargetStatus status,
        ArchiveEntry? entry,
        int matchCount)
    {
        Status = status;
        Entry = entry;
        MatchCount = matchCount;
    }

    public FalloutScriptListIntTargetStatus Status { get; }

    public ArchiveEntry? Entry { get; }

    public int MatchCount { get; }
}

public static class FalloutScriptListResolver
{
    public const string ScriptListPath = "scripts/scripts.lst";

    private const int MaximumListSize = 1024 * 1024;
    private const int ScriptListReadBufferSize = 260;
    private const int Fallout1EffectiveScriptNameLength = 15;
    private const int Fallout2MaximumScriptNameLength = 13;

    public static async Task<FalloutScriptListReadResult> ReadSlotsAsync(
        IGameArchive archive,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archive);

        var readResult = await ReadScriptListBytesAsync(archive, cancellationToken);
        if (!readResult.Success)
        {
            return ReadFailure(
                readResult.Diagnostic!.Code,
                readResult.Diagnostic.Message);
        }

        var slots = ParseSlots(archive.Game, readResult.Bytes!);
        return new FalloutScriptListReadResult(
            true,
            Array.AsReadOnly(slots.ToArray()),
            null);
    }

    private static async Task<ScriptListBytesReadResult> ReadScriptListBytesAsync(
        IGameArchive archive,
        CancellationToken cancellationToken)
    {
        if (!((archive.Game == FalloutGame.Fallout1 && archive.Format == ArchiveFormat.Dat1)
            || (archive.Game == FalloutGame.Fallout2 && archive.Format == ArchiveFormat.Dat2)))
        {
            return ReadBytesFailure(
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
            return ReadBytesFailure(
                FalloutScriptListDiagnosticCode.ScriptListAmbiguous,
                $"The archive contains multiple entries matching '{ScriptListPath}'.");
        }

        if (!entriesByPath.TryGetValue(normalizedListPath, out var listEntry))
        {
            return ReadBytesFailure(
                FalloutScriptListDiagnosticCode.ScriptListMissing,
                $"The archive entry '{ScriptListPath}' is missing.");
        }

        if (listEntry.UncompressedSize is < 0 or > MaximumListSize)
        {
            return ReadBytesFailure(
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
                return ReadBytesFailure(
                    FalloutScriptListDiagnosticCode.ScriptListLengthMismatch,
                    $"The script list declares {listEntry.UncompressedSize} bytes but {output.Length} bytes were read.");
            }

            listBytes = output.ToArray();
        }
        catch (ListSizeLimitExceededException)
        {
            return ReadBytesFailure(
                FalloutScriptListDiagnosticCode.ScriptListTooLarge,
                $"The script list exceeds the {MaximumListSize}-byte size limit.");
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return ReadBytesFailure(
                FalloutScriptListDiagnosticCode.ScriptListReadFailed,
                $"The script list could not be read: {exception.Message}");
        }

        return new ScriptListBytesReadResult(true, listBytes, null);
    }

    /// <summary>
    /// Maps one explicit slot to candidate paths. Success does not validate that either target
    /// entry exists in the archive.
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

        var readResult = await ReadScriptListBytesAsync(archive, cancellationToken);
        if (!readResult.Success)
        {
            var diagnostic = readResult.Diagnostic!;
            return Failure(slot, diagnostic.Code, diagnostic.Message);
        }

        if (!TryGetSlot(readResult.Bytes!, slot, out var line))
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

    public static FalloutScriptListIntTargetResolution FindIntTarget(
        IGameArchive archive,
        FalloutScriptListSlot? slot)
    {
        ArgumentNullException.ThrowIfNull(archive);

        if (slot is null
            || slot.Status != FalloutScriptListSlotStatus.Candidate
            || slot.IntPathCandidate is null)
        {
            return new FalloutScriptListIntTargetResolution(
                FalloutScriptListIntTargetStatus.Invalid,
                null,
                0);
        }

        var candidatePath = NormalizeArchivePath(slot.IntPathCandidate);
        ArchiveEntry? foundEntry = null;
        var matchCount = 0;
        foreach (var entry in archive.BrowserEntries)
        {
            if (!StringComparer.OrdinalIgnoreCase.Equals(
                    NormalizeArchivePath(entry.Path),
                    candidatePath))
            {
                continue;
            }

            foundEntry = entry;
            matchCount++;
        }

        return matchCount switch
        {
            0 => new FalloutScriptListIntTargetResolution(
                FalloutScriptListIntTargetStatus.Missing,
                null,
                0),
            1 => new FalloutScriptListIntTargetResolution(
                FalloutScriptListIntTargetStatus.Found,
                foundEntry,
                1),
            _ => new FalloutScriptListIntTargetResolution(
                FalloutScriptListIntTargetStatus.Ambiguous,
                null,
                matchCount)
        };
    }

    private static FalloutScriptListResolution Failure(
        int slot,
        FalloutScriptListDiagnosticCode code,
        string message) =>
        new(false, slot, null, null, null, new FalloutScriptListDiagnostic(code, message));

    private static FalloutScriptListReadResult ReadFailure(
        FalloutScriptListDiagnosticCode code,
        string message) =>
        new(false, Array.Empty<FalloutScriptListSlot>(), new FalloutScriptListDiagnostic(code, message));

    private static ScriptListBytesReadResult ReadBytesFailure(
        FalloutScriptListDiagnosticCode code,
        string message) =>
        new(false, null, new FalloutScriptListDiagnostic(code, message));

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

    private static List<FalloutScriptListSlot> ParseSlots(FalloutGame game, byte[] listBytes)
    {
        var lines = ReadSlots(listBytes);
        var slots = new List<FalloutScriptListSlot>(lines.Count);
        foreach (var line in lines)
        {
            var rawLine = new ReadOnlyMemory<byte>(listBytes, line.Offset, line.Length);
            var lineBytes = rawLine.Span;
            var status = FalloutScriptListSlotStatus.InvalidName;
            string? scriptName = null;
            string? intPathCandidate = null;

            if (IsBlank(lineBytes))
            {
                status = FalloutScriptListSlotStatus.Empty;
            }
            else if (TryGetScriptName(game, lineBytes, out scriptName))
            {
                status = FalloutScriptListSlotStatus.Candidate;
                intPathCandidate = $"scripts/{scriptName}.int";
            }

            slots.Add(new FalloutScriptListSlot(
                slots.Count,
                rawLine,
                status,
                scriptName,
                intPathCandidate));
        }

        return slots;
    }

    private static List<ScriptListLine> ReadSlots(byte[] listBytes)
    {
        var lines = new List<ScriptListLine>();
        var offset = 0;
        var lineStart = 0;
        var chunkLength = 0;

        // Match CE's 259-character reads; CRLF is one newline in its text-mode readers.
        while (offset < listBytes.Length)
        {
            var rawByteOffset = offset;
            var value = listBytes[offset++];
            if (value == (byte)'\r' && offset < listBytes.Length && listBytes[offset] == (byte)'\n')
            {
                value = listBytes[offset++];
            }

            chunkLength++;
            if (value == (byte)'\n')
            {
                lines.Add(new ScriptListLine(lineStart, rawByteOffset - lineStart));
                lineStart = offset;
                chunkLength = 0;
            }
            else if (chunkLength == ScriptListReadBufferSize - 1)
            {
                lines.Add(new ScriptListLine(lineStart, offset - lineStart));
                lineStart = offset;
                chunkLength = 0;
            }
        }

        if (chunkLength > 0)
        {
            lines.Add(new ScriptListLine(lineStart, offset - lineStart));
        }

        return lines;
    }

    private static bool IsBlank(ReadOnlySpan<byte> line)
    {
        foreach (var value in line)
        {
            if (value is not ((byte)' ' or (byte)'\t'))
            {
                return false;
            }
        }

        return true;
    }

    private readonly record struct ScriptListLine(int Offset, int Length);

    private sealed record ScriptListBytesReadResult(
        bool Success,
        byte[]? Bytes,
        FalloutScriptListDiagnostic? Diagnostic);

    private static bool TryGetScriptName(
        FalloutGame game,
        ReadOnlySpan<byte> line,
        out string? scriptName)
    {
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
