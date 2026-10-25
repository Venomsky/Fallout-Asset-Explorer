using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using FalloutAssetExplorer.Core;

namespace FalloutAssetExplorer.App.ViewModels;

public sealed class ProEditorViewModel : INotifyPropertyChanged
{
    private const int MaximumRawBytesDisplayed = 4096;
    private const string ModifiedRecordSaveUnavailable =
        "The PRO Editor cannot save modified PRO records; Archive Browser raw-byte export is separate and preserves inspected source bytes.";

    private static readonly IReadOnlyList<Fallout2PrototypeFieldEvidence> FieldEvidence =
        Array.AsReadOnly(
        [
            new Fallout2PrototypeFieldEvidence(
                "src/FalloutAssetExplorer.Core/Fallout2PrototypeResolver.cs",
                Fallout2PrototypeFieldEvidenceSource.ProductionCode,
                Fallout2PrototypeFieldEvidenceVerification.Unverified,
                "Field metadata is supplied by the current resolver; standalone selection is not verified against a prototype list.")
        ]);

    private Fallout2StandalonePrototypeInspection? _inspection;
    private IReadOnlyList<ProEditorFieldViewModel> _fields = [];
    private IReadOnlyList<string> _rawByteLines = [];
    private Fallout2PrototypeEditSession? _session;
    private string _statusMessage = "Select a .PRO entry in Archive Browser to inspect it here.";
    private string _fieldModelStatus = string.Empty;
    private string _rawByteTruncationNotice = string.Empty;
    private string _sessionStatus = "No prototype is selected.";
    private string _sessionActionStatus = "No changes to undo or redo.";
    private string _sampleSourceReference = string.Empty;
    private string _sampleGameEditionVersion = string.Empty;
    private string _sampleRecordIdentity = string.Empty;
    private string _sampleAcquisitionMethod = string.Empty;
    private string _sampleExpectedPidHex = string.Empty;
    private string _sampleExpectedPidReference = string.Empty;
    private Fallout2PrototypeSampleVerification? _sampleVerification;
    private string _sampleStatusMessage = "No standalone PRO sample selected.";

    public event PropertyChangedEventHandler? PropertyChanged;

    public Fallout2StandalonePrototypeInspection? Inspection
    {
        get => _inspection;
        private set
        {
            if (SetProperty(ref _inspection, value))
            {
                OnPropertyChanged(nameof(HasInspection));
                OnPropertyChanged(nameof(HasNoInspection));
                OnPropertyChanged(nameof(ArchiveSourceDisplay));
                OnPropertyChanged(nameof(EntryPathDisplay));
                OnPropertyChanged(nameof(GameDisplay));
                OnPropertyChanged(nameof(FormatDisplay));
                OnPropertyChanged(nameof(PrototypePidDisplay));
                OnPropertyChanged(nameof(PidTypeDisplay));
                OnPropertyChanged(nameof(SubtypeDisplay));
                OnPropertyChanged(nameof(VerificationDisplay));
                OnPropertyChanged(nameof(OutcomeDisplay));
                OnPropertyChanged(nameof(ReadCompletionDisplay));
                OnPropertyChanged(nameof(HasSupportedSubtype));
            }
        }
    }

    public bool HasInspection => Inspection is not null;

    public bool HasNoInspection => !HasInspection;

    public string EmptyStateMessage =>
        "Select a .PRO entry in Archive Browser. This workspace uses only the selected entry; it does not index archives.";

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string SampleSourceReference
    {
        get => _sampleSourceReference;
        set => SetSampleInput(ref _sampleSourceReference, value, nameof(SampleSourceReference));
    }

    public string SampleGameEditionVersion
    {
        get => _sampleGameEditionVersion;
        set => SetSampleInput(ref _sampleGameEditionVersion, value, nameof(SampleGameEditionVersion));
    }

    public string SampleRecordIdentity
    {
        get => _sampleRecordIdentity;
        set => SetSampleInput(ref _sampleRecordIdentity, value, nameof(SampleRecordIdentity));
    }

    public string SampleAcquisitionMethod
    {
        get => _sampleAcquisitionMethod;
        set => SetSampleInput(ref _sampleAcquisitionMethod, value, nameof(SampleAcquisitionMethod));
    }

    public string SampleExpectedPidHex
    {
        get => _sampleExpectedPidHex;
        set => SetSampleInput(ref _sampleExpectedPidHex, value, nameof(SampleExpectedPidHex));
    }

