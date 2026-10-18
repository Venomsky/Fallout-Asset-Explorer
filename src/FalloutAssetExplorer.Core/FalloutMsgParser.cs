namespace FalloutAssetExplorer.Core;

public static class FalloutMsgParser
{
    public const int MaximumFileSizeBytes = 16 * 1024 * 1024;
    public const int MaximumFieldSizeBytes = 1024;
    public const int MaximumEntryCount = 100_000;

    public static FalloutMsgParseResult Parse(ReadOnlyMemory<byte> data)
    {
        if (data.Length > MaximumFileSizeBytes)
        {
            return FalloutMsgParseResult.Failure(
                new FalloutMsgDiagnostic(
                    FalloutMsgDiagnosticCode.FileTooLarge,
                    0,
                    $"The MSG file exceeds the {MaximumFileSizeBytes}-byte size limit."));
        }

        var sourceSnapshot = new FalloutMsgSourceSnapshot(data.Span);
        var bytes = sourceSnapshot.Span;
        var entries = new List<FalloutMsgEntry>();
        var offset = 0;

        while (true)
        {
            var fieldStatus = ReadField(
                bytes,
                ref offset,
                allowEndOfInput: true,
                out var idFieldStart,
                out var idFieldLength,
                out var diagnostic);

            if (fieldStatus == FieldReadStatus.EndOfInput)
            {
                return FalloutMsgParseResult.Success(entries, sourceSnapshot);
            }

            if (fieldStatus == FieldReadStatus.Failure)
            {
                return FalloutMsgParseResult.Failure(diagnostic!);
            }

            if (entries.Count >= MaximumEntryCount)
            {
                return FalloutMsgParseResult.Failure(
                    new FalloutMsgDiagnostic(
                        FalloutMsgDiagnosticCode.TooManyEntries,
                        idFieldStart,
                        $"The MSG file exceeds the {MaximumEntryCount}-entry limit."));
            }

            var idField = bytes.Slice(idFieldStart, idFieldLength);
            if (idField.IsEmpty)
            {
                return FalloutMsgParseResult.Failure(
                    new FalloutMsgDiagnostic(
                        FalloutMsgDiagnosticCode.EmptyMessageId,
                        idFieldStart,
                        "A MSG message ID field is empty."));
            }

            if (!TryParseMessageId(
                    idField,
                    out var messageId,
                    out var isOutOfRange,
                    out var idErrorOffset))
            {
                var code = isOutOfRange
                    ? FalloutMsgDiagnosticCode.MessageIdOutOfRange
                    : FalloutMsgDiagnosticCode.InvalidMessageId;
                var message = isOutOfRange
                    ? "A MSG message ID is outside the signed 32-bit range."
                    : "A MSG message ID is not a signed decimal integer.";
                return FalloutMsgParseResult.Failure(
                    new FalloutMsgDiagnostic(code, idFieldStart + idErrorOffset, message));
            }

            fieldStatus = ReadField(
                bytes,
                ref offset,
                allowEndOfInput: false,
                out var audioFieldStart,
                out var audioFieldLength,
                out diagnostic);
            if (fieldStatus == FieldReadStatus.Failure)
            {
                return FalloutMsgParseResult.Failure(diagnostic!);
            }

            fieldStatus = ReadField(
                bytes,
                ref offset,
                allowEndOfInput: false,
                out var textFieldStart,
                out var textFieldLength,
                out diagnostic);
            if (fieldStatus == FieldReadStatus.Failure)
            {
                return FalloutMsgParseResult.Failure(diagnostic!);
            }

            entries.Add(new FalloutMsgEntry(
                messageId,
                CopyField(bytes.Slice(audioFieldStart, audioFieldLength)),
                CopyField(bytes.Slice(textFieldStart, textFieldLength)),
                new FalloutMsgByteRange(idFieldStart, idFieldLength),
                new FalloutMsgByteRange(audioFieldStart, audioFieldLength),
                new FalloutMsgByteRange(textFieldStart, textFieldLength)));
        }
    }

    private static FieldReadStatus ReadField(
        ReadOnlySpan<byte> data,
        ref int offset,
        bool allowEndOfInput,
        out int fieldStart,
        out int fieldLength,
        out FalloutMsgDiagnostic? diagnostic)
    {
        while (offset < data.Length && data[offset] != (byte)'{')
        {
            if (data[offset] == (byte)'}')
            {
                fieldStart = 0;
                fieldLength = 0;
                diagnostic = new FalloutMsgDiagnostic(
                    FalloutMsgDiagnosticCode.MismatchedDelimiter,
                    offset,
                    "A closing MSG delimiter has no matching opening delimiter.");
                return FieldReadStatus.Failure;
            }

            offset++;
        }

        if (offset == data.Length)
        {
            fieldStart = 0;
            fieldLength = 0;
            if (allowEndOfInput)
            {
                diagnostic = null;
                return FieldReadStatus.EndOfInput;
            }

            diagnostic = new FalloutMsgDiagnostic(
                FalloutMsgDiagnosticCode.IncompleteEntry,
                offset,
                "The MSG file ended before all fields in an entry were present.");
            return FieldReadStatus.Failure;
        }

        offset++;
        fieldStart = offset;
        var fieldByteCount = 0;
        while (offset < data.Length && data[offset] != (byte)'}')
        {
            if (data[offset] != (byte)'\n')
            {
                if (fieldByteCount >= MaximumFieldSizeBytes - 1)
                {
                    fieldLength = 0;
                    diagnostic = new FalloutMsgDiagnostic(
                        FalloutMsgDiagnosticCode.FieldTooLong,
                        offset,
                        $"A MSG field exceeds the {MaximumFieldSizeBytes - 1}-byte non-LF limit.");
                    return FieldReadStatus.Failure;
                }

                fieldByteCount++;
            }

            offset++;
        }

        if (offset == data.Length)
        {
            fieldLength = 0;
            diagnostic = new FalloutMsgDiagnostic(
                FalloutMsgDiagnosticCode.UnterminatedField,
                offset,
                "A MSG field has no closing delimiter before end of file.");
            return FieldReadStatus.Failure;
        }

        fieldLength = offset - fieldStart;
        offset++;
        diagnostic = null;
        return FieldReadStatus.Success;
    }

