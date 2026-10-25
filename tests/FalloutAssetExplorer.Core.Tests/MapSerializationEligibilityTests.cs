using System.Buffers.Binary;
using System.Reflection;
using FalloutAssetExplorer.App;
using Avalonia.Controls;
using FalloutAssetExplorer.App.ViewModels;
using FalloutAssetExplorer.App.Views;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class MapSerializationEligibilityTests
{
    [Theory]
    [InlineData(FalloutGame.Fallout1, 19)]
    [InlineData(FalloutGame.Fallout2, 19)]
    [InlineData(FalloutGame.Fallout2, 20)]
    public void Validate_AcceptsSupportedSourceBoundSyntheticMap(FalloutGame game, int version)
    {
        var decoded = DecodeMap(CreateMap(version, [CreateObject(0x03000001, 0, 123)]), game);

        var result = Validate(decoded);

        Assert.True(result.IsEligible);
        Assert.Equal(MapSerializationEligibilityStatus.Eligible, result.Status);
    }

    [Fact]
    public void Validate_RejectsIdenticalBytesFromDifferentSourceSnapshot()
    {
        var bytes = CreateMap(19, [CreateObject(0x03000001, 0, 123)]);
        var decoded = DecodeMap(bytes, FalloutGame.Fallout1);
        var identicalButDifferentSource = new MapSourceData(bytes);

        var result = MapSerializationEligibility.Validate(
            identicalButDifferentSource,
            decoded.Game,
            decoded.Header,
            decoded.TileGrids,
            decoded.ObjectPlacements,
            decoded.EditSession);

        Assert.Equal(MapSerializationEligibilityStatus.SourceIdentityMismatch, result.Status);
    }

    [Fact]
    public void Validate_RejectsMismatchedGameProvenance()
    {
        var decoded = DecodeMap(
            CreateMap(19, [CreateObject(0x03000001, 0, 123)]),
            FalloutGame.Fallout1);

        var result = MapSerializationEligibility.Validate(
            decoded.Source,
            FalloutGame.Fallout2,
            decoded.Header,
            decoded.TileGrids,
            decoded.ObjectPlacements,
            decoded.EditSession);

        Assert.Equal(MapSerializationEligibilityStatus.UnsupportedGameVersion, result.Status);
    }

    [Fact]
    public void Validate_RejectsMismatchedVersionProvenance()
    {
        var decoded = DecodeMap(
            CreateMap(19, [CreateObject(0x03000001, 0, 123)]),
            FalloutGame.Fallout1);

        var result = MapSerializationEligibility.Validate(
            decoded.Source,
            decoded.Game,
            decoded.Header with { Version = 20 },
            decoded.TileGrids,
            decoded.ObjectPlacements,
            decoded.EditSession);

        Assert.Equal(MapSerializationEligibilityStatus.UnsupportedGameVersion, result.Status);
    }

    [Fact]
    public void Validate_RejectsUnconsumedTrailingBytesEvenWhenObjectDataIsComplete()
    {
        var bytes = CreateMap(19, [CreateObject(0x03000001, 0, 123)])
            .Concat(new byte[] { 0xAA, 0xBB })
            .ToArray();
        var decoded = DecodeMap(bytes, FalloutGame.Fallout1);

        Assert.True(decoded.ObjectPlacements.IsComplete);
        Assert.Equal(2, decoded.ObjectPlacements.UnparsedByteCount);
        Assert.Equal(
            MapSerializationEligibilityStatus.UnconsumedTrailingBytes,
            Validate(decoded).Status);
    }

    [Fact]
    public void Validate_RejectsUnsupportedObjectPayloadLayout()
    {
        var decoded = DecodeMap(
            CreateMap(19, [CreateObject(0x00000001, 0, 123)]),
            FalloutGame.Fallout1);

        Assert.True(decoded.ObjectPlacements.HasUnsupportedObjectPayload);
        Assert.Equal(
            MapSerializationEligibilityStatus.UnsupportedObjectPayloadLayout,
            Validate(decoded).Status);
    }

    [Fact]
    public void Validate_RejectsMissingResultAfterTruncatedObjectDecode()
    {
        var bytes = CreateMap(19, [CreateObject(0x03000001, 0, 123)])[..^1];
        var source = new MapSourceData(bytes);
        var header = MapHeaderDecoder.Decode(source, FalloutGame.Fallout1);
        var grids = MapTileGridDecoder.Decode(source, header);

        Assert.Throws<InvalidDataException>(
            () => MapObjectPlacementDecoder.Decode(source, header));

        var result = MapSerializationEligibility.Validate(
            source,
            FalloutGame.Fallout1,
            header,
            grids,
            null,
            null);

        Assert.Equal(MapSerializationEligibilityStatus.ParsingIncompleteOrFailed, result.Status);
    }

    [Fact]
    public void Decode_RetainsInventoryQuantityByteOffset()
    {
        const int quantity = 7;
        var bytes = CreateMap(19,
        [
            CreateObject(0x03000001, 0, 123, inventoryLength: 1, inventoryCapacity: 1)
        ], [quantity], [CreateObject(0x01000001, 0, 456, critter: true)]);
        var source = new MapSourceData(bytes);
        var header = MapHeaderDecoder.Decode(source, FalloutGame.Fallout1);

        var decoded = MapObjectPlacementDecoder.Decode(source, header);
        var child = decoded.Objects.Single(placement => placement.ParentRecordIndex is not null);

        Assert.Equal(quantity, child.InventoryQuantity);
        Assert.NotNull(child.InventoryQuantityOffset);
        var parent = Assert.Single(
            decoded.Objects,
            placement => placement.ParentRecordIndex is null);
        Assert.Equal(
            parent.RawHexIndex,
            BinaryPrimitives.ReadInt32BigEndian(
                source.GetRange(parent.RawHexIndexOffset, sizeof(int)).ToArray()));
        Assert.Equal(
            quantity,
            BinaryPrimitives.ReadInt32BigEndian(
                source.GetRange(child.InventoryQuantityOffset!.Value, sizeof(int)).ToArray()));
    }

    [Fact]
    public void InventoryQuantityPatch_UneditedEligibleSessionProducesIdenticalBytes()
    {
        var original = CreateInventoryMap(quantity: 7);
        var decoded = DecodeMap(original, FalloutGame.Fallout1);

        var result = MapInventoryQuantityPatchBuilder.Create(
            decoded.Source,
            decoded.Game,
            decoded.Header,
            decoded.TileGrids,
            decoded.ObjectPlacements,
            decoded.EditSession);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(original, result.OutputBytes);
        Assert.Equal(original, decoded.Source.GetRange(0, decoded.Source.Length).ToArray());
    }

    [Theory]
    [InlineData(FalloutGame.Fallout1, 19)]
    [InlineData(FalloutGame.Fallout2, 19)]
    [InlineData(FalloutGame.Fallout2, 20)]
    public void InventoryQuantityPatch_ChangesOnlyQuantityBytesAndReparses(
        FalloutGame game,
        int version)
    {
        var original = CreateInventoryMap(quantity: 0x01020304, version: version);
        var decoded = DecodeMap(original, game);
        var child = decoded.EditSession.Objects
            .Single(item => item.Placement.ParentRecordIndex is not null);
        Assert.True(decoded.EditSession.TrySetInventoryQuantity(child.Id, 0x05060708, out var editError), editError);

        var result = MapInventoryQuantityPatchBuilder.Create(
            decoded.Source,
            decoded.Game,
            decoded.Header,
            decoded.TileGrids,
            decoded.ObjectPlacements,
            decoded.EditSession);

        Assert.True(result.IsSuccess, result.Message);
        var output = Assert.IsType<byte[]>(result.OutputBytes);
        var sourceChild = decoded.ObjectPlacements.Objects
            .Single(item => item.ParentRecordIndex is not null);
        var changedOffsets = original
            .Select((value, index) => (value, index))
            .Where(item => item.value != output[item.index])
            .Select(item => item.index)
            .ToArray();
        Assert.Equal(
            Enumerable.Range(sourceChild.InventoryQuantityOffset!.Value, sizeof(int)),
            changedOffsets);
        Assert.Equal(original, decoded.Source.GetRange(0, decoded.Source.Length).ToArray());

        var reparsed = DecodeMap(output, game);
        Assert.Equal(
            0x05060708,
            Assert.Single(
                reparsed.ObjectPlacements.Objects,
                item => item.ParentRecordIndex is not null).InventoryQuantity);
    }

    [Fact]
    public void InventoryQuantityPatch_UndoRestoresOriginalOutputAndCleanState()
    {
        var original = CreateInventoryMap(quantity: 7);
        var decoded = DecodeMap(original, FalloutGame.Fallout1);
        var child = decoded.EditSession.Objects
            .Single(item => item.Placement.ParentRecordIndex is not null);
        Assert.True(decoded.EditSession.TrySetInventoryQuantity(child.Id, 23, out var editError), editError);
        Assert.True(decoded.EditSession.IsDirty);

        Assert.True(decoded.EditSession.Undo(out var undoError), undoError);
        Assert.False(decoded.EditSession.IsDirty);

        var result = MapInventoryQuantityPatchBuilder.Create(
            decoded.Source,
            decoded.Game,
            decoded.Header,
            decoded.TileGrids,
            decoded.ObjectPlacements,
            decoded.EditSession);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(original, result.OutputBytes);
    }

    [Fact]
    public void InventoryQuantityPatch_RejectsDirtyHexTileEdit()
    {
        var decoded = DecodeMap(
            CreateMap(19, [CreateObject(0x03000001, 0, 123)]),
            FalloutGame.Fallout1);
        var sessionObject = Assert.Single(decoded.EditSession.Objects);
        Assert.True(
            decoded.EditSession.TrySetHexTileIndex(sessionObject.Id, 124, out var editError),
            editError);

        var result = MapInventoryQuantityPatchBuilder.Create(
            decoded.Source,
            decoded.Game,
            decoded.Header,
            decoded.TileGrids,
            decoded.ObjectPlacements,
            decoded.EditSession);

        Assert.False(result.IsSuccess);
        Assert.Contains("inventory-quantity edits only", result.Message);
        Assert.Null(result.OutputBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InventoryQuantityPatch_RejectsIncompleteOrUnsupportedParse(bool unsupportedLayout)
    {
        var bytes = unsupportedLayout
            ? CreateMap(19, [CreateObject(0x00000001, 0, 123)])
            : CreateInventoryMap(quantity: 7).Concat(new byte[] { 0xAA }).ToArray();
        var decoded = DecodeMap(bytes, FalloutGame.Fallout1);
        var child = decoded.EditSession.Objects
            .FirstOrDefault(item => item.Placement.ParentRecordIndex is not null);
        if (child is not null)
        {
            Assert.True(decoded.EditSession.TrySetInventoryQuantity(child.Id, 23, out var editError), editError);
        }

        var result = MapInventoryQuantityPatchBuilder.Create(
            decoded.Source,
            decoded.Game,
            decoded.Header,
            decoded.TileGrids,
            decoded.ObjectPlacements,
            decoded.EditSession);

        Assert.False(result.IsSuccess);
        Assert.Null(result.OutputBytes);
    }

    [Fact]
    public void InventoryQuantityPatch_RejectsOffsetThatDoesNotPrecedeChildRecord()
    {
        var decoded = DecodeMap(CreateInventoryMap(quantity: 7), FalloutGame.Fallout1);
        var forgedPlacements = ReplaceChildPlacement(
            decoded,
            child => child with
            {
                InventoryQuantityOffset = child.InventoryQuantityOffset!.Value - sizeof(int)
            });
        var forgedSession = new MapEditSession(decoded.Header, forgedPlacements);
        var child = forgedSession.Objects.Single(item => item.Placement.ParentRecordIndex is not null);
        Assert.True(forgedSession.TrySetInventoryQuantity(child.Id, 23, out var editError), editError);

        var result = MapInventoryQuantityPatchBuilder.Create(
            decoded.Source,
            decoded.Game,
            decoded.Header,
            decoded.TileGrids,
            forgedPlacements,
            forgedSession);

        Assert.False(result.IsSuccess);
        Assert.Contains("does not precede", result.Message);
    }

    [Fact]
    public void InventoryQuantityPatch_RejectsForgedOffsetsPointingToMatchingSourceValue()
    {
        var decoded = DecodeMap(CreateInventoryMap(quantity: 0), FalloutGame.Fallout1);
        var originalChild = decoded.EditSession.Objects
            .Single(item => item.Placement.ParentRecordIndex is not null);
        var originalOffset = originalChild.Placement.InventoryQuantityOffset!.Value;
        var sourceBytes = decoded.Source.GetRange(0, decoded.Source.Length).ToArray();
        var forgedOffset = Enumerable.Range(0, sourceBytes.Length - sizeof(int) + 1)
            .First(offset =>
                Math.Abs(offset - originalOffset) >= sizeof(int)
                && BinaryPrimitives.ReadInt32BigEndian(sourceBytes.AsSpan(offset, sizeof(int))) == 0);
        var forgedPlacements = ReplaceChildPlacement(
            decoded,
            child => child with
            {
                RecordOffset = forgedOffset + sizeof(int),
                InventoryQuantityOffset = forgedOffset
            });
        var forgedSession = new MapEditSession(decoded.Header, forgedPlacements);
        var child = forgedSession.Objects.Single(item => item.Placement.ParentRecordIndex is not null);
        Assert.True(forgedSession.TrySetInventoryQuantity(child.Id, 23, out var editError), editError);

        var result = MapInventoryQuantityPatchBuilder.Create(
            decoded.Source,
            decoded.Game,
            decoded.Header,
            decoded.TileGrids,
            forgedPlacements,
            forgedSession);

        Assert.False(result.IsSuccess);
        Assert.Contains("do not match the source decode", result.Message);
        Assert.Null(result.OutputBytes);
        Assert.Equal(sourceBytes, decoded.Source.GetRange(0, decoded.Source.Length).ToArray());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void InventoryQuantityPatch_RejectsOutOfBoundsQuantityOffset(int invalidOffset)
    {
        var decoded = DecodeMap(CreateInventoryMap(quantity: 7), FalloutGame.Fallout1);
        var forgedPlacements = ReplaceChildPlacement(
            decoded,
            child => child with { InventoryQuantityOffset = invalidOffset });
        var forgedSession = new MapEditSession(decoded.Header, forgedPlacements);
        var child = forgedSession.Objects.Single(item => item.Placement.ParentRecordIndex is not null);
        Assert.True(forgedSession.TrySetInventoryQuantity(child.Id, 23, out var editError), editError);

        var result = MapInventoryQuantityPatchBuilder.Create(
            decoded.Source,
            decoded.Game,
            decoded.Header,
            decoded.TileGrids,
            forgedPlacements,
            forgedSession);

        Assert.False(result.IsSuccess);
        Assert.Contains("invalid inventory-quantity byte offset", result.Message);
    }

    [Fact]
    public void InventoryQuantityPatch_RejectsSourceBytesThatDifferFromRecordedQuantity()
    {
        var decoded = DecodeMap(CreateInventoryMap(quantity: 7), FalloutGame.Fallout1);
        var forgedPlacements = ReplaceChildPlacement(
            decoded,
            child => child with { InventoryQuantity = 8 });
        var forgedSession = new MapEditSession(decoded.Header, forgedPlacements);
        var child = forgedSession.Objects.Single(item => item.Placement.ParentRecordIndex is not null);
        Assert.True(forgedSession.TrySetInventoryQuantity(child.Id, 23, out var editError), editError);

        var result = MapInventoryQuantityPatchBuilder.Create(
            decoded.Source,
            decoded.Game,
            decoded.Header,
            decoded.TileGrids,
            forgedPlacements,
            forgedSession);

        Assert.False(result.IsSuccess);
        Assert.Contains("do not match", result.Message);
    }

    [Fact]
    public void InventoryQuantityPatch_AcceptsMaximumPositiveInt32WithoutTruncation()
    {
        var decoded = DecodeMap(CreateInventoryMap(quantity: 7), FalloutGame.Fallout1);
        var child = decoded.EditSession.Objects
            .Single(item => item.Placement.ParentRecordIndex is not null);
        Assert.True(
            decoded.EditSession.TrySetInventoryQuantity(child.Id, int.MaxValue, out var editError),
            editError);

        var result = MapInventoryQuantityPatchBuilder.Create(
            decoded.Source,
            decoded.Game,
            decoded.Header,
            decoded.TileGrids,
            decoded.ObjectPlacements,
            decoded.EditSession);

        Assert.True(result.IsSuccess, result.Message);
        var output = Assert.IsType<byte[]>(result.OutputBytes);
        var childPlacement = decoded.ObjectPlacements.Objects
            .Single(item => item.ParentRecordIndex is not null);
        Assert.Equal(
            int.MaxValue,
            BinaryPrimitives.ReadInt32BigEndian(
                output.AsSpan(childPlacement.InventoryQuantityOffset!.Value, sizeof(int))));
    }

    [Fact]
    public async Task InventoryQuantitySaveAsFailureKeepsEditAndSourceBytes()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"fae-map-edit-preserve-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var original = CreateInventoryMap(quantity: 7);
            var decoded = DecodeMap(original, FalloutGame.Fallout1);
            var child = decoded.EditSession.Objects
                .Single(item => item.Placement.ParentRecordIndex is not null);
            Assert.True(decoded.EditSession.TrySetInventoryQuantity(child.Id, 23, out var editError), editError);
            var patch = MapInventoryQuantityPatchBuilder.Create(
                decoded.Source,
                decoded.Game,
                decoded.Header,
                decoded.TileGrids,
                decoded.ObjectPlacements,
                decoded.EditSession);
            Assert.True(patch.IsSuccess, patch.Message);
            var output = Assert.IsType<byte[]>(patch.OutputBytes);
            var outputBeforeSave = output.ToArray();
            var destination = Path.Combine(directory, "existing.map");
            byte[] existingOutput = [9, 8, 7];
            await File.WriteAllBytesAsync(destination, existingOutput);

            var cancelled = await MapSaveAsService.SaveAsync(
                output,
                destinationPath: null,
                originalArchivePath: null,
                overwriteConfirmed: false);
            Assert.Null(cancelled);
            await Assert.ThrowsAsync<IOException>(() =>
                MapSaveAsService.SaveAsync(
                    output,
                    destination,
                    originalArchivePath: null,
                    overwriteConfirmed: false));

            Assert.True(decoded.EditSession.IsDirty);
            Assert.Equal(original, decoded.Source.GetRange(0, decoded.Source.Length).ToArray());
            Assert.Equal(existingOutput, await File.ReadAllBytesAsync(destination));
            Assert.Equal(outputBeforeSave, output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task MapSaveAs_RejectsCandidateAfterSessionReplacementAndPreservesCurrentEdit()
    {
        using var directory = new TemporaryDirectory();
        var archivePath = Path.Combine(directory.Path, "synthetic.dat");
        var original = DecodeMap(CreateInventoryMap(quantity: 7), FalloutGame.Fallout1);
        using var viewModel = CreateMapSaveViewModel(original, archivePath);
        var originalChild = original.EditSession.Objects
            .Single(item => item.Placement.ParentRecordIndex is not null);
        Assert.True(original.EditSession.TrySetInventoryQuantity(originalChild.Id, 23, out var editError), editError);
        var candidate = Assert.IsType<MapSaveCandidate>(viewModel.CreateMapSaveCandidate());

        var replacement = DecodeMap(CreateInventoryMap(quantity: 9), FalloutGame.Fallout1);
        viewModel.MapInspector.SetMapObjectPlacements(replacement.ObjectPlacements.Objects);
        viewModel.MapInspector.SetMapEditSession(replacement.EditSession);
        viewModel.MapInspector.SetMapSerializationContext(
            replacement.Game,
            replacement.Header,
            replacement.TileGrids,
            replacement.ObjectPlacements,
            archivePath);
        var replacementChild = replacement.EditSession.Objects
            .Single(item => item.Placement.ParentRecordIndex is not null);
        Assert.True(
            replacement.EditSession.TrySetInventoryQuantity(replacementChild.Id, 31, out var replacementError),
            replacementError);

        var destination = Path.Combine(directory.Path, "stale-session.map");
        var savedPath = await viewModel.SaveMapAsAsync(
            candidate,
            destination,
            overwriteConfirmed: false);

        Assert.Null(savedPath);
        Assert.False(File.Exists(destination));
        Assert.True(replacement.EditSession.IsDirty);
        Assert.Contains("previously selected MAP copy", viewModel.StatusMessage);
        Assert.DoesNotContain(
            "changed while choosing a destination",
            viewModel.MapInspector.MapSaveStatusDisplay);
        Assert.Equal(31, replacement.EditSession.GetObject(replacementChild.Id)?.Placement.InventoryQuantity);
        Assert.Equal(9, BinaryPrimitives.ReadInt32BigEndian(
            replacement.Source.GetRange(
                replacementChild.Placement.InventoryQuantityOffset!.Value,
                sizeof(int)).ToArray()));
    }

    [Fact]
    public async Task MapSaveAs_RejectsCandidateAfterUndoRedoRevisionChangeAndPreservesEdit()
    {
        using var directory = new TemporaryDirectory();
        var archivePath = Path.Combine(directory.Path, "synthetic.dat");
        var decoded = DecodeMap(CreateInventoryMap(quantity: 7), FalloutGame.Fallout1);
        using var viewModel = CreateMapSaveViewModel(decoded, archivePath);
        var child = decoded.EditSession.Objects
            .Single(item => item.Placement.ParentRecordIndex is not null);
        Assert.True(decoded.EditSession.TrySetInventoryQuantity(child.Id, 23, out var editError), editError);
        var candidate = Assert.IsType<MapSaveCandidate>(viewModel.CreateMapSaveCandidate());
        var candidateRevision = candidate.EditRevision;

        Assert.True(viewModel.MapInspector.UndoMapEdit());
        Assert.True(viewModel.MapInspector.RedoMapEdit());
        Assert.True(viewModel.MapInspector.MapEditRevision > candidateRevision);

        var destination = Path.Combine(directory.Path, "stale-revision.map");
        var savedPath = await viewModel.SaveMapAsAsync(
            candidate,
            destination,
            overwriteConfirmed: false);

        Assert.Null(savedPath);
        Assert.False(File.Exists(destination));
        Assert.True(decoded.EditSession.IsDirty);
        Assert.Contains("previously selected MAP copy", viewModel.StatusMessage);
        Assert.Equal(23, decoded.EditSession.GetObject(child.Id)?.Placement.InventoryQuantity);
        Assert.Equal(7, BinaryPrimitives.ReadInt32BigEndian(
            decoded.Source.GetRange(child.Placement.InventoryQuantityOffset!.Value, sizeof(int)).ToArray()));
    }

    [Fact]
    public async Task MapSaveAs_WritesCandidateWhenSessionAndRevisionRemainCurrent()
    {
        using var directory = new TemporaryDirectory();
        var archivePath = Path.Combine(directory.Path, "synthetic.dat");
        var decoded = DecodeMap(CreateInventoryMap(quantity: 7), FalloutGame.Fallout1);
        using var viewModel = CreateMapSaveViewModel(decoded, archivePath);
        var child = decoded.EditSession.Objects
            .Single(item => item.Placement.ParentRecordIndex is not null);
        Assert.True(decoded.EditSession.TrySetInventoryQuantity(child.Id, 23, out var editError), editError);
        var candidate = Assert.IsType<MapSaveCandidate>(viewModel.CreateMapSaveCandidate());
        var destination = Path.Combine(directory.Path, "current-session.map");

        var savedPath = await viewModel.SaveMapAsAsync(
            candidate,
            destination,
            overwriteConfirmed: false);

        Assert.Equal(Path.GetFullPath(destination), savedPath);
        Assert.Equal(candidate.OutputBytes, await File.ReadAllBytesAsync(destination));
        Assert.True(decoded.EditSession.IsDirty);
        Assert.Contains("saved to a separate MAP copy", viewModel.MapInspector.EditSessionDirtyDisplay);
        Assert.Equal(7, BinaryPrimitives.ReadInt32BigEndian(
            decoded.Source.GetRange(child.Placement.InventoryQuantityOffset!.Value, sizeof(int)).ToArray()));
    }

    [Fact]
    public async Task MapSaveAs_CurrentCandidateWriteFailureKeepsInspectorError()
    {
        using var directory = new TemporaryDirectory();
        var archivePath = Path.Combine(directory.Path, "synthetic.dat");
        var decoded = DecodeMap(CreateInventoryMap(quantity: 7), FalloutGame.Fallout1);
        using var viewModel = CreateMapSaveViewModel(decoded, archivePath);
        var child = decoded.EditSession.Objects
            .Single(item => item.Placement.ParentRecordIndex is not null);
        Assert.True(decoded.EditSession.TrySetInventoryQuantity(child.Id, 23, out var editError), editError);
        var candidate = Assert.IsType<MapSaveCandidate>(viewModel.CreateMapSaveCandidate());
        var destination = Path.Combine(directory.Path, "existing.map");
        byte[] existing = [9, 8, 7];
        await File.WriteAllBytesAsync(destination, existing);

        await Assert.ThrowsAsync<IOException>(() =>
            viewModel.SaveMapAsAsync(
                candidate,
                destination,
                overwriteConfirmed: false));

        Assert.Contains("MAP Save As failed", viewModel.MapInspector.MapSaveStatusDisplay);
        Assert.Contains("overwrite was not confirmed", viewModel.MapInspector.MapSaveStatusDisplay);
        Assert.True(decoded.EditSession.IsDirty);
        Assert.Equal(existing, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public void MapSaveAs_StaleCandidateWriteFailureUsesShellStatus()
    {
        using var directory = new TemporaryDirectory();
        var archivePath = Path.Combine(directory.Path, "synthetic.dat");
        var decoded = DecodeMap(CreateInventoryMap(quantity: 7), FalloutGame.Fallout1);
        using var viewModel = CreateMapSaveViewModel(decoded, archivePath);
        var child = decoded.EditSession.Objects
            .Single(item => item.Placement.ParentRecordIndex is not null);
        Assert.True(decoded.EditSession.TrySetInventoryQuantity(child.Id, 23, out var editError), editError);
        var candidate = Assert.IsType<MapSaveCandidate>(viewModel.CreateMapSaveCandidate());

        Assert.True(decoded.EditSession.TrySetInventoryQuantity(child.Id, 31, out var changedError), changedError);
        viewModel.ReportMapSaveAsFailure(candidate, "synthetic write failure");

        Assert.Contains("previously selected MAP copy", viewModel.StatusMessage);
        Assert.Contains("synthetic write failure", viewModel.StatusMessage);
        Assert.DoesNotContain("synthetic write failure", viewModel.MapInspector.MapSaveStatusDisplay);
        Assert.True(decoded.EditSession.IsDirty);
        Assert.Equal(31, decoded.EditSession.GetObject(child.Id)?.Placement.InventoryQuantity);
    }

    [Fact]
    public void MapInspectorView_SaveAsButtonTracksEligibilityAndDirtyState()
    {
        AvaloniaTestEnvironment.EnsureInitialized();
        AvaloniaTestEnvironment.RunOnUiThread(() =>
        {
            var decoded = DecodeMap(CreateInventoryMap(quantity: 7), FalloutGame.Fallout1);
            using var archiveBrowser = new ArchiveBrowserViewModel();
            archiveBrowser.MapInspector.SetMapObjectPlacements(decoded.ObjectPlacements.Objects);
            archiveBrowser.MapInspector.SetMapEditSession(decoded.EditSession);
            archiveBrowser.MapInspector.SetMapSerializationContext(
                decoded.Game,
                decoded.Header,
                decoded.TileGrids,
                decoded.ObjectPlacements,
                "synthetic.dat");
            var view = new MapInspectorView { DataContext = archiveBrowser };
            var window = new Window { Content = view, Width = 800, Height = 600 };

            try
            {
                window.Show();
                var saveButton = Assert.IsType<Button>(view.FindControl<Button>("MapSaveAsButton"));
                Assert.False(saveButton.IsEnabled);
                Assert.Contains("no changed inventory quantities", archiveBrowser.MapInspector.MapSaveStatusDisplay);

                var child = decoded.EditSession.Objects
                    .Single(item => item.Placement.ParentRecordIndex is not null);
                Assert.True(decoded.EditSession.TrySetInventoryQuantity(child.Id, 23, out var editError), editError);

                Assert.True(saveButton.IsEnabled);
                Assert.Contains("eligible", archiveBrowser.MapInspector.MapSaveStatusDisplay);

                archiveBrowser.MapInspector.ReportMapSaveAsFailure("synthetic write failure");
                Assert.True(decoded.EditSession.IsDirty);
                Assert.True(saveButton.IsEnabled);
                Assert.Contains("synthetic write failure", archiveBrowser.MapInspector.MapSaveStatusDisplay);

                Assert.True(archiveBrowser.MapInspector.UndoMapEdit());
                Assert.False(saveButton.IsEnabled);
                Assert.False(decoded.EditSession.IsDirty);

                var hexEdit = DecodeMap(
                    CreateMap(19, [CreateObject(0x03000001, 0, 123)]),
                    FalloutGame.Fallout1);
                archiveBrowser.MapInspector.SetMapObjectPlacements(hexEdit.ObjectPlacements.Objects);
                archiveBrowser.MapInspector.SetMapEditSession(hexEdit.EditSession);
                archiveBrowser.MapInspector.SetMapSerializationContext(
                    hexEdit.Game,
                    hexEdit.Header,
                    hexEdit.TileGrids,
                    hexEdit.ObjectPlacements,
                    "synthetic.dat");
                Assert.True(hexEdit.EditSession.TrySetHexTileIndex(
                    Assert.Single(hexEdit.EditSession.Objects).Id,
                    124,
                    out var hexError), hexError);
                Assert.False(saveButton.IsEnabled);
                Assert.Contains("inventory-quantity edits only", archiveBrowser.MapInspector.MapSaveStatusDisplay);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static byte[] CreateInventoryMap(int quantity, int version = 19) =>
        CreateMap(
            version,
            [CreateObject(0x03000001, 0, 123, inventoryLength: 1, inventoryCapacity: 1)],
            [quantity],
            [CreateObject(0x01000001, 0, 456, critter: true)]);

    private static ArchiveBrowserViewModel CreateMapSaveViewModel(
        DecodedMap decoded,
        string archivePath)
    {
        var viewModel = new ArchiveBrowserViewModel();
        var archivePathField = typeof(ArchiveBrowserViewModel).GetField(
            "_archivePath",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(archivePathField);
        archivePathField.SetValue(viewModel, archivePath);
        viewModel.MapInspector.SetMapObjectPlacements(decoded.ObjectPlacements.Objects);
        viewModel.MapInspector.SetMapEditSession(decoded.EditSession);
        viewModel.MapInspector.SetMapSerializationContext(
            decoded.Game,
            decoded.Header,
            decoded.TileGrids,
            decoded.ObjectPlacements,
            archivePath);
        return viewModel;
    }

    private static MapObjectPlacementData ReplaceChildPlacement(
        DecodedMap decoded,
        Func<MapObjectPlacement, MapObjectPlacement> replace)
    {
        var objects = decoded.ObjectPlacements.Objects
            .Select(placement => placement.ParentRecordIndex is not null ? replace(placement) : placement)
            .ToArray();
        return decoded.ObjectPlacements with { Objects = Array.AsReadOnly(objects) };
    }

    private static MapSerializationEligibilityResult Validate(DecodedMap decoded) =>
        MapSerializationEligibility.Validate(
            decoded.Source,
            decoded.Game,
            decoded.Header,
            decoded.TileGrids,
            decoded.ObjectPlacements,
            decoded.EditSession);

    private static DecodedMap DecodeMap(byte[] bytes, FalloutGame game)
    {
        var source = new MapSourceData(bytes);
        var header = MapHeaderDecoder.Decode(source, game);
        var tileGrids = MapTileGridDecoder.Decode(source, header);
        var objectPlacements = MapObjectPlacementDecoder.Decode(source, header);
        var editSession = new MapEditSession(header, objectPlacements);
        return new DecodedMap(source, game, header, tileGrids, objectPlacements, editSession);
    }

    private static byte[] CreateMap(
        int version,
        IReadOnlyList<byte[]> objects,
        IReadOnlyList<int>? inventoryQuantities = null,
        IReadOnlyList<byte[]>? inventoryChildren = null)
    {
        inventoryQuantities ??= [];
        inventoryChildren ??= [];
        var bytes = new List<byte>();
        WriteInt32(bytes, version);
        bytes.AddRange(new byte[MapHeaderDecoder.HeaderSize - sizeof(int)]);
        WriteInt32At(bytes, 0x28, 0x0e);

        for (var index = 0; index < 5; index++)
        {
            WriteInt32(bytes, 0);
        }

        WriteInt32(bytes, objects.Count);
        var quantityIndex = 0;
        var childIndex = 0;
        for (var elevation = 0; elevation < 3; elevation++)
        {
            var elevationObjects = objects.Where(record =>
                BinaryPrimitives.ReadInt32BigEndian(record.AsSpan(10 * sizeof(int), sizeof(int))) == elevation)
                .ToArray();
            WriteInt32(bytes, elevationObjects.Length);
            foreach (var record in elevationObjects)
            {
                bytes.AddRange(record);
                if (record.Length > 0 && inventoryQuantities.Count > 0
                    && BinaryPrimitives.ReadInt32BigEndian(record.AsSpan(18 * sizeof(int), sizeof(int))) > 0)
                {
                    WriteInt32(bytes, inventoryQuantities[quantityIndex++]);
                    bytes.AddRange(inventoryChildren[childIndex++]);
                }
            }
        }

        return bytes.ToArray();
    }

    private static byte[] CreateObject(
        int pid,
        int elevation,
        int hexIndex,
        int inventoryLength = 0,
        int inventoryCapacity = 0,
        bool critter = false)
    {
        var words = new List<int>
        {
            101, hexIndex, 0, 0, 0, 0, 0, 0,
            0, 0, elevation, pid,
            0, 0, 0, 0, -1, 0,
            inventoryLength, inventoryCapacity, 0
        };
        if (critter)
        {
            words.AddRange(new int[11]);
        }
        else
        {
            words.Add(0);
        }

        var bytes = new byte[words.Count * sizeof(int)];
        for (var index = 0; index < words.Count; index++)
        {
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(index * sizeof(int)), words[index]);
        }

        return bytes;
    }

    private static void WriteInt32(List<byte> bytes, int value)
    {
        Span<byte> word = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(word, value);
        bytes.AddRange(word.ToArray());
    }

    private static void WriteInt32At(List<byte> bytes, int offset, int value)
    {
        Span<byte> word = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(word, value);
        for (var index = 0; index < word.Length; index++)
        {
            bytes[offset + index] = word[index];
        }
    }

    private sealed record DecodedMap(
        MapSourceData Source,
        FalloutGame Game,
        MapHeaderMetadata Header,
        MapTileGridData TileGrids,
        MapObjectPlacementData ObjectPlacements,
        MapEditSession EditSession);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"fae-map-save-race-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
