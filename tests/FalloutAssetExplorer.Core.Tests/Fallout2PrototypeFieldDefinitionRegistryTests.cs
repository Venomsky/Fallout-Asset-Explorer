using System.Buffers.Binary;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class Fallout2PrototypeFieldDefinitionRegistryTests
{
    private static readonly Fallout2PrototypeSchemaKey Schema = new(
        FalloutGame.Fallout2,
        ArchiveFormat.Dat2,
        pidType: 0,
        subtype: 1);

    private static readonly Fallout2PrototypeEvidenceClaim SyntheticClaim = new(
        "registry-test-fixture",
        "test-only",
        nameof(Fallout2PrototypeFieldDefinitionRegistryTests),
        "Synthetic bytes exercise registry matching only.",
        "test-fixture",
        Fallout2PrototypeEvidenceKind.TestFixture,
        Fallout2PrototypeEvidenceStatus.SyntheticOnly);

    [Fact]
    public void TryResolve_RequiresExactSchemaAndFieldRepresentation()
    {
        var bytes = EncodeInt32(5);
        var definition = CreateDefinition();
        var registry = Fallout2PrototypeFieldDefinitionRegistry.CreateForTests(definition);
        var inspection = CreateInspection(bytes);
        var field = CreateField("quantity", bytes);

        var status = registry.TryResolve(inspection, field, out var approvedField);

        Assert.Equal(Fallout2PrototypeFieldResolutionStatus.Resolved, status);
        Assert.NotNull(approvedField);
        Assert.Same(definition, approvedField.Definition);
    }

    [Theory]
    [InlineData("game")]
    [InlineData("format")]
    [InlineData("pidType")]
    [InlineData("subtype")]
    public void TryResolve_RejectsAnySchemaMismatch(string mismatch)
    {
        var bytes = EncodeInt32(5);
        var registry = Fallout2PrototypeFieldDefinitionRegistry.CreateForTests(CreateDefinition());
        var inspection = mismatch switch
        {
            "game" => new Fallout2PrototypeInspectionContext(
                FalloutGame.Fallout1, ArchiveFormat.Dat2, 0, 1, bytes),
            "format" => new Fallout2PrototypeInspectionContext(
                FalloutGame.Fallout2, ArchiveFormat.Dat1, 0, 1, bytes),
            "pidType" => CreateInspection(bytes, pidType: 2),
            "subtype" => CreateInspection(bytes, subtype: 2),
            _ => throw new ArgumentOutOfRangeException(nameof(mismatch))
        };

        var status = registry.TryResolve(inspection, CreateField("quantity", bytes), out var approvedField);

        Assert.Equal(Fallout2PrototypeFieldResolutionStatus.NotRegistered, status);
        Assert.Null(approvedField);
    }

    [Fact]
    public void FieldDefinition_RejectsInvalidOffsetsAndWidths()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateDefinition(offset: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateDefinition(width: 0));
        Assert.Throws<ArgumentException>(() => CreateDefinition(width: sizeof(int) - 1));
    }

    [Fact]
    public void TryResolve_RejectsFieldOffsetWidthAndRepresentationMismatches()
    {
        var bytes = EncodeInt32(5);
        var registry = Fallout2PrototypeFieldDefinitionRegistry.CreateForTests(CreateDefinition());
        var inspection = CreateInspection(bytes);

        Assert.Equal(
            Fallout2PrototypeFieldResolutionStatus.DefinitionMismatch,
            registry.TryResolve(inspection, CreateField("quantity", bytes, offset: 1), out _));

        var rawField = new Fallout2PrototypeEditorField(
            "quantity",
            "Quantity",
            Fallout2PrototypeFieldValueKind.RawBytes,
            null,
            bytes,
            Fallout2PrototypeFieldEditability.ReadOnly,
            Fallout2PrototypeEditorField.ConservativeReadOnlyRationale,
            [CreateEditorEvidence()],
            offset: 0,
            length: bytes.Length);
        Assert.Equal(
            Fallout2PrototypeFieldResolutionStatus.DefinitionMismatch,
            registry.TryResolve(inspection, rawField, out _));

        var byteDefinition = CreateDefinition(
            representation: Fallout2PrototypeFieldValueKind.UInt8,
            width: sizeof(byte));
        var byteRegistry = Fallout2PrototypeFieldDefinitionRegistry.CreateForTests(byteDefinition);
        Assert.Equal(
            Fallout2PrototypeFieldResolutionStatus.DefinitionMismatch,
            byteRegistry.TryResolve(inspection, CreateField("quantity", bytes), out _));
    }

    [Fact]
    public void TryResolve_RejectsRangesOutsideTheOriginalRecord()
    {
        var bytes = EncodeInt32(5);
        var definition = CreateDefinition(offset: 2);
        var registry = Fallout2PrototypeFieldDefinitionRegistry.CreateForTests(definition);

        var status = registry.TryResolve(
            CreateInspection(bytes),
            CreateField("quantity", bytes, offset: 2),
            out var approvedField);

        Assert.Equal(Fallout2PrototypeFieldResolutionStatus.RangeOutsideRecord, status);
        Assert.Null(approvedField);
    }

    [Fact]
    public void TryResolve_RejectsMissingDefinitionsAndIncompleteInspection()
    {
        var bytes = EncodeInt32(5);
        var emptyRegistry = Fallout2PrototypeFieldDefinitionRegistry.Production;

        Assert.Equal(
            Fallout2PrototypeFieldResolutionStatus.NotRegistered,
            emptyRegistry.TryResolve(CreateInspection(bytes), CreateField("quantity", bytes), out var missing));
        Assert.Null(missing);

        var incompleteInspection = new Fallout2PrototypeInspectionContext(
            FalloutGame.Fallout2,
            ArchiveFormat.Dat2,
            null,
            1,
            bytes);
        Assert.Equal(
            Fallout2PrototypeFieldResolutionStatus.IncompleteInspection,
            emptyRegistry.TryResolve(incompleteInspection, CreateField("quantity", bytes), out var incomplete));
        Assert.Null(incomplete);
    }

    [Fact]
    public void TryResolve_RejectsAmbiguousDefinitions()
    {
        var bytes = EncodeInt32(5);
        var definition = CreateDefinition();
        var registry = Fallout2PrototypeFieldDefinitionRegistry.CreateForTests(definition, definition);

        var status = registry.TryResolve(
            CreateInspection(bytes),
            CreateField("quantity", bytes),
            out var approvedField);

        Assert.Equal(Fallout2PrototypeFieldResolutionStatus.Ambiguous, status);
        Assert.Null(approvedField);
    }

    [Fact]
    public void ReviewedRegistry_RejectsSyntheticEvidence()
    {
        var syntheticDefinition = CreateDefinition();

        Assert.Throws<ArgumentException>(() =>
            Fallout2PrototypeFieldDefinitionRegistry.CreateReviewed([syntheticDefinition]));
    }

    [Fact]
    public void RegistryAndInspectionSnapshotDefensivelyCopyInputs()
    {
        var evidence = new List<Fallout2PrototypeEvidenceClaim> { SyntheticClaim };
        var definition = CreateDefinition(evidence: evidence);
        var definitions = new List<Fallout2PrototypeFieldDefinition> { definition };
        var registry = Fallout2PrototypeFieldDefinitionRegistry.CreateForTests(definitions.ToArray());
        var sourceBytes = EncodeInt32(5);
        var inspection = CreateInspection(sourceBytes);

        evidence.Clear();
        definitions.Clear();
        sourceBytes[3] = 99;

        Assert.Single(definition.Evidence);
        Assert.Single(registry.Definitions);
        Assert.Equal(EncodeInt32(5), inspection.OriginalRecordBytes.ToArray());

        var exposedDefinitions = Assert.IsAssignableFrom<IList<Fallout2PrototypeFieldDefinition>>(
            registry.Definitions);
        Assert.Throws<NotSupportedException>(() => exposedDefinitions.Add(CreateDefinition()));
    }

    [Fact]
    public void ProductionRegistryDoesNotAuthorizeSyntheticDefinitionsOrChangeReadOnlyFields()
    {
        var bytes = EncodeInt32(5);
        var decodedField = new Fallout2PrototypeField("quantity", 0, sizeof(int), 5);
        var readOnlyField = Fallout2PrototypeEditorField.FromDecodedField(
            decodedField,
            "Quantity",
            bytes,
            [CreateEditorEvidence()]);

        var status = Fallout2PrototypeFieldDefinitionRegistry.Production.TryResolve(
            CreateInspection(bytes),
            readOnlyField,
            out var approvedField);

        Assert.Equal(Fallout2PrototypeFieldEditability.ReadOnly, readOnlyField.Editability);
        Assert.Equal(Fallout2PrototypeFieldResolutionStatus.NotRegistered, status);
        Assert.Null(approvedField);
        Assert.Empty(Fallout2PrototypeFieldDefinitionRegistry.Production.Definitions);
    }

    [Fact]
    public void ApprovedCapabilityAndRegistryCannotBeConstructedPublicly()
    {
        const System.Reflection.BindingFlags publicInstance =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
        const System.Reflection.BindingFlags publicStatic =
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public;

        Assert.Empty(typeof(Fallout2ApprovedPrototypeField).GetConstructors(publicInstance));
        Assert.Empty(typeof(Fallout2PrototypeFieldDefinitionRegistry).GetConstructors(publicInstance));
        Assert.Null(typeof(Fallout2PrototypeFieldDefinitionRegistry).GetMethod(
            "CreateReviewed",
            publicStatic));
        Assert.Null(typeof(Fallout2PrototypeFieldDefinitionRegistry).GetMethod(
            "CreateForTests",
            publicStatic));
    }

    [Fact]
    public void TryResolve_RejectsOriginalByteAndDecodedValueMismatches()
    {
        var bytes = EncodeInt32(5);
        var registry = Fallout2PrototypeFieldDefinitionRegistry.CreateForTests(CreateDefinition());

        Assert.Equal(
            Fallout2PrototypeFieldResolutionStatus.OriginalBytesMismatch,
            registry.TryResolve(
                CreateInspection(EncodeInt32(6)),
                CreateField("quantity", bytes),
                out _));

        Assert.Equal(
            Fallout2PrototypeFieldResolutionStatus.DecodedValueMismatch,
            registry.TryResolve(
                CreateInspection(bytes),
                CreateField("quantity", bytes, currentValue: 6),
                out _));
    }

    private static Fallout2PrototypeFieldDefinition CreateDefinition(
        int offset = 0,
        int width = sizeof(int),
        Fallout2PrototypeFieldValueKind representation = Fallout2PrototypeFieldValueKind.Int32BigEndian,
        IReadOnlyList<Fallout2PrototypeEvidenceClaim>? evidence = null,
        long? minimumValue = 0,
        long? maximumValue = 10) =>
        new(
            Schema,
            "quantity",
            representation,
            offset,
            width,
            evidence ?? [SyntheticClaim],
            minimumValue,
            maximumValue);

    private static Fallout2PrototypeInspectionContext CreateInspection(
        ReadOnlyMemory<byte> bytes,
        int pidType = 0,
        int subtype = 1) =>
        new(FalloutGame.Fallout2, ArchiveFormat.Dat2, pidType, subtype, bytes);

    private static Fallout2PrototypeEditorField CreateField(
        string name,
        byte[] bytes,
        int offset = 0,
        long currentValue = 5) =>
        new(
            name,
            name,
            Fallout2PrototypeFieldValueKind.Int32BigEndian,
            currentValue,
            bytes,
            Fallout2PrototypeFieldEditability.ReadOnly,
            Fallout2PrototypeEditorField.ConservativeReadOnlyRationale,
            [CreateEditorEvidence()],
            offset,
            bytes.Length,
            minimumValue: 0,
            maximumValue: 10);

    private static Fallout2PrototypeFieldEvidence CreateEditorEvidence() =>
        new(
            "tests/FalloutAssetExplorer.Core.Tests/Fallout2PrototypeFieldDefinitionRegistryTests.cs",
            Fallout2PrototypeFieldEvidenceSource.Test,
            Fallout2PrototypeFieldEvidenceVerification.SyntheticData,
            "Synthetic registry test fixture only.");

    private static byte[] EncodeInt32(int value)
    {
        var bytes = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        return bytes;
    }
}