    private static bool TryParseMessageId(
        ReadOnlySpan<byte> field,
        out int messageId,
        out bool isOutOfRange,
        out int errorOffset)
    {
        var index = 0;
        var isNegative = false;
        if (field[index] is (byte)'+' or (byte)'-')
        {
            isNegative = field[index] == (byte)'-';
            index++;
        }

        if (index == field.Length)
        {
            messageId = 0;
            isOutOfRange = false;
            errorOffset = index;
            return false;
        }

        var limit = isNegative ? 2_147_483_648L : int.MaxValue;
        long magnitude = 0;
        for (; index < field.Length; index++)
        {
            var current = field[index];
            if (current is < (byte)'0' or > (byte)'9')
            {
                messageId = 0;
                isOutOfRange = false;
                errorOffset = index;
                return false;
            }

            var digit = current - (byte)'0';
            if (magnitude > (limit - digit) / 10)
            {
                messageId = 0;
                isOutOfRange = true;
                errorOffset = 0;
                return false;
            }

            magnitude = magnitude * 10 + digit;
        }

        messageId = isNegative
            ? magnitude == 2_147_483_648L ? int.MinValue : -(int)magnitude
            : (int)magnitude;
        isOutOfRange = false;
        errorOffset = 0;
        return true;
    }

    internal static bool MessageIdMatches(ReadOnlySpan<byte> field, int expectedMessageId) =>
        !field.IsEmpty
        && TryParseMessageId(field, out var messageId, out _, out _)
        && messageId == expectedMessageId;

    private static ReadOnlyMemory<byte> CopyField(ReadOnlySpan<byte> field) =>
        field.IsEmpty ? ReadOnlyMemory<byte>.Empty : field.ToArray();

    private enum FieldReadStatus
    {
        Success,
        EndOfInput,
        Failure
    }
}

public sealed record FalloutMsgEntry(
    int Id,
    ReadOnlyMemory<byte> Audio,
    ReadOnlyMemory<byte> Text,
    FalloutMsgByteRange IdRange,
    FalloutMsgByteRange AudioRange,
    FalloutMsgByteRange TextRange);

/// <summary>
/// A zero-based byte range for field content, excluding its opening and closing braces.
/// </summary>
public readonly record struct FalloutMsgByteRange(int StartOffset, int Length);

public sealed class FalloutMsgSourceSnapshot
{
    private readonly byte[] _bytes;

    internal FalloutMsgSourceSnapshot(ReadOnlySpan<byte> bytes)
    {
        _bytes = bytes.ToArray();
    }

    public int Length => _bytes.Length;

    public byte this[int index] => _bytes[index];

    /// <summary>
    /// Returns a copy of the retained source bytes.
    /// </summary>
    public byte[] ToArray() => _bytes.ToArray();

    internal ReadOnlySpan<byte> Span => _bytes;
}

public enum FalloutMsgDiagnosticCode
{
    FileTooLarge,
    TooManyEntries,
    MismatchedDelimiter,
    UnterminatedField,
    IncompleteEntry,
    FieldTooLong,
    EmptyMessageId,
    InvalidMessageId,
    MessageIdOutOfRange
}

public sealed record FalloutMsgDiagnostic(
    FalloutMsgDiagnosticCode Code,
    int ByteOffset,
    string Message);

public sealed class FalloutMsgParseResult
{
    internal FalloutMsgParseResult(
        bool isSuccess,
        IReadOnlyList<FalloutMsgEntry> entries,
        FalloutMsgSourceSnapshot? sourceSnapshot,
        FalloutMsgDiagnostic? diagnostic)
    {
        IsSuccess = isSuccess;
        Entries = entries;
        SourceSnapshot = sourceSnapshot;
        Diagnostic = diagnostic;
    }

    public bool IsSuccess { get; }

    public IReadOnlyList<FalloutMsgEntry> Entries { get; }

    public FalloutMsgSourceSnapshot? SourceSnapshot { get; }

    public FalloutMsgDiagnostic? Diagnostic { get; }

    internal static FalloutMsgParseResult Success(
        List<FalloutMsgEntry> entries,
        FalloutMsgSourceSnapshot sourceSnapshot) =>
        new(true, Array.AsReadOnly(entries.ToArray()), sourceSnapshot, null);

    internal static FalloutMsgParseResult Failure(FalloutMsgDiagnostic diagnostic) =>
        new(false, Array.Empty<FalloutMsgEntry>(), null, diagnostic);
}