    public string SampleExpectedPidReference
    {
        get => _sampleExpectedPidReference;
        set => SetSampleInput(ref _sampleExpectedPidReference, value, nameof(SampleExpectedPidReference));
    }

    public Fallout2PrototypeSampleVerification? SampleVerification
    {
        get => _sampleVerification;
        private set
        {
            if (SetProperty(ref _sampleVerification, value))
            {
                OnPropertyChanged(nameof(HasSampleVerification));
                OnPropertyChanged(nameof(SampleStructureDisplay));
                OnPropertyChanged(nameof(SampleObservedDataDisplay));
                OnPropertyChanged(nameof(SampleProvenanceDisplay));
            }
        }
    }

    public bool HasSampleVerification => SampleVerification is not null;

    public string SampleStatusMessage
    {
        get => _sampleStatusMessage;
        private set => SetProperty(ref _sampleStatusMessage, value);
    }

    public string SampleStructureDisplay => SampleVerification is { } verification
        ? $"{verification.StructureStatus}: {verification.StructureNote}{Environment.NewLine}{string.Join(Environment.NewLine, verification.ValidationMessages)}"
        : "No sample has been inspected.";

    public string SampleObservedDataDisplay
    {
        get
        {
            if (SampleVerification is not { } verification)
            {
                return "No observations available.";
            }

            var lines = new List<string>
            {
                $"Selected file: {verification.SelectedFileName}",
                $"Length: {verification.FileLengthBytes:N0} bytes",
                $"SHA-256: {verification.Sha256 ?? "Not calculated (over inspection limit)"}",
                $"Embedded PID bytes: {verification.RawPidBytesHex ?? "Unavailable"}",
                $"Embedded PID (FAE big-endian convention): {(verification.EmbeddedPid is uint pid ? $"0x{pid:X8}" : "Unavailable")}",
                $"PID type / prototype index: {(verification.PidType is int pidType ? pidType.ToString(CultureInfo.InvariantCulture) : "Unavailable")} / {(verification.PrototypeIndex is uint prototypeIndex ? prototypeIndex.ToString(CultureInfo.InvariantCulture) : "Unavailable")}",
                $"Subtype (FAE convention): {(verification.Subtype is int subtype ? subtype.ToString(CultureInfo.InvariantCulture) : "Unavailable")}",
                $"Reported expected PID comparison: {verification.IdentityComparison}"
            };

            if (verification.MaterialCandidate is { } candidate)
            {
                lines.Add($"Raw bytes at 0x24 (candidate field; uninterpreted): {candidate.RawBytesHex}");
                lines.Add($"Big-endian numeric display at 0x24 (not a signedness claim): {candidate.BigEndianNumericValue} (0x{candidate.BigEndianNumericValue:X8})");
            }
            else
            {
                lines.Add("Candidate bytes at 0x24 were not reported for this result.");
            }

            return string.Join(Environment.NewLine, lines);
        }
    }

    public string SampleProvenanceDisplay => SampleVerification is { } verification
        ? string.Join(
            Environment.NewLine,
            verification.Provenance.Select(item =>
                $"{item.Status}: {item.Name} = {item.Value ?? "Unknown"}"))
        : "No provenance metadata available.";

    public string SampleUnresolvedClaimsDisplay =>
        "Unresolved: source authenticity, whether bytes at 0x24 mean material, signedness, complete value domain, subtype coverage, canonical subtype-specific length, and read/write or retail round-trip behavior.";

    public string ArchiveSourceDisplay => Inspection?.ArchivePath ?? "Unavailable";

    public string EntryPathDisplay => Inspection?.EntryPath ?? "Unavailable";

    public string GameDisplay => Inspection?.Game?.ToString() ?? "Unavailable";

    public string FormatDisplay => Inspection?.Format?.ToString() ?? "Unavailable";

    public string PrototypePidDisplay => Inspection?.PrototypePid is int pid
        ? $"0x{unchecked((uint)pid):X8}"
        : "Unavailable";

    public string PidTypeDisplay => Inspection?.PidType is int pidType
        ? Inspection.PidTypeName is { } name
            ? $"{name} ({pidType})"
            : $"Unsupported ({pidType})"
        : "Unavailable";

    public string SubtypeDisplay => Inspection?.Subtype is int subtype
        ? Inspection.SubtypeName is { } name
            ? $"{name} ({subtype})"
            : $"Unsupported ({subtype})"
        : "Unavailable";

