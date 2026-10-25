using System.Buffers.Binary;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class Fallout2PrototypeEditSessionTests
{
    private static readonly Fallout2PrototypeFieldEvidence SyntheticEvidence = new(
        "tests/FalloutAssetExplorer.Core.Tests/Fallout2PrototypeEditSessionTests.cs",
        Fallout2PrototypeFieldEvidenceSource.Test,
        Fallout2PrototypeFieldEvidenceVerification.SyntheticData,
        "Synthetic model-only fixture; this is not evidence of Fallout game compatibility.");

    [Fact]
    public void Edit_LeavesConstructorArraysFieldsAndSnapshotUnchanged()
    {
        var sourceBytes = EncodeInt32(5);
        var sourceFieldBytes = EncodeInt32(5);
        var field = CreateCandidate("quantity", 5, sourceFieldBytes);
        var session = Fallout2PrototypeEditSession.CreateForTests(sourceBytes, [field]);

        Assert.True(session.TrySetNumericValue("quantity", 8, out var error));

        Assert.Null(error);
        Assert.Equal(5, field.CurrentNumericValue);
        Assert.Equal(5, session.GetField("quantity")!.OriginalValue);
        Assert.Equal(8, session.GetField("quantity")!.CurrentValue);
        Assert.Equal(EncodeInt32(5), sourceBytes);
        Assert.Equal(EncodeInt32(5), sourceFieldBytes);
        Assert.Equal(EncodeInt32(5), session.OriginalRecordBytes.ToArray());
    }

    [Fact]
    public void ConstructorDefensivelyCopiesSourceAndFieldByteArrays()
    {
        var sourceBytes = EncodeInt32(5);
        var sourceFieldBytes = EncodeInt32(5);
        var session = Fallout2PrototypeEditSession.CreateForTests(
            sourceBytes,
            [CreateCandidate("quantity", 5, sourceFieldBytes)]);
        sourceBytes[3] = 99;
        sourceFieldBytes[3] = 99;

        Assert.Equal(EncodeInt32(5), session.OriginalRecordBytes.ToArray());
        Assert.Equal(EncodeInt32(5), session.Fields[0].Definition.OriginalBytes.ToArray());
    }

    [Fact]
    public void TrySetNumericValue_RejectsReadOnlyAndUnknownFieldsWithoutHistory()
    {
        var readOnly = CreateReadOnlyField("field_34", 7, EncodeInt32(7));
        var session = Fallout2PrototypeEditSession.CreateForTests(EncodeInt32(7), [readOnly]);

        Assert.False(session.TrySetNumericValue("field_34", 8, out var error));
        Assert.Contains("read-only", error);
        Assert.False(session.TrySetNumericValue("not-present", 8, out var unknownError));
        Assert.Contains("not part", unknownError);
        Assert.False(session.Undo(out var undoError));
        Assert.Contains("no PRO field change", undoError);
        Assert.Equal(7, session.GetField("field_34")!.CurrentValue);
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void TrySetNumericValue_RejectsCandidatesWithoutCompleteValidation()
    {
        var field = new Fallout2PrototypeEditorField(
            "quantity",
            "Quantity",
            Fallout2PrototypeFieldValueKind.Int32BigEndian,
            5,
            EncodeInt32(5),
            Fallout2PrototypeFieldEditability.EditingCandidate,
            "Synthetic candidate without a validated range.",
            [SyntheticEvidence],
            offset: 0,
            length: sizeof(int),
            minimumValue: 0);
        var session = Fallout2PrototypeEditSession.CreateForTests(EncodeInt32(5), [field]);

        Assert.False(session.TrySetNumericValue("quantity", 6, out var error));
        Assert.Contains("validation bounds", error);
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void TrySetNumericValue_AcceptsSyntheticCandidateWithinExplicitBounds()
    {
        var session = CreateSession(5);

        Assert.True(session.TrySetNumericValue("quantity", 8, out var error));

        Assert.Null(error);
        Assert.Equal(8, session.GetField("quantity")!.CurrentValue);
        Assert.True(session.IsDirty);
        Assert.True(session.CanUndo);
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void TrySetNumericValue_RejectsInvalidValueWithoutChangingStateOrHistory()
    {
        var session = CreateSession(5);

        Assert.False(session.TrySetNumericValue("quantity", 11, out var rangeError));
        Assert.Contains("validated range", rangeError);
        Assert.False(session.TrySetNumericValue("quantity", (long)int.MaxValue + 1, out var typeError));
        Assert.Contains("representation", typeError);
        Assert.Equal(5, session.GetField("quantity")!.CurrentValue);
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void TrySetNumericValue_RejectsCandidateWithMismatchedEncodedWidth()
    {
        var field = new Fallout2PrototypeEditorField(
            "quantity",
            "Quantity",
            Fallout2PrototypeFieldValueKind.Int32BigEndian,
            5,
            new byte[] { 5 },
            Fallout2PrototypeFieldEditability.EditingCandidate,
            "Synthetic fixture with an invalid encoded width.",
            [SyntheticEvidence],
            offset: 0,
            length: 1,
            minimumValue: 0,
            maximumValue: 10);
        var session = Fallout2PrototypeEditSession.CreateForTests(new byte[] { 5 }, [field]);

        Assert.False(session.TrySetNumericValue("quantity", 6, out var error));
        Assert.Contains("source length", error);
        Assert.Equal(5, session.GetField("quantity")!.CurrentValue);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void UndoAndRedo_RestoreValuesAndDirtyStateDeterministically()
    {
        var session = CreateSession(5);
        Assert.True(session.TrySetNumericValue("quantity", 8, out _));

        Assert.True(session.Undo(out var undoError));
        Assert.Null(undoError);
        Assert.Equal(5, session.GetField("quantity")!.CurrentValue);
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.True(session.CanRedo);

        Assert.True(session.Redo(out var redoError));
        Assert.Null(redoError);
        Assert.Equal(8, session.GetField("quantity")!.CurrentValue);
        Assert.True(session.IsDirty);
        Assert.True(session.CanUndo);
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void MultipleEditsAcrossFields_UndoRedoInReverseOrderAndRestoreOriginalState()
    {
        var firstBytes = EncodeInt32(2);
        var secondBytes = EncodeInt32(4);
        var sourceBytes = firstBytes.Concat(secondBytes).ToArray();
        var session = Fallout2PrototypeEditSession.CreateForTests(
            sourceBytes,
            [
                CreateCandidate("first", 2, firstBytes, offset: 0),
                CreateCandidate("second", 4, secondBytes, offset: sizeof(int))
            ]);

        Assert.True(session.TrySetNumericValue("first", 3, out _));
        Assert.True(session.TrySetNumericValue("first", 4, out _));
        Assert.True(session.TrySetNumericValue("second", 5, out _));
        Assert.True(session.TrySetNumericValue("first", 6, out _));
        Assert.True(session.TrySetNumericValue("second", 7, out _));
        Assert.True(session.IsDirty);

        Assert.True(session.Undo(out _));
        Assert.Equal(6, session.GetField("first")!.CurrentValue);
        Assert.Equal(5, session.GetField("second")!.CurrentValue);
        Assert.True(session.Undo(out _));
        Assert.Equal(4, session.GetField("first")!.CurrentValue);
        Assert.True(session.Undo(out _));
        Assert.Equal(4, session.GetField("second")!.CurrentValue);
        Assert.True(session.Undo(out _));
        Assert.Equal(3, session.GetField("first")!.CurrentValue);
        Assert.True(session.Undo(out _));

        Assert.Equal(2, session.GetField("first")!.CurrentValue);
        Assert.Equal(4, session.GetField("second")!.CurrentValue);
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.True(session.CanRedo);

        Assert.True(session.Redo(out _));
        Assert.True(session.Redo(out _));
        Assert.True(session.Redo(out _));
        Assert.True(session.Redo(out _));
        Assert.True(session.Redo(out _));

        Assert.Equal(6, session.GetField("first")!.CurrentValue);
        Assert.Equal(7, session.GetField("second")!.CurrentValue);
        Assert.True(session.IsDirty);
        Assert.True(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.Equal(sourceBytes, session.OriginalRecordBytes.ToArray());
    }

    [Fact]
    public void NewChangeAfterUndo_ClearsRedoBranch()
    {
        var session = CreateSession(5);
        Assert.True(session.TrySetNumericValue("quantity", 8, out _));
        Assert.True(session.Undo(out _));

        Assert.True(session.TrySetNumericValue("quantity", 6, out var changeError));

        Assert.Null(changeError);
        Assert.Equal(6, session.GetField("quantity")!.CurrentValue);
        Assert.True(session.IsDirty);
        Assert.False(session.CanRedo);
        Assert.False(session.Redo(out var redoError));
        Assert.Contains("no PRO field change", redoError);
    }

    [Fact]
    public void NewChangeAfterMultipleUndos_ClearsTheEntireRedoBranch()
    {
        var firstBytes = EncodeInt32(2);
        var secondBytes = EncodeInt32(4);
        var sourceBytes = firstBytes.Concat(secondBytes).ToArray();
        var session = Fallout2PrototypeEditSession.CreateForTests(
            sourceBytes,
            [
                CreateCandidate("first", 2, firstBytes, offset: 0),
                CreateCandidate("second", 4, secondBytes, offset: sizeof(int))
            ]);

        Assert.True(session.TrySetNumericValue("first", 3, out _));
        Assert.True(session.TrySetNumericValue("second", 5, out _));
        Assert.True(session.TrySetNumericValue("first", 4, out _));
        Assert.True(session.Undo(out _));
        Assert.True(session.Undo(out _));
        Assert.Equal(3, session.GetField("first")!.CurrentValue);
        Assert.Equal(4, session.GetField("second")!.CurrentValue);
        Assert.True(session.CanUndo);
        Assert.True(session.CanRedo);

        Assert.True(session.TrySetNumericValue("second", 6, out _));

        Assert.Equal(3, session.GetField("first")!.CurrentValue);
        Assert.Equal(6, session.GetField("second")!.CurrentValue);
        Assert.True(session.IsDirty);
        Assert.True(session.CanUndo);
        Assert.False(session.CanRedo);

        Assert.True(session.Undo(out _));
        Assert.True(session.Undo(out _));
        Assert.Equal(2, session.GetField("first")!.CurrentValue);
        Assert.Equal(4, session.GetField("second")!.CurrentValue);
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.True(session.CanRedo);

        Assert.True(session.Redo(out _));
        Assert.True(session.Redo(out _));
        Assert.Equal(3, session.GetField("first")!.CurrentValue);
        Assert.Equal(6, session.GetField("second")!.CurrentValue);
        Assert.True(session.IsDirty);
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void RejectedEditWithUndoAndRedoHistory_PreservesValuesAndHistoryOrder()
    {
        var firstBytes = EncodeInt32(5);
        var secondBytes = EncodeInt32(2);
        var sourceBytes = firstBytes.Concat(secondBytes).ToArray();
        var session = Fallout2PrototypeEditSession.CreateForTests(
            sourceBytes,
            [
                CreateCandidate("first", 5, firstBytes, offset: 0),
                CreateCandidate("second", 2, secondBytes, offset: sizeof(int))
            ]);

        Assert.True(session.TrySetNumericValue("first", 6, out _));
        Assert.True(session.TrySetNumericValue("second", 3, out _));
        Assert.True(session.Undo(out _));
        Assert.Equal(6, session.GetField("first")!.CurrentValue);
        Assert.Equal(2, session.GetField("second")!.CurrentValue);
        Assert.True(session.IsDirty);
        Assert.True(session.CanUndo);
        Assert.True(session.CanRedo);

        Assert.False(session.TrySetNumericValue("first", 11, out var error));

        Assert.Contains("validated range", error);
        Assert.Equal(6, session.GetField("first")!.CurrentValue);
        Assert.Equal(2, session.GetField("second")!.CurrentValue);
        Assert.True(session.IsDirty);
        Assert.True(session.CanUndo);
        Assert.True(session.CanRedo);

        Assert.True(session.Redo(out _));
        Assert.Equal(6, session.GetField("first")!.CurrentValue);
        Assert.Equal(3, session.GetField("second")!.CurrentValue);
        Assert.True(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.True(session.Undo(out _));
        Assert.True(session.Undo(out _));
        Assert.Equal(5, session.GetField("first")!.CurrentValue);
        Assert.Equal(2, session.GetField("second")!.CurrentValue);
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.True(session.CanRedo);

        Assert.True(session.Redo(out _));
        Assert.True(session.Redo(out _));
        Assert.Equal(6, session.GetField("first")!.CurrentValue);
        Assert.Equal(3, session.GetField("second")!.CurrentValue);
        Assert.True(session.IsDirty);
        Assert.True(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.Equal(sourceBytes, session.OriginalRecordBytes.ToArray());
    }

    [Fact]
    public void SettingCurrentValue_DoesNotCreateHistory()
    {
        var session = CreateSession(5);

        Assert.True(session.TrySetNumericValue("quantity", 5, out var error));

        Assert.Null(error);
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void ReturningToOriginalValue_ClearsDirtyState()
    {
        var session = CreateSession(5);

        Assert.True(session.TrySetNumericValue("quantity", 8, out _));
        Assert.True(session.TrySetNumericValue("quantity", 5, out _));

        Assert.False(session.IsDirty);
        Assert.True(session.CanUndo);
    }

    [Fact]
    public void ConstructorRejectsDuplicateFieldIdentityAndMismatchedSourceBytes()
    {
        var first = CreateCandidate("quantity", 5, EncodeInt32(5));

        Assert.Throws<ArgumentException>(() => Fallout2PrototypeEditSession.CreateForTests(
            EncodeInt32(5),
            [first, first]));
        Assert.Throws<ArgumentException>(() => Fallout2PrototypeEditSession.CreateForTests(
            EncodeInt32(6),
            [first]));
    }

    [Fact]
    public void UndoRedoAndEditsNeverChangeOriginalBytesOrWriteFiles()
    {
        var sourceBytes = EncodeInt32(5);
        var session = Fallout2PrototypeEditSession.CreateForTests(
            sourceBytes,
            [CreateCandidate("quantity", 5, EncodeInt32(5))]);

        Assert.True(session.TrySetNumericValue("quantity", 8, out _));
        Assert.True(session.Undo(out _));
        Assert.True(session.Redo(out _));

        Assert.Equal(EncodeInt32(5), sourceBytes);
        Assert.Equal(EncodeInt32(5), session.OriginalRecordBytes.ToArray());
        Assert.Equal(8, session.GetField("quantity")!.CurrentValue);
    }

    private static Fallout2PrototypeEditSession CreateSession(int value)
    {
        var bytes = EncodeInt32(value);
        return Fallout2PrototypeEditSession.CreateForTests(
            bytes,
            [CreateCandidate("quantity", value, bytes)]);
    }

    private static Fallout2PrototypeEditorField CreateCandidate(
        string name,
        int value,
        byte[] originalBytes,
        int offset = 0) =>
        new(
            name,
            name,
            Fallout2PrototypeFieldValueKind.Int32BigEndian,
            value,
            originalBytes,
            Fallout2PrototypeFieldEditability.EditingCandidate,
            "Synthetic model-only validation fixture with an explicit integer range.",
            [SyntheticEvidence],
            offset: offset,
            length: sizeof(int),
            minimumValue: 0,
            maximumValue: 10);

    private static Fallout2PrototypeEditorField CreateReadOnlyField(
        string name,
        int value,
        byte[] originalBytes) =>
        new(
            name,
            name,
            Fallout2PrototypeFieldValueKind.Int32BigEndian,
            value,
            originalBytes,
            Fallout2PrototypeFieldEditability.ReadOnly,
            Fallout2PrototypeEditorField.ConservativeReadOnlyRationale,
            [SyntheticEvidence],
            offset: 0,
            length: sizeof(int));

    private static byte[] EncodeInt32(int value)
    {
        var bytes = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        return bytes;
    }
}
