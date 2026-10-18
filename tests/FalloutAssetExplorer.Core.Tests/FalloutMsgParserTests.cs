using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class FalloutMsgParserTests
{
    [Fact]
    public void Parse_ReadsMessageIdAudioAndTextBytes()
    {
        var result = FalloutMsgParser.Parse("{12}{voice-name}{message}"u8.ToArray());

        var entry = Assert.Single(result.Entries);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Diagnostic);
        Assert.Equal(12, entry.Id);
        Assert.Equal("voice-name"u8.ToArray(), entry.Audio.ToArray());
        Assert.Equal("message"u8.ToArray(), entry.Text.ToArray());
    }

    [Fact]
    public void Parse_AcceptsEmptyAudioField()
    {
        var result = FalloutMsgParser.Parse("{12}{}{message}"u8.ToArray());

        var entry = Assert.Single(result.Entries);
        Assert.True(result.IsSuccess);
        Assert.Empty(entry.Audio.ToArray());
        Assert.Equal("message"u8.ToArray(), entry.Text.ToArray());
    }

    [Fact]
    public void Parse_PreservesEntryOrder()
    {
        var result = FalloutMsgParser.Parse("{5}{}{first}{2}{}{second}"u8.ToArray());

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { 5, 2 }, result.Entries.Select(entry => entry.Id));
        Assert.Equal("first"u8.ToArray(), result.Entries[0].Text.ToArray());
        Assert.Equal("second"u8.ToArray(), result.Entries[1].Text.ToArray());
    }

    [Fact]
    public void Parse_PreservesDuplicateIdsAsSeparateEntries()
    {
        var result = FalloutMsgParser.Parse("{5}{}{first}{5}{}{second}"u8.ToArray());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Entries.Count);
        Assert.All(result.Entries, entry => Assert.Equal(5, entry.Id));
    }

    [Fact]
    public void Parse_AcceptsEmptyTextField()
    {
        var result = FalloutMsgParser.Parse("{12}{voice-name}{}"u8.ToArray());

        var entry = Assert.Single(result.Entries);
        Assert.True(result.IsSuccess);
        Assert.Empty(entry.Text.ToArray());
    }

    [Fact]
    public void Parse_PreservesBytesAboveAsciiWithoutDecoding()
    {
        byte[] data = [123, 49, 125, 123, 125, 123, 0x80, 0xFF, 125];

        var entry = Assert.Single(FalloutMsgParser.Parse(data).Entries);

        Assert.Equal(new byte[] { 0x80, 0xFF }, entry.Text.ToArray());
    }

    [Fact]
    public void Parse_PreservesCrLfAndLfInText()
    {
        byte[] data =
        [
            (byte)'{', (byte)'1', (byte)'}',
            (byte)'{', (byte)'}',
            (byte)'{', (byte)'A', (byte)'\r', (byte)'\n', (byte)'B', (byte)'\n', (byte)'C', (byte)'}'
        ];

        var entry = Assert.Single(FalloutMsgParser.Parse(data).Entries);

        Assert.Equal(new byte[] { (byte)'A', 13, 10, (byte)'B', 10, (byte)'C' }, entry.Text.ToArray());
    }

    [Fact]
    public void Parse_PreservesLineFeedsWithoutCountingThemAgainstEngineFieldLimit()
    {
        var text = Enumerable.Repeat((byte)'\n', FalloutMsgParser.MaximumFieldSizeBytes).ToArray();
        var result = FalloutMsgParser.Parse(CreateEntry(1, [], text));

        var entry = Assert.Single(result.Entries);

        Assert.True(result.IsSuccess);
        Assert.Equal(text, entry.Text.ToArray());
    }

    [Fact]
    public void Parse_AcceptsExplicitWhitespaceSeparatorsAroundAndBetweenFields()
    {
        var result = FalloutMsgParser.Parse(
            " \t{1} \t{audio}\r\n{text}\n{2}{}{second}\r\n"u8.ToArray());

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { 1, 2 }, result.Entries.Select(entry => entry.Id));
        Assert.Equal("audio"u8.ToArray(), result.Entries[0].Audio.ToArray());
        Assert.Equal("text"u8.ToArray(), result.Entries[0].Text.ToArray());
    }

    [Fact]
    public void Parse_SkipsArbitraryBytesBeforeFirstField()
    {
        byte[] prefix = [0x00, 0x23, 0x80, 0xFF, (byte)'x'];
        var data = prefix.Concat("{1}{}{text}"u8.ToArray()).ToArray();

        var result = FalloutMsgParser.Parse(data);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, Assert.Single(result.Entries).Id);
    }

    [Fact]
    public void Parse_SkipsArbitraryBytesBetweenFieldsAndEntries()
    {
        var data = "{5}"u8.ToArray()
            .Concat(new byte[] { 0x23, 0x80 })
            .Concat("{}"u8.ToArray())
            .Concat(new byte[] { (byte)'x', 0x00 })
            .Concat("{first}"u8.ToArray())
            .Concat(new byte[] { 0xFF, 0x23 })
            .Concat("{5}{}{second}"u8.ToArray())
            .ToArray();

        var result = FalloutMsgParser.Parse(data);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { 5, 5 }, result.Entries.Select(entry => entry.Id));
        Assert.Empty(result.Entries[0].Audio.ToArray());
        Assert.Equal("first"u8.ToArray(), result.Entries[0].Text.ToArray());
        Assert.Equal("second"u8.ToArray(), result.Entries[1].Text.ToArray());
    }

    [Fact]
    public void Parse_IgnoresArbitraryTrailingBytesWhileSeekingNextMessageId()
    {
        var data = "{1}{}{text}"u8.ToArray()
            .Concat(new byte[] { 0x23, 0x80, 0x00, (byte)'x' })
            .ToArray();

        var result = FalloutMsgParser.Parse(data);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, Assert.Single(result.Entries).Id);
    }

    [Fact]
    public void Parse_RejectsClosingDelimiterWhileSeekingNextField()
    {
        var result = FalloutMsgParser.Parse("{1}{}{first}}{2}{}{second}"u8.ToArray());

        AssertFailure(result, FalloutMsgDiagnosticCode.MismatchedDelimiter, 12);
    }

    [Theory]
    [InlineData("{abc}{}{text}", 1)]
    [InlineData("{+}{}{text}", 2)]
    [InlineData("{1x}{}{text}", 2)]
    public void Parse_RejectsNonNumericMessageIdsAtTheInvalidByte(string input, int expectedOffset)
    {
        var result = FalloutMsgParser.Parse(System.Text.Encoding.ASCII.GetBytes(input));

        AssertFailure(result, FalloutMsgDiagnosticCode.InvalidMessageId, expectedOffset);
    }

    [Fact]
    public void Parse_RejectsEmptyMessageId()
    {
        var result = FalloutMsgParser.Parse("{}{}{text}"u8.ToArray());

        AssertFailure(result, FalloutMsgDiagnosticCode.EmptyMessageId, 1);
    }

    [Fact]
    public void Parse_AcceptsSignedInt32MessageIds()
    {
        var result = FalloutMsgParser.Parse(
            "{-2147483648}{}{minimum}{+2147483647}{}{maximum}"u8.ToArray());

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { int.MinValue, int.MaxValue }, result.Entries.Select(entry => entry.Id));
    }

    [Theory]
    [InlineData("{2147483648}{}{text}")]
    [InlineData("{-2147483649}{}{text}")]
    public void Parse_RejectsMessageIdsOutsideSignedInt32Range(string input)
    {
        var result = FalloutMsgParser.Parse(System.Text.Encoding.ASCII.GetBytes(input));

        AssertFailure(result, FalloutMsgDiagnosticCode.MessageIdOutOfRange, 1);
    }

    [Fact]
    public void Parse_RejectsUnmatchedClosingDelimiter()
    {
        var result = FalloutMsgParser.Parse("}{1}{}{text}"u8.ToArray());

        AssertFailure(result, FalloutMsgDiagnosticCode.MismatchedDelimiter, 0);
    }

    [Fact]
    public void Parse_RejectsUnterminatedField()
    {
        var result = FalloutMsgParser.Parse("{1}{}{text"u8.ToArray());

        AssertFailure(result, FalloutMsgDiagnosticCode.UnterminatedField, 10);
    }

    [Fact]
    public void Parse_RejectsIncompleteEntry()
    {
        var result = FalloutMsgParser.Parse("{1}{audio}"u8.ToArray());

        AssertFailure(result, FalloutMsgDiagnosticCode.IncompleteEntry, 10);
    }

    [Fact]
    public void Parse_DoesNotReturnPartialEntriesAfterAnError()
    {
        var result = FalloutMsgParser.Parse("{1}{}{valid}{bad}{}{invalid}"u8.ToArray());

        AssertFailure(result, FalloutMsgDiagnosticCode.InvalidMessageId, 13);
    }

    [Fact]
    public void Parse_AcceptsEmptyFileAsAnEmptySyntacticResult()
    {
        var result = FalloutMsgParser.Parse(ReadOnlyMemory<byte>.Empty);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Entries);
        Assert.Null(result.Diagnostic);
    }

    [Theory]
    [InlineData(FalloutMsgParser.MaximumFieldSizeBytes - 1, true)]
    [InlineData(FalloutMsgParser.MaximumFieldSizeBytes, false)]
    public void Parse_EnforcesTheConfirmedFieldBufferLimit(int textLength, bool succeeds)
    {
        var data = CreateEntry(1, [], new byte[textLength]);

        var result = FalloutMsgParser.Parse(data);

        Assert.Equal(succeeds, result.IsSuccess);
        if (!succeeds)
        {
            AssertFailure(result, FalloutMsgDiagnosticCode.FieldTooLong, 3 + 2 + textLength);
        }
    }

    [Fact]
    public void Parse_RejectsAudioFieldBeyondTheConfirmedBufferLimit()
    {
        var data = CreateEntry(
            1,
            new byte[FalloutMsgParser.MaximumFieldSizeBytes],
            []);

        var result = FalloutMsgParser.Parse(data);

        AssertFailure(result, FalloutMsgDiagnosticCode.FieldTooLong, 3 + 1 + FalloutMsgParser.MaximumFieldSizeBytes - 1);
    }

    [Fact]
    public void Parse_RejectsFileBeyondConfiguredSizeLimit()
    {
        var result = FalloutMsgParser.Parse(new byte[FalloutMsgParser.MaximumFileSizeBytes + 1]);

        AssertFailure(result, FalloutMsgDiagnosticCode.FileTooLarge, 0);
    }

    [Fact]
    public void Parse_RejectsTooManyEntriesWithoutReturningPartialEntries()
    {
        var record = "{0}{}{}"u8;
        var data = new byte[record.Length * (FalloutMsgParser.MaximumEntryCount + 1)];
        for (var offset = 0; offset < data.Length; offset += record.Length)
        {
            record.CopyTo(data.AsSpan(offset));
        }

        var result = FalloutMsgParser.Parse(data);

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Entries);
        Assert.Equal(FalloutMsgDiagnosticCode.TooManyEntries, result.Diagnostic?.Code);
    }

    [Fact]
    public void Parse_CopiesFieldBytesFromInputMemory()
    {
        byte[] data = "{1}{}{text}"u8.ToArray();
        var result = FalloutMsgParser.Parse(data);
        data[^2] = (byte)'X';

        var entry = Assert.Single(result.Entries);

        Assert.Equal("text"u8.ToArray(), entry.Text.ToArray());
    }

    [Fact]
    public void Parse_SourceSnapshotIsIsolatedFromInputArrayMutation()
    {
        byte[] original = "{1}{}{text}"u8.ToArray();
        byte[] input = original.ToArray();

        var result = FalloutMsgParser.Parse(input);
        input[0] = (byte)'X';

        Assert.Equal(original, result.SourceSnapshot!.ToArray());
    }

    [Fact]
    public void Parse_SourceSnapshotDoesNotExposeMutableBackingBytes()
    {
        var result = FalloutMsgParser.Parse("{1}{}{text}"u8.ToArray());
        var snapshot = result.SourceSnapshot!;
        var exposedCopy = snapshot.ToArray();
        exposedCopy[0] = (byte)'X';

        Assert.Equal((byte)'{', snapshot[0]);
        Assert.Equal("{1}{}{text}"u8.ToArray(), snapshot.ToArray());
    }

    [Fact]
    public void Parse_RecordsFieldContentRangesForOrderedDuplicateOccurrences()
    {
        byte[] source = " {5}{}{first} \r\n{5}{voice}{}"u8.ToArray();

        var result = FalloutMsgParser.Parse(source);

        Assert.True(result.IsSuccess);
        Assert.Equal(source, result.SourceSnapshot!.ToArray());
        Assert.Equal(new[] { 5, 5 }, result.Entries.Select(entry => entry.Id));
        Assert.Equal(new FalloutMsgByteRange(2, 1), result.Entries[0].IdRange);
        Assert.Equal(new FalloutMsgByteRange(5, 0), result.Entries[0].AudioRange);
        Assert.Equal(new FalloutMsgByteRange(7, 5), result.Entries[0].TextRange);
        Assert.Equal(new FalloutMsgByteRange(17, 1), result.Entries[1].IdRange);
        Assert.Equal(new FalloutMsgByteRange(20, 5), result.Entries[1].AudioRange);
        Assert.Equal(new FalloutMsgByteRange(27, 0), result.Entries[1].TextRange);

        foreach (var entry in result.Entries)
        {
            Assert.Equal(
                entry.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                System.Text.Encoding.ASCII.GetString(
                    source,
                    entry.IdRange.StartOffset,
                    entry.IdRange.Length));
            Assert.Equal(
                entry.Audio.ToArray(),
                source.AsSpan(entry.AudioRange.StartOffset, entry.AudioRange.Length).ToArray());
            Assert.Equal(
                entry.Text.ToArray(),
                source.AsSpan(entry.TextRange.StartOffset, entry.TextRange.Length).ToArray());

            AssertRangeWithinSource(entry.IdRange, source.Length);
            AssertRangeWithinSource(entry.AudioRange, source.Length);
            AssertRangeWithinSource(entry.TextRange, source.Length);
        }
    }

    private static void AssertFailure(
        FalloutMsgParseResult result,
        FalloutMsgDiagnosticCode expectedCode,
        int expectedOffset)
    {
        Assert.False(result.IsSuccess);
        Assert.Empty(result.Entries);
        Assert.Null(result.SourceSnapshot);
        Assert.NotNull(result.Diagnostic);
        Assert.Equal(expectedCode, result.Diagnostic.Code);
        Assert.Equal(expectedOffset, result.Diagnostic.ByteOffset);
    }

    private static void AssertRangeWithinSource(FalloutMsgByteRange range, int sourceLength)
    {
        Assert.InRange(range.StartOffset, 0, sourceLength);
        Assert.InRange(range.Length, 0, sourceLength - range.StartOffset);
    }

    private static byte[] CreateEntry(int id, byte[] audio, byte[] text)
    {
        var idBytes = System.Text.Encoding.ASCII.GetBytes(id.ToString());
        var result = new byte[idBytes.Length + audio.Length + text.Length + 6];
        var offset = 0;
        result[offset++] = (byte)'{';
        idBytes.CopyTo(result, offset);
        offset += idBytes.Length;
        result[offset++] = (byte)'}';
        result[offset++] = (byte)'{';
        audio.CopyTo(result, offset);
        offset += audio.Length;
        result[offset++] = (byte)'}';
        result[offset++] = (byte)'{';
        text.CopyTo(result, offset);
        offset += text.Length;
        result[offset] = (byte)'}';
        return result;
    }
}
