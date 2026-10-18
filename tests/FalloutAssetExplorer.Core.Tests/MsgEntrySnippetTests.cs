using FalloutAssetExplorer.App.ViewModels;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class MsgEntrySnippetTests
{
    [Fact]
    public void Snippets_DecodeDuplicateOccurrencesInSourceOrderWithDefaultUtf8()
    {
        var viewModel = new MsgInspectorViewModel();
        viewModel.SetParseResult(Parse("{5}{}{first}{5}{}{Cr\u00e8me\r\nbr\u00fbl\u00e9e}"));

        Assert.Equal("UTF-8", viewModel.SelectedTextEncoding?.DisplayName);
        Assert.Equal(new[] { 0, 1 },
            viewModel.VisibleMessages.Select(message => message.SourceIndex));
        Assert.Equal(new[] { 5, 5 },
            viewModel.VisibleMessages.Select(message => message.Id));
        Assert.Equal(new[] { "first", "Cr\u00e8me br\u00fbl\u00e9e" },
            viewModel.VisibleMessages.Select(message => message.TextSnippet));
    }

    [Fact]
    public void Snippets_ShowAnEmptyMarkerForSuccessfullyDecodedEmptyText()
    {
        var viewModel = new MsgInspectorViewModel();
        viewModel.SetParseResult(Parse("{1}{}{}"));

        Assert.Equal("(empty)", Assert.Single(viewModel.VisibleMessages).TextSnippet);
    }

    [Fact]
    public void Snippets_CollapseWhitespaceAndRemainShort()
    {
        var source = CreateMessage("{1}{}", " \talpha \r\n beta\u00A0gamma    delta ");
        var viewModel = new MsgInspectorViewModel();
        viewModel.SetParseResult(FalloutMsgParser.Parse(source));

        Assert.Equal("alpha beta gamma delta", Assert.Single(viewModel.VisibleMessages).TextSnippet);

        viewModel.SetParseResult(FalloutMsgParser.Parse(
            CreateMessage("{1}{}", new string('x', 100))));

        var longSnippet = Assert.Single(viewModel.VisibleMessages).TextSnippet;
        Assert.Equal(new string('x', 80) + "...", longSnippet);
        Assert.Equal(83, longSnippet.Length);
    }

    [Fact]
    public void Snippets_UseNeutralPlaceholderWithoutEncodingOrAfterDecodeFailure()
    {
        var source = CreateMessage("{1}{}", [0xFF]);
        var viewModel = new MsgInspectorViewModel();
        viewModel.SetParseResult(FalloutMsgParser.Parse(source));
        var occurrence = Assert.Single(viewModel.VisibleMessages);

        Assert.Equal("UTF-8", viewModel.SelectedTextEncoding?.DisplayName);
        Assert.Equal("(no preview)", occurrence.TextSnippet);

        viewModel.SelectedTextEncoding = null;
        Assert.Equal("(no preview)", occurrence.TextSnippet);
        viewModel.SelectedTextEncoding = viewModel.TextEncodingOptions[0];

        Assert.Equal("(no preview)", occurrence.TextSnippet);
        Assert.DoesNotContain("\uFFFD", occurrence.TextSnippet);
    }

    [Fact]
    public void Snippets_RefreshOnEncodingChangeWithoutChangingSourceOrDraft()
    {
        var source = CreateMessage("{5}{}", [0xE9]);
        var viewModel = new MsgInspectorViewModel();
        viewModel.SetParseResult(FalloutMsgParser.Parse(source));
        var sourceSnapshot = viewModel.ParseResult!.SourceSnapshot!.ToArray();
        var occurrence = Assert.Single(viewModel.VisibleMessages);
        viewModel.SelectedMessage = occurrence;

        Assert.Equal("(no preview)", occurrence.TextSnippet);

        viewModel.SelectedTextEncoding = viewModel.TextEncodingOptions.Single(
            option => option.DisplayName == "ISO-8859-1");

        Assert.Equal("\u00e9", occurrence.TextSnippet);
        viewModel.BeginEdit();
        viewModel.DraftText = "keep this draft";
        viewModel.SelectedTextEncoding = viewModel.TextEncodingOptions[0];

        Assert.Equal("\u00e9", occurrence.TextSnippet);
        Assert.Equal("keep this draft", viewModel.DraftText);
        Assert.Equal(sourceSnapshot, viewModel.ParseResult.SourceSnapshot!.ToArray());
        Assert.Equal(source, viewModel.ParseResult.SourceSnapshot.ToArray());
    }

    [Fact]
    public void Encoding_DefaultsToUtf8AndResetsOnlyWhenTheSourceChanges()
    {
        var viewModel = new MsgInspectorViewModel();

        Assert.Equal("UTF-8", viewModel.SelectedTextEncoding?.DisplayName);

        viewModel.SetParseResult(Parse("{1}{}{first}{2}{}{second}"));
        Assert.Equal("UTF-8", viewModel.SelectedTextEncoding?.DisplayName);
        viewModel.SelectedMessage = viewModel.VisibleMessages[0];
        Assert.Equal("first", viewModel.TextPreview);
        Assert.Equal(new[] { "first", "second" },
            viewModel.VisibleMessages.Select(message => message.TextSnippet));

        var latin1 = viewModel.TextEncodingOptions.Single(
            option => option.DisplayName == "ISO-8859-1");
        viewModel.SelectedTextEncoding = latin1;
        viewModel.SelectedMessage = viewModel.VisibleMessages[1];

        Assert.Same(latin1, viewModel.SelectedTextEncoding);

        viewModel.SetParseResult(Parse("{3}{}{new source}"));

        Assert.Equal("UTF-8", viewModel.SelectedTextEncoding?.DisplayName);
        viewModel.SelectedMessage = Assert.Single(viewModel.VisibleMessages);
        Assert.Equal("new source", viewModel.TextPreview);
        Assert.Equal("new source", viewModel.SelectedMessage.TextSnippet);
    }

    private static FalloutMsgParseResult Parse(string input) =>
        FalloutMsgParser.Parse(System.Text.Encoding.UTF8.GetBytes(input));

    private static byte[] CreateMessage(string prefix, string text) =>
        CreateMessage(prefix, System.Text.Encoding.UTF8.GetBytes(text));

    private static byte[] CreateMessage(string prefix, byte[] text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(prefix + "{");
        return [.. bytes, .. text, (byte)'}'];
    }
}
