using System.Text;

namespace FalloutAssetExplorer.Core;

public static class FalloutMsgTextEditWorkflow
{
    public static FalloutMsgTextEditSessionResult BeginEdit(
        FalloutMsgParseResult? parseResult,
        int sourceOccurrenceIndex,
        Encoding? selectedEncoding)
    {
        var validationFailure = FalloutMsgTextPatcher.ValidateSourceOccurrence(
            parseResult,
            sourceOccurrenceIndex,
            out var entry,
            out _);
        if (validationFailure is not null)
        {
            return FalloutMsgTextEditSessionResult.Failed(
                CreateValidationFailure(validationFailure));
        }

        var decodeResult = FalloutMsgTextEncoding.Decode(entry!.Text, selectedEncoding);
        if (!decodeResult.IsSuccess)
        {
            return FalloutMsgTextEditSessionResult.Failed(
                new FalloutMsgTextEditFailure(
                    FalloutMsgTextEditFailureCode.DecodingFailed,
                    decodeResult.ErrorMessage!,
                    EncodingFailureCode: decodeResult.FailureCode));
        }

        var session = new FalloutMsgTextEditSession(
            parseResult!,
            sourceOccurrenceIndex,
            selectedEncoding!,
            entry.Id,
            decodeResult.Value!);
        return FalloutMsgTextEditSessionResult.Success(session);
    }

    private static FalloutMsgTextEditFailure CreateValidationFailure(
        FalloutMsgTextPatchResult patchResult)
    {
        var failureCode = patchResult.FailureCode switch
        {
            FalloutMsgTextPatchFailureCode.InvalidParseResult =>
                FalloutMsgTextEditFailureCode.InvalidParseResult,
            FalloutMsgTextPatchFailureCode.ParseFailed =>
                FalloutMsgTextEditFailureCode.ParseFailed,
            FalloutMsgTextPatchFailureCode.MissingSourceSnapshot =>
                FalloutMsgTextEditFailureCode.MissingSourceSnapshot,
            FalloutMsgTextPatchFailureCode.OccurrenceIndexOutOfRange =>
                FalloutMsgTextEditFailureCode.InvalidOccurrenceIndex,
            _ => FalloutMsgTextEditFailureCode.InvalidSourceData
        };

        return new FalloutMsgTextEditFailure(
            failureCode,
            patchResult.ErrorMessage!,
            PatchFailureCode: patchResult.FailureCode);
    }
}

public sealed class FalloutMsgTextEditSession
{
    private readonly FalloutMsgParseResult _parseResult;
    private readonly Encoding _selectedEncoding;

    internal FalloutMsgTextEditSession(
        FalloutMsgParseResult parseResult,
        int sourceOccurrenceIndex,
        Encoding selectedEncoding,
        int messageId,
        string originalText)
    {
        _parseResult = parseResult;
        SourceOccurrenceIndex = sourceOccurrenceIndex;
        _selectedEncoding = selectedEncoding;
        MessageId = messageId;
        OriginalText = originalText;
    }

    public int SourceOccurrenceIndex { get; }

    public int MessageId { get; }

    public string OriginalText { get; }

    public FalloutMsgTextEditResult Apply(string? editedText)
    {
        var encodeResult = FalloutMsgTextEncoding.Encode(editedText, _selectedEncoding);
        if (!encodeResult.IsSuccess)
        {
            return FalloutMsgTextEditResult.Failed(
                new FalloutMsgTextEditFailure(
                    FalloutMsgTextEditFailureCode.EncodingFailed,
                    encodeResult.ErrorMessage!,
                    EncodingFailureCode: encodeResult.FailureCode));
        }

        var replacementBytes = encodeResult.Value!;
        var patchResult = FalloutMsgTextPatcher.Patch(
            _parseResult,
            SourceOccurrenceIndex,
            replacementBytes);
        if (!patchResult.IsSuccess)
        {
            return FalloutMsgTextEditResult.Failed(
                new FalloutMsgTextEditFailure(
                    FalloutMsgTextEditFailureCode.PatcherFailed,
                    patchResult.ErrorMessage!,
                    PatchFailureCode: patchResult.FailureCode));
        }

        if (patchResult.PatchedBytes is not { } patchedBytes)
        {
            return FalloutMsgTextEditResult.Failed(
                new FalloutMsgTextEditFailure(
                    FalloutMsgTextEditFailureCode.InvalidPatcherResult,
                    "The MSG patcher reported success without producing output bytes."));
        }

        return FalloutMsgTextEditResult.Success(patchedBytes);
    }
}

public enum FalloutMsgTextEditFailureCode
{
    InvalidParseResult,
    ParseFailed,
    MissingSourceSnapshot,
    InvalidOccurrenceIndex,
    InvalidSourceData,
    DecodingFailed,
    EncodingFailed,
    PatcherFailed,
    InvalidPatcherResult
}

public sealed record FalloutMsgTextEditFailure(
    FalloutMsgTextEditFailureCode Code,
    string Message,
    FalloutMsgTextEncodingFailureCode? EncodingFailureCode = null,
    FalloutMsgTextPatchFailureCode? PatchFailureCode = null);

public sealed class FalloutMsgTextEditSessionResult
{
    private FalloutMsgTextEditSessionResult(
        bool isSuccess,
        FalloutMsgTextEditSession? session,
        FalloutMsgTextEditFailure? failure)
    {
        IsSuccess = isSuccess;
        Session = session;
        Failure = failure;
    }

    public bool IsSuccess { get; }

    public FalloutMsgTextEditSession? Session { get; }

    public FalloutMsgTextEditFailure? Failure { get; }

    internal static FalloutMsgTextEditSessionResult Success(
        FalloutMsgTextEditSession session) =>
        new(true, session, null);

    internal static FalloutMsgTextEditSessionResult Failed(
        FalloutMsgTextEditFailure failure) =>
        new(false, null, failure);
}

public sealed class FalloutMsgTextEditResult
{
    private FalloutMsgTextEditResult(
        bool isSuccess,
        byte[]? patchedBytes,
        FalloutMsgTextEditFailure? failure)
    {
        IsSuccess = isSuccess;
        PatchedBytes = patchedBytes;
        Failure = failure;
    }

    public bool IsSuccess { get; }

    public byte[]? PatchedBytes { get; }

    public FalloutMsgTextEditFailure? Failure { get; }

    internal static FalloutMsgTextEditResult Success(byte[] patchedBytes) =>
        new(true, patchedBytes, null);

    internal static FalloutMsgTextEditResult Failed(FalloutMsgTextEditFailure failure) =>
        new(false, null, failure);
}
