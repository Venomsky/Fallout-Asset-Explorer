using System.Buffers.Binary;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class Fallout2PrototypeRecordDraftBuilderTests
{
    private static readonly Fallout2PrototypeSchemaKey Schema = new(
        FalloutGame.Fallout2,
        ArchiveFormat.Dat2,
        pidType: 0,
        subtype: 1);

    private static readonly Fallout2PrototypeEvidenceClaim SyntheticClaim = new(
        "record-draft-test",
        "test-only",
        nameof(Fallout2PrototypeRecordDraftBuilderTests),
        "Synthetic fixture for in-memory record draft behavior only.",
        "test-fixture",
        Fallout2PrototypeEvidenceKind.TestFixture,
        Fallout2PrototypeEvidenceStatus.SyntheticOnly);

    private static readonly Fallout2PrototypeFieldEvidence SyntheticFieldEvidence = new(
        "tests/FalloutAssetExplorer.Core.Tests/Fallout2PrototypeRecordDraftBuilderTests.cs",
        Fallout2PrototypeFieldEvidenceSource.Test,
        Fallout2PrototypeFieldEvidenceVerification.SyntheticData,
        "Synthetic draft test fixture only.");

    [Fact]
    public void BuildWithoutChangesReturnsByteIdenticalDraft()
    {
        byte[] sourceBytes = [0xA1, 0x00, 0x00, 0x00, 0x05, 0xB2];
        var fixture = CreateSession(
            sourceBytes,
            new FieldSpec("quantity", Fallout2PrototypeFieldValueKind.Int32BigEndian, 1, 4, 0, 10));

        var result = Fallout2PrototypeRecordDraftBuilder.Build(fixture.Inspection, fixture.Session);

        Assert.Equal(Fallout2PrototypeRecordDraftStatus.Success, result.Status);
        Assert.Equal(0, result.ChangedFieldCount);
        Assert.Equal(sourceBytes, result.RecordBytes.ToArray());
        Assert.Contains("no fields have changed", result.Diagnostic);
    }

    [Fact]
    public void BuildAppliesSingleAuthorizedInt32ChangeInBigEndian()
    {
        byte[] sourceBytes = [0xA1, 0x00, 0x00, 0x00, 0x05, 0xB2];
        var fixture = CreateSession(
            sourceBytes,
            new FieldSpec("quantity", Fallout2PrototypeFieldValueKind.Int32BigEndian, 1, 4, 0, 10));
        Assert.True(fixture.Session.TrySetNumericValue("quantity", 9, out _));

        var result = Fallout2PrototypeRecordDraftBuilder.Build(fixture.Inspection, fixture.Session);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.ChangedFieldCount);
        Assert.Equal(new byte[] { 0xA1, 0x00, 0x00, 0x00, 0x09, 0xB2 }, result.RecordBytes.ToArray());
    }

    [Fact]
    public void BuildAppliesMultipleNonOverlappingChangesAndPreservesAllOtherBytes()
    {
        byte[] sourceBytes = [0xA1, 0x00, 0x00, 0x00, 0x05, 0xB2, 0xC3, 0x07, 0xD4];
        var fixture = CreateSession(
            sourceBytes,
            new FieldSpec("quantity", Fallout2PrototypeFieldValueKind.Int32BigEndian, 1, 4, 0, 10),
            new FieldSpec("soundCode", Fallout2PrototypeFieldValueKind.UInt8, 7, 1, 0, 255));
        Assert.True(fixture.Session.TrySetNumericValue("quantity", 9, out _));
        Assert.True(fixture.Session.TrySetNumericValue("soundCode", 0xA5, out _));

        var result = Fallout2PrototypeRecordDraftBuilder.Build(fixture.Inspection, fixture.Session);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.ChangedFieldCount);
        Assert.Equal(
            new byte[] { 0xA1, 0x00, 0x00, 0x00, 0x09, 0xB2, 0xC3, 0xA5, 0xD4 },
            result.RecordBytes.ToArray());
    }

    [Fact]
    public void BuildAfterUndoAndRedoReflectsCurrentSessionValues()
    {
        byte[] sourceBytes = [0xA1, 0x00, 0x00, 0x00, 0x05, 0xB2];
        var fixture = CreateSession(
            sourceBytes,
            new FieldSpec("quantity", Fallout2PrototypeFieldValueKind.Int32BigEndian, 1, 4, 0, 10));
        Assert.True(fixture.Session.TrySetNumericValue("quantity", 9, out _));

        Assert.True(fixture.Session.Undo(out _));
        var undone = Fallout2PrototypeRecordDraftBuilder.Build(fixture.Inspection, fixture.Session);
        Assert.True(undone.IsSuccess);
        Assert.Equal(sourceBytes, undone.RecordBytes.ToArray());
        Assert.Equal(0, undone.ChangedFieldCount);

        Assert.True(fixture.Session.Redo(out _));
        var redone = Fallout2PrototypeRecordDraftBuilder.Build(fixture.Inspection, fixture.Session);
        Assert.True(redone.IsSuccess);
        Assert.Equal(new byte[] { 0xA1, 0x00, 0x00, 0x00, 0x09, 0xB2 }, redone.RecordBytes.ToArray());
    }

    [Fact]
    public void BuildWaitsForConcurrentSessionMutationLock()
    {
        byte[] sourceBytes = [0x00, 0x00, 0x00, 0x05];
        var fixture = CreateSession(
            sourceBytes,
            new FieldSpec("quantity", Fallout2PrototypeFieldValueKind.Int32BigEndian, 0, 4, 0, 10));
        using var buildStarted = new ManualResetEventSlim();
        using var buildCompleted = new ManualResetEventSlim();
        Fallout2PrototypeRecordDraftResult? result = null;
        Exception? buildError = null;
        var buildThread = new Thread(() =>
        {
            buildStarted.Set();
            try
            {
                result = Fallout2PrototypeRecordDraftBuilder.Build(fixture.Inspection, fixture.Session);
            }
            catch (Exception exception)
            {
                buildError = exception;
            }
            finally
            {
                buildCompleted.Set();
            }
        });

        Monitor.Enter(fixture.Session.SyncRoot);
        try
        {
            buildThread.Start();
            Assert.True(buildStarted.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(SpinWait.SpinUntil(
                () => (buildThread.ThreadState & ThreadState.WaitSleepJoin) != 0,
                TimeSpan.FromSeconds(5)));
            Assert.False(buildCompleted.IsSet);
        }
        finally
        {
            Monitor.Exit(fixture.Session.SyncRoot);
        }

        Assert.True(buildCompleted.Wait(TimeSpan.FromSeconds(5)));
        Assert.Null(buildError);
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Equal(sourceBytes, result.RecordBytes.ToArray());
    }

    [Fact]
    public void BuildRejectsOverlappingAuthorizedChangesWithoutChangingSource()
    {
        byte[] sourceBytes = [0xA1, 0, 0, 0, 5, 0, 0, 0, 7, 0xB2];
        var fixture = CreateSession(
            sourceBytes,
            new FieldSpec("first", Fallout2PrototypeFieldValueKind.Int32BigEndian, 1, 4, 0, 10),
            new FieldSpec(
                "second",
                Fallout2PrototypeFieldValueKind.Int32BigEndian,
                3,
                4,
                int.MinValue,
                int.MaxValue));
        Assert.True(fixture.Session.TrySetNumericValue("first", 8, out _));
        Assert.True(fixture.Session.TrySetNumericValue("second", 9, out _));

        var result = Fallout2PrototypeRecordDraftBuilder.Build(fixture.Inspection, fixture.Session);

        Assert.Equal(Fallout2PrototypeRecordDraftStatus.OverlappingRanges, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Empty(result.RecordBytes.ToArray());
        Assert.Contains("overlapping", result.Diagnostic);
        Assert.Equal(new byte[] { 0xA1, 0, 0, 0, 5, 0, 0, 0, 7, 0xB2 }, sourceBytes);
        Assert.Equal(sourceBytes, fixture.Session.OriginalRecordBytes.ToArray());
    }

    [Fact]
    public void BuildRejectsDifferentInspectionContext()
    {
        byte[] sourceBytes = [0x00, 0x00, 0x00, 0x05];
        var fixture = CreateSession(
            sourceBytes,
            new FieldSpec("quantity", Fallout2PrototypeFieldValueKind.Int32BigEndian, 0, 4, 0, 10));
        var otherInspection = new Fallout2PrototypeInspectionContext(
            FalloutGame.Fallout2,
            ArchiveFormat.Dat2,
            pidType: 0,
            subtype: 1,
            sourceBytes);

        var result = Fallout2PrototypeRecordDraftBuilder.Build(otherInspection, fixture.Session);

        Assert.Equal(Fallout2PrototypeRecordDraftStatus.InspectionMismatch, result.Status);
        Assert.Empty(result.RecordBytes.ToArray());
    }

    [Fact]
    public void UnregisteredCandidateCannotBeEditedOrAppearInDraft()
    {
        byte[] sourceBytes = [0x00, 0x00, 0x00, 0x05];
        var fixture = CreateSession(
            sourceBytes,
            Fallout2PrototypeFieldDefinitionRegistry.Production,
            new FieldSpec("quantity", Fallout2PrototypeFieldValueKind.Int32BigEndian, 0, 4, 0, 10));

        Assert.False(fixture.Session.TrySetNumericValue("quantity", 8, out var error));
        Assert.Contains("NotRegistered", error);

        var result = Fallout2PrototypeRecordDraftBuilder.Build(fixture.Inspection, fixture.Session);

        Assert.True(result.IsSuccess);
        Assert.Equal(sourceBytes, result.RecordBytes.ToArray());
        Assert.Equal(0, result.ChangedFieldCount);
    }

    [Fact]
    public void SessionRejectsCandidateRangeOutsideSnapshotBeforeDraftCanBeBuilt()
    {
        byte[] sourceBytes = [0x00, 0x00, 0x00, 0x05];
        var inspection = CreateInspection(sourceBytes);
        var registry = CreateRegistry(
            new FieldSpec("quantity", Fallout2PrototypeFieldValueKind.Int32BigEndian, 1, 4, 0, 10));

        Assert.Throws<ArgumentException>(() => Fallout2PrototypeEditSession.CreateProduction(
            inspection,
            registry,
            [
                new Fallout2PrototypeEditorField(
                    "quantity",
                    "quantity",
                    Fallout2PrototypeFieldValueKind.Int32BigEndian,
                    5,
                    new byte[] { 0, 0, 0, 5 },
                    Fallout2PrototypeFieldEditability.EditingCandidate,
                    "Synthetic out-of-range fixture.",
                    [SyntheticFieldEvidence],
                    offset: 1,
                    length: sizeof(int),
                    minimumValue: 0,
                    maximumValue: 10)
            ]));
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x05 }, sourceBytes);
    }

    [Fact]
    public void EditSessionRejectsUnsupportedRepresentationBeforeItCanEnterDraft()
    {
        byte[] sourceBytes = [0x00, 0x00, 0x00, 0x05];
        var field = new Fallout2PrototypeEditorField(
            "unknown",
            "unknown",
            Fallout2PrototypeFieldValueKind.Unknown,
            5,
            sourceBytes,
            Fallout2PrototypeFieldEditability.EditingCandidate,
            "Synthetic unsupported-representation fixture.",
            [SyntheticFieldEvidence],
            offset: 0,
            length: sizeof(int),
            minimumValue: 0,
            maximumValue: 10);
        var session = Fallout2PrototypeEditSession.CreateForTests(sourceBytes, [field]);

        Assert.False(session.TrySetNumericValue("unknown", 6, out var error));

        Assert.Contains("supported numeric value model", error);
        Assert.False(session.IsDirty);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x05 }, sourceBytes);
        Assert.Equal(sourceBytes, session.OriginalRecordBytes.ToArray());
    }

    [Fact]
    public void BuildOutputAndSessionNeverExposeMutableSourceArrays()
    {
        byte[] sourceBytes = [0x00, 0x00, 0x00, 0x05, 0xA1];
        var fixture = CreateSession(
            sourceBytes,
            new FieldSpec("quantity", Fallout2PrototypeFieldValueKind.Int32BigEndian, 0, 4, 0, 10));
        Assert.True(fixture.Session.TrySetNumericValue("quantity", 8, out _));

        var result = Fallout2PrototypeRecordDraftBuilder.Build(fixture.Inspection, fixture.Session);
        var returnedDraft = result.RecordBytes.ToArray();
        returnedDraft[0] = 0xFF;
        var returnedSnapshot = fixture.Session.OriginalRecordBytes.ToArray();
        returnedSnapshot[0] = 0xEE;

        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x05, 0xA1 }, sourceBytes);
        Assert.Equal(sourceBytes, fixture.Session.OriginalRecordBytes.ToArray());
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x08, 0xA1 }, result.RecordBytes.ToArray());
    }

    [Fact]
    public void SessionRejectsInconsistentFieldBytesBeforeDraftCanBeBuilt()
    {
        byte[] sourceBytes = [0x00, 0x00, 0x00, 0x05];
        var inspection = CreateInspection(sourceBytes);
        var inconsistentField = CreateCandidate(
            new FieldSpec("quantity", Fallout2PrototypeFieldValueKind.Int32BigEndian, 0, 4, 0, 10),
            new byte[] { 0, 0, 0, 6 });
        var registry = CreateRegistry(
            new FieldSpec("quantity", Fallout2PrototypeFieldValueKind.Int32BigEndian, 0, 4, 0, 10));

        Assert.Throws<ArgumentException>(() => Fallout2PrototypeEditSession.CreateProduction(
            inspection,
            registry,
            [inconsistentField]));
        Assert.Equal(new byte[] { 0, 0, 0, 5 }, sourceBytes);
    }

    [Fact]
    public void BuildEncodesSignedInt32AndUInt8AtTheirDeclaredWidths()
    {
        byte[] sourceBytes = [0x00, 0, 0, 0, 0, 0x7F];
        var fixture = CreateSession(
            sourceBytes,
            new FieldSpec("signedValue", Fallout2PrototypeFieldValueKind.Int32BigEndian, 0, 4, -10, 10),
            new FieldSpec("byteValue", Fallout2PrototypeFieldValueKind.UInt8, 5, 1, 0, 255));
        Assert.True(fixture.Session.TrySetNumericValue("signedValue", -2, out _));
        Assert.True(fixture.Session.TrySetNumericValue("byteValue", 0xFE, out _));

        var result = Fallout2PrototypeRecordDraftBuilder.Build(fixture.Inspection, fixture.Session);

        Assert.True(result.IsSuccess);
        Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFF, 0xFE, 0x00, 0xFE }, result.RecordBytes.ToArray());
    }

    private static (Fallout2PrototypeInspectionContext Inspection, Fallout2PrototypeEditSession Session)
        CreateSession(byte[] sourceBytes, params FieldSpec[] specs) =>
        CreateSession(sourceBytes, (Fallout2PrototypeFieldDefinitionRegistry?)null, specs);

    private static (Fallout2PrototypeInspectionContext Inspection, Fallout2PrototypeEditSession Session)
        CreateSession(
            byte[] sourceBytes,
            Fallout2PrototypeFieldDefinitionRegistry? registry,
            params FieldSpec[] specs)
    {
        var inspection = CreateInspection(sourceBytes);
        var fieldDefinitions = specs
            .Select(spec => CreateCandidate(spec, sourceBytes))
            .ToArray();
        var session = Fallout2PrototypeEditSession.CreateProduction(
            inspection,
            registry ?? CreateRegistry(specs),
            fieldDefinitions);
        return (inspection, session);
    }

    private static Fallout2PrototypeInspectionContext CreateInspection(byte[] sourceBytes) =>
        new(FalloutGame.Fallout2, ArchiveFormat.Dat2, pidType: 0, subtype: 1, sourceBytes);

    private static Fallout2PrototypeFieldDefinitionRegistry CreateRegistry(params FieldSpec[] specs) =>
        Fallout2PrototypeFieldDefinitionRegistry.CreateForTests(
            specs.Select(spec => new Fallout2PrototypeFieldDefinition(
                Schema,
                spec.Name,
                spec.ValueKind,
                spec.Offset,
                spec.Width,
                [SyntheticClaim],
                spec.MinimumValue,
                spec.MaximumValue)).ToArray());

    private static Fallout2PrototypeEditorField CreateCandidate(FieldSpec spec, byte[] sourceBytes)
    {
        var bytes = sourceBytes.AsSpan(spec.Offset, spec.Width).ToArray();
        var value = spec.ValueKind switch
        {
            Fallout2PrototypeFieldValueKind.Int32BigEndian =>
                BinaryPrimitives.ReadInt32BigEndian(bytes),
            Fallout2PrototypeFieldValueKind.UInt8 => bytes[0],
            _ => throw new ArgumentOutOfRangeException(nameof(spec))
        };
        return new Fallout2PrototypeEditorField(
            spec.Name,
            spec.Name,
            spec.ValueKind,
            value,
            bytes,
            Fallout2PrototypeFieldEditability.EditingCandidate,
            "Synthetic fixture for approved in-memory draft tests.",
            [SyntheticFieldEvidence],
            spec.Offset,
            spec.Width,
            spec.MinimumValue,
            spec.MaximumValue);
    }

    private sealed record FieldSpec(
        string Name,
        Fallout2PrototypeFieldValueKind ValueKind,
        int Offset,
        int Width,
        long MinimumValue,
        long MaximumValue);
}
