using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace FalloutAssetExplorer.App.Views;

public sealed class MapObjectSearchHighlightTextBlock : TextBlock
{
    private static readonly IBrush MatchForeground = new SolidColorBrush(Color.Parse("#FFD166"));

    public static readonly StyledProperty<string?> DisplayTextProperty =
        AvaloniaProperty.Register<MapObjectSearchHighlightTextBlock, string?>(nameof(DisplayText));

    public static readonly StyledProperty<string?> SearchTextProperty =
        AvaloniaProperty.Register<MapObjectSearchHighlightTextBlock, string?>(nameof(SearchText));

    public string? DisplayText
    {
        get => GetValue(DisplayTextProperty);
        set => SetValue(DisplayTextProperty, value);
    }

    public string? SearchText
    {
        get => GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DisplayTextProperty || change.Property == SearchTextProperty)
        {
            UpdateInlines();
        }
    }

    private void UpdateInlines()
    {
        var inlines = new InlineCollection();

        foreach (var segment in MapObjectSearchHighlighter.CreateSegments(DisplayText, SearchText))
        {
            var run = new Run(segment.Text);
            if (segment.IsMatch)
            {
                run.Foreground = MatchForeground;
                run.FontWeight = FontWeight.SemiBold;
            }

            inlines.Add(run);
        }

        Inlines = inlines;
    }
}

public readonly record struct MapObjectSearchHighlightSegment(string Text, bool IsMatch);

public static class MapObjectSearchHighlighter
{
    public static IReadOnlyList<MapObjectSearchHighlightSegment> CreateSegments(
        string? displayText,
        string? searchText)
    {
        var text = displayText ?? string.Empty;
        if (text.Length == 0 || string.IsNullOrWhiteSpace(searchText))
        {
            return [new MapObjectSearchHighlightSegment(text, IsMatch: false)];
        }

        var query = searchText.Trim();
        var segments = new List<MapObjectSearchHighlightSegment>();
        var position = 0;

        while (position < text.Length)
        {
            var matchStart = text.IndexOf(query, position, StringComparison.OrdinalIgnoreCase);
            if (matchStart < 0)
            {
                segments.Add(new MapObjectSearchHighlightSegment(text[position..], IsMatch: false));
                break;
            }

            if (matchStart > position)
            {
                segments.Add(new MapObjectSearchHighlightSegment(
                    text[position..matchStart],
                    IsMatch: false));
            }

            var matchEnd = matchStart + query.Length;
            segments.Add(new MapObjectSearchHighlightSegment(
                text[matchStart..matchEnd],
                IsMatch: true));
            position = matchEnd;
        }

        return segments;
    }
}
