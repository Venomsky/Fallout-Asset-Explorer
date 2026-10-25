using System.Buffers.Binary;

namespace FalloutAssetExplorer.Core;

public enum Fallout2PrototypeEvidenceKind
{
    IndependentSpecification,
    IndependentImplementation,
    OriginalGameRecord,
    ProjectCode,
    TestFixture
}

public enum Fallout2PrototypeEvidenceStatus
{
    IndependentlyConfirmed,
    PartiallyConfirmed,
    ProjectCodeOnly,
    Hypothesis,
    Unresolved,
    SyntheticOnly
}

public enum Fallout2PrototypeFieldResolutionStatus
{
    Resolved,
    IncompleteInspection,
    NotRegistered,
    Ambiguous,
    DefinitionMismatch,
    RangeOutsideRecord,
    OriginalBytesMismatch,
    DecodedValueMismatch
}

public sealed class Fallout2PrototypeEvidenceClaim
{
    public Fallout2PrototypeEvidenceClaim(
        string sourceId,
        string sourceVersion,
        string location,
        string claimScope,
        string provenanceGroup,
        Fallout2PrototypeEvidenceKind kind,
        Fallout2PrototypeEvidenceStatus status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentException.ThrowIfNullOrWhiteSpace(claimScope);
        ArgumentException.ThrowIfNullOrWhiteSpace(provenanceGroup);

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        SourceId = sourceId;
        SourceVersion = sourceVersion;
        Location = location;
        ClaimScope = claimScope;
        ProvenanceGroup = provenanceGroup;
        Kind = kind;
        Status = status;
    }

    public string SourceId { get; }

    public string SourceVersion { get; }

    public string Location { get; }

    public string ClaimScope { get; }

    public string ProvenanceGroup { get; }

    public Fallout2PrototypeEvidenceKind Kind { get; }

    public Fallout2PrototypeEvidenceStatus Status { get; }
}

public sealed record Fallout2PrototypeSchemaKey
{
    public Fallout2PrototypeSchemaKey(
        FalloutGame game,
        ArchiveFormat format,
        int pidType,
        int subtype)
    {
        if (!Enum.IsDefined(game))
        {
            throw new ArgumentOutOfRangeException(nameof(game));
        }

        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format));
        }

        if (pidType < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pidType));
        }

        if (subtype < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(subtype));
        }

        Game = game;
        Format = format;
        PidType = pidType;
        Subtype = subtype;
    }

    public FalloutGame Game { get; }

    public ArchiveFormat Format { get; }

    public int PidType { get; }

    public int Subtype { get; }
}

public sealed class Fallout2PrototypeFieldDefinition
{
    public Fallout2PrototypeFieldDefinition(
        Fallout2PrototypeSchemaKey schema,
        string fieldId,
        Fallout2PrototypeFieldValueKind representation,
        int offset,
        int width,
        IReadOnlyList<Fallout2PrototypeEvidenceClaim> evidence,
        long? minimumValue = null,
        long? maximumValue = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldId);
        ArgumentNullException.ThrowIfNull(evidence);

        if (representation is not (Fallout2PrototypeFieldValueKind.Int32BigEndian
                or Fallout2PrototypeFieldValueKind.UInt8
                or Fallout2PrototypeFieldValueKind.RawBytes))
        {
            throw new ArgumentOutOfRangeException(nameof(representation));
        }

        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        var expectedWidth = representation switch
        {
            Fallout2PrototypeFieldValueKind.Int32BigEndian => sizeof(int),
            Fallout2PrototypeFieldValueKind.UInt8 => sizeof(byte),
            _ => width
        };
        if (width != expectedWidth)
        {
            throw new ArgumentException(
                "The field width does not match its representation.",
                nameof(width));
        }

