using Avalonia;
using FalloutAssetExplorer.App.Views;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class MapIsometricObjectMarkerGeometryTests
{
    [Theory]
    [InlineData(100, 100, 288, 230)]
    [InlineData(101, 100, 320, 230)]
    [InlineData(100, 101, 304, 242)]
    [InlineData(101, 101, 336, 242)]
    [InlineData(99, 100, 272, 242)]
    [InlineData(99, 101, 288, 254)]
    [InlineData(101, 99, 304, 218)]
    [InlineData(99, 99, 256, 230)]
    public void TryGetWorldPosition_MatchesIndependentHexParityReferences(
        int hexX,
        int hexY,
        double expectedX,
        double expectedY)
    {
        var placement = CreatePlacement(hexX, hexY);

        Assert.True(MapIsometricObjectMarkerGeometry.TryGetWorldPosition(
            placement,
            CreateProjection(),
            out var worldPosition));

        Assert.Equal(new Point(expectedX, expectedY), worldPosition);
    }

    [Theory]
    [InlineData(0, 0, -3712, -370)]
    [InlineData(199, 0, 1072, -1558)]
    [InlineData(0, 199, -528, 2018)]
    [InlineData(199, 199, 4256, 830)]
    public void TryGetWorldPosition_MatchesIndependentMapBoundaryReferences(
        int hexX,
        int hexY,
        double expectedX,
        double expectedY)
    {
        Assert.True(MapIsometricObjectMarkerGeometry.TryGetWorldPosition(
            CreatePlacement(hexX, hexY),
            CreateProjection(),
            out var worldPosition));

        Assert.Equal(new Point(expectedX, expectedY), worldPosition);
    }

    [Theory]
    [InlineData(-1, null, null)]
    [InlineData(40_000, null, null)]
    [InlineData(20_099, null, null)]
    [InlineData(20_099, 99, 100)]
    public void TryGetWorldPosition_RejectsMissingInvalidOrInconsistentHexPosition(
        int rawHexIndex,
        int? hexX,
        int? hexY)
    {
        Assert.False(MapIsometricObjectMarkerGeometry.TryGetWorldPosition(
            CreatePlacement(rawHexIndex, hexX, hexY),
            CreateProjection(),
            out _));
    }

    [Fact]
    public void TryGetWorldPosition_RejectsNonCanonicalProjectionCenter()
    {
        Assert.True(MapIsometricFloorGeometry.TryCreateViewTransform(
            101,
            100,
            640,
            480,
            0,
            0,
            out var projection));

        Assert.False(MapIsometricObjectMarkerGeometry.TryGetWorldPosition(
            CreatePlacement(100, 100),
            projection,
            out _));
    }

    [Fact]
    public void CameraTransformsChangeScreenPositionWithoutChangingWorldPosition()
    {
        var placement = CreatePlacement(101, 100);
        Assert.True(MapIsometricObjectMarkerGeometry.TryGetWorldPosition(
            placement,
            CreateProjection(),
            out var worldPosition));
        var unzoomedCamera = new MapIsometricFloorCameraTransform(1, 0, 0);
        var zoomedAndPannedCamera = new MapIsometricFloorCameraTransform(2, 100, -30);

        Assert.Equal(new Point(320, 230), worldPosition);
        Assert.Equal(new Point(320, 230), unzoomedCamera.ToScreen(worldPosition));
        Assert.Equal(new Point(740, 430), zoomedAndPannedCamera.ToScreen(worldPosition));
        Assert.Equal(worldPosition, zoomedAndPannedCamera.ToWorld(
            zoomedAndPannedCamera.ToScreen(worldPosition)));
    }

    private static MapIsometricFloorViewTransform CreateProjection()
    {
        Assert.True(MapIsometricFloorGeometry.TryCreateViewTransform(
            100,
            100,
            640,
            480,
            0,
            0,
            out var projection));
        return projection;
    }

    private static MapObjectPlacement CreatePlacement(int hexX, int hexY) =>
        CreatePlacement(
            hexY * MapObjectPlacementDecoder.HexGridWidth
                + MapObjectPlacementDecoder.HexGridWidth - 1 - hexX,
            hexX,
            hexY);

    private static MapObjectPlacement CreatePlacement(
        int rawHexIndex,
        int? hexX,
        int? hexY) =>
        new(
            RecordIndex: 0,
            RecordOffset: 0,
            ElevationGroup: 0,
            RawObjectId: 0,
            RawHexIndex: rawHexIndex,
            HexX: hexX,
            HexY: hexY,
            RawOffsetX: 0,
            RawOffsetY: 0,
            RawScreenX: 0,
            RawScreenY: 0,
            RawFrame: 0,
            RawRotation: 0,
            RawFid: 0,
            RawObjectFlags: 0,
            StoredElevation: 0,
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
            RawPayloadWords: Array.Empty<int>(),
            Warning: null,
            UnsupportedReason: null);
}
