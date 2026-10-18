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

public sealed class MsgMessageOccurrence : INotifyPropertyChanged
{
    private string _textSnippet = "(no preview)";

    public MsgMessageOccurrence(int sourceIndex, FalloutMsgEntry entry)
    {
        SourceIndex = sourceIndex;
        Entry = entry;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int SourceIndex { get; }

    public FalloutMsgEntry Entry { get; }

    public int Position => SourceIndex + 1;

    public int Id => Entry.Id;

    public int AudioLength => Entry.Audio.Length;

    public int TextLength => Entry.Text.Length;

    public string TextSnippet => _textSnippet;

    internal void SetTextSnippet(string value)
    {
        if (string.Equals(_textSnippet, value, StringComparison.Ordinal))
        {
            return;
        }

        _textSnippet = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TextSnippet)));
    }
}

public sealed record MsgTextEncodingOption(string DisplayName, Encoding Encoding);

public sealed class MsgInspectorViewModel : INotifyPropertyChanged
{
    private const int MaximumPayloadBytesDisplayed = 4096;
    private const int MaximumTextSnippetRunes = 80;
    private const string UnavailableTextSnippet = "(no preview)";

    private FalloutMsgParseResult? _parseResult;
    private FalloutMsgParseResult? _workingParseResult;
    private string _idFilterText = string.Empty;
    private IReadOnlyList<MsgMessageOccurrence> _visibleMessages = [];
    private MsgMessageOccurrence? _selectedMessage;
    private MsgPayloadField _selectedPayloadField = MsgPayloadField.Text;
    private string _selectedPayloadHex = string.Empty;
    private IReadOnlyList<MsgMessageOccurrence> _sourceMessages = [];
    private MsgTextEncodingOption? _selectedTextEncoding;
    private string _textPreview = string.Empty;
    private string _textPreviewStatus = "Select a message to preview its text.";
    private bool _canShowTextPreview;
    private FalloutMsgTextEditSession? _pendingEditSession;
    private FalloutMsgTextEditSession? _editSession;
    private bool _canBeginEdit;
    private string _draftText = string.Empty;
    private string _editStatus =
        "UTF-8 is selected by default. Select a message to preview or edit; encoding is not detected automatically.";
    private byte[]? _patchedOutputBytes;
    private readonly HashSet<int> _patchedOutputOccurrenceIndexes = [];
    private int? _patchedOutputOccurrenceIndex;
    private int? _patchedOutputMessageId;
    private string? _originalSourcePath;
    private string? _savedOutputPath;
    private string? _saveFailureMessage;
    private long _patchedOutputVersion;

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<MsgPayloadField> PayloadFields { get; } =
        [MsgPayloadField.Text, MsgPayloadField.Audio];

