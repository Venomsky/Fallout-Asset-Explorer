using System.Text;
using Avalonia.Skia;
using FalloutAssetExplorer.App.ViewModels;
using FalloutAssetExplorer.App.Views;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class MapObjectSpritePrototypeTests
{
    private static readonly Lazy<bool> AvaloniaInitialized = new(() =>
    {
        SkiaPlatform.Initialize();
        return true;
    });

    [Fact]
    public void FrameSelector_UsesMapRotationAndDirectionLocalFrame()
    {
        var placement = CreatePlacement(1, rotation: 2, frame: 1);
        var document = CreateDocument();

        var success = MapIsometricObjectSpriteFrameSelector.TrySelectFrame(
            placement,
            document,
            out var directionIndex,
            out var frame,
            out var error);

        Assert.True(success);
        Assert.Equal(2, directionIndex);
        Assert.NotNull(frame);
        Assert.Equal((byte)23, frame.Pixels[0]);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public void FrameSelector_RejectsInvalidDirectionsWithoutFallback(int direction)
    {
        var placement = CreatePlacement(1, rotation: direction, frame: 0);

        var success = MapIsometricObjectSpriteFrameSelector.TrySelectFrame(
            placement,
            CreateDocument(),
            out var directionIndex,
            out var frame,
            out var error);

        Assert.False(success);
        Assert.Equal(direction, directionIndex);
        Assert.Null(frame);
        Assert.Contains("Direction index", error);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void FrameSelector_RejectsInvalidDirectionLocalFramesWithoutFallback(int frameIndex)
    {
        var placement = CreatePlacement(1, rotation: 3, frame: frameIndex);

        var success = MapIsometricObjectSpriteFrameSelector.TrySelectFrame(
            placement,
            CreateDocument(),
            out var directionIndex,
            out var frame,
            out var error);

        Assert.False(success);
        Assert.Equal(3, directionIndex);
        Assert.Null(frame);
        Assert.Contains($"Frame index {frameIndex}", error);
    }

    [Fact]
    public void FrameSelector_RejectsUnavailableDirectionMetadata()
    {
        var placement = CreatePlacement(1, rotation: 1, frame: 0);
        var frame = new FrmFrame(1, 1, 0, 0, [1]);
        var document = new FrmDocument(
            3,
            10,
            0,
            1,
            [new FrmDirection(0, "NE", 0, 0, [frame])]);

        var success = MapIsometricObjectSpriteFrameSelector.TrySelectFrame(
            placement,
            document,
            out _,
            out var selectedFrame,
            out var error);

        Assert.False(success);
        Assert.Null(selectedFrame);
        Assert.Contains("FRM has no direction 1", error);
    }

    [Fact]
    public void SpriteGeometry_UsesCeBaseProjectionOnceWithZeroOffsetsAndShifts()
    {
        // CE square origin is (16,2) above/left of the tile origin, then object rendering adds (16,8).
        // Thus the projected FAE marker needs one net (+32,+10) delta before frame alignment.
        var success = MapIsometricObjectSpriteGeometry.TryGetImageBounds(
            new Avalonia.Point(100, 200),
            new MapIsometricFloorCameraTransform(1, 0, 0),
            pixelWidth: 8,
            pixelHeight: 6,
            artShiftX: 0,
            artShiftY: 0,
            mapOffsetX: 0,
            mapOffsetY: 0,
            out var bounds);

        Assert.True(success);
        Assert.Equal(new Avalonia.Rect(128, 205, 8, 6), bounds);
    }

    [Theory]
    [InlineData(5, -3, 90, 50)]
    [InlineData(-7, 4, 66, 64)]
    public void SpriteGeometry_AddsPositiveAndNegativeMapOffsetsOnce(
        int mapOffsetX,
        int mapOffsetY,
        int expectedLeft,
        int expectedTop)
    {
        var success = MapIsometricObjectSpriteGeometry.TryGetImageBounds(
            new Avalonia.Point(10, 20),
            new MapIsometricFloorCameraTransform(2, 0, 0),
            pixelWidth: 4,
            pixelHeight: 3,
            artShiftX: 0,
            artShiftY: 0,
            mapOffsetX,
            mapOffsetY,
            out var bounds);

        Assert.True(success);
        Assert.Equal(new Avalonia.Rect(expectedLeft, expectedTop, 8, 6), bounds);
    }

    [Theory]
    [InlineData(7, -4, 35, 1)]
    [InlineData(-5, 3, 23, 8)]
    public void SpriteGeometry_AddsPositiveAndNegativeFrmArtShiftsOnce(
        short artShiftX,
        short artShiftY,
        int expectedLeft,
        int expectedTop)
    {
        var success = MapIsometricObjectSpriteGeometry.TryGetImageBounds(
            new Avalonia.Point(0, 0),
            new MapIsometricFloorCameraTransform(1, 0, 0),
            pixelWidth: 8,
            pixelHeight: 6,
            artShiftX,
            artShiftY,
            mapOffsetX: 0,
            mapOffsetY: 0,
            out var bounds);

        Assert.True(success);
        Assert.Equal(new Avalonia.Rect(expectedLeft, expectedTop, 8, 6), bounds);
    }

    [Theory]
    [InlineData(5, 9, 30, 2)]
    [InlineData(12, 4, 26, 7)]
    public void SpriteGeometry_UsesCeIntegerFrameDimensionAlignment(
        int pixelWidth,
        int pixelHeight,
        int expectedLeft,
        int expectedTop)
    {
        var success = MapIsometricObjectSpriteGeometry.TryGetImageBounds(
            new Avalonia.Point(0, 0),
            new MapIsometricFloorCameraTransform(1, 0, 0),
            pixelWidth,
            pixelHeight,
            artShiftX: 0,
            artShiftY: 0,
            mapOffsetX: 0,
            mapOffsetY: 0,
            out var bounds);

        Assert.True(success);
        Assert.Equal(new Avalonia.Rect(expectedLeft, expectedTop, pixelWidth, pixelHeight), bounds);
    }

    [Fact]
    public void SpriteGeometry_UsesDifferentDirectionSpecificArtShifts()
    {
        var direction0 = new FrmDirection(0, "NE", -5, 4, []);
        var direction3 = new FrmDirection(3, "SW", 7, -6, []);

        var firstDirectionSuccess = MapIsometricObjectSpriteGeometry.TryGetImageBounds(
            new Avalonia.Point(0, 0),
            new MapIsometricFloorCameraTransform(1, 0, 0),
            pixelWidth: 5,
            pixelHeight: 9,
            direction0.ShiftX,
            direction0.ShiftY,
            mapOffsetX: 2,
            mapOffsetY: -1,
            out var firstDirectionBounds);
        var secondDirectionSuccess = MapIsometricObjectSpriteGeometry.TryGetImageBounds(
            new Avalonia.Point(0, 0),
            new MapIsometricFloorCameraTransform(1, 0, 0),
            pixelWidth: 5,
            pixelHeight: 9,
            direction3.ShiftX,
            direction3.ShiftY,
            mapOffsetX: 2,
            mapOffsetY: -1,
            out var secondDirectionBounds);

        Assert.True(firstDirectionSuccess);
        Assert.True(secondDirectionSuccess);
        Assert.Equal(new Avalonia.Rect(27, 5, 5, 9), firstDirectionBounds);
        Assert.Equal(new Avalonia.Rect(39, -5, 5, 9), secondDirectionBounds);
    }

    [Fact]
    public void SpriteGeometry_RejectsUnavailableImageDimensions()
    {
        var success = MapIsometricObjectSpriteGeometry.TryGetImageBounds(
            new Avalonia.Point(0, 0),
            new MapIsometricFloorCameraTransform(1, 0, 0),
            pixelWidth: 0,
            pixelHeight: 1,
            artShiftX: 0,
            artShiftY: 0,
            mapOffsetX: 0,
            mapOffsetY: 0,
            out var bounds);

        Assert.False(success);
        Assert.Equal(default, bounds);
    }

    [Fact]
    public async Task PrepareSelectedSprite_ResolvesAndPreparesRequestedItemDirectionAndFrame()
    {
        EnsureAvaloniaInitialized();
        using var viewModel = CreateViewModel(out var archive);
        var placement = CreatePlacement(1, rotation: 2, frame: 1);
        SelectPlacement(viewModel, placement);
        Assert.Same(placement, viewModel.MapInspector.SelectedMapObject);

        await viewModel.PrepareSelectedMapObjectSpriteAsync();

        Assert.True(
            viewModel.SelectedMapObjectSprite is not null,
            viewModel.MapObjectSpriteStatusDisplay);
        var sprite = Assert.IsType<MapIsometricObjectSprite>(viewModel.SelectedMapObjectSprite);
        Assert.Equal(placement.RecordIndex, sprite.RecordIndex);
        Assert.Equal("art/items/item.frm", sprite.ResourcePath);
        Assert.Equal(2, sprite.DirectionIndex);
        Assert.Equal(1, sprite.FrameIndex);
        Assert.Contains("placement applied", viewModel.MapObjectSpriteStatusDisplay);
        Assert.Equal(1, archive.AssetReadCount);
    }

    [Fact]
    public async Task PrepareSelectedSprite_ReusesCachedBitmapForPreviouslyPreparedItem()
    {
        EnsureAvaloniaInitialized();
        using var viewModel = CreateViewModel(out var archive);
        var item = CreatePlacement(1, rotation: 2, frame: 1);
        var unsupported = CreatePlacement(2, rotation: 0, frame: 0) with
        {
            PidTypeIdentifier = 2,
            RawPid = 0x02000001
        };

        SelectPlacement(viewModel, item);
        await viewModel.PrepareSelectedMapObjectSpriteAsync();
        var prepared = Assert.IsType<MapIsometricObjectSprite>(viewModel.SelectedMapObjectSprite);

        SelectPlacement(viewModel, unsupported);
        await viewModel.PrepareSelectedMapObjectSpriteAsync();
        Assert.Null(viewModel.SelectedMapObjectSprite);

        SelectPlacement(viewModel, item);
        await viewModel.PrepareSelectedMapObjectSpriteAsync();

        var cached = Assert.IsType<MapIsometricObjectSprite>(viewModel.SelectedMapObjectSprite);
        Assert.Same(prepared.Bitmap, cached.Bitmap);
        Assert.Equal(1, archive.AssetReadCount);
        Assert.Contains("cached", viewModel.MapObjectSpriteStatusDisplay, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public async Task PrepareSelectedSprite_InvalidDirectionRetainsMarkerFallback(int direction)
    {
        using var viewModel = CreateViewModel(out _);
        SelectPlacement(viewModel, CreatePlacement(1, rotation: direction, frame: 0));

        await viewModel.PrepareSelectedMapObjectSpriteAsync();

        Assert.Null(viewModel.SelectedMapObjectSprite);
        Assert.Contains("Direction index", viewModel.MapObjectSpriteStatusDisplay);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public async Task PrepareSelectedSprite_InvalidFrameRetainsMarkerFallback(int frame)
    {
        using var viewModel = CreateViewModel(out _);
        SelectPlacement(viewModel, CreatePlacement(1, rotation: 2, frame: frame));

        await viewModel.PrepareSelectedMapObjectSpriteAsync();

        Assert.Null(viewModel.SelectedMapObjectSprite);
        Assert.Contains($"Frame index {frame}", viewModel.MapObjectSpriteStatusDisplay);
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(0, 2)]
    public async Task PrepareSelectedSprite_UnsupportedPidOrFidCombinationRetainsMarkerFallback(
        int pidType,
        int fidArtType)
    {
        using var viewModel = CreateViewModel(out _);
        var placement = CreatePlacement(1, rotation: 0, frame: 0) with
        {
            PidTypeIdentifier = pidType,
            RawPid = pidType << 24 | 1,
            RawFid = fidArtType << 24 | 1
        };
        SelectPlacement(viewModel, placement);

        await viewModel.PrepareSelectedMapObjectSpriteAsync();

        Assert.Null(viewModel.SelectedMapObjectSprite);
        Assert.Contains("not an eligible", viewModel.MapObjectSpriteStatusDisplay);
    }

    [Fact]
    public async Task PrepareSelectedSprite_InventoryChildRetainsMarkerFallback()
    {
        using var viewModel = CreateViewModel(out _);
        var placement = CreatePlacement(1, rotation: 0, frame: 0) with
        {
            ParentRecordIndex = 0,
            InventoryDepth = 1
        };
        viewModel.MapObjectPlacements.Add(placement);
        viewModel.MapInspector.SelectedMapObject = placement;
        viewModel.MapInspector.SelectedMapDiagnosticLayerIndex =
            MapInspectorViewModel.IsometricObjectMarkersLayerIndex;

        await viewModel.PrepareSelectedMapObjectSpriteAsync();

        Assert.Null(viewModel.SelectedMapObjectSprite);
        Assert.Contains("not an eligible", viewModel.MapObjectSpriteStatusDisplay);
    }

    [Fact]
    public async Task PrepareSelectedSprite_InvalidHexPositionRetainsMarkerFallback()
    {
        using var viewModel = CreateViewModel(out _);
        var placement = CreatePlacement(1, rotation: 0, frame: 0) with
        {
            RawHexIndex = -1,
            HexX = null,
            HexY = null
        };
        SelectPlacement(viewModel, placement);

        await viewModel.PrepareSelectedMapObjectSpriteAsync();

        Assert.Null(viewModel.SelectedMapObjectSprite);
        Assert.Contains("not an eligible", viewModel.MapObjectSpriteStatusDisplay);
    }

    [Theory]
    [InlineData(false, "Could not resolve item FID")]
    [InlineData(true, "is not an FRM")]
    public async Task PrepareSelectedSprite_MissingOrNonFrmResourceRetainsMarkerFallback(
        bool includeNonFrmAsset,
        string expectedStatus)
    {
        using var viewModel = CreateViewModel(
            out _,
            listContent: includeNonFrmAsset ? "item.pro\n" : "missing.frm\n",
            includeNonFrmAsset: includeNonFrmAsset);
        SelectPlacement(viewModel, CreatePlacement(1, rotation: 0, frame: 0));

        await viewModel.PrepareSelectedMapObjectSpriteAsync();

        Assert.Null(viewModel.SelectedMapObjectSprite);
        Assert.Contains(expectedStatus, viewModel.MapObjectSpriteStatusDisplay);
    }

    [Fact]
    public async Task PrepareSelectedSprite_StaleReadCannotReplaceNewSelection()
    {
        using var viewModel = CreateViewModel(out var archive, blockAssetRead: true);
        var item = CreatePlacement(1, rotation: 2, frame: 1);
        var unsupported = CreatePlacement(2, rotation: 0, frame: 0) with
        {
            PidTypeIdentifier = 2,
            RawPid = 0x02000001
        };
        SelectPlacement(viewModel, item);

        var oldLoad = viewModel.PrepareSelectedMapObjectSpriteAsync();
        await archive.AssetReadStarted.WaitAsync(TimeSpan.FromSeconds(10));

        SelectPlacement(viewModel, unsupported);
        await viewModel.PrepareSelectedMapObjectSpriteAsync();
        archive.ReleaseAssetRead();
        await oldLoad.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Null(viewModel.SelectedMapObjectSprite);
        Assert.Contains("not an eligible", viewModel.MapObjectSpriteStatusDisplay);
    }

    private static ArchiveBrowserViewModel CreateViewModel(
        out SyntheticSpriteArchive archive,
        string listContent = "item.frm\n",
        bool includeNonFrmAsset = false,
        bool blockAssetRead = false)
    {
        var viewModel = new ArchiveBrowserViewModel();
        archive = new SyntheticSpriteArchive(listContent, includeNonFrmAsset, blockAssetRead);
        typeof(ArchiveBrowserViewModel)
            .GetField("_archive", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(viewModel, archive);
        viewModel.MapInspector.SetAvailableElevations([0], preferredElevationIndex: 0);
        viewModel.MapInspector.SetMapInspectorActive(true);
        viewModel.MapInspector.SelectedMapDiagnosticLayerIndex =
            MapInspectorViewModel.IsometricObjectMarkersLayerIndex;
        return viewModel;
    }

    private static void EnsureAvaloniaInitialized() => _ = AvaloniaInitialized.Value;

    private static void SelectPlacement(
        ArchiveBrowserViewModel viewModel,
        MapObjectPlacement placement)
    {
        if (!viewModel.MapObjectPlacements.Contains(placement))
        {
            viewModel.MapObjectPlacements.Add(placement);
        }

        viewModel.MapInspector.SelectMapObjectFromIsometricMarker(
            placement.RecordIndex,
            viewModel.MapInspector.SelectedMapElevationIndex);
    }

    private static MapObjectPlacement CreatePlacement(int recordIndex, int rotation, int frame)
    {
        const int rawHexIndex = 1000;
        var (hexX, hexY) = MapObjectPlacementDecoder.GetHexCoordinates(rawHexIndex);
        return new MapObjectPlacement(
            recordIndex,
            0,
            0,
            0,
            rawHexIndex,
            hexX,
            hexY,
            0,
            0,
            0,
            0,
            frame,
            rotation,
            0,
            0,
            0,
            1,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            [],
            null,
            null);
    }

    private static FrmDocument CreateDocument()
    {
        var directions = Enumerable.Range(0, 6)
            .Select(direction => new[]
            {
                new FrmFixtureFrame(1, 1, 0, 0, [(byte)(direction + 1)]),
                new FrmFixtureFrame(1, 1, 0, 0, [(byte)(direction + 21)])
            })
            .ToArray();
        return FrmDecoder.Decode(FrmFixture.Create(directions));
    }

    private sealed class SyntheticSpriteArchive : IGameArchive
    {
        private const string ItemResourcePath = "art/items/item.frm";
        private readonly Dictionary<ArchiveEntry, byte[]> _contents = [];
        private readonly TaskCompletionSource<bool> _assetReadStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _releaseAssetRead =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly bool _blockAssetRead;
        private int _assetReadCount;

        public SyntheticSpriteArchive(
            string listContent,
            bool includeNonFrmAsset,
            bool blockAssetRead)
        {
            Path = typeof(MapObjectSpritePrototypeTests).Assembly.Location;
            _blockAssetRead = blockAssetRead;
            AddEntry("art/items/items.lst", Encoding.ASCII.GetBytes(listContent));
            AddEntry("COLOR.PAL", new byte[FalloutPalette.MinimalFileSize]);
            if (includeNonFrmAsset)
            {
                AddEntry("art/items/item.pro", [1]);
            }
            else if (!listContent.StartsWith("missing.", StringComparison.Ordinal))
            {
                AddEntry(ItemResourcePath, FrmFixture.Create(
                    Enumerable.Range(0, 6)
                        .Select(direction => new[]
                        {
                            new FrmFixtureFrame(1, 1, 0, 0, [(byte)(direction + 1)]),
                            new FrmFixtureFrame(1, 1, 0, 0, [(byte)(direction + 21)])
                        })
                        .ToArray()));
            }

            BrowserEntries = _contents.Keys.ToArray();
        }

        public string Path { get; }

        public FalloutGame Game => FalloutGame.Fallout2;

        public ArchiveFormat Format => ArchiveFormat.Dat2;

        public IReadOnlyList<ArchiveEntry> BrowserEntries { get; }

        public int AssetReadCount => Volatile.Read(ref _assetReadCount);

        public Task AssetReadStarted => _assetReadStarted.Task;

        public void ReleaseAssetRead() => _releaseAssetRead.TrySetResult(true);

        public async Task CopyEntryToAsync(
            ArchiveEntry entry,
            Stream destination,
            CancellationToken cancellationToken = default)
        {
            if (string.Equals(entry.Path, ItemResourcePath, StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref _assetReadCount);
                if (_blockAssetRead)
                {
                    _assetReadStarted.TrySetResult(true);
                    await _releaseAssetRead.Task.ConfigureAwait(false);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!_contents.TryGetValue(entry, out var bytes))
            {
                throw new IOException($"Unexpected synthetic archive entry '{entry.Path}'.");
            }

            await destination.WriteAsync(bytes, cancellationToken);
        }

        private void AddEntry(string path, byte[] bytes) =>
            _contents.Add(new ArchiveEntry(path, bytes.Length, null, Path), bytes);
    }
}
