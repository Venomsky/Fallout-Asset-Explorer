using System.Text;

namespace FalloutAssetExplorer.Core;

public static class FalloutMsgTextEncoding
{
    public static FalloutMsgTextEncodingResult<string> Decode(
        ReadOnlyMemory<byte> bytes,
        Encoding? selectedEncoding)
    {
        if (!TryCreateStrictEncoding(selectedEncoding, out var encoding, out var failure))
        {
            return FalloutMsgTextEncodingResult<string>.Failure(failure!);
        }

        try
        {
            return FalloutMsgTextEncodingResult<string>.Success(
                encoding.GetString(bytes.Span));
        }
        catch (DecoderFallbackException exception)
        {
            return FalloutMsgTextEncodingResult<string>.Failure(
                FalloutMsgTextEncodingFailureCode.InvalidByteSequence,
                $"The selected encoding cannot decode the MSG text bytes: {exception.Message}");
        }
        catch (Exception exception) when (IsUnsupportedEncodingException(exception))
        {
            return FalloutMsgTextEncodingResult<string>.Failure(
                FalloutMsgTextEncodingFailureCode.UnsupportedEncoding,
                $"The selected encoding cannot decode MSG text: {exception.Message}");
        }
    }

    public static FalloutMsgTextEncodingResult<byte[]> Encode(
        string? text,
        Encoding? selectedEncoding)
    {
        if (text is null)
        {
            return FalloutMsgTextEncodingResult<byte[]>.Failure(
                FalloutMsgTextEncodingFailureCode.MissingText,
                "MSG text is required.");
        }

        if (!TryCreateStrictEncoding(selectedEncoding, out var encoding, out var failure))
        {
            return FalloutMsgTextEncodingResult<byte[]>.Failure(failure!);
        }

        try
        {
            return FalloutMsgTextEncodingResult<byte[]>.Success(
                encoding.GetBytes(text));
        }
        catch (EncoderFallbackException exception)
        {
            return FalloutMsgTextEncodingResult<byte[]>.Failure(
                FalloutMsgTextEncodingFailureCode.UnrepresentableCharacter,
                $"The selected encoding cannot represent all MSG text characters: {exception.Message}");
        }
        catch (Exception exception) when (IsUnsupportedEncodingException(exception))
        {
            return FalloutMsgTextEncodingResult<byte[]>.Failure(
                FalloutMsgTextEncodingFailureCode.UnsupportedEncoding,
                $"The selected encoding cannot encode MSG text: {exception.Message}");
        }
    }

    private static bool TryCreateStrictEncoding(
        Encoding? selectedEncoding,
        out Encoding encoding,
        out FalloutMsgTextEncodingFailure? failure)
    {
        encoding = null!;
        failure = null;

        if (selectedEncoding is null)
        {
            failure = new FalloutMsgTextEncodingFailure(
                FalloutMsgTextEncodingFailureCode.MissingEncoding,
                "An explicit MSG text encoding is required.");
            return false;
        }

        try
        {
            if (selectedEncoding.Clone() is not Encoding strictEncoding)
            {
                failure = new FalloutMsgTextEncodingFailure(
                    FalloutMsgTextEncodingFailureCode.UnsupportedEncoding,
                    "The selected MSG text encoding could not be cloned for strict fallback handling.");
                return false;
            }

            strictEncoding.DecoderFallback = DecoderFallback.ExceptionFallback;
            strictEncoding.EncoderFallback = EncoderFallback.ExceptionFallback;
            if (strictEncoding.DecoderFallback is not DecoderExceptionFallback
                || strictEncoding.EncoderFallback is not EncoderExceptionFallback)
            {
                failure = new FalloutMsgTextEncodingFailure(
                    FalloutMsgTextEncodingFailureCode.UnsupportedEncoding,
                    "The selected MSG text encoding does not support exception fallbacks.");
                return false;
            }

            encoding = strictEncoding;
            return true;
        }
        catch (Exception exception) when (IsUnsupportedEncodingException(exception))
        {
            failure = new FalloutMsgTextEncodingFailure(
                FalloutMsgTextEncodingFailureCode.UnsupportedEncoding,
                $"The selected MSG text encoding cannot be configured for strict fallbacks: {exception.Message}");
            return false;
        }
    }

    private static bool IsUnsupportedEncodingException(Exception exception) =>
        exception is ArgumentException
            or InvalidOperationException
            or NotSupportedException;
}

public enum FalloutMsgTextEncodingFailureCode
{
    MissingEncoding,
    MissingText,
    InvalidByteSequence,
    UnrepresentableCharacter,
    UnsupportedEncoding
}

public sealed class FalloutMsgTextEncodingResult<T>
{
    private FalloutMsgTextEncodingResult(
        bool isSuccess,
        T? value,
        FalloutMsgTextEncodingFailureCode? failureCode,
        string? errorMessage)
    {
        IsSuccess = isSuccess;
        Value = value;
        FailureCode = failureCode;
        ErrorMessage = errorMessage;
    }

    public bool IsSuccess { get; }

    public T? Value { get; }

    public FalloutMsgTextEncodingFailureCode? FailureCode { get; }

    public string? ErrorMessage { get; }

    internal static FalloutMsgTextEncodingResult<T> Success(T value) =>
        new(true, value, null, null);

    internal static FalloutMsgTextEncodingResult<T> Failure(
        FalloutMsgTextEncodingFailure failure) =>
        new(false, default, failure.Code, failure.Message);

    internal static FalloutMsgTextEncodingResult<T> Failure(
        FalloutMsgTextEncodingFailureCode failureCode,
        string errorMessage) =>
        new(false, default, failureCode, errorMessage);
}

internal sealed record FalloutMsgTextEncodingFailure(
    FalloutMsgTextEncodingFailureCode Code,
    string Message);
