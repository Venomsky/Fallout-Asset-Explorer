using System.Buffers.Binary;

namespace FalloutAssetExplorer.Core;

public sealed record Fallout2PrototypeEditSessionField(
    Fallout2PrototypeEditorField Definition,
    long? OriginalValue,
    long? CurrentValue)
{
    public string Name => Definition.Name;
}

public sealed class Fallout2PrototypeEditSession
{
    private readonly record struct FieldValueChange(
        string FieldName,
        long BeforeValue,
        long AfterValue);

    private readonly byte[] _originalRecordBytes;
    private readonly Dictionary<string, Fallout2PrototypeEditSessionField> _fieldsByName;
    private readonly List<string> _fieldOrder;
    private readonly Fallout2PrototypeFieldDefinitionRegistry? _registry;
    private readonly Fallout2PrototypeInspectionContext? _inspection;
    private readonly bool _isSyntheticTestSession;
    private readonly Stack<FieldValueChange> _undoHistory = new();
    private readonly Stack<FieldValueChange> _redoHistory = new();
    private readonly object _syncRoot = new();

    private Fallout2PrototypeEditSession(
        ReadOnlyMemory<byte> originalRecordBytes,
        IReadOnlyList<Fallout2PrototypeEditorField> fields,
        bool isSyntheticTestSession,
        Fallout2PrototypeInspectionContext? inspection = null,
        Fallout2PrototypeFieldDefinitionRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(fields);

        if (!isSyntheticTestSession)
        {
            ArgumentNullException.ThrowIfNull(inspection);
            ArgumentNullException.ThrowIfNull(registry);
            if (!inspection.OriginalRecordBytes.Span.SequenceEqual(originalRecordBytes.Span))
            {
                throw new ArgumentException(
                    "The production inspection bytes must match the session source snapshot.",
                    nameof(inspection));
            }
        }

        _isSyntheticTestSession = isSyntheticTestSession;
        _inspection = inspection;
        _registry = registry;
        _originalRecordBytes = originalRecordBytes.ToArray();
        _fieldsByName = new Dictionary<string, Fallout2PrototypeEditSessionField>(
            fields.Count,
            StringComparer.Ordinal);
        _fieldOrder = new List<string>(fields.Count);

        foreach (var sourceField in fields)
        {
            ArgumentNullException.ThrowIfNull(sourceField);
            var field = sourceField.Copy();
            ValidateSourceField(field, _originalRecordBytes);
            if (!_fieldsByName.TryAdd(
                    field.Name,
                    new Fallout2PrototypeEditSessionField(
                        field,
                        field.CurrentNumericValue,
                        field.CurrentNumericValue)))
            {
                throw new ArgumentException(
                    $"The PRO edit session contains duplicate field identity '{field.Name}'.",
                    nameof(fields));
            }

            _fieldOrder.Add(field.Name);
        }
    }

