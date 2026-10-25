namespace FalloutAssetExplorer.Core;

public enum Fallout2PrototypeFieldValueKind
{
    Int32BigEndian,
    UInt8,
    RawBytes,
    Unknown
}

public enum Fallout2PrototypeFieldEditability
{
    ReadOnly,
    EditingCandidate
}

public enum Fallout2PrototypeFieldEvidenceSource
{
    ProductionCode,
    Test,
    Documentation
}

public enum Fallout2PrototypeFieldEvidenceVerification
{
    RealGameData,
    SyntheticData,
    Unverified
}

public sealed record Fallout2PrototypeFieldEvidence(
    string RepositoryPath,
    Fallout2PrototypeFieldEvidenceSource Source,
    Fallout2PrototypeFieldEvidenceVerification Verification,
    string Note);

public sealed class Fallout2PrototypeEditorField
{
    public const string ConservativeReadOnlyRationale =
        "The current PRO workflow is inspection-only; field-level editing and round-trip preservation are not implemented.";

    public Fallout2PrototypeEditorField(
        string name,
        string uiLabel,
        Fallout2PrototypeFieldValueKind valueKind,
        long? currentNumericValue,
        ReadOnlyMemory<byte> originalBytes,
        Fallout2PrototypeFieldEditability editability,
        string editabilityRationale,
        IReadOnlyList<Fallout2PrototypeFieldEvidence> evidence,
        int? offset = null,
        int? length = null,
        long? minimumValue = null,
        long? maximumValue = null,
        string? interpretation = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(uiLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(editabilityRationale);
        ArgumentNullException.ThrowIfNull(evidence);

        if (!Enum.IsDefined(valueKind))
        {
            throw new ArgumentOutOfRangeException(nameof(valueKind));
        }

        if (!Enum.IsDefined(editability))
        {
            throw new ArgumentOutOfRangeException(nameof(editability));
        }

        if ((offset is null) != (length is null))
        {
            throw new ArgumentException("Offset and length must either both be provided or both be unknown.");
        }

        if (offset is int knownOffset && (knownOffset < 0 || length is not > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "A known byte range must have a nonnegative offset and positive length.");
        }

        if (length is int knownLength && originalBytes.Length != knownLength)
        {
            throw new ArgumentException("Original field bytes must match the declared field length.", nameof(originalBytes));
        }

        ValidateValue(valueKind, currentNumericValue, originalBytes);
        ValidateRange(currentNumericValue, minimumValue, maximumValue);
        ValidateEvidence(evidence);

        if (editability == Fallout2PrototypeFieldEditability.EditingCandidate
            && (offset is null || originalBytes.IsEmpty))
        {
            throw new ArgumentException(
                "An editing candidate must retain its source byte range.",
                nameof(editability));
        }

        Name = name;
        UiLabel = uiLabel;
        ValueKind = valueKind;
        CurrentNumericValue = currentNumericValue;
        _originalBytes = originalBytes.ToArray();
        Editability = editability;
        EditabilityRationale = editabilityRationale;
        Evidence = Array.AsReadOnly(evidence.ToArray());
        Offset = offset;
        Length = length;
        MinimumValue = minimumValue;
        MaximumValue = maximumValue;
        Interpretation = interpretation;
    }

    private readonly byte[] _originalBytes;

    public string Name { get; }

    public string UiLabel { get; }

    public Fallout2PrototypeFieldValueKind ValueKind { get; }

    public long? CurrentNumericValue { get; }

    public ReadOnlyMemory<byte> OriginalBytes => _originalBytes.ToArray();

    public Fallout2PrototypeFieldEditability Editability { get; }

    public string EditabilityRationale { get; }

    public IReadOnlyList<Fallout2PrototypeFieldEvidence> Evidence { get; }

    public int? Offset { get; }

    public int? Length { get; }

    public long? MinimumValue { get; }

    public long? MaximumValue { get; }

    public string? Interpretation { get; }

    public static Fallout2PrototypeEditorField FromDecodedField(
        Fallout2PrototypeField field,
        string uiLabel,
        ReadOnlyMemory<byte> originalRecord,
        IReadOnlyList<Fallout2PrototypeFieldEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(field);

        if (field.Offset < 0
            || field.SizeInBytes <= 0
            || field.Offset > originalRecord.Length
            || field.SizeInBytes > originalRecord.Length - field.Offset)
        {
            throw new ArgumentException(
                "The decoded field range is outside the available original record bytes.",
                nameof(field));
        }

        var valueKind = (field.StorageType, field.SizeInBytes) switch
        {
            ("int32 BE", sizeof(int)) => Fallout2PrototypeFieldValueKind.Int32BigEndian,
            ("uint8", sizeof(byte)) => Fallout2PrototypeFieldValueKind.UInt8,
            _ => Fallout2PrototypeFieldValueKind.Unknown
        };

        return new Fallout2PrototypeEditorField(
            field.Name,
            uiLabel,
            valueKind,
            field.Value,
            originalRecord.Slice(field.Offset, field.SizeInBytes),
            Fallout2PrototypeFieldEditability.ReadOnly,
            ConservativeReadOnlyRationale,
            evidence,
            field.Offset,
            field.SizeInBytes,
            interpretation: field.Interpretation);
    }

    public static Fallout2PrototypeEditorField FromUninterpretedBytes(
        string name,
        string uiLabel,
        ReadOnlyMemory<byte> originalBytes,
        string rationale,
        IReadOnlyList<Fallout2PrototypeFieldEvidence> evidence) =>
        new(
            name,
            uiLabel,
            Fallout2PrototypeFieldValueKind.RawBytes,
            null,
            originalBytes,
            Fallout2PrototypeFieldEditability.ReadOnly,
            rationale,
            evidence);

    internal Fallout2PrototypeEditorField Copy() =>
        new(
            Name,
            UiLabel,
            ValueKind,
            CurrentNumericValue,
            _originalBytes,
            Editability,
            EditabilityRationale,
            Evidence,
            Offset,
            Length,
            MinimumValue,
            MaximumValue,
            Interpretation);

    private static void ValidateValue(
        Fallout2PrototypeFieldValueKind valueKind,
        long? currentNumericValue,
        ReadOnlyMemory<byte> originalBytes)
    {
        switch (valueKind)
        {
            case Fallout2PrototypeFieldValueKind.Int32BigEndian:
                if (currentNumericValue is not long signedValue
                    || signedValue < int.MinValue
                    || signedValue > int.MaxValue)
                {
                    throw new ArgumentException("An int32 field requires a value within the signed 32-bit range.", nameof(currentNumericValue));
                }
                break;
            case Fallout2PrototypeFieldValueKind.UInt8:
                if (currentNumericValue is not long byteValue || byteValue is < byte.MinValue or > byte.MaxValue)
                {
                    throw new ArgumentException("A uint8 field requires a value within the unsigned 8-bit range.", nameof(currentNumericValue));
                }
                break;
            case Fallout2PrototypeFieldValueKind.RawBytes:
                if (currentNumericValue is not null || originalBytes.IsEmpty)
                {
                    throw new ArgumentException("A raw-byte field requires nonempty bytes and no numeric value.");
                }
                break;
            case Fallout2PrototypeFieldValueKind.Unknown:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(valueKind));
        }
    }