    public string VerificationDisplay => Inspection?.Provenance
        ?? "No prototype verification is available until an entry is selected.";

    public string OutcomeDisplay => Inspection?.Outcome.ToString() ?? "Not inspected";

    public string ReadCompletionDisplay => Inspection is null
        ? "Not inspected"
        : Inspection.IsReadComplete ? "Complete" : "Partial";

    public bool HasSupportedSubtype => Inspection?.HasSupportedSubtype == true;

    public IReadOnlyList<ProEditorFieldViewModel> Fields
    {
        get => _fields;
        private set
        {
            if (SetProperty(ref _fields, value))
            {
                OnPropertyChanged(nameof(HasFields));
                OnPropertyChanged(nameof(HasNoFields));
            }
        }
    }

    public bool HasFields => Fields.Count > 0;

    public bool HasNoFields => !HasFields;

    public string NoFieldsMessage => string.IsNullOrEmpty(FieldModelStatus)
        ? "No semantic fields are available; the original bytes and inspection diagnostics are retained."
        : FieldModelStatus;

    public string FieldModelStatus
    {
        get => _fieldModelStatus;
        private set
        {
            if (SetProperty(ref _fieldModelStatus, value))
            {
                OnPropertyChanged(nameof(NoFieldsMessage));
                OnPropertyChanged(nameof(HasFieldModelStatus));
            }
        }
    }

    public bool HasFieldModelStatus => !string.IsNullOrEmpty(FieldModelStatus);

    public IReadOnlyList<string> RawByteLines
    {
        get => _rawByteLines;
        private set => SetProperty(ref _rawByteLines, value);
    }

    public string RawByteTruncationNotice
    {
        get => _rawByteTruncationNotice;
        private set
        {
            if (SetProperty(ref _rawByteTruncationNotice, value))
            {
                OnPropertyChanged(nameof(HasRawByteTruncationNotice));
            }
        }
    }

    public bool HasRawByteTruncationNotice => !string.IsNullOrEmpty(RawByteTruncationNotice);

    public string SessionStatus
    {
        get => _sessionStatus;
        private set => SetProperty(ref _sessionStatus, value);
    }

    public bool IsDirty => _session?.IsDirty == true;

    public string IsDirtyDisplay => IsDirty
        ? $"In-memory changes are pending. {ModifiedRecordSaveUnavailable}"
        : "No in-memory changes.";

    public bool CanUndo => _session?.CanUndo == true;

    public bool CanRedo => _session?.CanRedo == true;

    public string SessionActionStatus
    {
        get => _sessionActionStatus;
        private set => SetProperty(ref _sessionActionStatus, value);
    }

    public void SetStatus(string status)
    {
        ArgumentNullException.ThrowIfNull(status);
        StatusMessage = status;
    }

    public void VerifyStandaloneSample(string selectedFileName, ReadOnlyMemory<byte> fileBytes)
    {
        var provenance = new Fallout2PrototypeSampleProvenance(
            SourceReference: SampleSourceReference,
            GameEditionVersion: SampleGameEditionVersion,
            RecordIdentity: SampleRecordIdentity,
            AcquisitionMethod: SampleAcquisitionMethod,
            ExpectedPidHex: SampleExpectedPidHex,
            ExpectedPidReference: SampleExpectedPidReference);
        SampleVerification = Fallout2PrototypeSampleVerifier.Verify(
            selectedFileName,
            fileBytes.Span,
            provenance);
        SampleStatusMessage = $"Standalone sample inspected: {selectedFileName}. The source file was not changed.";
    }

    public void ReportSampleVerificationFailure(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        SampleVerification = null;
        SampleStatusMessage = message;
    }

    public void ReportSampleVerificationCancelled()
    {
        SampleVerification = null;
        SampleStatusMessage = "Standalone PRO selection was cancelled; no verification is displayed.";
    }

    public void SetInspection(
        Fallout2StandalonePrototypeInspection? inspection,
        IReadOnlyList<string>? formattedRawByteLines = null)
    {
        Inspection = inspection;
        _session = null;
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(IsDirtyDisplay));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        SessionActionStatus = "No changes to undo or redo.";
        FieldModelStatus = string.Empty;
        Fields = [];
        RawByteLines = [];
        RawByteTruncationNotice = string.Empty;
        SessionStatus = inspection is null
            ? "No prototype is selected."
            : "No in-memory snapshot is attached.";

        if (inspection is null)
        {
            StatusMessage = "Select a .PRO entry in Archive Browser to inspect it here.";
            return;
        }

