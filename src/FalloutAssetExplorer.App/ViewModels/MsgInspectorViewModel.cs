using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using FalloutAssetExplorer.Core;

namespace FalloutAssetExplorer.App.ViewModels;

public enum MsgPayloadField
{
    Text,
    Audio
}

public sealed record MsgMessageOccurrence(int SourceIndex, FalloutMsgEntry Entry)
{
    public int Position => SourceIndex + 1;

    public int Id => Entry.Id;

    public int AudioLength => Entry.Audio.Length;

    public int TextLength => Entry.Text.Length;
}

public sealed class MsgInspectorViewModel : INotifyPropertyChanged
{
    private const int MaximumPayloadBytesDisplayed = 4096;

    private FalloutMsgParseResult? _parseResult;
    private string _idFilterText = string.Empty;
    private IReadOnlyList<MsgMessageOccurrence> _visibleMessages = [];
    private MsgMessageOccurrence? _selectedMessage;
    private MsgPayloadField _selectedPayloadField = MsgPayloadField.Text;
    private string _selectedPayloadHex = string.Empty;
    private IReadOnlyList<MsgMessageOccurrence> _sourceMessages = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<MsgPayloadField> PayloadFields { get; } =
        [MsgPayloadField.Text, MsgPayloadField.Audio];

    public FalloutMsgParseResult? ParseResult
    {
        get => _parseResult;
        private set => SetProperty(ref _parseResult, value);
    }

    public string IdFilterText
    {
        get => _idFilterText;
        set
        {
            if (SetProperty(ref _idFilterText, value))
            {
                RefreshVisibleMessages();
            }
        }
    }

    public bool IsIdFilterValid => TryParseIdFilter(out _);

    public string FilterStatus
    {
        get
        {
            if (!TryParseIdFilter(out var id))
            {
                return "Enter a signed 32-bit message ID; invalid input shows no messages.";
            }

            if (string.IsNullOrWhiteSpace(IdFilterText))
            {
                return ParseResult?.IsSuccess == true
                    ? $"Showing all {_sourceMessages.Count:N0} messages in source order."
                    : "No parsed messages are available.";
            }

            return VisibleMessages.Count == 0
                ? $"No messages with ID {id}."
                : $"Showing {VisibleMessages.Count:N0} of {_sourceMessages.Count:N0} messages with ID {id}; duplicates remain separate.";
        }
    }

    public IReadOnlyList<MsgMessageOccurrence> VisibleMessages
    {
        get => _visibleMessages;
        private set => SetProperty(ref _visibleMessages, value);
    }

    public MsgMessageOccurrence? SelectedMessage
    {
        get => _selectedMessage;
        set
        {
            if (value is not null
                && !VisibleMessages.Any(message => ReferenceEquals(message, value)))
            {
                value = null;
            }

            if (SetProperty(ref _selectedMessage, value))
            {
                UpdateSelectedPayload();
            }
        }
    }

    public MsgPayloadField SelectedPayloadField
    {
        get => _selectedPayloadField;
        set
        {
            if (SetProperty(ref _selectedPayloadField, value))
            {
                UpdateSelectedPayload();
            }
        }
    }

    public string SelectedPayloadStatus
    {
        get
        {
            if (SelectedMessage is null)
            {
                return "Select a message to inspect its raw bytes.";
            }

            var byteCount = GetSelectedPayload().Length;
            var displayedByteCount = Math.Min(byteCount, MaximumPayloadBytesDisplayed);
            return byteCount > displayedByteCount
                ? $"{SelectedPayloadField} field: {byteCount:N0} bytes; showing {displayedByteCount:N0}, "
                    + $"{byteCount - displayedByteCount:N0} omitted from this presentation."
                : $"{SelectedPayloadField} field: {byteCount:N0} bytes; all bytes shown.";
        }
    }

    public string SelectedPayloadHex
    {
        get => _selectedPayloadHex;
        private set => SetProperty(ref _selectedPayloadHex, value);
    }

    public void SetParseResult(FalloutMsgParseResult? parseResult)
    {
        ParseResult = parseResult;
        _sourceMessages = parseResult?.IsSuccess == true
            ? parseResult.Entries
                .Select((entry, index) => new MsgMessageOccurrence(index, entry))
                .ToArray()
            : [];

        SelectedMessage = null;
        SelectedPayloadField = MsgPayloadField.Text;
        if (_idFilterText.Length > 0)
        {
            _idFilterText = string.Empty;
            OnPropertyChanged(nameof(IdFilterText));
        }

        RefreshVisibleMessages();
        UpdateSelectedPayload();
    }

    public void Reset() => SetParseResult(null);

    private void RefreshVisibleMessages()
    {
        var selectedMessage = SelectedMessage;
        if (!TryParseIdFilter(out var requestedId))
        {
            VisibleMessages = [];
        }
        else if (string.IsNullOrWhiteSpace(IdFilterText))
        {
            VisibleMessages = _sourceMessages;
        }
        else
        {
            VisibleMessages = _sourceMessages
                .Where(message => message.Id == requestedId)
                .ToArray();
        }

        OnPropertyChanged(nameof(IsIdFilterValid));
        OnPropertyChanged(nameof(FilterStatus));
        SelectedMessage = selectedMessage is not null
            && VisibleMessages.Any(message => ReferenceEquals(message, selectedMessage))
                ? selectedMessage
                : null;
    }

    private bool TryParseIdFilter(out int id)
    {
        var value = IdFilterText.Trim();
        return value.Length == 0
            ? SetEmptyFilter(out id)
            : int.TryParse(
                value,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out id);
    }

    private static bool SetEmptyFilter(out int id)
    {
        id = default;
        return true;
    }

    private ReadOnlyMemory<byte> GetSelectedPayload() =>
        SelectedMessage is null
            ? ReadOnlyMemory<byte>.Empty
            : SelectedPayloadField switch
            {
                MsgPayloadField.Audio => SelectedMessage.Entry.Audio,
                _ => SelectedMessage.Entry.Text
            };

    private void UpdateSelectedPayload()
    {
        SelectedPayloadHex = FormatPayloadHex(GetSelectedPayload().Span);
        OnPropertyChanged(nameof(SelectedPayloadStatus));
    }

    private static string FormatPayloadHex(ReadOnlySpan<byte> bytes)
    {
        var displayedLength = Math.Min(bytes.Length, MaximumPayloadBytesDisplayed);
        var builder = new StringBuilder();
        for (var offset = 0; offset < displayedLength; offset += 16)
        {
            if (offset > 0)
            {
                builder.AppendLine();
            }

            builder.Append(offset.ToString("X8", CultureInfo.InvariantCulture));
            builder.Append("  ");
            var lineEnd = Math.Min(offset + 16, displayedLength);
            for (var index = offset; index < lineEnd; index++)
            {
                if (index > offset)
                {
                    builder.Append(' ');
                }

                builder.Append(bytes[index].ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }

    private bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
