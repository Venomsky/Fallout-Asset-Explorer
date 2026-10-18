using System.Text;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class FalloutMsgTextEditWorkflowTests
{
    [Fact]
    public void Edit_PatchesOneOccurrenceAndPreservesOtherBytesAndSourceModel()
    {
        byte[] source = "#prefix\r\n{4}{snd}{old}\t{9}{}{unrelated}\r\n"u8.ToArray();
        var parseResult = FalloutMsgParser.Parse(source);
        var originalSnapshot = parseResult.SourceSnapshot!.ToArray();
        var originalEntries = parseResult.Entries.ToArray();
        var sessionResult = FalloutMsgTextEditWorkflow.BeginEdit(
            parseResult,
            0,
            Encoding.ASCII);

        Assert.True(sessionResult.IsSuccess, sessionResult.Failure?.Message);
        var session = sessionResult.Session!;
        Assert.Equal(0, session.SourceOccurrenceIndex);
        Assert.Equal(4, session.MessageId);
        Assert.Equal("old", session.OriginalText);

        var editResult = session.Apply("new text");

        var patchedBytes = AssertEditSuccess(editResult);
        Assert.Equal("#prefix\r\n{4}{snd}{new text}\t{9}{}{unrelated}\r\n"u8.ToArray(), patchedBytes);
        Assert.Equal(originalSnapshot, parseResult.SourceSnapshot.ToArray());
        Assert.Equal(originalEntries, parseResult.Entries);
        var reparsed = FalloutMsgParser.Parse(patchedBytes);
        Assert.True(reparsed.IsSuccess);
        Assert.Equal("new text", Encoding.ASCII.GetString(reparsed.Entries[0].Text.Span));
        Assert.Equal("unrelated", Encoding.ASCII.GetString(reparsed.Entries[1].Text.Span));
    }

    [Fact]
    public void Edit_AddressesDuplicateIdsBySeparateSourceOccurrenceIndexes()
    {
        var parseResult = FalloutMsgParser.Parse("{5}{}{first}{5}{}{second}"u8.ToArray());
        var firstSession = FalloutMsgTextEditWorkflow.BeginEdit(parseResult, 0, Encoding.ASCII).Session!;
        var secondSession = FalloutMsgTextEditWorkflow.BeginEdit(parseResult, 1, Encoding.ASCII).Session!;

        var firstEdit = AssertEditSuccess(firstSession.Apply("first changed"));
        var secondEdit = AssertEditSuccess(secondSession.Apply("second changed"));

        var firstParse = FalloutMsgParser.Parse(firstEdit);
        var secondParse = FalloutMsgParser.Parse(secondEdit);
        Assert.Equal(new[] { 5, 5 }, firstParse.Entries.Select(entry => entry.Id));
        Assert.Equal(new[] { 5, 5 }, secondParse.Entries.Select(entry => entry.Id));
        Assert.Equal("first changed", Encoding.ASCII.GetString(firstParse.Entries[0].Text.Span));
        Assert.Equal("second", Encoding.ASCII.GetString(firstParse.Entries[1].Text.Span));
        Assert.Equal("first", Encoding.ASCII.GetString(secondParse.Entries[0].Text.Span));
        Assert.Equal("second changed", Encoding.ASCII.GetString(secondParse.Entries[1].Text.Span));
    }

    [Fact]
    public void BeginEdit_PropagatesDecodingFailureWithoutCreatingSession()
    {
        byte[] source = [.. "{1}{}{"u8.ToArray(), 0xC3, 0x28, (byte)'}'];
        var parseResult = FalloutMsgParser.Parse(source);

        var result = FalloutMsgTextEditWorkflow.BeginEdit(
            parseResult,
            0,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Assert.Null(result.Session);
        Assert.Equal(FalloutMsgTextEditFailureCode.DecodingFailed, result.Failure?.Code);
        Assert.Equal(
            FalloutMsgTextEncodingFailureCode.InvalidByteSequence,
            result.Failure?.EncodingFailureCode);
    }

    [Fact]
    public void Apply_PropagatesEncodingFailureWithoutReturningPatchedBytes()
    {
        var parseResult = FalloutMsgParser.Parse("{1}{}{plain}"u8.ToArray());
        var session = FalloutMsgTextEditWorkflow.BeginEdit(parseResult, 0, Encoding.ASCII).Session!;

        var result = session.Apply("café");

        AssertEditFailure(result, FalloutMsgTextEditFailureCode.EncodingFailed);
        Assert.Equal(
            FalloutMsgTextEncodingFailureCode.UnrepresentableCharacter,
            result.Failure?.EncodingFailureCode);
    }

    [Fact]
    public void Apply_PropagatesPatcherFieldLimitFailureWithoutPartialOutput()
    {
        var parseResult = FalloutMsgParser.Parse("{1}{}{short}"u8.ToArray());
        var session = FalloutMsgTextEditWorkflow.BeginEdit(parseResult, 0, Encoding.ASCII).Session!;

        var result = session.Apply(new string('x', FalloutMsgParser.MaximumFieldSizeBytes));

        AssertEditFailure(result, FalloutMsgTextEditFailureCode.PatcherFailed);
        Assert.Equal(
            FalloutMsgTextPatchFailureCode.ReplacementFieldTooLong,
            result.Failure?.PatchFailureCode);
    }

    [Fact]
    public void Apply_PropagatesPatcherFileSizeFailureWithoutPartialOutput()
    {
        var source = CreateMaximumSizeMessage();
        var parseResult = FalloutMsgParser.Parse(source);
        Assert.True(parseResult.IsSuccess);
        var session = FalloutMsgTextEditWorkflow.BeginEdit(parseResult, 0, Encoding.ASCII).Session!;

        var result = session.Apply(session.OriginalText + "\n");

        AssertEditFailure(result, FalloutMsgTextEditFailureCode.PatcherFailed);
        Assert.Equal(
            FalloutMsgTextPatchFailureCode.PatchedFileTooLarge,
            result.Failure?.PatchFailureCode);
    }

    [Fact]
    public void Edit_PreservesEmptyAndUnchangedText()
    {
        var emptySource = "{1}{}{}"u8.ToArray();
        var emptyParse = FalloutMsgParser.Parse(emptySource);
        var emptySession = FalloutMsgTextEditWorkflow.BeginEdit(emptyParse, 0, Encoding.ASCII).Session!;

        Assert.Equal(string.Empty, emptySession.OriginalText);
        Assert.Equal(emptySource, AssertEditSuccess(emptySession.Apply(string.Empty)));
        Assert.Equal("{1}{}{same}"u8.ToArray(),
            AssertEditSuccess(
                FalloutMsgTextEditWorkflow
                    .BeginEdit(
                        FalloutMsgParser.Parse("{1}{}{same}"u8.ToArray()),
                        0,
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
                    .Session!
                    .Apply("same")));
    }

    [Fact]
    public void BeginEdit_RejectsInvalidIndexMissingSnapshotAndInvalidSourceData()
    {
        var parseResult = FalloutMsgParser.Parse("{1}{}{text}"u8.ToArray());

        AssertSessionFailure(
            FalloutMsgTextEditWorkflow.BeginEdit(parseResult, -1, Encoding.ASCII),
            FalloutMsgTextEditFailureCode.InvalidOccurrenceIndex);
        AssertSessionFailure(
            FalloutMsgTextEditWorkflow.BeginEdit(parseResult, 1, Encoding.ASCII),
            FalloutMsgTextEditFailureCode.InvalidOccurrenceIndex);
        AssertSessionFailure(
            FalloutMsgTextEditWorkflow.BeginEdit(null, 0, Encoding.ASCII),
            FalloutMsgTextEditFailureCode.InvalidParseResult);
        AssertSessionFailure(
            FalloutMsgTextEditWorkflow.BeginEdit(
                FalloutMsgParser.Parse("{1}{}{broken"u8.ToArray()),
                0,
                Encoding.ASCII),
            FalloutMsgTextEditFailureCode.ParseFailed);

        var missingSnapshot = new FalloutMsgParseResult(
            isSuccess: true,
            parseResult.Entries,
            sourceSnapshot: null,
            diagnostic: null);
        AssertSessionFailure(
            FalloutMsgTextEditWorkflow.BeginEdit(missingSnapshot, 0, Encoding.ASCII),
            FalloutMsgTextEditFailureCode.MissingSourceSnapshot);

        var invalidEntry = parseResult.Entries[0] with
        {
            TextRange = new FalloutMsgByteRange(-1, 1)
        };
        var invalidSource = new FalloutMsgParseResult(
            isSuccess: true,
            Array.AsReadOnly(new[] { invalidEntry }),
            parseResult.SourceSnapshot,
            diagnostic: null);
        var invalidSourceResult = FalloutMsgTextEditWorkflow.BeginEdit(
            invalidSource,
            0,
            Encoding.ASCII);
        AssertSessionFailure(
            invalidSourceResult,
            FalloutMsgTextEditFailureCode.InvalidSourceData);
        Assert.Equal(
            FalloutMsgTextPatchFailureCode.InvalidFieldRange,
            invalidSourceResult.Failure?.PatchFailureCode);
    }

    private static byte[] CreateMaximumSizeMessage()
    {
        byte[] source = new byte[FalloutMsgParser.MaximumFileSizeBytes];
        var prefix = "{1}{}{"u8;
        prefix.CopyTo(source);
        source.AsSpan(prefix.Length, source.Length - prefix.Length - 1).Fill((byte)'\n');
        source[^1] = (byte)'}';
        return source;
    }

    private static byte[] AssertEditSuccess(FalloutMsgTextEditResult result)
    {
        Assert.True(result.IsSuccess, result.Failure?.Message);
        Assert.Null(result.Failure);
        return Assert.IsType<byte[]>(result.PatchedBytes);
    }

    private static void AssertEditFailure(
        FalloutMsgTextEditResult result,
        FalloutMsgTextEditFailureCode expectedFailureCode)
    {
        Assert.False(result.IsSuccess);
        Assert.Null(result.PatchedBytes);
        Assert.Equal(expectedFailureCode, result.Failure?.Code);
        Assert.False(string.IsNullOrWhiteSpace(result.Failure?.Message));
    }

    private static void AssertSessionFailure(
        FalloutMsgTextEditSessionResult result,
        FalloutMsgTextEditFailureCode expectedFailureCode)
    {
        Assert.False(result.IsSuccess);
        Assert.Null(result.Session);
        Assert.Equal(expectedFailureCode, result.Failure?.Code);
        Assert.False(string.IsNullOrWhiteSpace(result.Failure?.Message));
    }
}
