using System.Buffers.Binary;
using System.Reflection;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class Fallout2PrototypeEditSessionAuthorizationTests
{
    private static readonly Fallout2PrototypeSchemaKey Schema = new(
        FalloutGame.Fallout2,
        ArchiveFormat.Dat2,
        pidType: 0,
        subtype: 1);

    private static readonly Fallout2PrototypeEvidenceClaim SyntheticClaim = new(
        "session-authorization-test",
        "test-only",
        nameof(Fallout2PrototypeEditSessionAuthorizationTests),
        "Synthetic bytes test session authorization only.",
        "test-fixture",
        Fallout2PrototypeEvidenceKind.TestFixture,
        Fallout2PrototypeEvidenceStatus.SyntheticOnly);

    [Fact]
    public void ProductionSession_RejectsSyntheticCandidateWhenRegistryIsEmpty()
    {
        var bytes = EncodeInt32(5);
        var session = Fallout2PrototypeEditSession.CreateProduction(
            CreateInspection(bytes),
            Fallout2PrototypeFieldDefinitionRegistry.Production,
            [CreateCandidate("quantity", bytes)]);

        Assert.False(session.TrySetNumericValue("quantity", 8, out var error));

        Assert.Contains("NotRegistered", error);
        Assert.Equal(5, session.GetField("quantity")!.CurrentValue);
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.Equal(bytes, session.OriginalRecordBytes.ToArray());
        Assert.False(session.Undo(out var undoError));
        Assert.Contains("no PRO field change", undoError);
        Assert.False(session.Redo(out var redoError));
        Assert.Contains("no PRO field change", redoError);
        Assert.Equal(5, session.GetField("quantity")!.CurrentValue);
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void ProductionSession_RejectsDefinitionForDifferentInspectionContext()
    {
        var bytes = EncodeInt32(5);
        var registry = CreateTestRegistry("quantity");
        var session = Fallout2PrototypeEditSession.CreateProduction(
            CreateInspection(bytes, subtype: 2),
            registry,
            [CreateCandidate("quantity", bytes)]);

        Assert.False(session.TrySetNumericValue("quantity", 8, out var error));

        Assert.Contains("NotRegistered", error);
        Assert.Equal(5, session.GetField("quantity")!.CurrentValue);
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void ProductionSession_RejectsCandidateWithValueBoundsDifferentFromRegistry()
    {
        var bytes = EncodeInt32(5);
        var session = Fallout2PrototypeEditSession.CreateProduction(
            CreateInspection(bytes),
            CreateTestRegistry("quantity"),
            [CreateCandidate("quantity", bytes, maximumValue: 100)]);

        Assert.False(session.TrySetNumericValue("quantity", 8, out var error));

        Assert.Contains("DefinitionMismatch", error);
        Assert.Equal(5, session.GetField("quantity")!.CurrentValue);
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void RejectedUnregisteredField_PreservesProductionSessionHistory()
    {
        var firstBytes = EncodeInt32(5);
        var secondBytes = EncodeInt32(2);
        var recordBytes = firstBytes.Concat(secondBytes).ToArray();
        var session = Fallout2PrototypeEditSession.CreateProduction(
            CreateInspection(recordBytes),
            CreateTestRegistry("registeredForTest", offset: 0),
            [
                CreateCandidate("registeredForTest", firstBytes, offset: 0),
                CreateCandidate(
                    "unregisteredSynthetic",
                    secondBytes,
                    offset: sizeof(int),
                    currentValue: 2)
            ]);

        Assert.True(session.TrySetNumericValue("registeredForTest", 6, out _));
        Assert.True(session.TrySetNumericValue("registeredForTest", 7, out _));
        Assert.True(session.Undo(out _));

        Assert.False(session.TrySetNumericValue("unregisteredSynthetic", 3, out var error));

        Assert.Contains("NotRegistered", error);
        Assert.Equal(6, session.GetField("registeredForTest")!.CurrentValue);
        Assert.Equal(2, session.GetField("unregisteredSynthetic")!.CurrentValue);
        Assert.True(session.IsDirty);
        Assert.True(session.CanUndo);
        Assert.True(session.CanRedo);

        Assert.True(session.Redo(out _));
        Assert.Equal(7, session.GetField("registeredForTest")!.CurrentValue);
        Assert.Equal(2, session.GetField("unregisteredSynthetic")!.CurrentValue);
        Assert.True(session.Undo(out _));
        Assert.Equal(6, session.GetField("registeredForTest")!.CurrentValue);
        Assert.Equal(2, session.GetField("unregisteredSynthetic")!.CurrentValue);
        Assert.True(session.CanUndo);
        Assert.True(session.CanRedo);
    }

    [Fact]
    public void ProductionSession_RejectsInspectionBytesThatDifferFromSessionSnapshot()
    {
        Assert.Throws<ArgumentException>(() => Fallout2PrototypeEditSession.CreateProduction(
            CreateInspection(EncodeInt32(5)),
            Fallout2PrototypeFieldDefinitionRegistry.Production,
            [CreateCandidate("quantity", EncodeInt32(6))]));
    }

    [Fact]
    public void ProductionSessionHasNoPublicConstructorAndSyntheticPathIsInternal()
    {
        const BindingFlags publicInstance = BindingFlags.Instance | BindingFlags.Public;

        Assert.Empty(typeof(Fallout2PrototypeEditSession).GetConstructors(publicInstance));
        Assert.True(typeof(Fallout2PrototypeEditSession).GetMethod(
            "CreateForTests",
            BindingFlags.Static | BindingFlags.NonPublic) is not null);
        Assert.True(typeof(Fallout2PrototypeEditSession).GetMethod(
            "CreateProduction",
            BindingFlags.Static | BindingFlags.Public) is not null);
    }

    private static Fallout2PrototypeFieldDefinitionRegistry CreateTestRegistry(
        string fieldId,
        int offset = 0) =>
        Fallout2PrototypeFieldDefinitionRegistry.CreateForTests(
            new Fallout2PrototypeFieldDefinition(
                Schema,
                fieldId,
                Fallout2PrototypeFieldValueKind.Int32BigEndian,
                offset,
                width: sizeof(int),
                evidence: [SyntheticClaim],
                minimumValue: 0,
                maximumValue: 10));

    private static Fallout2PrototypeInspectionContext CreateInspection(
        ReadOnlyMemory<byte> bytes,
        int subtype = 1) =>
        new(FalloutGame.Fallout2, ArchiveFormat.Dat2, 0, subtype, bytes);

    private static Fallout2PrototypeEditorField CreateCandidate(
        string name,
        byte[] bytes,
        int offset = 0,
        long maximumValue = 10,
        long currentValue = 5) =>
        new(
            name,
            name,
            Fallout2PrototypeFieldValueKind.Int32BigEndian,
            currentValue,
            bytes,
            Fallout2PrototypeFieldEditability.EditingCandidate,
            "Synthetic candidate; no production-format claim.",
            [new Fallout2PrototypeFieldEvidence(
                "tests/FalloutAssetExplorer.Core.Tests/Fallout2PrototypeEditSessionAuthorizationTests.cs",
                Fallout2PrototypeFieldEvidenceSource.Test,
                Fallout2PrototypeFieldEvidenceVerification.SyntheticData,
                "Synthetic authorization fixture only.")],
            offset: offset,
            length: sizeof(int),
            minimumValue: 0,
            maximumValue: maximumValue);

    private static byte[] EncodeInt32(int value)
    {
        var bytes = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        return bytes;
    }
}
