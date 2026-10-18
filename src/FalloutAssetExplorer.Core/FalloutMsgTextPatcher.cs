namespace FalloutAssetExplorer.Core;

public static class FalloutMsgTextPatcher
{
    public static FalloutMsgTextPatchResult Patch(
        FalloutMsgParseResult? parseResult,
        int sourceOccurrenceIndex,
        ReadOnlySpan<byte> replacementText)
    {
        var validationError = ValidateSourceOccurrence(
            parseResult,
            sourceOccurrenceIndex,
            out var target,
            out var sourceSnapshot);
        if (validationError is not null)
        {
            return validationError;
        }

        var retainedSource = sourceSnapshot!;
        if (replacementText.IndexOf((byte)'}') >= 0)
        {
            return FalloutMsgTextPatchResult.Failure(
                FalloutMsgTextPatchFailureCode.ClosingDelimiterInReplacement,
                "Replacement text cannot contain the closing MSG field delimiter '}'.");
        }

        var nonLineFeedByteCount = 0;
        foreach (var value in replacementText)
        {
            if (value != (byte)'\n'
                && ++nonLineFeedByteCount >= FalloutMsgParser.MaximumFieldSizeBytes)
            {
                return FalloutMsgTextPatchResult.Failure(
                    FalloutMsgTextPatchFailureCode.ReplacementFieldTooLong,
                    $"Replacement text exceeds the {FalloutMsgParser.MaximumFieldSizeBytes - 1}-byte non-LF field limit.");
            }
        }

        var targetRange = target!.TextRange;
        var patchedLength = (long)retainedSource.Length
            - targetRange.Length
            + replacementText.Length;
        if (patchedLength > FalloutMsgParser.MaximumFileSizeBytes)
        {
            return FalloutMsgTextPatchResult.Failure(
                FalloutMsgTextPatchFailureCode.PatchedFileTooLarge,
                $"Patched MSG data exceeds the {FalloutMsgParser.MaximumFileSizeBytes}-byte size limit.");
        }

        var sourceBytes = retainedSource.Span;
        var patchedBytes = new byte[(int)patchedLength];
        var textEndOffset = targetRange.StartOffset + targetRange.Length;
        sourceBytes[..targetRange.StartOffset].CopyTo(patchedBytes);
        replacementText.CopyTo(patchedBytes.AsSpan(targetRange.StartOffset));
        sourceBytes[textEndOffset..].CopyTo(
            patchedBytes.AsSpan(targetRange.StartOffset + replacementText.Length));

        return FalloutMsgTextPatchResult.Success(patchedBytes);
    }

    internal static FalloutMsgTextPatchResult? ValidateSourceOccurrence(
        FalloutMsgParseResult? parseResult,
        int sourceOccurrenceIndex,
        out FalloutMsgEntry? target,
        out FalloutMsgSourceSnapshot? sourceSnapshot)
    {
        target = null;
        sourceSnapshot = null;

        if (parseResult is null)
        {
            return FalloutMsgTextPatchResult.Failure(
                FalloutMsgTextPatchFailureCode.InvalidParseResult,
                "A parsed MSG result is required.");
        }

        if (!parseResult.IsSuccess)
        {
            return FalloutMsgTextPatchResult.Failure(
                FalloutMsgTextPatchFailureCode.ParseFailed,
                "Text can only be patched from a successful MSG parse.");
        }

        if (parseResult.SourceSnapshot is not { } retainedSource)
        {
            return FalloutMsgTextPatchResult.Failure(
                FalloutMsgTextPatchFailureCode.MissingSourceSnapshot,
                "The successful MSG parse has no source snapshot.");
        }

        if ((uint)sourceOccurrenceIndex >= (uint)parseResult.Entries.Count)
        {
            return FalloutMsgTextPatchResult.Failure(
                FalloutMsgTextPatchFailureCode.OccurrenceIndexOutOfRange,
                $"MSG occurrence index {sourceOccurrenceIndex} is outside the parsed entry list.");
        }

        var validationError = ValidateEntries(parseResult.Entries, retainedSource);
        if (validationError is not null)
        {
            return validationError;
        }

        target = parseResult.Entries[sourceOccurrenceIndex];
        sourceSnapshot = retainedSource;
        return null;
    }

