using FalloutAssetExplorer.App.ViewModels;
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
        Assert.Null(viewModel.SelectedMessage);
        Assert.Equal(MsgPayloadField.Text, viewModel.SelectedPayloadField);
        Assert.Empty(viewModel.SelectedPayloadHex);
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

    private static FalloutMsgParseResult Parse(string data) =>
        FalloutMsgParser.Parse(System.Text.Encoding.ASCII.GetBytes(data));

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
