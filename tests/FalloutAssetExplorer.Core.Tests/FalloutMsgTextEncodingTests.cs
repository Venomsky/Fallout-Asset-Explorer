using System.Text;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class FalloutMsgTextEncodingTests
{
    [Fact]
    public void Decode_UsesExplicitEncodingForNonAsciiText()
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        byte[] bytes = "Crème brûlée — café"u8.ToArray();

        var result = FalloutMsgTextEncoding.Decode(bytes, encoding);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("Crème brûlée — café", result.Value);
    }

    [Fact]
    public void Encode_UsesExplicitEncodingForNonAsciiText()
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        var result = FalloutMsgTextEncoding.Encode("Crème brûlée — café", encoding);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("Crème brûlée — café"u8.ToArray(), result.Value);
    }

    [Fact]
    public void Decode_RejectsMalformedBytesInsteadOfUsingReplacementFallback()
    {
        var encoding = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: false);
        byte[] malformed = [0xC3, 0x28];

        var result = FalloutMsgTextEncoding.Decode(malformed, encoding);

        AssertFailure(result, FalloutMsgTextEncodingFailureCode.InvalidByteSequence);
        Assert.IsType<DecoderReplacementFallback>(encoding.DecoderFallback);
    }

    [Fact]
    public void Encode_RejectsUnrepresentableCharactersInsteadOfUsingReplacementFallback()
    {
        var encoding = Encoding.ASCII;

        var result = FalloutMsgTextEncoding.Encode("café", encoding);

        AssertFailure(result, FalloutMsgTextEncodingFailureCode.UnrepresentableCharacter);
        Assert.IsType<EncoderReplacementFallback>(encoding.EncoderFallback);
    }

    [Fact]
    public void DecodeAndEncode_PreserveWhitespaceAndLineEndings()
    {
        const string text = " \tFirst\r\nSecond\nLast \t";
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        var decoded = FalloutMsgTextEncoding.Decode(Encoding.UTF8.GetBytes(text), encoding);
        var encoded = FalloutMsgTextEncoding.Encode(decoded.Value, encoding);

        Assert.True(decoded.IsSuccess, decoded.ErrorMessage);
        Assert.True(encoded.IsSuccess, encoded.ErrorMessage);
        Assert.Equal(text, decoded.Value);
        Assert.Equal(Encoding.UTF8.GetBytes(text), encoded.Value);
    }

    [Fact]
    public void DecodeAndEncode_AcceptEmptyBytesAndText()
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        var decoded = FalloutMsgTextEncoding.Decode(ReadOnlyMemory<byte>.Empty, encoding);
        var encoded = FalloutMsgTextEncoding.Encode(string.Empty, encoding);

        Assert.True(decoded.IsSuccess, decoded.ErrorMessage);
        Assert.Equal(string.Empty, decoded.Value);
        Assert.True(encoded.IsSuccess, encoded.ErrorMessage);
        Assert.Empty(encoded.Value!);
    }

    [Fact]
    public void DecodeAndEncode_DoNotChangeSourceSnapshot()
    {
        byte[] source = "{1}{}{Crème\r\ncafé}"u8.ToArray();
        var parseResult = FalloutMsgParser.Parse(source);
        var originalSnapshot = parseResult.SourceSnapshot!.ToArray();
        var textBytes = Assert.Single(parseResult.Entries).Text;
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        var decoded = FalloutMsgTextEncoding.Decode(textBytes, encoding);
        var encoded = FalloutMsgTextEncoding.Encode(decoded.Value, encoding);

        Assert.True(decoded.IsSuccess, decoded.ErrorMessage);
        Assert.True(encoded.IsSuccess, encoded.ErrorMessage);
        Assert.Equal(originalSnapshot, parseResult.SourceSnapshot.ToArray());
        Assert.Equal(source, parseResult.SourceSnapshot.ToArray());
    }

    [Fact]
    public void EncodingOperations_RejectMissingOrUnsupportedConfiguration()
    {
        AssertFailure(
            FalloutMsgTextEncoding.Decode("text"u8.ToArray(), null),
            FalloutMsgTextEncodingFailureCode.MissingEncoding);
        AssertFailure(
            FalloutMsgTextEncoding.Encode("text", null),
            FalloutMsgTextEncodingFailureCode.MissingEncoding);
        AssertFailure(
            FalloutMsgTextEncoding.Encode(null, Encoding.UTF8),
            FalloutMsgTextEncodingFailureCode.MissingText);
        AssertFailure(
            FalloutMsgTextEncoding.Decode("text"u8.ToArray(), new UnsupportedEncoding()),
            FalloutMsgTextEncodingFailureCode.UnsupportedEncoding);
    }

    private static void AssertFailure<T>(
        FalloutMsgTextEncodingResult<T> result,
        FalloutMsgTextEncodingFailureCode expectedFailureCode)
    {
        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(expectedFailureCode, result.FailureCode);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    private sealed class UnsupportedEncoding : Encoding
    {
        public override object Clone() => throw new NotSupportedException("Synthetic encoding.");

        public override int GetByteCount(char[] chars, int index, int count) =>
            throw new NotSupportedException();

        public override int GetBytes(
            char[] chars,
            int charIndex,
            int charCount,
            byte[] bytes,
            int byteIndex) =>
            throw new NotSupportedException();

        public override int GetCharCount(byte[] bytes, int index, int count) =>
            throw new NotSupportedException();

        public override int GetChars(
            byte[] bytes,
            int byteIndex,
            int byteCount,
            char[] chars,
            int charIndex) =>
            throw new NotSupportedException();

        public override int GetMaxByteCount(int charCount) =>
            throw new NotSupportedException();

        public override int GetMaxCharCount(int byteCount) =>
            throw new NotSupportedException();
    }
}
