using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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
    public void SpriteDiagnostics_CountNonZeroPaletteIndexPixelsFromFrameData()
    {
        var frame = new FrmFrame(3, 2, 0, 0, [0, 1, 0, 255, 2, 0]);
        var emptyFrame = new FrmFrame(2, 1, 0, 0, [0, 0]);

        Assert.Equal(3, MapIsometricObjectSpriteDiagnostics.CountNonZeroPaletteIndexPixels(frame));
        Assert.Equal(0, MapIsometricObjectSpriteDiagnostics.CountNonZeroPaletteIndexPixels(emptyFrame));
    }

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
        var placement = CreatePlacement(1, rotation: 2, frame: 1) with
        {
            RawOffsetX = 7,
            RawOffsetY = -3
        };
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
        Assert.Equal(1, sprite.Bitmap.PixelSize.Width);
        Assert.Equal(1, sprite.Bitmap.PixelSize.Height);
        Assert.Equal(1, sprite.NonZeroPaletteIndexPixelCount);
        Assert.Equal((short)0, sprite.FrameOffsetX);
        Assert.Equal((short)0, sprite.FrameOffsetY);
        Assert.Contains("FRM 'art/items/item.frm'", viewModel.MapObjectSpriteStatusDisplay);
        Assert.Contains("bitmap 1x1", viewModel.MapObjectSpriteStatusDisplay);
        Assert.Contains("source pixels with palette index != 0: 1", viewModel.MapObjectSpriteStatusDisplay);
        Assert.Contains("frame offset (0, 0)", viewModel.MapObjectSpriteStatusDisplay);
        Assert.Contains("direction shift (0, 0)", viewModel.MapObjectSpriteStatusDisplay);
        Assert.Contains("MAP offsets (7, -3)", viewModel.MapObjectSpriteStatusDisplay);
        Assert.Contains("placement applied", viewModel.MapObjectSpriteStatusDisplay);
        Assert.Equal(1, archive.AssetReadCount);
    }

    [Fact]
    public async Task PrepareSelectedSprite_ResolvesAndPreparesRequestedSceneryDirectionAndFrame()
    {
        EnsureAvaloniaInitialized();
        using var viewModel = CreateViewModel(
            out var archive,
            listContent: "scenery.frm\n",
            artDirectory: "scenery",
            resourceName: "scenery.frm");
        var placement = CreatePlacement(
            1,
            rotation: 2,
            frame: 1,
            pidType: 2,
            fidArtType: 2) with
        {
            RawOffsetX = -5,
            RawOffsetY = 9
        };
        SelectPlacement(viewModel, placement);

        await viewModel.PrepareSelectedMapObjectSpriteAsync();

        Assert.True(
            viewModel.SelectedMapObjectSprite is not null,
            viewModel.MapObjectSpriteStatusDisplay);
        var sprite = Assert.IsType<MapIsometricObjectSprite>(viewModel.SelectedMapObjectSprite);
        Assert.Equal(placement.RecordIndex, sprite.RecordIndex);
        Assert.Equal("art/scenery/scenery.frm", sprite.ResourcePath);
        Assert.Equal(2, sprite.DirectionIndex);
        Assert.Equal(1, sprite.FrameIndex);
        Assert.Equal(1, sprite.Bitmap.PixelSize.Width);
        Assert.Equal(1, sprite.Bitmap.PixelSize.Height);
        Assert.Equal(1, sprite.NonZeroPaletteIndexPixelCount);
        Assert.Contains("source pixels with palette index != 0: 1", viewModel.MapObjectSpriteStatusDisplay);
        Assert.Contains("MAP offsets (-5, 9)", viewModel.MapObjectSpriteStatusDisplay);
        Assert.Contains("Prepared scenery sprite", viewModel.MapObjectSpriteStatusDisplay);
        Assert.Equal(1, archive.AssetReadCount);
    }

    [Fact]
    public async Task PrepareMapObjectSprites_PreservesPlacementOrderAndContinuesAfterAssetFailure()
    {
        EnsureAvaloniaInitialized();
        var validFrm = FrmFixture.Create(
            Enumerable.Range(0, 6)
                .Select(direction => new[]
                {
                    new FrmFixtureFrame(1, 1, 0, 0, [(byte)(direction + 1)]),
                    new FrmFixtureFrame(1, 1, 0, 0, [(byte)(direction + 21)])
                })
                .ToArray());
        using var viewModel = CreateViewModel(
            out var archive,
            listContent: "first.frm\nbroken.frm\nlast.frm\n",
            resourceBytesByName: new Dictionary<string, byte[]>
            {
                ["first.frm"] = validFrm,
                ["broken.frm"] = [1],
                ["last.frm"] = validFrm
            });
        var placements = new[]
        {
            CreatePlacement(9, rotation: 2, frame: 1, fidListIndex: 0),
            CreatePlacement(4, rotation: 0, frame: 0, fidListIndex: 1),
            CreatePlacement(2, rotation: 4, frame: 0, fidListIndex: 2)
        };
        foreach (var placement in placements)
        {
            viewModel.MapObjectPlacements.Add(placement);
        }

        SelectPlacement(viewModel, placements[0]);
        await viewModel.PrepareMapObjectSpritesAsync();

        Assert.Equal(new[] { 9, 2 }, viewModel.MapObjectSprites.Select(sprite => sprite.RecordIndex));
        Assert.Equal((2, 1), (
            viewModel.MapObjectSprites[0].DirectionIndex,
            viewModel.MapObjectSprites[0].FrameIndex));
        Assert.Equal((4, 0), (
            viewModel.MapObjectSprites[1].DirectionIndex,
            viewModel.MapObjectSprites[1].FrameIndex));
        Assert.Equal(9, viewModel.SelectedMapObjectSprite?.RecordIndex);
        Assert.Contains("Prepared 2 of 3", viewModel.MapObjectSpritesStatusDisplay);
        Assert.Contains("object #4", viewModel.MapObjectSpritesStatusDisplay);
        Assert.Equal(3, archive.AssetReadCount);
    }

    [Fact]
    public async Task PrepareMapObjectSprites_EmptyCandidateSetProducesEmptyCollection()
    {
        using var viewModel = CreateViewModel(out _);
        viewModel.MapObjectPlacements.Add(CreatePlacement(1, rotation: 0, frame: 0) with
        {
            PidTypeIdentifier = 1,
            RawPid = 0x01000001,
            RawFid = 0x01000001
        });

        await viewModel.PrepareMapObjectSpritesAsync();

        Assert.Empty(viewModel.MapObjectSprites);
        Assert.Contains("No eligible", viewModel.MapObjectSpritesStatusDisplay);
    }

    [Fact]
    public async Task PrepareMapObjectSprites_ReusesCachedBitmapAcrossRecords()
    {
        EnsureAvaloniaInitialized();
        using var viewModel = CreateViewModel(out var archive);
        var first = CreatePlacement(8, rotation: 2, frame: 1);
        var second = CreatePlacement(3, rotation: 2, frame: 1);
        viewModel.MapObjectPlacements.Add(first);
        viewModel.MapObjectPlacements.Add(second);

        await viewModel.PrepareMapObjectSpritesAsync();

        Assert.Equal(new[] { 8, 3 }, viewModel.MapObjectSprites.Select(sprite => sprite.RecordIndex));
        Assert.Same(viewModel.MapObjectSprites[0].Bitmap, viewModel.MapObjectSprites[1].Bitmap);
        Assert.Equal(1, archive.AssetReadCount);

        await viewModel.PrepareMapObjectSpritesAsync();

        Assert.Equal(1, archive.AssetReadCount);
    }

    [Fact]
    public void Render_ComposesOverlappingSpritesAndPreservesTransparencyInInputOrder()
    {
        EnsureAvaloniaInitialized();

        const int spriteSize = 512;
        const int viewportWidth = 640;
        const int viewportHeight = 480;
        var placements = new[]
        {
            CreatePlacement(1, rotation: 0, frame: 0),
            CreatePlacement(2, rotation: 0, frame: 0)
        };
        using var firstBitmap = CreateOverlapTestSprite(spriteSize, isSecondSprite: false);
        using var secondBitmap = CreateOverlapTestSprite(spriteSize, isSecondSprite: true);
        var sprites = new[]
        {
            CreateRenderTestSprite(placements[0], firstBitmap),
            CreateRenderTestSprite(placements[1], secondBitmap)
        };
        var view = new MapIsometricFloorView
        {
            Placements = placements,
            Elevation = 0,
            ShowObjectMarkers = true,
            ObjectSprites = sprites
        };
        view.Measure(new Size(viewportWidth, viewportHeight));
        view.Arrange(new Rect(0, 0, viewportWidth, viewportHeight));
        view.FitToView();

        Assert.True(MapIsometricFloorGeometry.TryCreateDefaultViewTransform(out var projection));
        var markers = MapIsometricObjectMarkerFilter.GetMarkers(placements, 0, projection);
        Assert.Equal(2, markers.Count);
        var camera = CreateFitCameraForRenderTest(
            markers,
            sprites,
            viewportWidth,
            viewportHeight);
        Assert.True(MapIsometricObjectSpriteGeometry.TryGetImageBounds(
            markers[0].WorldPosition,
            camera,
            spriteSize,
            spriteSize,
            sprites[0].ArtShiftX,
            sprites[0].ArtShiftY,
            placements[0].RawOffsetX,
            placements[0].RawOffsetY,
            out var imageBounds));

        using var rendered = new RenderTargetBitmap(new PixelSize(viewportWidth, viewportHeight));
        rendered.Render(view);

        Assert.Equal(
            (R: (byte)255, G: (byte)0, B: (byte)0, A: (byte)255),
            ReadPixelAtSourcePoint(rendered, imageBounds, 80, 256, spriteSize));
        Assert.Equal(
            (R: (byte)0, G: (byte)0, B: (byte)255, A: (byte)255),
            ReadPixelAtSourcePoint(rendered, imageBounds, 432, 256, spriteSize));
        Assert.Equal(
            (R: (byte)0, G: (byte)0, B: (byte)255, A: (byte)255),
            ReadPixelAtSourcePoint(rendered, imageBounds, 256, 256, spriteSize));
        Assert.Equal(
            (R: (byte)0x17, G: (byte)0x1A, B: (byte)0x1F, A: (byte)0xFF),
            ReadPixelAtSourcePoint(rendered, imageBounds, 256, 32, spriteSize));
    }

    [Fact]
    public async Task PrepareMapObjectSprites_MissingBitmapLeavesEmptyCollectionAndReportsFailure()
    {
        using var viewModel = CreateViewModel(out _, listContent: "missing.item\n");
        var placement = CreatePlacement(1, rotation: 0, frame: 0);
        viewModel.MapObjectPlacements.Add(placement);

        await viewModel.PrepareMapObjectSpritesAsync();

        Assert.Empty(viewModel.MapObjectSprites);
        Assert.Contains("Could not resolve item FID", viewModel.MapObjectSpritesStatusDisplay);
    }

    [Fact]
    public async Task PrepareSelectedSprite_StatusReportsNonzeroFrmFrameAndDirectionOffsets()
    {
        EnsureAvaloniaInitialized();
        var directionBlocks = Enumerable.Range(0, 6)
            .Select(direction => new[]
            {
                new FrmFixtureFrame(1, 1, 0, 0, [(byte)(direction + 1)]),
                new FrmFixtureFrame(2, 1, -4, 6, [(byte)(direction + 21), (byte)(direction + 22)])
            })
            .ToArray();
        var frmBytes = FrmFixture.Create(directionBlocks);
        BinaryPrimitives.WriteInt16BigEndian(frmBytes.AsSpan(10 + 2 * 2), 11);
        BinaryPrimitives.WriteInt16BigEndian(frmBytes.AsSpan(22 + 2 * 2), -13);
        using var viewModel = CreateViewModel(
            out _,
            listContent: "scenery.frm\n",
            artDirectory: "scenery",
            resourceName: "scenery.frm",
            resourceBytes: frmBytes);
        var placement = CreatePlacement(
            1,
            rotation: 2,
            frame: 1,
            pidType: 2,
            fidArtType: 2);
        SelectPlacement(viewModel, placement);

        await viewModel.PrepareSelectedMapObjectSpriteAsync();

        var sprite = Assert.IsType<MapIsometricObjectSprite>(viewModel.SelectedMapObjectSprite);
        Assert.Equal((short)-4, sprite.FrameOffsetX);
        Assert.Equal((short)6, sprite.FrameOffsetY);
        Assert.Equal((short)11, sprite.ArtShiftX);
        Assert.Equal((short)-13, sprite.ArtShiftY);
        Assert.Contains("frame offset (-4, 6)", viewModel.MapObjectSpriteStatusDisplay);
        Assert.Contains("direction shift (11, -13)", viewModel.MapObjectSpriteStatusDisplay);
    }

    [Theory]
    [InlineData(0, "items", "item.frm")]
    [InlineData(2, "scenery", "scenery.frm")]
    public async Task PrepareSelectedSprite_ReusesCachedBitmapForPreviouslyPreparedObject(
        int objectType,
        string artDirectory,
        string resourceName)
    {
        EnsureAvaloniaInitialized();
        using var viewModel = CreateViewModel(
            out var archive,
            listContent: $"{resourceName}\n",
            artDirectory: artDirectory,
            resourceName: resourceName);
        var item = CreatePlacement(
            1,
            rotation: 2,
            frame: 1,
            pidType: objectType,
            fidArtType: objectType);
        var unsupported = CreatePlacement(2, rotation: 0, frame: 0) with
        {
            PidTypeIdentifier = 1,
            RawPid = 0x01000001
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
        Assert.Equal(prepared.NonZeroPaletteIndexPixelCount, cached.NonZeroPaletteIndexPixelCount);
        Assert.Contains($"FRM 'art/{artDirectory}/{resourceName}'", viewModel.MapObjectSpriteStatusDisplay);
        Assert.Contains("bitmap 1x1", viewModel.MapObjectSpriteStatusDisplay);
        Assert.Contains("source pixels with palette index != 0: 1", viewModel.MapObjectSpriteStatusDisplay);
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

    [Fact]
    public async Task PrepareSelectedSprite_SceneryOnAnotherElevationRetainsMarkerFallback()
    {
        using var viewModel = CreateViewModel(
            out var archive,
            listContent: "scenery.frm\n",
            artDirectory: "scenery",
            resourceName: "scenery.frm",
            elevationOptions: [0, 1]);
        var placement = CreatePlacement(
            1,
            rotation: 0,
            frame: 0,
            pidType: 2,
            fidArtType: 2,
            elevation: 1);
        viewModel.MapObjectPlacements.Add(placement);
        viewModel.MapInspector.SelectMapObjectFromIsometricMarker(placement.RecordIndex, elevationIndex: 1);

        Assert.Same(placement, viewModel.MapInspector.SelectedMapObject);
        Assert.Equal(0, viewModel.MapInspector.SelectedMapElevationIndex);

        await viewModel.PrepareSelectedMapObjectSpriteAsync();

        Assert.Null(viewModel.SelectedMapObjectSprite);
        Assert.Contains("not an eligible", viewModel.MapObjectSpriteStatusDisplay);
        Assert.Equal(0, archive.AssetReadCount);
    }

    [Fact]
    public async Task PrepareSelectedSprite_SceneryInventoryChildRetainsMarkerFallback()
    {
        using var viewModel = CreateViewModel(
            out var archive,
            listContent: "scenery.frm\n",
            artDirectory: "scenery",
            resourceName: "scenery.frm");
        var placement = CreatePlacement(
            1,
            rotation: 0,
            frame: 0,
            pidType: 2,
            fidArtType: 2) with
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
        Assert.Equal(0, archive.AssetReadCount);
    }

    [Fact]
    public async Task PrepareSelectedSprite_SceneryWithUnsupportedReasonRetainsMarkerFallback()
    {
        using var viewModel = CreateViewModel(out var archive, artDirectory: "scenery");
        var placement = CreatePlacement(
            1,
            rotation: 0,
            frame: 0,
            pidType: 2,
            fidArtType: 2) with
        {
            UnsupportedReason = "Scenery PRO subtype metadata is unavailable."
        };
        SelectPlacement(viewModel, placement);

        await viewModel.PrepareSelectedMapObjectSpriteAsync();

        Assert.Null(viewModel.SelectedMapObjectSprite);
        Assert.Contains("Scenery PRO subtype metadata is unavailable", viewModel.MapObjectSpriteStatusDisplay);
        Assert.Equal(0, archive.AssetReadCount);
    }

    [Fact]
    public async Task PrepareSelectedSprite_ItemWithUnsupportedReasonPreservesExistingBehavior()
    {
        using var viewModel = CreateViewModel(out _);
        var placement = CreatePlacement(1, rotation: 0, frame: 0) with
        {
            UnsupportedReason = "Existing Item decoder diagnostic."
        };
        SelectPlacement(viewModel, placement);

        await viewModel.PrepareSelectedMapObjectSpriteAsync();

        Assert.NotNull(viewModel.SelectedMapObjectSprite);
        Assert.Contains("Prepared item sprite", viewModel.MapObjectSpriteStatusDisplay);
    }

    [Fact]
    public async Task PrepareSelectedSprite_SceneryMissingResourceRetainsMarkerFallback()
    {
        using var viewModel = CreateViewModel(
            out _,
            listContent: "missing.frm\n",
            artDirectory: "scenery",
            resourceName: "scenery.frm");
        SelectPlacement(
            viewModel,
            CreatePlacement(1, rotation: 0, frame: 0, pidType: 2, fidArtType: 2));

        await viewModel.PrepareSelectedMapObjectSpriteAsync();

        Assert.Null(viewModel.SelectedMapObjectSprite);
        Assert.Contains("Could not resolve scenery FID", viewModel.MapObjectSpriteStatusDisplay);
    }

    [Fact]
    public async Task PrepareSelectedSprite_SceneryWithMalformedFrmRetainsMarkerFallback()
    {
        using var viewModel = CreateViewModel(
            out _,
            listContent: "scenery.frm\n",
            artDirectory: "scenery",
            resourceName: "scenery.frm",
            resourceBytes: [0x01]);
        SelectPlacement(
            viewModel,
            CreatePlacement(1, rotation: 0, frame: 0, pidType: 2, fidArtType: 2));

        await viewModel.PrepareSelectedMapObjectSpriteAsync();

        Assert.Null(viewModel.SelectedMapObjectSprite);
        Assert.Contains("Could not prepare scenery sprite", viewModel.MapObjectSpriteStatusDisplay);
    }

    [Fact]
    public async Task PrepareSelectedSprite_SceneryWithNonFrmResourceRetainsMarkerFallback()
    {
        using var viewModel = CreateViewModel(
            out _,
            listContent: "scenery.pro\n",
            includeNonFrmAsset: true,
            artDirectory: "scenery",
            resourceName: "scenery.frm");
        SelectPlacement(
            viewModel,
            CreatePlacement(1, rotation: 0, frame: 0, pidType: 2, fidArtType: 2));

        await viewModel.PrepareSelectedMapObjectSpriteAsync();

        Assert.Null(viewModel.SelectedMapObjectSprite);
        Assert.Contains("is not an FRM", viewModel.MapObjectSpriteStatusDisplay);
    }

    [Fact]
    public async Task PrepareSelectedSprite_SceneryWithInvalidFrameRetainsMarkerFallback()
    {
        using var viewModel = CreateViewModel(
            out _,
            listContent: "scenery.frm\n",
            artDirectory: "scenery",
            resourceName: "scenery.frm");
        SelectPlacement(
            viewModel,
            CreatePlacement(1, rotation: 0, frame: 2, pidType: 2, fidArtType: 2));

        await viewModel.PrepareSelectedMapObjectSpriteAsync();

        Assert.Null(viewModel.SelectedMapObjectSprite);
        Assert.Contains("Frame index 2", viewModel.MapObjectSpriteStatusDisplay);
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

    [Theory]
    [InlineData(0, "items", "item.frm")]
    [InlineData(2, "scenery", "scenery.frm")]
    public async Task PrepareSelectedSprite_StaleReadCannotReplaceNewSelection(
        int objectType,
        string artDirectory,
        string resourceName)
    {
        using var viewModel = CreateViewModel(
            out var archive,
            listContent: $"{resourceName}\n",
            blockAssetRead: true,
            artDirectory: artDirectory,
            resourceName: resourceName);
        var item = CreatePlacement(
            1,
            rotation: 2,
            frame: 1,
            pidType: objectType,
            fidArtType: objectType);
        var unsupported = CreatePlacement(2, rotation: 0, frame: 0, pidType: 1);
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
        bool blockAssetRead = false,
        string artDirectory = "items",
        string resourceName = "item.frm",
        byte[]? resourceBytes = null,
        IReadOnlyDictionary<string, byte[]>? resourceBytesByName = null,
        int[]? elevationOptions = null)
    {
        var viewModel = new ArchiveBrowserViewModel();
        archive = new SyntheticSpriteArchive(
            listContent,
            includeNonFrmAsset,
            blockAssetRead,
            artDirectory,
            resourceName,
            resourceBytes,
            resourceBytesByName);
        typeof(ArchiveBrowserViewModel)
            .GetField("_archive", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(viewModel, archive);
        viewModel.MapInspector.SetAvailableElevations(elevationOptions ?? [0], preferredElevationIndex: 0);
        viewModel.MapInspector.SetMapInspectorActive(true);
        viewModel.MapInspector.SelectedMapDiagnosticLayerIndex =
            MapInspectorViewModel.IsometricObjectMarkersLayerIndex;
        return viewModel;
    }

    private static void EnsureAvaloniaInitialized() => _ = AvaloniaInitialized.Value;

    private static WriteableBitmap CreateOverlapTestSprite(int size, bool isSecondSprite)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(size, size),
            new Vector(96, 96),
            PixelFormat.Rgba8888,
            AlphaFormat.Unpremul);
        using var framebuffer = bitmap.Lock();
        var row = new byte[size * 4];
        for (var y = 0; y < size; y++)
        {
            Array.Clear(row);
            for (var x = 0; x < size; x++)
            {
                var isOpaque = y >= 80 && y < 432 && (isSecondSprite
                    ? x >= 176 && x < 496
                    : x >= 16 && x < 336);
                if (!isOpaque)
                {
                    continue;
                }

                var pixelOffset = x * 4;
                row[pixelOffset] = isSecondSprite ? (byte)0 : (byte)255;
                row[pixelOffset + 1] = 0;
                row[pixelOffset + 2] = isSecondSprite ? (byte)255 : (byte)0;
                row[pixelOffset + 3] = 255;
            }

            Marshal.Copy(row, 0, IntPtr.Add(framebuffer.Address, y * framebuffer.RowBytes), row.Length);
        }

        return bitmap;
    }

    private static MapIsometricObjectSprite CreateRenderTestSprite(
        MapObjectPlacement placement,
        Bitmap bitmap) =>
        new(
            placement.RecordIndex,
            bitmap,
            $"synthetic/{placement.RecordIndex}.frm",
            DirectionIndex: 0,
            FrameIndex: 0,
            ArtShiftX: 0,
            ArtShiftY: 0,
            FrameOffsetX: 0,
            FrameOffsetY: 0,
            NonZeroPaletteIndexPixelCount: 1);

    private static MapIsometricFloorCameraTransform CreateFitCameraForRenderTest(
        IReadOnlyList<MapIsometricObjectMarker> markers,
        IReadOnlyList<MapIsometricObjectSprite> sprites,
        int viewportWidth,
        int viewportHeight)
    {
        Assert.True(MapIsometricFloorGeometry.TryCreateDefaultViewTransform(out var projection));
        var corners = new[]
        {
            (X: 0, Y: 0),
            (X: 99, Y: 0),
            (X: 0, Y: 99),
            (X: 99, Y: 99)
        };
        var points = corners
            .Select(corner =>
            {
                Assert.True(MapIsometricFloorGeometry.TryProjectSquareCoordinates(
                    corner.X,
                    corner.Y,
                    projection,
                    out var point));
                return point;
            })
            .ToArray();
        var left = points.Min(point => point.X);
        var top = points.Min(point => point.Y);
        var right = points.Max(point => point.X);
        var bottom = points.Max(point => point.Y);
        var contentBounds = new Rect(left, top, right - left, bottom - top);

        foreach (var sprite in sprites)
        {
            Assert.True(MapIsometricObjectSpriteGeometry.TryGetSpriteWorldBounds(
                sprite,
                markers,
                out var spriteBounds));
            var boundsWithSprite = MapIsometricFloorViewportGeometry.IncludeBounds(
                contentBounds,
                spriteBounds);
            Assert.True(boundsWithSprite.HasValue);
            contentBounds = boundsWithSprite.Value;
        }

        Assert.True(MapIsometricFloorViewportGeometry.TryGetFitTransform(
            contentBounds,
            viewportWidth,
            viewportHeight,
            out var camera));
        return camera;
    }

    private static (byte R, byte G, byte B, byte A) ReadPixelAtSourcePoint(
        Bitmap rendered,
        Rect imageBounds,
        int sourceX,
        int sourceY,
        int sourceSize)
    {
        var x = (int)Math.Floor(imageBounds.X + (sourceX + 0.5) * imageBounds.Width / sourceSize);
        var y = (int)Math.Floor(imageBounds.Y + (sourceY + 0.5) * imageBounds.Height / sourceSize);
        using var pixels = new WriteableBitmap(
            rendered.PixelSize,
            new Vector(96, 96),
            PixelFormat.Rgba8888,
            AlphaFormat.Unpremul);
        using var framebuffer = pixels.Lock();
        rendered.CopyPixels(framebuffer, AlphaFormat.Unpremul);
        var offset = y * framebuffer.RowBytes + x * 4;
        return (
            Marshal.ReadByte(framebuffer.Address, offset),
            Marshal.ReadByte(framebuffer.Address, offset + 1),
            Marshal.ReadByte(framebuffer.Address, offset + 2),
            Marshal.ReadByte(framebuffer.Address, offset + 3));
    }

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

    private static MapObjectPlacement CreatePlacement(
        int recordIndex,
        int rotation,
        int frame,
        int pidType = 0,
        int fidArtType = 0,
        int elevation = 0,
        string? unsupportedReason = null,
        int fidListIndex = 0)
    {
        const int rawHexIndex = 1000;
        var (hexX, hexY) = MapObjectPlacementDecoder.GetHexCoordinates(rawHexIndex);
        return new MapObjectPlacement(
            recordIndex,
            0,
            elevation,
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
            (fidArtType << 24) | fidListIndex,
            0,
            0,
            (pidType << 24) | 1,
            pidType,
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
            unsupportedReason);
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
        private readonly Dictionary<ArchiveEntry, byte[]> _contents = [];
        private readonly TaskCompletionSource<bool> _assetReadStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _releaseAssetRead =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly bool _blockAssetRead;
        private readonly HashSet<string> _resourcePaths = new(StringComparer.OrdinalIgnoreCase);
        private int _assetReadCount;

        public SyntheticSpriteArchive(
            string listContent,
            bool includeNonFrmAsset,
            bool blockAssetRead,
            string artDirectory,
            string resourceName,
            byte[]? resourceBytes,
            IReadOnlyDictionary<string, byte[]>? resourceBytesByName)
        {
            Path = typeof(MapObjectSpritePrototypeTests).Assembly.Location;
            _blockAssetRead = blockAssetRead;
            AddEntry(
                $"art/{artDirectory}/{artDirectory}.lst",
                Encoding.ASCII.GetBytes(listContent));
            AddEntry("COLOR.PAL", new byte[FalloutPalette.MinimalFileSize]);
            if (includeNonFrmAsset)
            {
                AddResource(
                    $"art/{artDirectory}/{System.IO.Path.ChangeExtension(resourceName, ".pro")}",
                    [1]);
            }
            else if (!listContent.StartsWith("missing.", StringComparison.Ordinal))
            {
                if (resourceBytesByName is not null)
                {
                    foreach (var (name, bytes) in resourceBytesByName)
                    {
                        AddResource($"art/{artDirectory}/{name}", bytes);
                    }
                }
                else
                {
                    AddResource($"art/{artDirectory}/{resourceName}", resourceBytes ?? CreateFrmBytes());
                }
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
            if (_resourcePaths.Contains(entry.Path))
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

        private void AddResource(string path, byte[] bytes)
        {
            AddEntry(path, bytes);
            _resourcePaths.Add(path);
        }

        private static byte[] CreateFrmBytes() =>
            FrmFixture.Create(
                Enumerable.Range(0, 6)
                    .Select(direction => new[]
                    {
                        new FrmFixtureFrame(1, 1, 0, 0, [(byte)(direction + 1)]),
                        new FrmFixtureFrame(1, 1, 0, 0, [(byte)(direction + 21)])
                    })
                    .ToArray());
    }
}
