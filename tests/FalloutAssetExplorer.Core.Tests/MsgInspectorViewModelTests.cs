using Avalonia.Controls;
using FalloutAssetExplorer.App;
using FalloutAssetExplorer.App.ViewModels;
using FalloutAssetExplorer.App.Views;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class MsgInspectorViewModelTests
{
    [Fact]
    public void SetParseResult_ShowsAllEntriesInSourceOrderForEmptyFilter()
    {
        var viewModel = new MsgInspectorViewModel();

        viewModel.SetParseResult(Parse("{5}{}{first}{2}{}{second}{5}{}{third}"));
        viewModel.IdFilterText = " \t";

        Assert.True(viewModel.IsIdFilterValid);
        Assert.Equal(new[] { 0, 1, 2 }, viewModel.VisibleMessages.Select(message => message.SourceIndex));
        Assert.Equal(new[] { 1, 2, 3 }, viewModel.VisibleMessages.Select(message => message.Position));
        Assert.Equal(new[] { 5, 2, 5 }, viewModel.VisibleMessages.Select(message => message.Id));
        Assert.Contains("all 3 messages in source order", viewModel.FilterStatus);
    }

    [Fact]
    public void IdFilter_AcceptsSignedInt32AndKeepsAllMatchingOccurrencesInOrder()
    {
        var viewModel = new MsgInspectorViewModel();
        viewModel.SetParseResult(Parse("{5}{}{first}{-2}{}{negative}{5}{}{third}"));

        viewModel.IdFilterText = " +5 ";

        Assert.True(viewModel.IsIdFilterValid);
        Assert.Equal(new[] { 1, 3 }, viewModel.VisibleMessages.Select(message => message.Position));
        Assert.Equal(new[] { 0, 2 }, viewModel.VisibleMessages.Select(message => message.SourceIndex));

        viewModel.IdFilterText = "-2";

        Assert.Single(viewModel.VisibleMessages);
        Assert.Equal(-2, viewModel.VisibleMessages[0].Id);
    }

    [Fact]
    public void IdFilter_PreservesSelectedOccurrenceOnlyWhileItRemainsVisible()
    {
        var viewModel = new MsgInspectorViewModel();
        viewModel.SetParseResult(Parse(
            "{5}{first-audio}{first-text}{5}{second-audio}{second-text}{6}{other-audio}{other-text}"));
        var selectedOccurrence = viewModel.VisibleMessages[1];
        viewModel.SelectedMessage = selectedOccurrence;

        Assert.Same(selectedOccurrence, viewModel.SelectedMessage);
        Assert.Equal("second-text"u8.ToArray(), viewModel.SelectedMessage?.Entry.Text.ToArray());
        Assert.Contains("00000000  73 65 63 6F 6E 64 2D 74 65 78 74", viewModel.SelectedPayloadHex);

        viewModel.IdFilterText = "5";

        Assert.Equal(new[] { 0, 1 }, viewModel.VisibleMessages.Select(message => message.SourceIndex));
        Assert.Same(selectedOccurrence, viewModel.SelectedMessage);
        Assert.Contains("00000000  73 65 63 6F 6E 64 2D 74 65 78 74", viewModel.SelectedPayloadHex);

        viewModel.IdFilterText = "6";

        Assert.Single(viewModel.VisibleMessages);
        Assert.Equal(6, viewModel.VisibleMessages[0].Id);
        Assert.Null(viewModel.SelectedMessage);
        Assert.Empty(viewModel.SelectedPayloadHex);
        Assert.Equal("Select a message to inspect its raw bytes.", viewModel.SelectedPayloadStatus);
    }

    [Theory]
    [InlineData("+")]
    [InlineData("-")]
    [InlineData("2147483648")]
    [InlineData("5x")]
    public void IdFilter_InvalidValueIsInvalidAndShowsNoEntries(string filter)
    {
        var viewModel = new MsgInspectorViewModel();
        viewModel.SetParseResult(Parse("{5}{}{first}{5}{}{second}"));

        viewModel.IdFilterText = filter;

        Assert.False(viewModel.IsIdFilterValid);
        Assert.Empty(viewModel.VisibleMessages);
        Assert.Contains("invalid input shows no messages", viewModel.FilterStatus);
    }

    [Fact]
    public void SelectedMessage_UsesOccurrenceIndexWhenIdsAreDuplicated()
    {
        var viewModel = new MsgInspectorViewModel();
        viewModel.SetParseResult(Parse("{5}{first-audio}{first-text}{5}{second-audio}{second-text}"));
        var secondOccurrence = viewModel.VisibleMessages[1];

        viewModel.SelectedMessage = secondOccurrence;

        Assert.Equal(1, viewModel.SelectedMessage?.SourceIndex);
        Assert.Equal(2, viewModel.SelectedMessage?.Position);
        Assert.Equal(5, viewModel.SelectedMessage?.Id);
        Assert.Equal("second-text"u8.ToArray(), viewModel.SelectedMessage?.Entry.Text.ToArray());
    }

    [Fact]
    public void SelectedPayloadField_SwitchesBetweenRawTextAndAudioBytes()
    {
        byte[] data =
        [
            (byte)'{', (byte)'5', (byte)'}',
            (byte)'{', 0xA0, 0xFF, (byte)'}',
            (byte)'{', 0x80, 0xFF, (byte)'}'
        ];
        var viewModel = new MsgInspectorViewModel();
        viewModel.SetParseResult(FalloutMsgParser.Parse(data));
        viewModel.SelectedMessage = Assert.Single(viewModel.VisibleMessages);

        Assert.Equal(MsgPayloadField.Text, viewModel.SelectedPayloadField);
        Assert.Contains("Text field: 2 bytes", viewModel.SelectedPayloadStatus);
        Assert.Contains("00000000  80 FF", viewModel.SelectedPayloadHex);
        Assert.Equal(new byte[] { 0x80, 0xFF }, viewModel.SelectedMessage.Entry.Text.ToArray());

        viewModel.SelectedPayloadField = MsgPayloadField.Audio;

        Assert.Contains("Audio field: 2 bytes", viewModel.SelectedPayloadStatus);
        Assert.Contains("00000000  A0 FF", viewModel.SelectedPayloadHex);
        Assert.Equal(new byte[] { 0xA0, 0xFF }, viewModel.SelectedMessage.Entry.Audio.ToArray());
    }

    [Fact]
    public void PayloadHex_TruncatesOnlyPresentationAndReportsOmittedBytes()
    {
        var textBytes = Enumerable.Repeat((byte)'\n', 5000).ToArray();
        var viewModel = new MsgInspectorViewModel();
        viewModel.SetParseResult(FalloutMsgParser.Parse(CreateSingleMessage(7, [], textBytes)));
        viewModel.SelectedMessage = Assert.Single(viewModel.VisibleMessages);

        Assert.Equal(5000, viewModel.SelectedMessage.Entry.Text.Length);
        Assert.Equal(
            $"Text field: {5000:N0} bytes; showing {4096:N0}, {904:N0} omitted from this presentation.",
            viewModel.SelectedPayloadStatus);
        Assert.Contains("00000FF0", viewModel.SelectedPayloadHex);
        Assert.DoesNotContain("00001000", viewModel.SelectedPayloadHex);
        Assert.Equal(textBytes, viewModel.SelectedMessage.Entry.Text.ToArray());
    }

    [Fact]
    public void SetParseResult_ResetsFilterSelectionAndPayloadWhenChangingFiles()
    {
        var viewModel = new MsgInspectorViewModel();
        viewModel.SetParseResult(Parse("{5}{audio}{text}{5}{other-audio}{other-text}"));
        viewModel.IdFilterText = "5";
        viewModel.SelectedMessage = viewModel.VisibleMessages[1];
        viewModel.SelectedPayloadField = MsgPayloadField.Audio;

        viewModel.SetParseResult(Parse("{9}{}{new file}"));

        Assert.Equal(string.Empty, viewModel.IdFilterText);
        Assert.Single(viewModel.VisibleMessages);
        Assert.Equal(9, viewModel.VisibleMessages[0].Id);
        Assert.Same(viewModel.VisibleMessages[0], viewModel.SelectedMessage);
        Assert.Equal(0, viewModel.SelectedMessage?.SourceIndex);
        Assert.Equal("new file", viewModel.VisibleMessages[0].TextSnippet);
        Assert.Equal("new file", viewModel.TextPreview);
        Assert.True(viewModel.CanShowTextPreview);
        Assert.Equal(MsgPayloadField.Text, viewModel.SelectedPayloadField);
        Assert.Contains("6E 65 77 20 66 69 6C 65", viewModel.SelectedPayloadHex);
    }

    [Fact]
    public void SetParseResult_RejectsSelectionFromPreviousFileWithSameSourceIndex()
    {
        var viewModel = new MsgInspectorViewModel();
        viewModel.SetParseResult(Parse("{5}{old-audio}{old-text}"));
        var oldOccurrence = Assert.Single(viewModel.VisibleMessages);
        viewModel.SelectedMessage = oldOccurrence;
        Assert.Contains("6F 6C 64 2D 74 65 78 74", viewModel.SelectedPayloadHex);

        viewModel.SetParseResult(Parse("{5}{new-audio}{new-text}"));
        viewModel.SelectedMessage = oldOccurrence;

        Assert.Single(viewModel.VisibleMessages);
        Assert.Equal(0, viewModel.VisibleMessages[0].SourceIndex);
        Assert.Null(viewModel.SelectedMessage);
        Assert.Empty(viewModel.SelectedPayloadHex);
        Assert.Equal("Select a message to inspect its raw bytes.", viewModel.SelectedPayloadStatus);
    }

    [Fact]
    public void SetParseResult_ClearsEntriesAndSelectionForMalformedFile()
    {
        var viewModel = new MsgInspectorViewModel();
        viewModel.SetParseResult(Parse("{5}{}{first}"));
        viewModel.SelectedMessage = Assert.Single(viewModel.VisibleMessages);

        viewModel.SetParseResult(FalloutMsgParser.Parse("{1}{}{broken"u8.ToArray()));

        Assert.False(viewModel.ParseResult?.IsSuccess);
        Assert.Empty(viewModel.VisibleMessages);
        Assert.Null(viewModel.SelectedMessage);
        Assert.Empty(viewModel.SelectedPayloadHex);
        Assert.Empty(viewModel.IdFilterText);
    }

    [Fact]
    public void BeginEdit_UsesDefaultUtf8AndAllowsSelectingAnotherEncoding()
    {
        var viewModel = new MsgInspectorViewModel();
        viewModel.SetParseResult(FalloutMsgParser.Parse(
            CreateSingleMessage(5, [], System.Text.Encoding.Latin1.GetBytes("caf\u00e9"))));
        var occurrence = Assert.Single(viewModel.VisibleMessages);
        viewModel.SelectedMessage = occurrence;

        Assert.Equal("UTF-8", viewModel.SelectedTextEncoding?.DisplayName);
        Assert.False(viewModel.CanBeginEdit);
        Assert.False(viewModel.IsEditing);
        Assert.False(viewModel.CanShowTextPreview);

        viewModel.SelectedTextEncoding = viewModel.TextEncodingOptions.Single(
            option => option.DisplayName == "ISO-8859-1");

        Assert.True(viewModel.CanBeginEdit);
        viewModel.BeginEdit();

        Assert.True(viewModel.IsEditing);
        Assert.Same(occurrence, viewModel.SelectedMessage);
        Assert.Equal("caf\u00e9", viewModel.DraftText);
        Assert.False(viewModel.CanChangeSelection);
        Assert.False(viewModel.CanSelectTextEncoding);
        Assert.Contains("using selected", viewModel.EditStatus);
    }

    [Fact]
    public void TextPreview_UsesDefaultUtf8WithoutStartingAnEditSession()
    {
        var viewModel = new MsgInspectorViewModel();
        viewModel.SetParseResult(Parse("{5}{}{source text}"));
        viewModel.SelectedMessage = Assert.Single(viewModel.VisibleMessages);

        Assert.Equal("UTF-8", viewModel.SelectedTextEncoding?.DisplayName);
        Assert.True(viewModel.CanShowTextPreview);
        Assert.Equal("source text", viewModel.TextPreview);
        Assert.False(viewModel.IsEditing);
        Assert.False(viewModel.CanSaveAs);

        viewModel.SelectedTextEncoding = null;

        Assert.False(viewModel.CanShowTextPreview);
        Assert.Contains("Select an encoding", viewModel.TextPreviewStatus);
        Assert.Empty(viewModel.TextPreview);

        viewModel.SelectedTextEncoding = viewModel.TextEncodingOptions[0];

        Assert.True(viewModel.CanShowTextPreview);
        Assert.Equal("source text", viewModel.TextPreview);
    }

    [Fact]
    public void TextPreview_DecodesDefaultUtf8AndPreservesWhitespaceAndLineEndings()
    {
        const string expectedText = " \tCr\u00e8me\r\ncaf\u00e9\n ";
        var source = CreateSingleMessage(
            5,
            [],
            System.Text.Encoding.UTF8.GetBytes(expectedText));
        var viewModel = new MsgInspectorViewModel();
        viewModel.SetParseResult(FalloutMsgParser.Parse(source));
        viewModel.SelectedMessage = Assert.Single(viewModel.VisibleMessages);

        Assert.True(viewModel.CanShowTextPreview);
        Assert.Equal(expectedText, viewModel.TextPreview);
        Assert.False(viewModel.IsEditing);
        Assert.Empty(viewModel.DraftText);
        Assert.False(viewModel.CanSaveAs);
        Assert.Equal(source, viewModel.ParseResult!.SourceSnapshot!.ToArray());
        Assert.Equal(
            System.Text.Encoding.UTF8.GetBytes(expectedText),
            viewModel.SelectedMessage!.Entry.Text.ToArray());
    }

    [Fact]
    public void TextPreview_ReportsDecodeFailureAndRecoversWithSuitableEncoding()
    {
        var source = CreateSingleMessage(5, [], [0xE9]);
        var viewModel = new MsgInspectorViewModel();
        viewModel.SetParseResult(FalloutMsgParser.Parse(source));
        viewModel.SelectedMessage = Assert.Single(viewModel.VisibleMessages);

        Assert.False(viewModel.CanShowTextPreview);
        Assert.Equal("UTF-8", viewModel.SelectedTextEncoding?.DisplayName);
        Assert.Empty(viewModel.TextPreview);
        Assert.Contains("Cannot preview", viewModel.TextPreviewStatus);
        Assert.DoesNotContain("\uFFFD", viewModel.TextPreview);

        viewModel.SelectedTextEncoding = viewModel.TextEncodingOptions.Single(
            option => option.DisplayName == "ISO-8859-1");

        Assert.True(viewModel.CanShowTextPreview);
        Assert.Equal("\u00e9", viewModel.TextPreview);
        Assert.False(viewModel.IsEditing);
    }

    [Fact]
    public void TextPreview_TracksSelectedDuplicateOccurrenceOutsideEditSession()
    {
        var viewModel = new MsgInspectorViewModel();
        viewModel.SetParseResult(Parse("{5}{}{first}{5}{}{second}"));
        viewModel.SelectedTextEncoding = viewModel.TextEncodingOptions[0];
        viewModel.SelectedMessage = viewModel.VisibleMessages[0];

        Assert.Equal("first", viewModel.TextPreview);

        viewModel.SelectedMessage = viewModel.VisibleMessages[1];

        Assert.Equal(1, viewModel.SelectedMessage?.SourceIndex);
        Assert.Equal(5, viewModel.SelectedMessage?.Id);
        Assert.Equal("second", viewModel.TextPreview);
        Assert.False(viewModel.IsEditing);
    }

    [Fact]
    public void TextPreview_RemainsSourceTextAcrossBeginFailedApplyCancelAndSuccessfulApply()
    {
        var viewModel = new MsgInspectorViewModel();
        viewModel.SetParseResult(Parse("{5}{}{original}{5}{}{other}"));
        viewModel.SelectedMessage = viewModel.VisibleMessages[0];
        viewModel.SelectedTextEncoding = viewModel.TextEncodingOptions[0];
        Assert.Equal("original", viewModel.TextPreview);
        Assert.False(viewModel.CanSaveAs);

        viewModel.BeginEdit();
        Assert.True(viewModel.IsEditing);
        Assert.Equal("original", viewModel.TextPreview);
        Assert.Equal("original", viewModel.DraftText);

        viewModel.DraftText = "bad } text";
        viewModel.ApplyEdit();
        Assert.True(viewModel.IsEditing);
        Assert.Equal("bad } text", viewModel.DraftText);
        Assert.Equal("original", viewModel.TextPreview);
        Assert.False(viewModel.CanSaveAs);

        viewModel.CancelEdit();
        Assert.False(viewModel.IsEditing);
        Assert.Empty(viewModel.DraftText);
        Assert.Equal("original", viewModel.TextPreview);
        Assert.False(viewModel.CanSaveAs);

        viewModel.BeginEdit();
        viewModel.DraftText = "changed";
        viewModel.ApplyEdit();

        Assert.False(viewModel.IsEditing);
        Assert.Equal("original", viewModel.TextPreview);
        Assert.Equal("changed", viewModel.DraftText);
        Assert.True(viewModel.CanSaveAs);
    }

    [Fact]
    public void ApplyEdit_UsesOccurrenceIndexForDuplicateIdsAndPreservesUnrelatedBytes()
    {
        var source = "{5}{first-audio}{first-text}{5}{second-audio}{second-text}"u8.ToArray();
        var viewModel = new MsgInspectorViewModel();
        viewModel.SetParseResult(FalloutMsgParser.Parse(source));
        viewModel.SelectedMessage = viewModel.VisibleMessages[1];
        viewModel.SelectedTextEncoding = viewModel.TextEncodingOptions[0];
        viewModel.BeginEdit();
        viewModel.DraftText = "edited";

        viewModel.ApplyEdit();

        Assert.False(viewModel.IsEditing);
        Assert.Equal(
            "{5}{first-audio}{first-text}{5}{second-audio}{edited}",
            System.Text.Encoding.ASCII.GetString(viewModel.PatchedOutputBytes!));
        Assert.Equal(source, viewModel.ParseResult!.SourceSnapshot!.ToArray());
        Assert.Equal("second-text"u8.ToArray(), viewModel.SelectedMessage!.Entry.Text.ToArray());
        Assert.Equal(1, viewModel.SelectedMessage.SourceIndex);
        Assert.Contains("NOT saved to disk", viewModel.PatchedOutputStatus);
    }

    [Fact]
    public void ApplyEdit_AccumulatesOccurrencesAndReplacesTheSameOccurrenceOnLaterEdit()
    {
        byte[] source =
            "#prefix\r\n{5}{audio-a}{first}{5}{audio-b}{second}{8}{audio-c}{untouched}"u8.ToArray();
        var viewModel = new MsgInspectorViewModel();
        viewModel.SetParseResult(FalloutMsgParser.Parse(source));

        ApplyOccurrence(viewModel, 0, "first replacement");
        ApplyOccurrence(viewModel, 1, "second replacement");
        ApplyOccurrence(viewModel, 0, "final first");

        byte[] expected =
            "#prefix\r\n{5}{audio-a}{final first}{5}{audio-b}{second replacement}{8}{audio-c}{untouched}"u8.ToArray();
        Assert.Equal(expected, viewModel.PatchedOutputBytes);
        Assert.Equal(source, viewModel.ParseResult!.SourceSnapshot!.ToArray());
        Assert.Equal("first"u8.ToArray(), viewModel.ParseResult.Entries[0].Text.ToArray());
        Assert.Equal("second"u8.ToArray(), viewModel.ParseResult.Entries[1].Text.ToArray());
        Assert.Contains("2 source occurrences", viewModel.PatchedOutputStatus);
    }

    [Fact]
    public void ApplyEdit_FailedLaterEditPreservesEarlierPendingOutput()
    {
        byte[] source = "{5}{audio-a}{first}{5}{audio-b}{second}"u8.ToArray();
        var viewModel = new MsgInspectorViewModel();
        viewModel.SetParseResult(FalloutMsgParser.Parse(source));
        ApplyOccurrence(viewModel, 0, "pending first");
        var expected = "{5}{audio-a}{pending first}{5}{audio-b}{second}"u8.ToArray();
        var outputVersion = viewModel.PatchedOutputVersion;

        viewModel.SelectedMessage = viewModel.VisibleMessages[1];
        viewModel.BeginEdit();
        viewModel.DraftText = "invalid } replacement";
        viewModel.ApplyEdit();

        Assert.True(viewModel.IsEditing);
        Assert.Equal(expected, viewModel.PatchedOutputBytes);
        Assert.Equal(outputVersion, viewModel.PatchedOutputVersion);
        Assert.Equal(source, viewModel.ParseResult!.SourceSnapshot!.ToArray());
    }

    [Fact]
    public void CancelEdit_LaterEditPreservesEarlierPendingOutput()
    {
        var viewModel = new MsgInspectorViewModel();
        viewModel.SetParseResult(Parse("{5}{audio-a}{first}{5}{audio-b}{second}"));
        ApplyOccurrence(viewModel, 0, "pending first");
        byte[] expected = "{5}{audio-a}{pending first}{5}{audio-b}{second}"u8.ToArray();

        viewModel.SelectedMessage = viewModel.VisibleMessages[1];
        viewModel.BeginEdit();
        viewModel.DraftText = "cancelled second";
        viewModel.CancelEdit();

        Assert.False(viewModel.IsEditing);
        Assert.Equal(expected, viewModel.PatchedOutputBytes);
        Assert.Empty(viewModel.DraftText);
    }

    [Fact]
    public void BeginEdit_ReportsDecodingFailureWithoutExposingRawBytesAsText()
    {
        byte[] source = [(byte)'{', (byte)'5', (byte)'}', (byte)'{', (byte)'}', (byte)'{', 0xFF, (byte)'}'];
        var viewModel = new MsgInspectorViewModel();
        viewModel.SetParseResult(FalloutMsgParser.Parse(source));
        viewModel.SelectedMessage = Assert.Single(viewModel.VisibleMessages);
        viewModel.SelectedTextEncoding = viewModel.TextEncodingOptions[0];

        Assert.False(viewModel.CanBeginEdit);
        Assert.False(viewModel.IsEditing);
        Assert.Empty(viewModel.DraftText);
        Assert.Contains("cannot decode", viewModel.EditStatus);
    }

    [Fact]
    public void ApplyEdit_RetainsDraftAfterEncodingFailure()
    {
        var viewModel = new MsgInspectorViewModel();
        viewModel.SetParseResult(Parse("{5}{}{plain text}"));
        viewModel.SelectedMessage = Assert.Single(viewModel.VisibleMessages);
        viewModel.SelectedTextEncoding = viewModel.TextEncodingOptions.Single(
            option => option.DisplayName == "US-ASCII");
        viewModel.BeginEdit();
        viewModel.DraftText = "unrepresentable \u20ac";

        viewModel.ApplyEdit();

        Assert.True(viewModel.IsEditing);
        Assert.True(viewModel.CanApplyEdit);
        Assert.Equal("unrepresentable \u20ac", viewModel.DraftText);
        Assert.Null(viewModel.PatchedOutputBytes);
        Assert.Contains("cannot represent", viewModel.EditStatus);
    }

    [Fact]
    public void ApplyEdit_RetainsDraftAfterPatcherFailure()
    {
        var viewModel = new MsgInspectorViewModel();
        viewModel.SetParseResult(Parse("{5}{}{original}"));
        viewModel.SelectedMessage = Assert.Single(viewModel.VisibleMessages);
        viewModel.SelectedTextEncoding = viewModel.TextEncodingOptions[0];
        viewModel.BeginEdit();
        viewModel.DraftText = "invalid } delimiter";

        viewModel.ApplyEdit();

        Assert.True(viewModel.IsEditing);
        Assert.True(viewModel.CanApplyEdit);
        Assert.Equal("invalid } delimiter", viewModel.DraftText);
        Assert.Null(viewModel.PatchedOutputBytes);
        Assert.Contains("closing MSG field delimiter", viewModel.EditStatus);
    }

    [Fact]
    public void SelectionAndFilterChanges_AreBlockedDuringEdit()
    {
        var viewModel = new MsgInspectorViewModel();
        viewModel.SetParseResult(Parse("{5}{}{first}{6}{}{second}"));
        var selectedOccurrence = viewModel.VisibleMessages[0];
        viewModel.SelectedMessage = selectedOccurrence;
        viewModel.SelectedTextEncoding = viewModel.TextEncodingOptions[0];
        viewModel.BeginEdit();

        viewModel.SelectedMessage = viewModel.VisibleMessages[1];
        viewModel.IdFilterText = "6";

        Assert.Same(selectedOccurrence, viewModel.SelectedMessage);
        Assert.Equal(string.Empty, viewModel.IdFilterText);
        Assert.Equal(new[] { 0, 1 }, viewModel.VisibleMessages.Select(message => message.SourceIndex));
        Assert.Contains("locked", viewModel.EditStatus);
    }

    [Fact]
    public void SetParseResult_CancelsActiveEditAndClearsPatchedOutput()
    {
        var viewModel = new MsgInspectorViewModel();
        viewModel.SetParseResult(Parse("{5}{}{original}"));
        viewModel.SelectedMessage = Assert.Single(viewModel.VisibleMessages);
        viewModel.SelectedTextEncoding = viewModel.TextEncodingOptions[0];
        viewModel.BeginEdit();
        viewModel.DraftText = "draft";

        viewModel.SetParseResult(Parse("{9}{}{new source}"));

        Assert.False(viewModel.IsEditing);
        Assert.Empty(viewModel.DraftText);
        Assert.Same(viewModel.VisibleMessages[0], viewModel.SelectedMessage);
        Assert.Equal("new source", viewModel.TextPreview);
        Assert.Null(viewModel.PatchedOutputBytes);
        Assert.Contains("active edit was cancelled", viewModel.EditStatus);
    }

    [Fact]
    public void ApplyEdit_AllowsEmptyTextAndPreservesSourceWhenUnchanged()
    {
        var source = "{5}{}{}"u8.ToArray();
        var viewModel = new MsgInspectorViewModel();
        viewModel.SetParseResult(FalloutMsgParser.Parse(source));
        viewModel.SelectedMessage = Assert.Single(viewModel.VisibleMessages);
        viewModel.SelectedTextEncoding = viewModel.TextEncodingOptions[0];
        viewModel.BeginEdit();

        viewModel.ApplyEdit();

        Assert.Equal(source, viewModel.PatchedOutputBytes);
        Assert.Equal(source, viewModel.ParseResult!.SourceSnapshot!.ToArray());
    }

    [Fact]
    public void ArchiveBrowser_PreservesActiveMsgDraftAcrossWorkspacesAndBlocksSourceChanges()
    {
        AvaloniaTestEnvironment.EnsureInitialized();
        AvaloniaTestEnvironment.RunOnUiThread(() =>
        {
            using var viewModel = new ArchiveBrowserViewModel();
            var originalEntry = new ArchiveEntry("text\\first.msg", 16, null, "source.dat");
            var otherEntry = new ArchiveEntry("text\\second.msg", 16, null, "source.dat");
            viewModel.SetEntries([originalEntry, otherEntry]);
            viewModel.SelectedEntry = originalEntry;
            viewModel.MsgInspector.SetParseResult(Parse("{5}{}{source}"));
            viewModel.MsgInspector.SelectedMessage =
                Assert.Single(viewModel.MsgInspector.VisibleMessages);
            viewModel.MsgInspector.BeginEdit();
            viewModel.MsgInspector.DraftText = "unsaved draft";

            var window = new MainWindow { DataContext = viewModel };
            try
            {
                var tabs = Assert.IsType<TabControl>(window.FindControl<TabControl>("WorkspaceTabs"));
                tabs.SelectedIndex = ArchiveBrowserViewModel.MsgEditorWorkspaceIndex;
                tabs.SelectedIndex = ArchiveBrowserViewModel.SpriteExplorerWorkspaceIndex;
                tabs.SelectedIndex = 0;

                Assert.True(viewModel.MsgInspector.IsEditing);
                Assert.Equal("unsaved draft", viewModel.MsgInspector.DraftText);
                Assert.Same(originalEntry, viewModel.SelectedEntry);

                viewModel.SelectedEntry = otherEntry;
                viewModel.SearchText = "second";
                viewModel.SelectedExtension = ".MAP";
                viewModel.LoadArchiveAsync("replacement.dat").GetAwaiter().GetResult();

                Assert.Same(originalEntry, viewModel.SelectedEntry);
                Assert.Equal(string.Empty, viewModel.SearchText);
                Assert.Equal(ArchiveEntryFilter.AllExtensions, viewModel.SelectedExtension);
                Assert.True(viewModel.MsgInspector.IsEditing);
                Assert.Equal("unsaved draft", viewModel.MsgInspector.DraftText);
                Assert.False(viewModel.CanChangeArchiveSource);
                Assert.False(viewModel.CanOpenArchive);
                Assert.Contains("Apply or cancel", viewModel.StatusMessage);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void MainWindow_MsgEditControlsReflectTheViewModelState()
    {
        AvaloniaTestEnvironment.EnsureInitialized();
        AvaloniaTestEnvironment.RunOnUiThread(() =>
        {
            using var viewModel = new ArchiveBrowserViewModel();
            var msgEntry = new ArchiveEntry("text/english/test.msg", 14, null, "archive.dat");
            viewModel.SetEntries([msgEntry]);
            viewModel.SelectedEntry = msgEntry;
            viewModel.MsgInspector.SetParseResult(Parse("{5}{}{text}"));
            viewModel.MsgInspector.SelectedMessage =
                Assert.Single(viewModel.MsgInspector.VisibleMessages);
            viewModel.SelectedWorkspaceIndex = ArchiveBrowserViewModel.MsgEditorWorkspaceIndex;

            var window = new MainWindow { DataContext = viewModel };
            try
            {
                var encoding = Assert.IsType<ComboBox>(
                    window.FindControl<ComboBox>("MsgTextEncodingSelector"));
                var begin = Assert.IsType<Button>(
                    window.FindControl<Button>("BeginMsgTextEditButton"));
                var apply = Assert.IsType<Button>(
                    window.FindControl<Button>("ApplyMsgTextEditButton"));
                var cancel = Assert.IsType<Button>(
                    window.FindControl<Button>("CancelMsgTextEditButton"));
                var draft = Assert.IsType<TextBox>(
                    window.FindControl<TextBox>("MsgTextDraftEditor"));
                var textPreview = Assert.IsType<TextBox>(
                    window.FindControl<TextBox>("MsgTextPreview"));
                var filter = Assert.IsType<TextBox>(
                    window.FindControl<TextBox>("MsgIdFilterInput"));
                var entries = Assert.IsType<ListBox>(
                    window.FindControl<ListBox>("MsgEntryList"));
                var msgFiles = Assert.IsType<ListBox>(
                    window.FindControl<ListBox>("MsgFilesList"));

                Assert.Equal(
                    "UTF-8",
                    Assert.IsType<MsgTextEncodingOption>(encoding.SelectedItem).DisplayName);
                Assert.Same(viewModel.SelectedMsgFile, msgFiles.SelectedItem);
                Assert.Single(viewModel.VisibleMsgFiles);
                Assert.True(begin.IsEnabled);
                Assert.False(apply.IsEnabled);
                Assert.False(cancel.IsEnabled);
                Assert.False(draft.IsEnabled);
                Assert.True(textPreview.IsReadOnly);
                Assert.True(textPreview.IsVisible);
                Assert.Equal("text", textPreview.Text);
                Assert.True(filter.IsEnabled);
                Assert.True(entries.IsEnabled);
                Assert.True(msgFiles.IsEnabled);

                viewModel.MsgInspector.BeginEdit();

                Assert.False(encoding.IsEnabled);
                Assert.False(begin.IsEnabled);
                Assert.True(apply.IsEnabled);
                Assert.True(cancel.IsEnabled);
                Assert.True(draft.IsEnabled);
                Assert.True(textPreview.IsVisible);
                Assert.Equal("text", textPreview.Text);
                Assert.False(filter.IsEnabled);
                Assert.False(entries.IsEnabled);
                Assert.False(msgFiles.IsEnabled);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static FalloutMsgParseResult Parse(string data) =>
        FalloutMsgParser.Parse(System.Text.Encoding.ASCII.GetBytes(data));

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

    private static byte[] CreateSingleMessage(int id, byte[] audio, byte[] text)
    {
        using var stream = new MemoryStream();
        WriteField(stream, System.Text.Encoding.ASCII.GetBytes(id.ToString()));
        WriteField(stream, audio);
        WriteField(stream, text);
        return stream.ToArray();
    }

    private static void WriteField(Stream stream, byte[] bytes)
    {
        stream.WriteByte((byte)'{');
        stream.Write(bytes);
        stream.WriteByte((byte)'}');
    }
}