    public static Fallout2PrototypeEditSession CreateProduction(
        Fallout2PrototypeInspectionContext inspection,
        Fallout2PrototypeFieldDefinitionRegistry registry,
        IReadOnlyList<Fallout2PrototypeEditorField> fields)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(fields);
        return new Fallout2PrototypeEditSession(
            inspection.OriginalRecordBytes,
            fields,
            isSyntheticTestSession: false,
            inspection,
            registry);
    }

    internal static Fallout2PrototypeEditSession CreateForTests(
        ReadOnlyMemory<byte> originalRecordBytes,
        IReadOnlyList<Fallout2PrototypeEditorField> fields) =>
        new(
            originalRecordBytes,
            fields,
            isSyntheticTestSession: true);

    public ReadOnlyMemory<byte> OriginalRecordBytes => _originalRecordBytes.ToArray();

    internal object SyncRoot => _syncRoot;

    public IReadOnlyList<Fallout2PrototypeEditSessionField> Fields
    {
        get
        {
            lock (_syncRoot)
            {
                return Array.AsReadOnly(_fieldOrder.Select(name => _fieldsByName[name]).ToArray());
            }
        }
    }

    public bool CanUndo
    {
        get
        {
            lock (_syncRoot)
            {
                return _undoHistory.Count > 0;
            }
        }
    }

    public bool CanRedo
    {
        get
        {
            lock (_syncRoot)
            {
                return _redoHistory.Count > 0;
            }
        }
    }

    public bool IsDirty
    {
        get
        {
            lock (_syncRoot)
            {
                return _fieldsByName.Values.Any(state => state.CurrentValue != state.OriginalValue);
            }
        }
    }

    public Fallout2PrototypeEditSessionField? GetField(string? name)
    {
        lock (_syncRoot)
        {
            return name is not null && _fieldsByName.TryGetValue(name, out var field)
                ? field
                : null;
        }
    }

    internal bool IsBoundTo(Fallout2PrototypeInspectionContext inspection)
    {
        lock (_syncRoot)
        {
            return ReferenceEquals(_inspection, inspection)
                && inspection.OriginalRecordBytes.Span.SequenceEqual(_originalRecordBytes);
        }
    }

    internal bool TryValidateDraftField(
        Fallout2PrototypeEditSessionField field,
        out bool authorized,
        out string? error)
    {
        lock (_syncRoot)
        {
            authorized = false;
            if (!_fieldsByName.TryGetValue(field.Name, out var sessionField)
                || !ReferenceEquals(sessionField.Definition, field.Definition)
                || sessionField.OriginalValue != field.OriginalValue
                || sessionField.CurrentValue != field.CurrentValue)
            {
                error = $"Field '{field.Name}' does not match the current PRO edit-session state.";
                return false;
            }

            if (!TryValidateEditableField(field.Definition, out error))
            {
                return false;
            }

            authorized = true;
            return TryValidateValue(field.Definition, field.CurrentValue!.Value, out error);
        }
    }

    public bool TrySetNumericValue(string? fieldName, long value, out string? error)
    {
        lock (_syncRoot)
        {
            return TrySetNumericValueCore(fieldName, value, out error);
        }
    }

    private bool TrySetNumericValueCore(string? fieldName, long value, out string? error)
    {
        if (string.IsNullOrWhiteSpace(fieldName)
            || !_fieldsByName.TryGetValue(fieldName, out var field))
        {
            error = "The PRO field is not part of this edit session.";
            return false;
        }

        if (!TryValidateEditableField(field.Definition, out error)
            || !TryValidateValue(field.Definition, value, out error))
        {
            return false;
        }

        if (field.CurrentValue == value)
        {
            error = null;
            return true;
        }

        var change = new FieldValueChange(field.Name, field.CurrentValue!.Value, value);
        ApplyValue(field, value);
        _undoHistory.Push(change);
        _redoHistory.Clear();
        error = null;
        return true;
    }

    public bool Undo(out string? error)
    {
        lock (_syncRoot)
        {
            return UndoCore(out error);
        }
    }

    private bool UndoCore(out string? error)
    {
        if (!_undoHistory.TryPeek(out var change))
        {
            error = "There is no PRO field change to undo.";
            return false;
        }

        if (!TryApplyHistoryChange(change, undo: true, out error))
        {
            return false;
        }

        _undoHistory.Pop();
        _redoHistory.Push(change);
        return true;
    }

    public bool Redo(out string? error)
    {
        lock (_syncRoot)
        {
            return RedoCore(out error);
        }
    }

    private bool RedoCore(out string? error)
    {
        if (!_redoHistory.TryPeek(out var change))
        {
            error = "There is no PRO field change to redo.";
            return false;
        }

        if (!TryApplyHistoryChange(change, undo: false, out error))
        {
            return false;
        }

        _redoHistory.Pop();
        _undoHistory.Push(change);
        return true;
    }

    private bool TryApplyHistoryChange(FieldValueChange change, bool undo, out string? error)
    {
        if (!_fieldsByName.TryGetValue(change.FieldName, out var field))
        {
            error = "The history entry refers to an unknown field in this PRO edit session.";
            return false;
        }

        var expectedValue = undo ? change.AfterValue : change.BeforeValue;
        if (field.CurrentValue != expectedValue)
        {
            error = "The current field value does not match the history entry.";
            return false;
        }

        if (!TryValidateEditableField(field.Definition, out error))
        {
            return false;
        }

        var targetValue = undo ? change.BeforeValue : change.AfterValue;
        if (!TryValidateValue(field.Definition, targetValue, out error))
        {
            return false;
        }

        ApplyValue(field, targetValue);
        error = null;
        return true;
    }

    private void ApplyValue(Fallout2PrototypeEditSessionField field, long value) =>
        _fieldsByName[field.Name] = field with { CurrentValue = value };

    private bool TryValidateEditableField(
        Fallout2PrototypeEditorField field,
        out string? error)
    {
        if (field.Editability != Fallout2PrototypeFieldEditability.EditingCandidate)
        {
            error = $"Field '{field.Name}' is read-only.";
            return false;
        }

        if (!_isSyntheticTestSession)
        {
            if (_registry is null || _inspection is null)
            {
                error = "The PRO field has no production registry or inspection context.";
                return false;
            }

            var resolution = _registry.TryResolve(_inspection, field, out var approval);
            if (resolution != Fallout2PrototypeFieldResolutionStatus.Resolved
                || approval is null
                || !_inspection.TryGetSchema(out var schema))
            {
                error = $"Field '{field.Name}' has no matching approved definition ({resolution}).";
                return false;
            }

            var approvedDefinition = approval.Definition;
            if (approvedDefinition.Schema != schema
                || !string.Equals(approvedDefinition.FieldId, field.Name, StringComparison.Ordinal)
                || approvedDefinition.Offset != field.Offset
                || approvedDefinition.Width != field.Length
                || approvedDefinition.Representation != field.ValueKind
                || approvedDefinition.MinimumValue != field.MinimumValue
                || approvedDefinition.MaximumValue != field.MaximumValue)
            {
                error = $"Field '{field.Name}' approval does not match its session definition.";
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(field.EditabilityRationale)
            || field.Evidence.Count == 0)
        {
            error = $"Field '{field.Name}' lacks an editability rationale or evidence.";
            return false;
        }

        if (field.ValueKind is not (Fallout2PrototypeFieldValueKind.Int32BigEndian
                or Fallout2PrototypeFieldValueKind.UInt8)
            || field.CurrentNumericValue is null)
        {
            error = $"Field '{field.Name}' has no supported numeric value model.";
            return false;
        }

        if (field.Offset is null
            || field.Length is null
            || field.MinimumValue is null
            || field.MaximumValue is null)
        {
            error = $"Field '{field.Name}' lacks a confirmed source range or complete validation bounds.";
            return false;
        }

        var expectedLength = field.ValueKind == Fallout2PrototypeFieldValueKind.Int32BigEndian
            ? sizeof(int)
            : sizeof(byte);
        if (field.Length != expectedLength)
        {
            error = $"Field '{field.Name}' source length does not match its numeric value kind.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryValidateValue(
        Fallout2PrototypeEditorField field,
        long value,
        out string? error)
    {
        var typeValid = field.ValueKind switch
        {
            Fallout2PrototypeFieldValueKind.Int32BigEndian => value is >= int.MinValue and <= int.MaxValue,
            Fallout2PrototypeFieldValueKind.UInt8 => value is >= byte.MinValue and <= byte.MaxValue,
            _ => false
        };
        if (!typeValid)
        {
            error = $"The value is outside the supported {field.ValueKind} representation.";
            return false;
        }

        if (field.MinimumValue is not long minimum
            || field.MaximumValue is not long maximum
            || value < minimum
            || value > maximum)
        {
            error = $"The value must be within the validated range {field.MinimumValue}..{field.MaximumValue}.";
            return false;
        }

        error = null;
        return true;
    }

    private static void ValidateSourceField(
        Fallout2PrototypeEditorField field,
        ReadOnlySpan<byte> originalRecordBytes)
    {
        if (field.Offset is not int offset || field.Length is not int length)
        {
            if (field.Editability == Fallout2PrototypeFieldEditability.EditingCandidate)
            {
                throw new ArgumentException(
                    $"Editing candidate '{field.Name}' has no source byte range.",
                    nameof(field));
            }

            return;
        }

        if (offset > originalRecordBytes.Length
            || length > originalRecordBytes.Length - offset)
        {
            throw new ArgumentException(
                $"Field '{field.Name}' lies outside the original PRO record.",
                nameof(field));
        }

        var sourceBytes = originalRecordBytes.Slice(offset, length);
        if (!sourceBytes.SequenceEqual(field.OriginalBytes.Span))
        {
            throw new ArgumentException(
                $"Field '{field.Name}' original bytes do not match the session source snapshot.",
                nameof(field));
        }

        var decodedValueMatches = field.ValueKind switch
        {
            Fallout2PrototypeFieldValueKind.Int32BigEndian when length == sizeof(int) =>
                BinaryPrimitives.ReadInt32BigEndian(sourceBytes) == field.CurrentNumericValue,
            Fallout2PrototypeFieldValueKind.UInt8 when length == sizeof(byte) =>
                sourceBytes[0] == field.CurrentNumericValue,
            _ => true
        };
        if (!decodedValueMatches)
        {
            throw new ArgumentException(
                $"Field '{field.Name}' parsed value does not match its original source bytes.",
                nameof(field));
        }
    }
}