        if (representation is Fallout2PrototypeFieldValueKind.Int32BigEndian
                or Fallout2PrototypeFieldValueKind.UInt8)
        {
            if (minimumValue is not long minimum
                || maximumValue is not long maximum
                || minimum > maximum)
            {
                throw new ArgumentException(
                    "Editable numeric definitions require an ordered minimum and maximum value.",
                    nameof(minimumValue));
            }

            var representableMinimum = representation == Fallout2PrototypeFieldValueKind.Int32BigEndian
                ? int.MinValue
                : byte.MinValue;
            var representableMaximum = representation == Fallout2PrototypeFieldValueKind.Int32BigEndian
                ? int.MaxValue
                : byte.MaxValue;
            if (minimum < representableMinimum || maximum > representableMaximum)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumValue),
                    "The validated range exceeds the field representation.");
            }
        }
        else if (minimumValue is not null || maximumValue is not null)
        {
            throw new ArgumentException(
                "Raw-byte definitions cannot declare numeric value bounds.",
                nameof(minimumValue));
        }

        if (evidence.Count == 0 || evidence.Any(claim => claim is null))
        {
            throw new ArgumentException("At least one complete evidence claim is required.", nameof(evidence));
        }

        Schema = schema;
        FieldId = fieldId;
        Representation = representation;
        Offset = offset;
        Width = width;
        MinimumValue = minimumValue;
        MaximumValue = maximumValue;
        Evidence = Array.AsReadOnly(evidence.ToArray());
    }

    public Fallout2PrototypeSchemaKey Schema { get; }

    public string FieldId { get; }

    public Fallout2PrototypeFieldValueKind Representation { get; }

    public int Offset { get; }

    public int Width { get; }

    public long? MinimumValue { get; }

    public long? MaximumValue { get; }

    public IReadOnlyList<Fallout2PrototypeEvidenceClaim> Evidence { get; }
}

public sealed class Fallout2PrototypeInspectionContext
{
    private readonly byte[] _originalRecordBytes;

    public Fallout2PrototypeInspectionContext(
        FalloutGame? game,
        ArchiveFormat? format,
        int? pidType,
        int? subtype,
        ReadOnlyMemory<byte> originalRecordBytes)
    {
        Game = game;
        Format = format;
        PidType = pidType;
        Subtype = subtype;
        _originalRecordBytes = originalRecordBytes.ToArray();
    }

    public FalloutGame? Game { get; }

    public ArchiveFormat? Format { get; }

    public int? PidType { get; }

    public int? Subtype { get; }

    public ReadOnlyMemory<byte> OriginalRecordBytes => _originalRecordBytes.ToArray();

    internal bool TryGetSchema(out Fallout2PrototypeSchemaKey? schema)
    {
        schema = null;
        if (Game is not FalloutGame game
            || Format is not ArchiveFormat format
            || !Enum.IsDefined(game)
            || !Enum.IsDefined(format)
            || PidType is not int pidType
            || Subtype is not int subtype
            || pidType < 0
            || subtype < 0)
        {
            return false;
        }

        schema = new Fallout2PrototypeSchemaKey(game, format, pidType, subtype);
        return true;
    }
}

public sealed class Fallout2ApprovedPrototypeField
{
    internal Fallout2ApprovedPrototypeField(Fallout2PrototypeFieldDefinition definition)
    {
        Definition = definition;
    }

    public Fallout2PrototypeFieldDefinition Definition { get; }
}

public sealed class Fallout2PrototypeFieldDefinitionRegistry
{
    private readonly IReadOnlyList<Fallout2PrototypeFieldDefinition> _definitions;

    private Fallout2PrototypeFieldDefinitionRegistry(
        IReadOnlyList<Fallout2PrototypeFieldDefinition> definitions)
    {
        _definitions = Array.AsReadOnly(definitions.ToArray());
    }

    public static Fallout2PrototypeFieldDefinitionRegistry Production { get; } =
        new(Array.Empty<Fallout2PrototypeFieldDefinition>());

    public IReadOnlyList<Fallout2PrototypeFieldDefinition> Definitions => _definitions;