        StatusMessage = inspection.Status;
        SetRawByteLines(inspection.RawBytes, formattedRawByteLines);

        var decodedFields = inspection.ItemFields.Count > 0
            ? inspection.ItemFields
            : inspection.SceneryFields;
        Fallout2PrototypeEditorField[] fieldDefinitions;
        try
        {
            fieldDefinitions = decodedFields
                .Select(field => Fallout2PrototypeEditorField.FromDecodedField(
                    field,
                    field.Name,
                    inspection.RawBytes,
                    FieldEvidence))
                .ToArray();
        }
        catch (ArgumentException exception)
        {
            FieldModelStatus =
                $"Decoded field metadata could not be represented safely: {exception.Message}";
            return;
        }

        if (inspection.IsReadComplete
            && inspection.HasSupportedSubtype
            && inspection.Game == FalloutGame.Fallout2
            && inspection.Format == ArchiveFormat.Dat2
            && fieldDefinitions.Length > 0)
        {
            try
            {
                _session = Fallout2PrototypeEditSession.CreateProduction(
                    new Fallout2PrototypeInspectionContext(
                        inspection.Game,
                        inspection.Format,
                        inspection.PidType,
                        inspection.Subtype,
                        inspection.RawBytes),
                    Fallout2PrototypeFieldDefinitionRegistry.Production,
                    fieldDefinitions);
                SessionStatus =
                    $"In-memory snapshot attached. Production fields remain read-only. {ModifiedRecordSaveUnavailable}";
            }
            catch (ArgumentException exception)
            {
                SessionStatus = $"In-memory snapshot unavailable: {exception.Message}";
            }
        }
        else if (!inspection.IsReadComplete)
        {
            SessionStatus =
                "No in-memory snapshot is attached because the selected entry was not read completely.";
        }
        else if (!inspection.HasSupportedSubtype)
        {
            SessionStatus =
                "No in-memory snapshot is attached because the subtype is unsupported.";
        }
        else if (inspection.Game != FalloutGame.Fallout2
            || inspection.Format != ArchiveFormat.Dat2)
        {
            SessionStatus =
                "No in-memory snapshot is attached because semantic PRO fields are supported only for Fallout 2 DAT2 inspections.";
        }
        else
        {
            SessionStatus =
                "No in-memory snapshot is attached because no decoded fields are available.";
        }

