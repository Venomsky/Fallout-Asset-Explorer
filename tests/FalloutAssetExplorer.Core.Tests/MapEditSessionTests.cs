using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class MapEditSessionTests
{
    [Fact]
    public void ConstructorRetainsSnapshotAndCreatesSessionScopedObjectIds()
    {
        var sourceData = new MapSourceData(new byte[] { 1, 2, 3 });
        var placement = CreatePlacement(7);

        var session = new MapEditSession(sourceData, [placement]);

        var sessionObject = Assert.Single(session.Objects);
        Assert.Same(sourceData, session.SourceData);
        Assert.NotEqual(Guid.Empty, session.SessionId);
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.Equal(new MapEditObjectId(session.SessionId, placement.RecordIndex), sessionObject.Id);
        Assert.Same(sessionObject, session.GetObject(sessionObject.Id));
        Assert.Same(sessionObject, session.GetObject(placement.RecordIndex));
    }

    [Fact]
    public void GetObjectRejectsUnknownAndOtherSessionIds()
    {
        var sourceData = new MapSourceData(new byte[] { 1 });
        var placement = CreatePlacement(7);
        var session = new MapEditSession(sourceData, [placement]);
        var otherSession = new MapEditSession(sourceData, [placement]);
        var currentId = Assert.Single(session.Objects).Id;

        Assert.Null(session.GetObject(new MapEditObjectId(session.SessionId, 99)));
        Assert.Null(otherSession.GetObject(currentId));
    }

    [Fact]
    public void ConstructorCopiesPlacementsAndNestedPayloadAwayFromSourceModels()
    {
        var payload = new List<int> { 10, 20 };
        var placement = CreatePlacement(7, payload);
        var sourcePlacements = new List<MapObjectPlacement> { placement };
        var session = new MapEditSession(new MapSourceData(new byte[] { 1, 2, 3 }), sourcePlacements);
        var workingPlacement = Assert.Single(session.Objects).Placement;

        payload[0] = 99;
        sourcePlacements[0] = CreatePlacement(7, [30, 40]);

        Assert.NotSame(placement, workingPlacement);
        Assert.NotSame(placement.RawPayloadWords, workingPlacement.RawPayloadWords);
        Assert.Equal(new[] { 10, 20 }, workingPlacement.RawPayloadWords);
        Assert.Equal(99, placement.RawPayloadWords[0]);
    }

    [Fact]
    public void ConstructorRejectsDuplicateSessionObjectIds()
    {
        var sourceData = new MapSourceData(new byte[] { 1 });
        var duplicatePlacements = new[]
        {
            CreatePlacement(7),
            CreatePlacement(7)
        };

        Assert.Throws<ArgumentException>(
            () => new MapEditSession(sourceData, duplicatePlacements));
    }

    [Fact]
    public void TrySetInventoryQuantityUpdatesWorkingCopyAndCanBeRetrievedAgain()
    {
        var sourceBytes = new byte[] { 1, 2, 3, 4 };
        var sourceData = new MapSourceData(sourceBytes);
        var parent = CreatePlacement(3);
        var child = CreatePlacement(7, parentRecordIndex: 3, inventoryQuantity: 4);
        var session = new MapEditSession(sourceData, [parent, child]);
        var childId = new MapEditObjectId(session.SessionId, child.RecordIndex);
        var originalSessionObject = session.GetObject(childId);

        Assert.True(session.TrySetInventoryQuantity(childId, 12, out var error));

        var updatedSessionObject = session.GetObject(childId);
        Assert.Null(error);
        Assert.NotSame(originalSessionObject, updatedSessionObject);
        Assert.Equal(12, updatedSessionObject!.Placement.InventoryQuantity);
        Assert.Same(
            updatedSessionObject,
            session.Objects.Single(sessionObject => sessionObject.Id == childId));
        Assert.Equal(4, child.InventoryQuantity);
        Assert.Equal(sourceBytes, sourceData.GetRange(0, sourceData.Length).ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TrySetInventoryQuantityRejectsNonPositiveValuesWithoutChangingSession(int quantity)
    {
        var child = CreatePlacement(7, parentRecordIndex: 3, inventoryQuantity: 4);
        var session = new MapEditSession(
            new MapSourceData(new byte[] { 1, 2, 3 }),
            [CreatePlacement(3), child]);
        var childId = new MapEditObjectId(session.SessionId, child.RecordIndex);
        var originalSessionObject = session.GetObject(childId);

        Assert.False(session.TrySetInventoryQuantity(childId, quantity, out var error));

        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Same(originalSessionObject, session.GetObject(childId));
        Assert.Equal(4, session.GetObject(childId)!.Placement.InventoryQuantity);
    }

    [Fact]
    public void TrySetInventoryQuantityRejectsNonInventoryAndForeignObjects()
    {
        var topLevelPlacement = CreatePlacement(3);
        var session = new MapEditSession(new MapSourceData(new byte[] { 1 }), [topLevelPlacement]);
        var topLevelId = new MapEditObjectId(session.SessionId, topLevelPlacement.RecordIndex);
        var originalSessionObject = session.GetObject(topLevelId);

        Assert.False(session.TrySetInventoryQuantity(topLevelId, 2, out var nonInventoryError));
        Assert.False(string.IsNullOrWhiteSpace(nonInventoryError));
        Assert.False(
            session.TrySetInventoryQuantity(
                new MapEditObjectId(Guid.NewGuid(), topLevelPlacement.RecordIndex),
                2,
                out var foreignObjectError));

        Assert.False(string.IsNullOrWhiteSpace(foreignObjectError));
        Assert.Same(originalSessionObject, session.GetObject(topLevelId));
        Assert.Null(session.GetObject(topLevelId)!.Placement.InventoryQuantity);
    }

    [Fact]
    public void InventoryQuantityChangeSupportsUndoRedoAndRestoresInitialDirtyState()
    {
        var sourceBytes = new byte[] { 1, 2, 3, 4 };
        var sourceData = new MapSourceData(sourceBytes);
        var child = CreatePlacement(7, parentRecordIndex: 3, inventoryQuantity: 4);
        var sourceParent = CreatePlacement(3);
        var session = new MapEditSession(sourceData, [sourceParent, child]);
        var childId = new MapEditObjectId(session.SessionId, child.RecordIndex);

        Assert.True(session.TrySetInventoryQuantity(childId, 12, out var changeError));
        Assert.Null(changeError);
        Assert.True(session.IsDirty);
        Assert.True(session.CanUndo);
        Assert.False(session.CanRedo);

        Assert.True(session.Undo(out var undoError));
        Assert.Null(undoError);
        Assert.Equal(4, session.GetObject(childId)!.Placement.InventoryQuantity);
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.True(session.CanRedo);

        Assert.True(session.Redo(out var redoError));
        Assert.Null(redoError);
        Assert.Equal(12, session.GetObject(childId)!.Placement.InventoryQuantity);
        Assert.True(session.IsDirty);
        Assert.True(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.Equal(4, child.InventoryQuantity);
        Assert.Equal(sourceBytes, sourceData.GetRange(0, sourceData.Length).ToArray());
    }

    [Fact]
    public void UndoRedoApplyMultipleChangesInStackOrder()
    {
        var firstChild = CreatePlacement(7, parentRecordIndex: 3, inventoryQuantity: 4);
        var secondChild = CreatePlacement(8, parentRecordIndex: 3, inventoryQuantity: 8);
        var session = new MapEditSession(
            new MapSourceData(new byte[] { 1 }),
            [CreatePlacement(3), firstChild, secondChild]);
        var firstId = new MapEditObjectId(session.SessionId, firstChild.RecordIndex);
        var secondId = new MapEditObjectId(session.SessionId, secondChild.RecordIndex);

        Assert.True(session.TrySetInventoryQuantity(firstId, 5, out _));
        Assert.True(session.TrySetInventoryQuantity(secondId, 10, out _));
        Assert.True(session.TrySetInventoryQuantity(firstId, 6, out _));

        Assert.True(session.Undo(out _));
        Assert.Equal(5, session.GetObject(firstId)!.Placement.InventoryQuantity);
        Assert.Equal(10, session.GetObject(secondId)!.Placement.InventoryQuantity);
        Assert.True(session.Undo(out _));
        Assert.Equal(8, session.GetObject(secondId)!.Placement.InventoryQuantity);
        Assert.True(session.Undo(out _));
        Assert.Equal(4, session.GetObject(firstId)!.Placement.InventoryQuantity);
        Assert.False(session.IsDirty);

        Assert.True(session.Redo(out _));
        Assert.Equal(5, session.GetObject(firstId)!.Placement.InventoryQuantity);
        Assert.True(session.Redo(out _));
        Assert.Equal(10, session.GetObject(secondId)!.Placement.InventoryQuantity);
        Assert.True(session.Redo(out _));
        Assert.Equal(6, session.GetObject(firstId)!.Placement.InventoryQuantity);
        Assert.True(session.IsDirty);
    }

    [Fact]
    public void NewInventoryQuantityChangeAfterUndoClearsRedoHistory()
    {
        var child = CreatePlacement(7, parentRecordIndex: 3, inventoryQuantity: 4);
        var session = new MapEditSession(
            new MapSourceData(new byte[] { 1 }),
            [CreatePlacement(3), child]);
        var childId = new MapEditObjectId(session.SessionId, child.RecordIndex);

        Assert.True(session.TrySetInventoryQuantity(childId, 5, out _));
        Assert.True(session.Undo(out _));
        Assert.True(session.CanRedo);

        Assert.True(session.TrySetInventoryQuantity(childId, 6, out var changeError));

        Assert.Null(changeError);
        Assert.False(session.CanRedo);
        Assert.False(session.Redo(out var redoError));
        Assert.Null(redoError);
        Assert.Equal(6, session.GetObject(childId)!.Placement.InventoryQuantity);
        Assert.True(session.IsDirty);
    }

    [Fact]
    public void EmptyHistoryDoesNotChangeSessionState()
    {
        var child = CreatePlacement(7, parentRecordIndex: 3, inventoryQuantity: 4);
        var session = new MapEditSession(
            new MapSourceData(new byte[] { 1 }),
            [CreatePlacement(3), child]);
        var childId = new MapEditObjectId(session.SessionId, child.RecordIndex);
        var sessionObject = session.GetObject(childId);

        Assert.False(session.Undo(out var undoError));
        Assert.Null(undoError);
        Assert.False(session.Redo(out var redoError));
        Assert.Null(redoError);
        Assert.Same(sessionObject, session.GetObject(childId));
        Assert.False(session.IsDirty);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void RejectedInventoryQuantityChangeDoesNotAffectHistoryOrDirtyState(int quantity)
    {
        var child = CreatePlacement(7, parentRecordIndex: 3, inventoryQuantity: 4);
        var session = new MapEditSession(
            new MapSourceData(new byte[] { 1 }),
            [CreatePlacement(3), child]);
        var childId = new MapEditObjectId(session.SessionId, child.RecordIndex);
        var sessionObject = session.GetObject(childId);

        Assert.False(session.TrySetInventoryQuantity(childId, quantity, out var error));

        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.False(session.Undo(out var undoError));
        Assert.Null(undoError);
        Assert.Same(sessionObject, session.GetObject(childId));
    }

    [Fact]
    public void SettingInventoryQuantityToCurrentValueDoesNotCreateHistory()
    {
        var child = CreatePlacement(7, parentRecordIndex: 3, inventoryQuantity: 4);
        var session = new MapEditSession(
            new MapSourceData(new byte[] { 1 }),
            [CreatePlacement(3), child]);
        var childId = new MapEditObjectId(session.SessionId, child.RecordIndex);
        var sessionObject = session.GetObject(childId);

        Assert.True(session.TrySetInventoryQuantity(childId, 4, out var error));

        Assert.Null(error);
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.Same(sessionObject, session.GetObject(childId));
    }

    [Fact]
    public void UndoRestoresOriginalQuantityEvenWhenItWasNotEditable()
    {
        var child = CreatePlacement(7, parentRecordIndex: 3, inventoryQuantity: 0);
        var session = new MapEditSession(
            new MapSourceData(new byte[] { 1 }),
            [CreatePlacement(3), child]);
        var childId = new MapEditObjectId(session.SessionId, child.RecordIndex);

        Assert.True(session.TrySetInventoryQuantity(childId, 2, out _));
        Assert.True(session.IsDirty);
        Assert.True(session.Undo(out var error));

        Assert.Null(error);
        Assert.Equal(0, session.GetObject(childId)!.Placement.InventoryQuantity);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void EditHistoryIsIsolatedToItsSessionAndRejectsStaleIds()
    {
        var sourceData = new MapSourceData(new byte[] { 1 });
        var child = CreatePlacement(7, parentRecordIndex: 3, inventoryQuantity: 4);
        var placements = new[] { CreatePlacement(3), child };
        var session = new MapEditSession(sourceData, placements);
        var otherSession = new MapEditSession(sourceData, placements);
        var childId = new MapEditObjectId(session.SessionId, child.RecordIndex);
        var staleId = new MapEditObjectId(session.SessionId, 99);

        Assert.True(session.TrySetInventoryQuantity(childId, 5, out _));
        Assert.False(otherSession.Undo(out var emptyHistoryError));
        Assert.Null(emptyHistoryError);
        Assert.False(
            otherSession.TrySetInventoryQuantity(childId, 6, out var foreignIdError));
        Assert.False(string.IsNullOrWhiteSpace(foreignIdError));
        Assert.False(session.TrySetInventoryQuantity(staleId, 6, out var staleIdError));
        Assert.False(string.IsNullOrWhiteSpace(staleIdError));
        Assert.Equal(5, session.GetObject(childId)!.Placement.InventoryQuantity);
        Assert.Equal(4, otherSession.GetObject(child.RecordIndex)!.Placement.InventoryQuantity);
        Assert.True(session.IsDirty);
        Assert.False(otherSession.IsDirty);
    }

    private static MapObjectPlacement CreatePlacement(
        int recordIndex,
        IReadOnlyList<int>? payload = null,
        int? parentRecordIndex = null,
        int? inventoryQuantity = null) =>
        new MapObjectPlacement(
            RecordIndex: recordIndex,
            RecordOffset: 32,
            ElevationGroup: 1,
            RawObjectId: 0,
            RawHexIndex: 20_099,
            HexX: 100,
            HexY: 100,
            RawOffsetX: 0,
            RawOffsetY: 0,
            RawScreenX: 0,
            RawScreenY: 0,
            RawFrame: 0,
            RawRotation: 0,
            RawFid: 0,
            RawObjectFlags: 0,
            StoredElevation: 1,
            RawPid: 0x03000001,
            PidTypeIdentifier: 3,
            RawCid: 0,
            RawLightDistance: 0,
            RawLightIntensity: 0,
            RawField74: 0,
            RawScriptId: 0,
            RawScriptField: 0,
            RawInventoryLength: 0,
            RawInventoryCapacity: 0,
            RawInventoryPointer: 0,
            RawPayloadWords: payload ?? [],
            Warning: null,
            UnsupportedReason: null)
        {
            ParentRecordIndex = parentRecordIndex,
            InventoryQuantity = inventoryQuantity
        };
}