    private static FalloutMsgTextPatchResult? ValidateEntries(
        IReadOnlyList<FalloutMsgEntry> entries,
        FalloutMsgSourceSnapshot sourceSnapshot)
    {
        var sourceBytes = sourceSnapshot.Span;
        var previousFieldEndOffset = 0;

        foreach (var entry in entries)
        {
            if (!TryValidateRange(
                    entry.IdRange,
                    sourceBytes,
                    ref previousFieldEndOffset,
                    out var idBytes,
                    out var rangeError)
                || !TryValidateRange(
                    entry.AudioRange,
                    sourceBytes,
                    ref previousFieldEndOffset,
                    out var audioBytes,
                    out rangeError)
                || !TryValidateRange(
                    entry.TextRange,
                    sourceBytes,
                    ref previousFieldEndOffset,
                    out var textBytes,
                    out rangeError))
            {
                return rangeError;
            }

            if (!FalloutMsgParser.MessageIdMatches(idBytes, entry.Id)
                || !audioBytes.SequenceEqual(entry.Audio.Span)
                || !textBytes.SequenceEqual(entry.Text.Span))
            {
                return FalloutMsgTextPatchResult.Failure(
                    FalloutMsgTextPatchFailureCode.InconsistentFieldData,
                    "Parsed field values do not match their ranges in the source snapshot.");
            }
        }

        return null;
    }

    private static bool TryValidateRange(
        FalloutMsgByteRange range,
        ReadOnlySpan<byte> sourceBytes,
        ref int previousFieldEndOffset,
        out ReadOnlySpan<byte> fieldBytes,
        out FalloutMsgTextPatchResult? error)
    {
        fieldBytes = default;
        error = null;

        var endOffset = (long)range.StartOffset + range.Length;
        if (range.StartOffset <= 0
            || range.Length < 0
            || endOffset >= sourceBytes.Length)
        {
            error = FalloutMsgTextPatchResult.Failure(
                FalloutMsgTextPatchFailureCode.InvalidFieldRange,
                "A MSG field range is negative or outside the source snapshot.");
            return false;
        }

        var openingDelimiterOffset = range.StartOffset - 1;
        var closingDelimiterOffset = (int)endOffset;
        if (openingDelimiterOffset < previousFieldEndOffset)
        {
            error = FalloutMsgTextPatchResult.Failure(
                FalloutMsgTextPatchFailureCode.OverlappingFieldRanges,
                "MSG field ranges overlap or are not in source order.");
            return false;
        }

        if (sourceBytes[openingDelimiterOffset] != (byte)'{'
            || sourceBytes[closingDelimiterOffset] != (byte)'}')
        {
            error = FalloutMsgTextPatchResult.Failure(
                FalloutMsgTextPatchFailureCode.InvalidFieldRange,
                "A MSG field range is not enclosed by its source delimiters.");
            return false;
        }

        fieldBytes = sourceBytes.Slice(range.StartOffset, range.Length);
        previousFieldEndOffset = closingDelimiterOffset + 1;
        return true;
    }
}

public enum FalloutMsgTextPatchFailureCode
{
    InvalidParseResult,
    ParseFailed,
    MissingSourceSnapshot,
    OccurrenceIndexOutOfRange,
    InvalidFieldRange,
    OverlappingFieldRanges,
    InconsistentFieldData,
    ClosingDelimiterInReplacement,
    ReplacementFieldTooLong,
    PatchedFileTooLarge
}

public sealed class FalloutMsgTextPatchResult
{
    private FalloutMsgTextPatchResult(
        bool isSuccess,
        byte[]? patchedBytes,
        FalloutMsgTextPatchFailureCode? failureCode,
        string? errorMessage)
    {
        IsSuccess = isSuccess;
        PatchedBytes = patchedBytes;
        FailureCode = failureCode;
        ErrorMessage = errorMessage;
    }

    public bool IsSuccess { get; }

    public byte[]? PatchedBytes { get; }

    public FalloutMsgTextPatchFailureCode? FailureCode { get; }

    public string? ErrorMessage { get; }

    internal static FalloutMsgTextPatchResult Success(byte[] patchedBytes) =>
        new(true, patchedBytes, null, null);

    internal static FalloutMsgTextPatchResult Failure(
        FalloutMsgTextPatchFailureCode failureCode,
        string errorMessage) =>
        new(false, null, failureCode, errorMessage);
}
