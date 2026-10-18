using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class FalloutMsgTextPatcherTests
{
    [Theory]
    [InlineData(0, "first edit")]
    [InlineData(1, "middle edit")]
    [InlineData(2, "last edit")]
    public void Patch_ReplacesSelectedOccurrenceAndPreservesAllOtherSourceBytes(
        int occurrenceIndex,
        string replacementText)
    {
        byte[] source = "{5}{}{one}\r\n{5}{snd}{two}\t{7}{}{three} #tail"u8.ToArray();
        var parseResult = FalloutMsgParser.Parse(source);
        var originalSource = parseResult.SourceSnapshot!.ToArray();
        var originalTexts = parseResult.Entries.Select(entry => entry.Text.ToArray()).ToArray();
        byte[] replacement = System.Text.Encoding.ASCII.GetBytes(replacementText);
        var targetRange = parseResult.Entries[occurrenceIndex].TextRange;

        var result = FalloutMsgTextPatcher.Patch(parseResult, occurrenceIndex, replacement);

        var patchedBytes = AssertSuccess(result);
        var suffixOffset = targetRange.StartOffset + targetRange.Length;
        Assert.Equal(source[..targetRange.StartOffset], patchedBytes[..targetRange.StartOffset]);
        Assert.Equal(
            source[suffixOffset..],
            patchedBytes[(targetRange.StartOffset + replacement.Length)..]);
        Assert.Equal(originalSource, parseResult.SourceSnapshot.ToArray());
        Assert.Equal(
            originalTexts,
            parseResult.Entries.Select(entry => entry.Text.ToArray()).ToArray());

        var reparsed = FalloutMsgParser.Parse(patchedBytes);
        Assert.True(reparsed.IsSuccess);
        Assert.Equal(replacement, reparsed.Entries[occurrenceIndex].Text.ToArray());
    }

    [Fact]
    public void Patch_UsesOccurrenceIndexToTargetDuplicateIds()
    {
        var parseResult = FalloutMsgParser.Parse("{5}{}{first}{5}{}{second}"u8.ToArray());

        var result = FalloutMsgTextPatcher.Patch(parseResult, 1, "updated"u8);

        var patchedBytes = AssertSuccess(result);
        var reparsed = FalloutMsgParser.Parse(patchedBytes);
        Assert.True(reparsed.IsSuccess);
        Assert.Equal(new[] { 5, 5 }, reparsed.Entries.Select(entry => entry.Id));
        Assert.Equal("first"u8.ToArray(), reparsed.Entries[0].Text.ToArray());
        Assert.Equal("updated"u8.ToArray(), reparsed.Entries[1].Text.ToArray());
    }

    [Fact]
    public void Patch_AcceptsEmptyReplacementAndEmptyOriginalText()
    {
        var nonEmpty = FalloutMsgParser.Parse("{1}{}{text}"u8.ToArray());
        var emptiedResult = FalloutMsgTextPatcher.Patch(nonEmpty, 0, ReadOnlySpan<byte>.Empty);
        var emptiedBytes = AssertSuccess(emptiedResult);
        Assert.Equal("{1}{}{}"u8.ToArray(), emptiedBytes);
        Assert.Empty(Assert.Single(FalloutMsgParser.Parse(emptiedBytes).Entries).Text.ToArray());

        var empty = FalloutMsgParser.Parse("{1}{}{}"u8.ToArray());
        var filledResult = FalloutMsgTextPatcher.Patch(empty, 0, "filled"u8);
        var filledBytes = AssertSuccess(filledResult);
        Assert.Equal("filled"u8.ToArray(), Assert.Single(FalloutMsgParser.Parse(filledBytes).Entries).Text.ToArray());
    }

    [Fact]
    public void Patch_PreservesReplacementBytesAboveAsciiWithoutDecoding()
    {
        var parseResult = FalloutMsgParser.Parse("{1}{}{old}"u8.ToArray());
        byte[] replacement = [0x80, 0xFF];

        var result = FalloutMsgTextPatcher.Patch(parseResult, 0, replacement);

        var patchedBytes = AssertSuccess(result);
        var reparsed = FalloutMsgParser.Parse(patchedBytes);
        Assert.True(reparsed.IsSuccess);
        Assert.Equal(replacement, Assert.Single(reparsed.Entries).Text.ToArray());
    }

    [Fact]
    public void Patch_RejectsClosingDelimiterInReplacement()
    {
        var parseResult = FalloutMsgParser.Parse("{1}{}{old}"u8.ToArray());

        var result = FalloutMsgTextPatcher.Patch(parseResult, 0, "bad}text"u8);

        AssertFailure(result, FalloutMsgTextPatchFailureCode.ClosingDelimiterInReplacement);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void Patch_RejectsOccurrenceIndexOutsideEntryList(int occurrenceIndex)
    {
        var parseResult = FalloutMsgParser.Parse("{1}{}{text}"u8.ToArray());

        var result = FalloutMsgTextPatcher.Patch(parseResult, occurrenceIndex, "new"u8);

        AssertFailure(result, FalloutMsgTextPatchFailureCode.OccurrenceIndexOutOfRange);
    }

    [Fact]
    public void Patch_RejectsUnsuccessfulParseAndMissingSourceSnapshot()
    {
        var failedParse = FalloutMsgParser.Parse("{1}{}{unterminated"u8.ToArray());
        AssertFailure(
            FalloutMsgTextPatcher.Patch(failedParse, 0, "new"u8),
            FalloutMsgTextPatchFailureCode.ParseFailed);

        var successfulParse = FalloutMsgParser.Parse("{1}{}{text}"u8.ToArray());
        var missingSnapshot = new FalloutMsgParseResult(
            isSuccess: true,
            successfulParse.Entries,
            sourceSnapshot: null,
            diagnostic: null);

        AssertFailure(
            FalloutMsgTextPatcher.Patch(missingSnapshot, 0, "new"u8),
            FalloutMsgTextPatchFailureCode.MissingSourceSnapshot);
    }

    [Fact]
    public void Patch_RejectsNegativeAndOutOfBoundsFieldRanges()
    {
        var parseResult = FalloutMsgParser.Parse("{1}{}{text}"u8.ToArray());
        var entry = Assert.Single(parseResult.Entries);

        var negativeRangeResult = FalloutMsgTextPatcher.Patch(
            WithEntry(parseResult, entry with { TextRange = new FalloutMsgByteRange(-1, 2) }),
            0,
            "new"u8);
        AssertFailure(negativeRangeResult, FalloutMsgTextPatchFailureCode.InvalidFieldRange);

        var outOfBoundsResult = FalloutMsgTextPatcher.Patch(
            WithEntry(
                parseResult,
                entry with
                {
                    TextRange = new FalloutMsgByteRange(parseResult.SourceSnapshot!.Length, 0)
                }),
            0,
            "new"u8);
        AssertFailure(outOfBoundsResult, FalloutMsgTextPatchFailureCode.InvalidFieldRange);
    }

    [Fact]
    public void Patch_RejectsOverlappingAndInconsistentFieldRanges()
    {
        var parseResult = FalloutMsgParser.Parse("{1}{}{text}"u8.ToArray());
        var entry = Assert.Single(parseResult.Entries);

        var overlappingResult = FalloutMsgTextPatcher.Patch(
            WithEntry(parseResult, entry with { TextRange = entry.IdRange }),
            0,
            "new"u8);
        AssertFailure(overlappingResult, FalloutMsgTextPatchFailureCode.OverlappingFieldRanges);

        var inconsistentResult = FalloutMsgTextPatcher.Patch(
            WithEntry(parseResult, entry with { Text = "changed"u8.ToArray() }),
            0,
            "new"u8);
        AssertFailure(inconsistentResult, FalloutMsgTextPatchFailureCode.InconsistentFieldData);
    }

    [Fact]
    public void Patch_RejectsReplacementBeyondCurrentParserFieldLimit()
    {
        var parseResult = FalloutMsgParser.Parse("{1}{}{}"u8.ToArray());
        var replacement = new byte[FalloutMsgParser.MaximumFieldSizeBytes];

        var result = FalloutMsgTextPatcher.Patch(parseResult, 0, replacement);

        AssertFailure(result, FalloutMsgTextPatchFailureCode.ReplacementFieldTooLong);
    }

    private static FalloutMsgParseResult WithEntry(
        FalloutMsgParseResult source,
        FalloutMsgEntry entry) =>
        new(
            isSuccess: true,
            Array.AsReadOnly([entry]),
            source.SourceSnapshot,
            diagnostic: null);

    private static byte[] AssertSuccess(FalloutMsgTextPatchResult result)
    {
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Null(result.FailureCode);
        Assert.Null(result.ErrorMessage);
        return Assert.IsType<byte[]>(result.PatchedBytes);
    }

    private static void AssertFailure(
        FalloutMsgTextPatchResult result,
        FalloutMsgTextPatchFailureCode expectedFailureCode)
    {
        Assert.False(result.IsSuccess);
        Assert.Null(result.PatchedBytes);
        Assert.Equal(expectedFailureCode, result.FailureCode);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }
}
