using Avalonia;
using FalloutAssetExplorer.App.Views;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class MapIsometricFloorRegistrationDiagnosticGeometryTests
{
    [Fact]
    public void NearbyTilesReportSerializedCellsAndTheSameBoundsAsTheFloorRenderer()
    {
        var values = new uint[MapTileGridDecoder.ValuesPerGrid];
        var bitmapSizes = new Dictionary<uint, PixelSize>
        {
            [0] = new PixelSize(64, 32)
        };
        Assert.True(MapIsometricFloorGeometry.TryCreateDefaultViewTransform(out var projection));
        var camera = new MapIsometricFloorCameraTransform(1.5, 200, 100);

        var tiles = MapIsometricFloorRegistrationDiagnosticGeometry.GetNearbyTiles(
            values,
            50,
            50,
            floorId => bitmapSizes.TryGetValue(floorId, out var size) ? size : null,
            projection,
            camera,
            10000,
            10000);

        Assert.Equal(9, tiles.Count);
        var center = Assert.Single(tiles, tile => tile.X == 50 && tile.Y == 50);
        Assert.Equal(MapTileGridDecoder.GetSerializedIndex(50, 50), center.SerializedIndex);
        Assert.Equal((uint)0, center.FloorArtId);
        Assert.Equal(MapIsometricFloorRegistrationTileStatus.Drawn, center.Status);
        Assert.NotNull(center.WorldAnchor);
        Assert.Equal(
            new Rect(center.WorldAnchor!.Value, new Size(64, 32)),
            center.WorldBounds);
        Assert.Equal(
            MapIsometricFloorViewportGeometry.GetImageBounds(
                camera,
                center.WorldAnchor.Value,
                64,
                32),
            center.ScreenBounds);
    }

    [Fact]
    public void NearbyTilesIdentifyEmptyAndUnavailableFloorArtWithoutInventingBounds()
    {
        var values = new uint[MapTileGridDecoder.ValuesPerGrid];
        var emptyIndex = MapTileGridDecoder.GetSerializedIndex(50, 50);
        var unavailableIndex = MapTileGridDecoder.GetSerializedIndex(51, 50);
        values[emptyIndex] = MapFloorTileDiagnosticGeometry.CeEmptyFloorArtId;
        values[unavailableIndex] = 2;
        Assert.True(MapIsometricFloorGeometry.TryCreateDefaultViewTransform(out var projection));

        var tiles = MapIsometricFloorRegistrationDiagnosticGeometry.GetNearbyTiles(
            values,
            50,
            50,
            _ => null,
            projection,
            new MapIsometricFloorCameraTransform(1, 0, 0),
            1000,
            1000);

        var empty = Assert.Single(tiles, tile => tile.SerializedIndex == emptyIndex);
        Assert.Equal(MapIsometricFloorRegistrationTileStatus.Empty, empty.Status);
        Assert.Null(empty.WorldBounds);
        var unavailable = Assert.Single(tiles, tile => tile.SerializedIndex == unavailableIndex);
        Assert.Equal(MapIsometricFloorRegistrationTileStatus.BitmapUnavailable, unavailable.Status);
        Assert.Null(unavailable.WorldBounds);
    }

    [Fact]
    public void RegistrationOverlayIsDisabledByDefault()
    {
        var view = new MapIsometricFloorView();

        Assert.False(view.ShowRegistrationDiagnostics);
    }
}