    public IReadOnlyList<MsgTextEncodingOption> TextEncodingOptions { get; } =
    [
        new("UTF-8", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)),
        new("US-ASCII", Encoding.ASCII),
        new("ISO-8859-1", Encoding.Latin1)
    ];

    public MsgInspectorViewModel()
    {
        _selectedTextEncoding = TextEncodingOptions[0];
    }

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
            if (IsEditing && !string.Equals(value, _idFilterText, StringComparison.Ordinal))
            {
                OnPropertyChanged(nameof(IdFilterText));
                EditStatus = "Filtering is locked while editing. Apply or cancel this edit first.";
                return;
            }

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

            if (IsEditing && !ReferenceEquals(value, _selectedMessage))
            {
                OnPropertyChanged(nameof(SelectedMessage));
                EditStatus = "Selection is locked while editing. Apply or cancel this edit first.";
                return;
            }

            if (SetProperty(ref _selectedMessage, value))
            {
                UpdateSelectedPayload();
                if (!IsEditing)
                {
                    DraftText = string.Empty;
                    RefreshTextPreview();
                    RefreshEditAvailability();
                }
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

    public MsgTextEncodingOption? SelectedTextEncoding
    {
        get => _selectedTextEncoding;
        set
        {
            if (IsEditing && !ReferenceEquals(value, _selectedTextEncoding))
            {
                OnPropertyChanged(nameof(SelectedTextEncoding));
                EditStatus = "Encoding is locked while editing. Apply or cancel this edit first.";
                return;
            }

            if (SetProperty(ref _selectedTextEncoding, value))
            {
                RefreshTextSnippets();
                RefreshTextPreview();
                RefreshEditAvailability();
            }
        }
    }

    public string TextPreview
    {
        get => _textPreview;
        private set => SetProperty(ref _textPreview, value);
    }

    public string TextPreviewStatus
    {
        get => _textPreviewStatus;
        private set => SetProperty(ref _textPreviewStatus, value);
    }

    public bool CanShowTextPreview
    {
        get => _canShowTextPreview;
        private set => SetProperty(ref _canShowTextPreview, value);
    }

    public bool CanBeginEdit => _canBeginEdit;

    public bool IsEditing => _editSession is not null;

    public bool CanChangeSelection => !IsEditing;

    public bool CanSelectTextEncoding => !IsEditing;

    public bool CanEditDraft => IsEditing;

    public bool CanApplyEdit => IsEditing;

    public bool CanCancelEdit => IsEditing;

    public string DraftText
    {
        get => _draftText;
        set => SetProperty(ref _draftText, value);
    }

    public string EditStatus
    {
        get => _editStatus;
        private set => SetProperty(ref _editStatus, value);
    }

    public byte[]? PatchedOutputBytes => _patchedOutputBytes?.ToArray();

    public bool CanSaveAs => _patchedOutputBytes is not null;

    public bool HasUnsavedOutput => _patchedOutputBytes is not null && _savedOutputPath is null;

    public string? OriginalSourcePath => _originalSourcePath;

    public long PatchedOutputVersion => _patchedOutputVersion;

    public string PatchedOutputStatus
    {
        get
        {
            if (_patchedOutputBytes is null)
            {
                return "No patched output is currently held in memory.";
            }

            if (_savedOutputPath is not null)
            {
                return $"Saved standalone MSG to {_savedOutputPath}. "
                    + "The exact in-memory bytes were saved; retail compatibility has not been verified.";
            }

            var failureStatus = _saveFailureMessage is null
                ? string.Empty
                : $" Save failed: {_saveFailureMessage} In-memory output remains available for retry.";
            var occurrenceCount = _patchedOutputOccurrenceIndexes.Count;
            var occurrenceLabel = occurrenceCount == 1
                ? "1 source occurrence"
                : $"{occurrenceCount:N0} source occurrences";
            return $"In-memory patched output for {occurrenceLabel}; latest edit is occurrence "
                + $"{_patchedOutputOccurrenceIndex!.Value + 1} (ID {_patchedOutputMessageId}): "
                + $"{_patchedOutputBytes.Length:N0} bytes. NOT saved to disk."
                + failureStatus;
        }
    }

    public void BeginEdit()
    {
        if (!CanBeginEdit || _pendingEditSession is not { } session)
        {
            return;
        }

        _pendingEditSession = null;
        _editSession = session;
        DraftText = session.OriginalText;
        EditStatus =
            $"Editing source occurrence {session.SourceOccurrenceIndex + 1} (ID {session.MessageId}) "
            + $"using selected {_selectedTextEncoding!.DisplayName}. "
            + "Selection and encoding are locked until Apply or Cancel.";
        OnEditingStateChanged();
        SetCanBeginEdit(false);
    }

    public void ApplyEdit()
    {
        if (_editSession is not { } session)
        {
            return;
        }

        var result = session.Apply(DraftText);
        if (!result.IsSuccess)
        {
            EditStatus =
                $"Could not apply the edit: {result.Failure!.Message} "
                + "The draft is retained; correct it or cancel.";
            return;
        }

        var patchedBytes = result.PatchedBytes!;
        var updatedParseResult = FalloutMsgParser.Parse(patchedBytes);
        if (!updatedParseResult.IsSuccess
            || _workingParseResult is not { IsSuccess: true } previousParseResult
            || !HasSameOccurrenceIdentity(previousParseResult, updatedParseResult))
        {
            var detail = updatedParseResult.Diagnostic?.Message
                ?? "The parsed occurrence sequence changed unexpectedly.";
            EditStatus =
                $"Could not refresh MSG offsets after applying the edit: {detail} "
                + "The draft and any prior pending output are retained; correct it or cancel.";
            return;
        }

        _workingParseResult = updatedParseResult;
        _patchedOutputBytes = patchedBytes.ToArray();
        _patchedOutputOccurrenceIndexes.Add(session.SourceOccurrenceIndex);
        _patchedOutputOccurrenceIndex = session.SourceOccurrenceIndex;
        _patchedOutputMessageId = session.MessageId;
        _savedOutputPath = null;
        _saveFailureMessage = null;
        _patchedOutputVersion++;
        _editSession = null;
        OnPropertyChanged(nameof(PatchedOutputBytes));
        OnPropertyChanged(nameof(CanSaveAs));
        OnPropertyChanged(nameof(HasUnsavedOutput));
        OnPropertyChanged(nameof(PatchedOutputVersion));
        OnPropertyChanged(nameof(PatchedOutputStatus));
        OnEditingStateChanged();
        RefreshEditAvailability(updateStatus: false);
        EditStatus =
            $"Applied text for source occurrence {session.SourceOccurrenceIndex + 1} in memory. "
            + "The original MSG data is unchanged; cumulative output is NOT saved to disk.";
    }

    public bool IsCurrentPatchedOutput(long version) =>
        CanSaveAs && _patchedOutputVersion == version;

    public void MarkOutputSaved(string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (!CanSaveAs)
        {
            return;
        }

        _savedOutputPath = destinationPath;
        _saveFailureMessage = null;
        OnPropertyChanged(nameof(HasUnsavedOutput));
        OnPropertyChanged(nameof(PatchedOutputStatus));
    }

    public void ReportSaveAsFailure(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (!CanSaveAs)
        {
            return;
        }

        _savedOutputPath = null;
        _saveFailureMessage = message;
        OnPropertyChanged(nameof(HasUnsavedOutput));
        OnPropertyChanged(nameof(PatchedOutputStatus));
    }

    public void CancelEdit()
    {
        if (_editSession is null)
        {
            return;
        }

        _editSession = null;
        DraftText = string.Empty;
        OnEditingStateChanged();
        RefreshEditAvailability(updateStatus: false);
        EditStatus = "Edit cancelled. The original MSG data is unchanged.";
    }

    public void SetParseResult(
        FalloutMsgParseResult? parseResult,
        string? originalSourcePath = null)
    {
        var cancelledActiveEdit = IsEditing;
        _pendingEditSession = null;
        _editSession = null;
        _patchedOutputBytes = null;
        _patchedOutputOccurrenceIndexes.Clear();
        _patchedOutputOccurrenceIndex = null;
        _patchedOutputMessageId = null;
        _originalSourcePath = originalSourcePath;
        _savedOutputPath = null;
        _saveFailureMessage = null;
        _patchedOutputVersion++;
        ParseResult = parseResult;
        _workingParseResult = parseResult;
        _sourceMessages = parseResult?.IsSuccess == true
            ? parseResult.Entries
                .Select((entry, index) => new MsgMessageOccurrence(index, entry))
                .ToArray()
            : [];

        _selectedMessage = null;
        _selectedTextEncoding = TextEncodingOptions[0];
        _draftText = string.Empty;
        OnPropertyChanged(nameof(SelectedMessage));
        OnPropertyChanged(nameof(SelectedTextEncoding));
        OnPropertyChanged(nameof(DraftText));
        OnPropertyChanged(nameof(PatchedOutputBytes));
        OnPropertyChanged(nameof(CanSaveAs));
        OnPropertyChanged(nameof(HasUnsavedOutput));
        OnPropertyChanged(nameof(OriginalSourcePath));
        OnPropertyChanged(nameof(PatchedOutputVersion));
        OnPropertyChanged(nameof(PatchedOutputStatus));
        OnEditingStateChanged();
        SelectedPayloadField = MsgPayloadField.Text;
        if (_idFilterText.Length > 0)
        {
            _idFilterText = string.Empty;
            OnPropertyChanged(nameof(IdFilterText));
        }

        EditStatus = cancelledActiveEdit
            ? "The MSG source changed; the active edit was cancelled. No source data was modified."
            : "UTF-8 is selected by default. Select a message to preview or edit; encoding is not detected automatically.";
        RefreshVisibleMessages();
        RefreshTextSnippets();
        SelectedMessage = VisibleMessages.FirstOrDefault();
        UpdateSelectedPayload();
        RefreshTextPreview();
        RefreshEditAvailability(updateStatus: false);
        if (cancelledActiveEdit)
        {
            EditStatus = "The MSG source changed; the active edit was cancelled. No source data was modified.";
        }
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

    private void RefreshEditAvailability(bool updateStatus = true)
    {
        _pendingEditSession = null;
        if (IsEditing)
        {
            SetCanBeginEdit(false);
            return;
        }

        if (SelectedMessage is null)
        {
            SetCanBeginEdit(false);
            if (updateStatus)
            {
                EditStatus = "Select a source occurrence to preview or edit; UTF-8 is the default encoding.";
            }

            return;
        }

        if (SelectedTextEncoding is null)
        {
            SetCanBeginEdit(false);
            if (updateStatus)
            {
                EditStatus = "No encoding is selected. Choose an encoding; none is detected automatically.";
            }

            return;
        }

        var beginResult = FalloutMsgTextEditWorkflow.BeginEdit(
            _workingParseResult,
            SelectedMessage.SourceIndex,
            SelectedTextEncoding.Encoding);
        if (!beginResult.IsSuccess)
        {
            SetCanBeginEdit(false);
            if (updateStatus)
            {
                EditStatus =
                    $"Cannot begin editing with {SelectedTextEncoding.DisplayName}: "
                    + beginResult.Failure!.Message;
            }

            return;
        }

        _pendingEditSession = beginResult.Session;
        SetCanBeginEdit(true);
        if (updateStatus)
        {
            EditStatus =
                $"Using selected {SelectedTextEncoding.DisplayName}. "
                + $"Ready to edit source occurrence {SelectedMessage.SourceIndex + 1}; "
                + "encoding is not detected automatically.";
        }
    }

    private static bool HasSameOccurrenceIdentity(
        FalloutMsgParseResult previous,
        FalloutMsgParseResult updated)
    {
        if (!updated.IsSuccess || previous.Entries.Count != updated.Entries.Count)
        {
            return false;
        }

        for (var index = 0; index < previous.Entries.Count; index++)
        {
            if (previous.Entries[index].Id != updated.Entries[index].Id)
            {
                return false;
            }
        }

        return true;
    }

    private void RefreshTextPreview()
    {
        if (SelectedMessage is not { } selectedMessage)
        {
            TextPreview = string.Empty;
            CanShowTextPreview = false;
            TextPreviewStatus = "Select a message to preview its text.";
            return;
        }

        if (SelectedTextEncoding is not { } selectedEncoding)
        {
            TextPreview = string.Empty;
            CanShowTextPreview = false;
            TextPreviewStatus =
                "Select an encoding to preview this entry; no encoding is detected automatically.";
            return;
        }

        var result = FalloutMsgTextEncoding.Decode(
            selectedMessage.Entry.Text,
            selectedEncoding.Encoding);
        if (!result.IsSuccess)
        {
            TextPreview = string.Empty;
            CanShowTextPreview = false;
            TextPreviewStatus =
                $"Cannot preview source occurrence {selectedMessage.Position} with "
                + $"{selectedEncoding.DisplayName}: {result.ErrorMessage}";
            return;
        }

        TextPreview = result.Value!;
        CanShowTextPreview = true;
        TextPreviewStatus =
            $"Read-only preview of source occurrence {selectedMessage.Position} using selected "
            + $"{selectedEncoding.DisplayName}; no encoding is detected automatically.";
    }

    private void RefreshTextSnippets()
    {
        if (SelectedTextEncoding is not { } selectedEncoding)
        {
            foreach (var message in _sourceMessages)
            {
                message.SetTextSnippet(UnavailableTextSnippet);
            }

            return;
        }

        foreach (var message in _sourceMessages)
        {
            var result = FalloutMsgTextEncoding.Decode(
                message.Entry.Text,
                selectedEncoding.Encoding);
            message.SetTextSnippet(
                result.IsSuccess
                    ? CreateTextSnippet(result.Value!)
                    : UnavailableTextSnippet);
        }
    }

    private static string CreateTextSnippet(string text)
    {
        if (text.Length == 0)
        {
            return "(empty)";
        }

        var builder = new StringBuilder(Math.Min(text.Length, MaximumTextSnippetRunes + 3));
        var hasPendingWhitespace = false;
        var runeCount = 0;
        var wasTruncated = false;
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                hasPendingWhitespace = builder.Length > 0;
                continue;
            }

            if (runeCount >= MaximumTextSnippetRunes)
            {
                wasTruncated = true;
                break;
            }

            if (hasPendingWhitespace)
            {
                builder.Append(' ');
            }

            builder.Append(rune.ToString());
            runeCount++;
            hasPendingWhitespace = false;
        }

        if (builder.Length == 0)
        {
            return "(whitespace)";
        }

        if (wasTruncated)
        {
            builder.Append("...");
        }

        return builder.ToString();
    }

    private void SetCanBeginEdit(bool value)
    {
        if (SetProperty(ref _canBeginEdit, value, nameof(CanBeginEdit)))
        {
            OnPropertyChanged(nameof(CanApplyEdit));
            OnPropertyChanged(nameof(CanCancelEdit));
        }
    }

    private void OnEditingStateChanged()
    {
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(CanChangeSelection));
        OnPropertyChanged(nameof(CanSelectTextEncoding));
        OnPropertyChanged(nameof(CanEditDraft));
        OnPropertyChanged(nameof(CanApplyEdit));
        OnPropertyChanged(nameof(CanCancelEdit));
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
