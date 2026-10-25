using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class Fallout2PrototypeEditorFieldTests
{
    private static readonly Fallout2PrototypeFieldEvidence SyntheticTestEvidence = new(
        "tests/FalloutAssetExplorer.Core.Tests/Fallout2PrototypeResolverTests.cs",
        Fallout2PrototypeFieldEvidenceSource.Test,
        Fallout2PrototypeFieldEvidenceVerification.SyntheticData,
        "The resolver field offsets are asserted using synthetic prototype bytes.");

    [Fact]
    public void FromDecodedField_PreservesTypedValueOffsetLengthAndSourceBytesAsReadOnly()
    {
        byte[] record = [0x00, 0x00, 0x00, 0x00, 0x12, 0x34, 0x56, 0x78, 0xA5];
        var parsedField = new Fallout2PrototypeField("flags", 4, sizeof(int), 0x12345678, IsFlags: true);

        var model = Fallout2PrototypeEditorField.FromDecodedField(
            parsedField,
            "Flags",
            record,
            [SyntheticTestEvidence]);

        Assert.Equal("flags", model.Name);
        Assert.Equal("Flags", model.UiLabel);
        Assert.Equal(Fallout2PrototypeFieldValueKind.Int32BigEndian, model.ValueKind);
        Assert.Equal(0x12345678, model.CurrentNumericValue);
        Assert.Equal(Fallout2PrototypeFieldEditability.ReadOnly, model.Editability);
        Assert.Null(model.MinimumValue);
        Assert.Null(model.MaximumValue);
        Assert.Equal(4, model.Offset);
        Assert.Equal(sizeof(int), model.Length);
        Assert.Equal(new byte[] { 0x12, 0x34, 0x56, 0x78 }, model.OriginalBytes.ToArray());

        record[4] = 0xFF;
        Assert.Equal(new byte[] { 0x12, 0x34, 0x56, 0x78 }, model.OriginalBytes.ToArray());
    }

    [Fact]
    public void FromDecodedField_UsesByteStorageOnlyForOneByteFields()
    {
        byte[] record = [0x2A];
        var parsedField = new Fallout2PrototypeField(
            "field_34",
            0,
            sizeof(byte),
            0x2A,
            Interpretation: "Uninterpreted byte; sound ID is not confirmed.",
            StorageType: "uint8");

        var model = Fallout2PrototypeEditorField.FromDecodedField(
            parsedField,
            "field_34",
            record,
            [SyntheticTestEvidence]);

        Assert.Equal(Fallout2PrototypeFieldValueKind.UInt8, model.ValueKind);
        Assert.Equal(0x2A, model.CurrentNumericValue);
        Assert.Equal(parsedField.Interpretation, model.Interpretation);
        Assert.Equal(new byte[] { 0x2A }, model.OriginalBytes.ToArray());
        Assert.Equal(Fallout2PrototypeFieldEditability.ReadOnly, model.Editability);
    }

    [Fact]
    public void FromDecodedField_LeavesUnrecognizedStorageTypeUnknownWithoutLosingValueOrBytes()
    {
        byte[] record = [0xDE, 0xAD];
        var parsedField = new Fallout2PrototypeField("futureField", 0, 2, 0x1234, StorageType: "unrecognized");

        var model = Fallout2PrototypeEditorField.FromDecodedField(
            parsedField,
            "Future field",
            record,
            [SyntheticTestEvidence]);

        Assert.Equal(Fallout2PrototypeFieldValueKind.Unknown, model.ValueKind);
        Assert.Equal(0x1234, model.CurrentNumericValue);
        Assert.Equal(record, model.OriginalBytes.ToArray());
        Assert.Equal(0, model.Offset);
        Assert.Equal(2, model.Length);
    }

    [Fact]
    public void FromUninterpretedBytes_RetainsUnknownDataWithoutInventingOffsetsOrMeaning()
    {
        byte[] original = [0xFE, 0xED, 0xFA, 0xCE];

        var model = Fallout2PrototypeEditorField.FromUninterpretedBytes(
            "unknownPayload",
            "Unknown payload",
            original,
            "The field boundary and meaning have not been established.",
            [SyntheticTestEvidence]);

        Assert.Equal(Fallout2PrototypeFieldValueKind.RawBytes, model.ValueKind);
        Assert.Null(model.CurrentNumericValue);
        Assert.Null(model.Offset);
        Assert.Null(model.Length);
        Assert.Null(model.Interpretation);
        Assert.Equal(Fallout2PrototypeFieldEditability.ReadOnly, model.Editability);
        Assert.Equal(original, model.OriginalBytes.ToArray());

        original[0] = 0;
        Assert.Equal(new byte[] { 0xFE, 0xED, 0xFA, 0xCE }, model.OriginalBytes.ToArray());
    }

    [Fact]
    public void CandidateForEditing_RequiresAndPreservesItsSourceRange()
    {
        var candidate = new Fallout2PrototypeEditorField(
            "validatedQuantity",
            "Quantity",
            Fallout2PrototypeFieldValueKind.Int32BigEndian,
            5,
            new byte[] { 0, 0, 0, 5 },
            Fallout2PrototypeFieldEditability.EditingCandidate,
            "Synthetic test fixture only; not a classification of a production PRO field.",
            [SyntheticTestEvidence],
            offset: 12,
            length: sizeof(int),
            minimumValue: 0,
            maximumValue: 10);

        Assert.Equal(Fallout2PrototypeFieldEditability.EditingCandidate, candidate.Editability);
        Assert.Equal(0, candidate.MinimumValue);
        Assert.Equal(10, candidate.MaximumValue);
        Assert.Equal(12, candidate.Offset);
        Assert.Equal(new byte[] { 0, 0, 0, 5 }, candidate.OriginalBytes.ToArray());
    }

    [Fact]
    public void Constructor_RejectsInvalidValuesRangesAndMissingEvidence()
    {
        Assert.Throws<ArgumentException>(() => new Fallout2PrototypeEditorField(
            "byte",
            "Byte",
            Fallout2PrototypeFieldValueKind.UInt8,
            256,
            new byte[] { 0xFF },
            Fallout2PrototypeFieldEditability.ReadOnly,
            "Invalid test value.",
            [SyntheticTestEvidence],
            offset: 0,
            length: 1));
        Assert.Throws<ArgumentException>(() => new Fallout2PrototypeEditorField(
            "bounded",
            "Bounded",
            Fallout2PrototypeFieldValueKind.Int32BigEndian,
            11,
            new byte[] { 0, 0, 0, 11 },
            Fallout2PrototypeFieldEditability.ReadOnly,
            "Invalid test range.",
            [SyntheticTestEvidence],
            offset: 0,
            length: 4,
            minimumValue: 0,
            maximumValue: 10));
        Assert.Throws<ArgumentException>(() => new Fallout2PrototypeEditorField(
            "missingEvidence",
            "Missing evidence",
            Fallout2PrototypeFieldValueKind.Int32BigEndian,
            0,
            new byte[] { 0, 0, 0, 0 },
            Fallout2PrototypeFieldEditability.ReadOnly,
            "Evidence is required.",
            [],
            offset: 0,
            length: 4));
    }

    [Fact]
    public void FromDecodedField_RejectsIncompleteOrOutOfRangeSourceFields()
    {
        var invalidRanges = new[]
        {
            new Fallout2PrototypeField("negative", -1, 1, 0, StorageType: "uint8"),
            new Fallout2PrototypeField("pastEnd", 2, 1, 0, StorageType: "uint8"),
            new Fallout2PrototypeField("truncated", 1, sizeof(int), 0)
        };

        foreach (var field in invalidRanges)
        {
            Assert.Throws<ArgumentException>(() => Fallout2PrototypeEditorField.FromDecodedField(
                field,
                "Invalid field",
                new byte[] { 0, 0 },
                [SyntheticTestEvidence]));
        }
    }
}