    internal static Fallout2PrototypeFieldDefinitionRegistry CreateReviewed(
        IReadOnlyList<Fallout2PrototypeFieldDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        var copiedDefinitions = definitions.ToArray();
        if (copiedDefinitions.Any(definition => definition is null))
        {
            throw new ArgumentException("The registry cannot contain null field definitions.", nameof(definitions));
        }

        foreach (var definition in copiedDefinitions)
        {
            if (definition.Evidence.Any(claim =>
                    claim.Status != Fallout2PrototypeEvidenceStatus.IndependentlyConfirmed
                    || claim.Kind is not (Fallout2PrototypeEvidenceKind.IndependentSpecification
                        or Fallout2PrototypeEvidenceKind.IndependentImplementation
                        or Fallout2PrototypeEvidenceKind.OriginalGameRecord)))
            {
                throw new ArgumentException(
                    $"Field '{definition.FieldId}' does not have exclusively independently confirmed evidence.",
                    nameof(definitions));
            }
        }

        EnsureUniqueDefinitions(copiedDefinitions, nameof(definitions));
        return new Fallout2PrototypeFieldDefinitionRegistry(copiedDefinitions);
    }

    internal static Fallout2PrototypeFieldDefinitionRegistry CreateForTests(
        params Fallout2PrototypeFieldDefinition[] definitions) =>
        new(definitions);

    public Fallout2PrototypeFieldResolutionStatus TryResolve(
        Fallout2PrototypeInspectionContext inspection,
        Fallout2PrototypeEditorField field,
        out Fallout2ApprovedPrototypeField? approvedField)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        ArgumentNullException.ThrowIfNull(field);
        approvedField = null;

        if (!inspection.TryGetSchema(out var schema))
        {
            return Fallout2PrototypeFieldResolutionStatus.IncompleteInspection;
        }

        var matches = _definitions
            .Where(definition => definition.Schema == schema
                && string.Equals(definition.FieldId, field.Name, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (matches.Length == 0)
        {
            return Fallout2PrototypeFieldResolutionStatus.NotRegistered;
        }

        if (matches.Length > 1)
        {
            return Fallout2PrototypeFieldResolutionStatus.Ambiguous;
        }

        var definition = matches[0];
        if (field.Offset != definition.Offset
            || field.Length != definition.Width
            || field.ValueKind != definition.Representation
            || field.MinimumValue != definition.MinimumValue
            || field.MaximumValue != definition.MaximumValue)
        {
            return Fallout2PrototypeFieldResolutionStatus.DefinitionMismatch;
        }

        var recordBytes = inspection.OriginalRecordBytes.Span;
        if (definition.Offset > recordBytes.Length
            || definition.Width > recordBytes.Length - definition.Offset)
        {
            return Fallout2PrototypeFieldResolutionStatus.RangeOutsideRecord;
        }

        var sourceBytes = recordBytes.Slice(definition.Offset, definition.Width);
        if (!sourceBytes.SequenceEqual(field.OriginalBytes.Span))
        {
            return Fallout2PrototypeFieldResolutionStatus.OriginalBytesMismatch;
        }

        if (!DecodedValueMatches(field, sourceBytes))
        {
            return Fallout2PrototypeFieldResolutionStatus.DecodedValueMismatch;
        }

        approvedField = new Fallout2ApprovedPrototypeField(definition);
        return Fallout2PrototypeFieldResolutionStatus.Resolved;
    }

    private static bool DecodedValueMatches(
        Fallout2PrototypeEditorField field,
        ReadOnlySpan<byte> sourceBytes) =>
        field.ValueKind switch
        {
            Fallout2PrototypeFieldValueKind.Int32BigEndian when sourceBytes.Length == sizeof(int) =>
                BinaryPrimitives.ReadInt32BigEndian(sourceBytes) == field.CurrentNumericValue,
            Fallout2PrototypeFieldValueKind.UInt8 when sourceBytes.Length == sizeof(byte) =>
                sourceBytes[0] == field.CurrentNumericValue,
            Fallout2PrototypeFieldValueKind.RawBytes =>
                field.CurrentNumericValue is null,
            _ => false
        };

    private static void EnsureUniqueDefinitions(
        IReadOnlyList<Fallout2PrototypeFieldDefinition> definitions,
        string parameterName)
    {
        var identities = new HashSet<(Fallout2PrototypeSchemaKey Schema, string FieldId)>();
        foreach (var definition in definitions)
        {
            if (!identities.Add((definition.Schema, definition.FieldId)))
            {
                throw new ArgumentException(
                    $"The registry contains conflicting definitions for field '{definition.FieldId}'.",
                    parameterName);
            }
        }
    }
}
