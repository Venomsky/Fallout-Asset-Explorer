using Avalonia;
using FalloutAssetExplorer.App.Views;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class MapIsometricObjectMarkerGeometryTests
{
    [Theory]
    [InlineData(100, 100, 320, 240)]
    [InlineData(101, 100, 352, 240)]
    [InlineData(100, 101, 336, 252)]
    [InlineData(101, 101, 368, 252)]
    [InlineData(99, 100, 304, 252)]
    [InlineData(99, 101, 320, 264)]
    [InlineData(101, 99, 336, 228)]
    [InlineData(99, 99, 288, 240)]
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
    [InlineData(0, 0, -3680, -360)]
    [InlineData(199, 0, 1104, -1548)]
    [InlineData(0, 199, -496, 2028)]
    [InlineData(199, 199, 4288, 840)]
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

        Assert.Equal(new Point(352, 240), worldPosition);
        Assert.Equal(new Point(352, 240), unzoomedCamera.ToScreen(worldPosition));
        Assert.Equal(new Point(804, 450), zoomedAndPannedCamera.ToScreen(worldPosition));
        Assert.Equal(worldPosition, zoomedAndPannedCamera.ToWorld(
            zoomedAndPannedCamera.ToScreen(worldPosition)));
    }

    [Fact]
    public void MarkerUsesHexAnchorAtViewportCenterWhileSpritePlacementRemainsUnchanged()
    {
        var projection = CreateProjection();
        Assert.True(MapIsometricFloorGeometry.TryProjectSquareCoordinates(
            50,
            50,
            projection,
            out var squareOrigin));
        Assert.True(MapIsometricObjectMarkerGeometry.TryGetWorldPosition(
            CreatePlacement(100, 100),
            projection,
            out var markerPosition));

        // CE's tileSetCenter puts the center hex's tile origin at ((W - 32) / 2, (H - 16) / 2) and the
        // square origin 16 px left / 2 px above it; objects are anchored at tile origin + (16, 8),
        // which is the exact center of the 640 x 480 viewport for the center hex.
        Assert.Equal(new Point(288, 230), squareOrigin);
        Assert.Equal(new Point(320, 240), markerPosition);
        Assert.True(MapIsometricObjectSpriteGeometry.TryGetImageBounds(
            markerPosition,
            new MapIsometricFloorCameraTransform(1, 0, 0),
            pixelWidth: 20,
            pixelHeight: 30,
            artShiftX: 0,
            artShiftY: 0,
            mapOffsetX: 0,
            mapOffsetY: 0,
            out var spriteBounds));
        Assert.Equal(new Rect(310, 211, 20, 30), spriteBounds);
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