    private static void ValidateRange(long? value, long? minimumValue, long? maximumValue)
    {
        if ((minimumValue is not null || maximumValue is not null) && value is null)
        {
            throw new ArgumentException("A numeric validation bound requires a current numeric value.");
        }

        if (minimumValue is long minimum && maximumValue is long maximum && minimum > maximum)
        {
            throw new ArgumentException("The minimum value cannot exceed the maximum value.");
        }

        if (value is long current
            && (minimumValue is long lower && current < lower
                || maximumValue is long upper && current > upper))
        {
            throw new ArgumentException("The current value is outside the supplied validation bounds.");
        }
    }

    private static void ValidateEvidence(IReadOnlyList<Fallout2PrototypeFieldEvidence> evidence)
    {
        if (evidence.Count == 0)
        {
            throw new ArgumentException("At least one evidence reference is required.", nameof(evidence));
        }

        foreach (var item in evidence)
        {
            if (item is null
                || string.IsNullOrWhiteSpace(item.RepositoryPath)
                || string.IsNullOrWhiteSpace(item.Note)
                || !Enum.IsDefined(item.Source)
                || !Enum.IsDefined(item.Verification))
            {
                throw new ArgumentException("Evidence references must include a path, note, and valid classification.", nameof(evidence));
            }
        }
    }
}
