using System.Text;
using FalloutAssetExplorer.App;
using FalloutAssetExplorer.App.ViewModels;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class MsgSaveAsRoundTripTests
{
    [Fact]
    public async Task RoundTrip_SavesEditedDuplicateOccurrenceWithNonAsciiTextAndPreservesOtherBytes()
    {
        byte[] source =
            "#prefix\r\n{5}{sound-one}{first text}\t{5}{sound-two}{old}\r\n{9}{sound-three}{last}"u8.ToArray();
        var parseResult = FalloutMsgParser.Parse(source);
        Assert.True(parseResult.IsSuccess, parseResult.Diagnostic?.Message);
        var originalSnapshot = parseResult.SourceSnapshot!.ToArray();
        var encoding = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);
        const int selectedOccurrenceIndex = 1;
        const string replacementText = "Cr\u00e8me br\u00fbl\u00e9e \u2014 caf\u00e9";

        var beginResult = FalloutMsgTextEditWorkflow.BeginEdit(
            parseResult,
            selectedOccurrenceIndex,
            encoding);
        Assert.True(beginResult.IsSuccess, beginResult.Failure?.Message);
        Assert.Equal("old", beginResult.Session!.OriginalText);

        var editResult = beginResult.Session.Apply(replacementText);
        Assert.True(editResult.IsSuccess, editResult.Failure?.Message);
        var patchedBytes = editResult.PatchedBytes!;
        byte[] expectedBytes =
            "#prefix\r\n{5}{sound-one}{first text}\t{5}{sound-two}{Cr\u00e8me br\u00fbl\u00e9e \u2014 caf\u00e9}\r\n{9}{sound-three}{last}"u8.ToArray();
        Assert.Equal(expectedBytes, patchedBytes);

        var targetRange = parseResult.Entries[selectedOccurrenceIndex].TextRange;
        var replacementBytes = encoding.GetBytes(replacementText);
        Assert.Equal(source[..targetRange.StartOffset], patchedBytes[..targetRange.StartOffset]);
        var sourceSuffixStart = targetRange.StartOffset + targetRange.Length;
        var patchedSuffixStart = targetRange.StartOffset + replacementBytes.Length;
        Assert.Equal(source[sourceSuffixStart..], patchedBytes[patchedSuffixStart..]);

        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, "round-trip.msg");
        var savedPath = await MsgSaveAsService.SaveAsync(
            patchedBytes,
            destination,
            originalSourcePath: null,
            overwriteConfirmed: false);

        Assert.Equal(Path.GetFullPath(destination), savedPath);
        var savedBytes = await File.ReadAllBytesAsync(destination);
        Assert.Equal(expectedBytes, savedBytes);

        var reparsed = FalloutMsgParser.Parse(savedBytes);
        Assert.True(reparsed.IsSuccess, reparsed.Diagnostic?.Message);
        Assert.Equal(new[] { 5, 5, 9 }, reparsed.Entries.Select(entry => entry.Id));
        Assert.Equal("sound-one"u8.ToArray(), reparsed.Entries[0].Audio.ToArray());
        Assert.Equal("sound-two"u8.ToArray(), reparsed.Entries[1].Audio.ToArray());
        Assert.Equal("sound-three"u8.ToArray(), reparsed.Entries[2].Audio.ToArray());
        Assert.Equal("first text"u8.ToArray(), reparsed.Entries[0].Text.ToArray());
        Assert.Equal("last"u8.ToArray(), reparsed.Entries[2].Text.ToArray());
        var decodedText = FalloutMsgTextEncoding.Decode(
            reparsed.Entries[selectedOccurrenceIndex].Text,
            encoding);
        Assert.True(decodedText.IsSuccess, decodedText.ErrorMessage);
        Assert.Equal(replacementText, decodedText.Value);
        Assert.Equal(originalSnapshot, parseResult.SourceSnapshot.ToArray());
        Assert.Equal(source, parseResult.SourceSnapshot.ToArray());
    }

    [Fact]
    public async Task RoundTrip_SavesEmptyTextAndRetainsTheOtherOccurrence()
    {
        byte[] source = "{2}{sound}{erase me}{2}{}{ }"u8.ToArray();
        var parseResult = FalloutMsgParser.Parse(source);
        Assert.True(parseResult.IsSuccess, parseResult.Diagnostic?.Message);
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        var beginResult = FalloutMsgTextEditWorkflow.BeginEdit(parseResult, 0, encoding);
        Assert.True(beginResult.IsSuccess, beginResult.Failure?.Message);
        var editResult = beginResult.Session!.Apply(string.Empty);
        Assert.True(editResult.IsSuccess, editResult.Failure?.Message);
        byte[] expectedBytes = "{2}{sound}{}{2}{}{ }"u8.ToArray();
        Assert.Equal(expectedBytes, editResult.PatchedBytes);

        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, "empty-text.msg");
        await MsgSaveAsService.SaveAsync(
            editResult.PatchedBytes,
            destination,
            originalSourcePath: null,
            overwriteConfirmed: false);

        var savedBytes = await File.ReadAllBytesAsync(destination);
        Assert.Equal(expectedBytes, savedBytes);
        var reparsed = FalloutMsgParser.Parse(savedBytes);
        Assert.True(reparsed.IsSuccess, reparsed.Diagnostic?.Message);
        Assert.Equal(new[] { 2, 2 }, reparsed.Entries.Select(entry => entry.Id));
        Assert.Equal("sound"u8.ToArray(), reparsed.Entries[0].Audio.ToArray());
        Assert.Empty(reparsed.Entries[0].Text.ToArray());
        Assert.Equal(" "u8.ToArray(), reparsed.Entries[1].Text.ToArray());
        var decodedEmptyText = FalloutMsgTextEncoding.Decode(
            reparsed.Entries[0].Text,
            encoding);
        Assert.True(decodedEmptyText.IsSuccess, decodedEmptyText.ErrorMessage);
        Assert.Equal(string.Empty, decodedEmptyText.Value);
    }

    [Fact]
    public async Task RoundTrip_SavesCumulativeEditsAndContinuesFromSavedOutput()
    {
        byte[] source =
            "#prefix\r\n{5}{audio-a}{alpha}{5}{audio-b}{beta}{9}{audio-c}{gamma}"u8.ToArray();
        var viewModel = new MsgInspectorViewModel();
        viewModel.SetParseResult(FalloutMsgParser.Parse(source), "source.msg");

        ApplyOccurrence(viewModel, 0, "first edit");
        byte[] afterFirstEdit =
            "#prefix\r\n{5}{audio-a}{first edit}{5}{audio-b}{beta}{9}{audio-c}{gamma}"u8.ToArray();

        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, "cumulative.msg");
        var firstSavedPath = await MsgSaveAsService.SaveAsync(
            viewModel.PatchedOutputBytes,
            destination,
            "source.msg",
            overwriteConfirmed: false);
        Assert.Equal(Path.GetFullPath(destination), firstSavedPath);
        viewModel.MarkOutputSaved(firstSavedPath!);
        Assert.Equal(afterFirstEdit, await File.ReadAllBytesAsync(destination));
        Assert.False(viewModel.HasUnsavedOutput);

        ApplyOccurrence(viewModel, 1, "second edit with a different length");
        byte[] expected =
            "#prefix\r\n{5}{audio-a}{first edit}{5}{audio-b}{second edit with a different length}{9}{audio-c}{gamma}"u8.ToArray();

        Assert.Equal(expected, viewModel.PatchedOutputBytes);
        Assert.True(viewModel.HasUnsavedOutput);
        Assert.Equal(source, viewModel.ParseResult!.SourceSnapshot!.ToArray());

        var secondSavedPath = await MsgSaveAsService.SaveAsync(
            viewModel.PatchedOutputBytes,
            destination,
            "source.msg",
            overwriteConfirmed: true);
        Assert.Equal(Path.GetFullPath(destination), secondSavedPath);
        viewModel.MarkOutputSaved(secondSavedPath!);

        Assert.Equal(expected, await File.ReadAllBytesAsync(destination));
        var reparsed = FalloutMsgParser.Parse(await File.ReadAllBytesAsync(destination));
        Assert.True(reparsed.IsSuccess, reparsed.Diagnostic?.Message);
        Assert.Equal(new[] { 5, 5, 9 }, reparsed.Entries.Select(entry => entry.Id));
        Assert.Equal("first edit"u8.ToArray(), reparsed.Entries[0].Text.ToArray());
        Assert.Equal(
            "second edit with a different length"u8.ToArray(),
            reparsed.Entries[1].Text.ToArray());
        Assert.Equal("gamma"u8.ToArray(), reparsed.Entries[2].Text.ToArray());
        Assert.Equal(source, viewModel.ParseResult.SourceSnapshot.ToArray());
    }

    [Fact]
    public async Task MalformedSavedBytes_FailReparseAndCannotBeginAnEditSession()
    {
        byte[] malformedBytes = "{5}{sound}{unterminated"u8.ToArray();
        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, "malformed.msg");

        var savedPath = await MsgSaveAsService.SaveAsync(
            malformedBytes,
            destination,
            originalSourcePath: null,
            overwriteConfirmed: false);

        Assert.Equal(Path.GetFullPath(destination), savedPath);
        var savedBytes = await File.ReadAllBytesAsync(destination);
        Assert.Equal(malformedBytes, savedBytes);
        var reparsed = FalloutMsgParser.Parse(savedBytes);
        Assert.False(reparsed.IsSuccess);
        Assert.NotNull(reparsed.Diagnostic);

        var beginResult = FalloutMsgTextEditWorkflow.BeginEdit(
            reparsed,
            0,
            Encoding.UTF8);
        Assert.False(beginResult.IsSuccess);
        Assert.Null(beginResult.Session);
        Assert.Equal(FalloutMsgTextEditFailureCode.ParseFailed, beginResult.Failure?.Code);
    }

    private static void ApplyOccurrence(
        MsgInspectorViewModel viewModel,
        int sourceOccurrenceIndex,
        string text)
    {
        viewModel.SelectedMessage = viewModel.VisibleMessages
            .Single(message => message.SourceIndex == sourceOccurrenceIndex);
        Assert.True(viewModel.CanBeginEdit);
        viewModel.BeginEdit();
        Assert.True(viewModel.IsEditing);
        viewModel.DraftText = text;
        viewModel.ApplyEdit();
        Assert.False(viewModel.IsEditing);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"fae-msg-round-trip-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