        Fields = Array.AsReadOnly(fieldDefinitions
            .Select((field, index) => new ProEditorFieldViewModel(
                field,
                _session?.GetField(field.Name)?.CurrentValue ?? field.CurrentNumericValue,
                decodedFields[index].DisplayAsHex,
                decodedFields[index].IsFlags))
            .ToArray());
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(IsDirtyDisplay));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    public void Undo()
    {
        if (_session is null)
        {
            SessionActionStatus = "Undo is unavailable because no in-memory session is attached.";
            return;
        }

        if (!_session.Undo(out var error))
        {
            SessionActionStatus = error ?? "Undo could not be applied.";
            return;
        }

        RefreshSessionFields();
        SessionActionStatus = "Undo applied in memory only; original bytes are unchanged.";
    }

    public void Redo()
    {
        if (_session is null)
        {
            SessionActionStatus = "Redo is unavailable because no in-memory session is attached.";
            return;
        }

        if (!_session.Redo(out var error))
        {
            SessionActionStatus = error ?? "Redo could not be applied.";
            return;
        }

        RefreshSessionFields();
        SessionActionStatus = "Redo applied in memory only; original bytes are unchanged.";
    }

    private void RefreshSessionFields()
    {
        if (_session is null)
        {
            return;
        }

        Fields = Array.AsReadOnly(Fields
            .Select(field => field.WithCurrentValue(
                _session.GetField(field.Name)?.CurrentValue ?? field.OriginalNumericValue))
            .ToArray());
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(IsDirtyDisplay));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private void SetRawByteLines(
        ReadOnlyMemory<byte> rawBytes,
        IReadOnlyList<string>? formattedLines)
    {
        const int bytesPerLine = 16;
        var displayedLength = Math.Min(rawBytes.Length, MaximumRawBytesDisplayed);
        var maximumLines = (displayedLength + bytesPerLine - 1) / bytesPerLine;
        var lines = formattedLines is null
            ? FormatRawByteLines(rawBytes.Span[..displayedLength])
            : formattedLines.Take(maximumLines).ToArray();
        RawByteLines = Array.AsReadOnly(lines.ToArray());
        RawByteTruncationNotice = rawBytes.Length > displayedLength
            ? $"{rawBytes.Length - displayedLength:N0} additional bytes omitted from this view; the inspection retains the original available bytes."
            : string.Empty;
    }

    private static string[] FormatRawByteLines(ReadOnlySpan<byte> bytes)
    {
        const int bytesPerLine = 16;
        var lines = new string[(bytes.Length + bytesPerLine - 1) / bytesPerLine];
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var offset = lineIndex * bytesPerLine;
            var length = Math.Min(bytesPerLine, bytes.Length - offset);
            var line = new StringBuilder(10 + length * 3);
            line.Append(offset.ToString("X8", CultureInfo.InvariantCulture));
            line.Append("  ");
            for (var index = 0; index < length; index++)
            {
                if (index > 0)
                {
                    line.Append(' ');
                }

                line.Append(bytes[offset + index].ToString("X2", CultureInfo.InvariantCulture));
            }

            lines[lineIndex] = line.ToString();
        }

        return lines;
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
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void SetSampleInput(ref string field, string value, string propertyName)
    {
        if (SetProperty(ref field, value, propertyName))
        {
            if (SampleVerification is not null)
            {
                SampleVerification = null;
                SampleStatusMessage =
                    "Previous sample verification cleared because provenance inputs changed; inspect the sample again.";
            }
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class ProEditorFieldViewModel
{
    public ProEditorFieldViewModel(
        Fallout2PrototypeEditorField definition,
        long? currentNumericValue,
        bool displayAsHex = false,
        bool isFlags = false)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Definition = definition;
        CurrentNumericValue = currentNumericValue;
        DisplayAsHex = displayAsHex;
        IsFlags = isFlags;
    }

    public Fallout2PrototypeEditorField Definition { get; }

    public string Name => Definition.Name;

    public string UiLabel => Definition.UiLabel;

    public long? OriginalNumericValue => Definition.CurrentNumericValue;

    public long? CurrentNumericValue { get; }

    public bool DisplayAsHex { get; }

    public bool IsFlags { get; }

    public ProEditorFieldViewModel WithCurrentValue(long? currentNumericValue) =>
        new(Definition, currentNumericValue, DisplayAsHex, IsFlags);

    public string OriginalValueDisplay => FormatValue(OriginalNumericValue);

    public string CurrentValueDisplay => FormatValue(CurrentNumericValue);

    public string ValueKindDisplay => IsFlags
        ? $"{Definition.ValueKind} (flags; bit meanings not decoded)"
        : Definition.ValueKind.ToString();

    public bool HasInterpretation => !string.IsNullOrWhiteSpace(Definition.Interpretation);

    public string InterpretationDisplay => Definition.Interpretation ?? string.Empty;

    public string EditabilityDisplay => Definition.Editability.ToString();

    public string EditabilityRationale => Definition.EditabilityRationale;

    public string EvidenceDisplay => Definition.Evidence.Count == 0
        ? "No evidence recorded."
        : string.Join(
            Environment.NewLine,
            Definition.Evidence.Select(evidence =>
                $"{evidence.Verification}: {evidence.RepositoryPath} — {evidence.Note}"));

    public string OffsetDisplay => Definition.Offset is int offset && Definition.Length is int length
        ? $"0x{offset:X} ({length} bytes)"
        : "Unavailable";

    public string ValidationErrorDisplay => "None reported for read-only inspection.";

    private string FormatValue(long? value)
    {
        if (value is not long numericValue)
        {
            return Convert.ToHexString(Definition.OriginalBytes.Span);
        }

        if (DisplayAsHex || IsFlags)
        {
            if (Definition.ValueKind == Fallout2PrototypeFieldValueKind.Int32BigEndian
                && numericValue is >= int.MinValue and <= int.MaxValue)
            {
                var rawBits = unchecked((uint)(int)numericValue);
                return $"0x{rawBits:X8} (int32 BE: {numericValue.ToString(CultureInfo.InvariantCulture)})";
            }

            if (Definition.ValueKind == Fallout2PrototypeFieldValueKind.UInt8
                && numericValue is >= byte.MinValue and <= byte.MaxValue)
            {
                return $"0x{numericValue:X2} (uint8: {numericValue.ToString(CultureInfo.InvariantCulture)})";
            }
        }

        return numericValue.ToString(CultureInfo.InvariantCulture);
    }
}
