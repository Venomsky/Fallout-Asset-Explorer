using System.Buffers.Binary;
using System.Text;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class MapObjectPlacementDecoderTests
{
    [Theory]
    [InlineData(0, 199, 0)]
    [InlineData(199, 0, 0)]
    [InlineData(200, 199, 1)]
    [InlineData(201, 198, 1)]
    [InlineData(39999, 0, 199)]
    public void GetHexCoordinates_MapsSerializedIndex(int index, int expectedX, int expectedY)
    {
        Assert.Equal((expectedX, expectedY), MapObjectPlacementDecoder.GetHexCoordinates(index));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(40000)]
    public void GetHexCoordinates_RejectsIndicesOutsideHexGrid(int index)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MapObjectPlacementDecoder.GetHexCoordinates(index));
    }

    [Theory]
    [InlineData(FalloutGame.Fallout1, 19)]
    [InlineData(FalloutGame.Fallout2, 19)]
    [InlineData(FalloutGame.Fallout2, 20)]
    public void Decode_ReadsConfirmedCritterLayout(FalloutGame game, int version)
    {
        var map = MapFixture.Create(version, [
            MapFixture.ObjectRecord(pid: 0x01000001, tile: 201, elevation: 2, critter: true)
        ], [0, 0, 1]);
        var header = MapHeaderDecoder.Decode(map, game);

        var decoded = MapObjectPlacementDecoder.Decode(map, header);
        var placement = Assert.Single(decoded.Objects);

        Assert.True(decoded.IsComplete);
        Assert.Equal(1, decoded.TotalObjectCount);
        Assert.Equal(2, placement.ElevationGroup);
        Assert.Equal(2, placement.StoredElevation);
        Assert.Equal(201, placement.RawHexIndex);
        Assert.Equal((198, 1), (placement.HexX, placement.HexY));
        Assert.Equal(0x01000001, placement.RawPid);
        Assert.Equal(1, placement.PidTypeIdentifier);
        Assert.Equal(11, placement.RawPayloadWords.Count);
        Assert.Equal(unchecked((int)0xF0000001u), placement.RawPayloadWords[0]);
    }

    [Fact]
    public void Decode_ReadsMultipleRecordsAcrossElevationGroupsAndPreservesRawFields()
    {
        var map = MapFixture.Create(19, [
            MapFixture.ObjectRecord(pid: 0x0300002A, tile: 0, elevation: 0, flags: unchecked((int)0xFEDCBA98)),
            MapFixture.ObjectRecord(pid: 0x05000001, tile: 39999, elevation: 1),
            MapFixture.ObjectRecord(pid: 0x05000010, tile: 20000, elevation: 2, extraWords: [7, 8, 9, 10])
        ], [1, 1, 1]);
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout1);

        var decoded = MapObjectPlacementDecoder.Decode(map, header);

        Assert.True(decoded.IsComplete);
        Assert.Equal(3, decoded.Objects.Count);
        Assert.Equal(new[] { 0, 1, 2 }, decoded.Objects.Select(item => item.ElevationGroup));
        Assert.Equal((199, 0), (decoded.Objects[0].HexX, decoded.Objects[0].HexY));
        Assert.Equal((0, 199), (decoded.Objects[1].HexX, decoded.Objects[1].HexY));
        Assert.Equal(unchecked((int)0xFEDCBA98u), decoded.Objects[0].RawObjectFlags);
        Assert.Equal(new[] { 7, 8, 9, 10 }, decoded.Objects[2].RawPayloadWords.Skip(1));
        Assert.Equal(0, decoded.UnparsedByteCount);
    }

    [Fact]
    public void Decode_IsCompleteWhenDeclaredRecordsAreReadEvenIfTrailingBytesRemain()
    {
        var map = MapFixture.Create(19, [
            MapFixture.ObjectRecord(pid: 0x01000001, tile: 100, elevation: 0, critter: true)
        ], [1, 0, 0]).Concat(new byte[] { 0xAA, 0xBB }).ToArray();
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout1);

        var decoded = MapObjectPlacementDecoder.Decode(map, header);

        Assert.True(decoded.IsComplete);
        Assert.Null(decoded.UnsupportedReason);
        Assert.Equal(1, decoded.DecodedTopLevelObjectCount);
        Assert.Equal(2, decoded.UnparsedByteCount);
    }

    [Fact]
    public void Decode_SkipsScriptExtentsBeforeObjectSection()
    {
        var spatialScriptExtent = MapFixture.CreateScriptExtent(scriptId: 0x01000001);
        var map = MapFixture.Create(
            20,
            [MapFixture.ObjectRecord(pid: 0x03000001, tile: 100, elevation: 0)],
            [1, 0, 0],
            scriptCounts: [1, 0, 0, 0, 0],
            scriptExtents: [spatialScriptExtent]);
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout2);

        var decoded = MapObjectPlacementDecoder.Decode(map, header);

        Assert.True(decoded.IsComplete);
        Assert.Equal(
            MapHeaderDecoder.HeaderSize + sizeof(int) + spatialScriptExtent.Length + 4 * sizeof(int),
            decoded.ObjectSectionOffset);
        Assert.Single(decoded.Objects);
    }

    [Theory]
    [InlineData(0x00000001, "PRO subtype")]
    [InlineData(0x02000001, "PRO subtype")]
    public void Decode_ReportsUnsupportedSubtypeDependentRecordLayouts(int pid, string reason)
    {
        var map = MapFixture.Create(19, [
            MapFixture.ObjectRecord(pid, tile: 1234, elevation: 1, extraWords: [0x12345678])
        ], [0, 1, 0]);
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout1);

        var decoded = MapObjectPlacementDecoder.Decode(map, header);
        var placement = Assert.Single(decoded.Objects);

        Assert.False(decoded.IsComplete);
        Assert.Contains(reason, decoded.UnsupportedReason);
        Assert.Contains(reason, placement.UnsupportedReason);
        Assert.Equal((165, 6), (placement.HexX, placement.HexY));
        Assert.Equal(1234, placement.RawHexIndex);
    }

    [Fact]
    public async Task DecodeAsync_AdvancesPastGenericSceneryUsingValidatedPrototypeMetadata()
    {
        const int sceneryPid = 0x02000001;
        var archive = PrototypeArchive.Create(
            [("00000001.PRO", PrototypeArchive.CreateSceneryPrototype(sceneryPid, sceneryType: 5))]);
        var map = MapFixture.Create(20, [
            MapFixture.ObjectRecord(sceneryPid, tile: 123, elevation: 0),
            MapFixture.ObjectRecord(pid: 0x01000001, tile: 456, elevation: 0, critter: true)
        ], [2, 0, 0]);
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout2);

        var decoded = await MapObjectPlacementDecoder.DecodeAsync(map, header, archive);

        Assert.True(decoded.IsComplete);
        Assert.Equal(2, decoded.Objects.Count);
        Assert.Equal(sceneryPid, decoded.Objects[0].RawPid);
        Assert.Null(decoded.Objects[0].UnsupportedReason);
        Assert.Equal(11, decoded.Objects[1].RawPayloadWords.Count);
    }

    [Fact]
    public async Task DecodeAsync_AdvancesPastDoorSceneryAndPreservesPayloadBeforeCritter()
    {
        const int sceneryPid = 0x02000001;
        const int doorPayload = unchecked((int)0xA1B2C3D4);
        var archive = PrototypeArchive.Create(
            [("00000001.PRO", PrototypeArchive.CreateSceneryPrototype(sceneryPid, sceneryType: 0))]);
        var map = MapFixture.Create(20, [
            MapFixture.ObjectRecord(sceneryPid, tile: 123, elevation: 0, extraWords: [doorPayload]),
            MapFixture.ObjectRecord(pid: 0x01000001, tile: 456, elevation: 0, critter: true)
        ], [2, 0, 0]);
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout2);

        var decoded = await MapObjectPlacementDecoder.DecodeAsync(map, header, archive);

        Assert.True(decoded.IsComplete);
        Assert.Equal(2, decoded.Objects.Count);
        Assert.Equal(new[] { 0, 1 }, decoded.Objects.Select(item => item.RecordIndex));
        Assert.Equal(sceneryPid, decoded.Objects[0].RawPid);
        Assert.Null(decoded.Objects[0].UnsupportedReason);
        Assert.Equal(new[] { 0x11223344, doorPayload }, decoded.Objects[0].RawPayloadWords);
        Assert.Equal(0x01000001, decoded.Objects[1].RawPid);
        Assert.Equal(11, decoded.Objects[1].RawPayloadWords.Count);
    }

    [Fact]
    public async Task DecodeAsync_AdvancesPastStairsSceneryAndPreservesPayloadBeforeCritter()
    {
        const int sceneryPid = 0x02000001;
        const int destinationBuiltTile = unchecked((int)0xA1B2C3D4);
        const int destinationMap = unchecked((int)0xE5F60718);
        var archive = PrototypeArchive.Create(
            [("00000001.PRO", PrototypeArchive.CreateSceneryPrototype(sceneryPid, sceneryType: 1))]);
        var map = MapFixture.Create(20, [
            MapFixture.ObjectRecord(
                sceneryPid,
                tile: 123,
                elevation: 0,
                extraWords: [destinationBuiltTile, destinationMap]),
            MapFixture.ObjectRecord(pid: 0x01000001, tile: 456, elevation: 0, critter: true)
        ], [2, 0, 0]);
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout2);

        var decoded = await MapObjectPlacementDecoder.DecodeAsync(map, header, archive);

        Assert.True(decoded.IsComplete);
        Assert.Equal(2, decoded.Objects.Count);
        Assert.Equal(new[] { 0, 1 }, decoded.Objects.Select(item => item.RecordIndex));
        Assert.Equal(sceneryPid, decoded.Objects[0].RawPid);
        Assert.Null(decoded.Objects[0].UnsupportedReason);
        Assert.Equal(
            new[] { 0x11223344, destinationBuiltTile, destinationMap },
            decoded.Objects[0].RawPayloadWords);
        Assert.Equal(0x01000001, decoded.Objects[1].RawPid);
        Assert.Equal(11, decoded.Objects[1].RawPayloadWords.Count);
    }

    [Fact]
    public async Task DecodeAsync_RejectsTruncatedStairsPayload()
    {
        const int sceneryPid = 0x02000001;
        var archive = PrototypeArchive.Create(
            [("00000001.PRO", PrototypeArchive.CreateSceneryPrototype(sceneryPid, sceneryType: 1))]);
        var map = MapFixture.Create(20, [
            MapFixture.ObjectRecord(sceneryPid, tile: 123, elevation: 2, extraWords: [99, 98])
        ], [0, 0, 1]);
        Array.Resize(ref map, map.Length - sizeof(int));
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout2);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => MapObjectPlacementDecoder.DecodeAsync(map, header, archive));

        Assert.Contains("scenery subtype 1 payload field 1", exception.Message);
        Assert.Contains("extends beyond", exception.Message);
    }

    [Fact]
    public async Task DecodeAsync_AdvancesPastElevatorSceneryAndPreservesPayloadBeforeCritter()
    {
        const int sceneryPid = 0x02000001;
        const int elevatorType = unchecked((int)0xA1B2C3D4);
        const int elevatorLevel = unchecked((int)0xE5F60718);
        var archive = PrototypeArchive.Create(
            [("00000001.PRO", PrototypeArchive.CreateSceneryPrototype(sceneryPid, sceneryType: 2))]);
        var map = MapFixture.Create(20, [
            MapFixture.ObjectRecord(
                sceneryPid,
                tile: 123,
                elevation: 0,
                extraWords: [elevatorType, elevatorLevel]),
            MapFixture.ObjectRecord(pid: 0x01000001, tile: 456, elevation: 0, critter: true)
        ], [2, 0, 0]);
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout2);

        var decoded = await MapObjectPlacementDecoder.DecodeAsync(map, header, archive);

        Assert.True(decoded.IsComplete);
        Assert.Equal(2, decoded.Objects.Count);
        Assert.Equal(new[] { 0, 1 }, decoded.Objects.Select(item => item.RecordIndex));
        Assert.Equal(sceneryPid, decoded.Objects[0].RawPid);
        Assert.Null(decoded.Objects[0].UnsupportedReason);
        Assert.Equal(new[] { 0x11223344, elevatorType, elevatorLevel }, decoded.Objects[0].RawPayloadWords);
        Assert.Equal(0x01000001, decoded.Objects[1].RawPid);
        Assert.Equal(11, decoded.Objects[1].RawPayloadWords.Count);
    }

    [Fact]
    public async Task DecodeAsync_RejectsTruncatedElevatorPayload()
    {
        const int sceneryPid = 0x02000001;
        var archive = PrototypeArchive.Create(
            [("00000001.PRO", PrototypeArchive.CreateSceneryPrototype(sceneryPid, sceneryType: 2))]);
        var map = MapFixture.Create(20, [
            MapFixture.ObjectRecord(sceneryPid, tile: 123, elevation: 2, extraWords: [99, 98])
        ], [0, 0, 1]);
        Array.Resize(ref map, map.Length - sizeof(int));
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout2);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => MapObjectPlacementDecoder.DecodeAsync(map, header, archive));

        Assert.Contains("scenery subtype 2 payload field 1", exception.Message);
        Assert.Contains("extends beyond", exception.Message);
    }

    [Theory]
    [InlineData(3, 19)]
    [InlineData(3, 20)]
    [InlineData(4, 19)]
    [InlineData(4, 20)]
    public async Task DecodeAsync_AdvancesPastLadderSceneryForMapVersion(
        int sceneryType,
        int mapVersion)
    {
        const int sceneryPid = 0x02000001;
        const int destinationMap = unchecked((int)0xA1B2C3D4);
        const int destinationBuiltTile = unchecked((int)0xE5F60718);
        var ladderPayload = mapVersion == 19
            ? new[] { destinationBuiltTile }
            : new[] { destinationMap, destinationBuiltTile };
        var archive = PrototypeArchive.Create(
            [("00000001.PRO", PrototypeArchive.CreateSceneryPrototype(sceneryPid, sceneryType))]);
        var map = MapFixture.Create(mapVersion, [
            MapFixture.ObjectRecord(
                sceneryPid,
                tile: 123,
                elevation: 0,
                extraWords: ladderPayload),
            MapFixture.ObjectRecord(pid: 0x01000001, tile: 456, elevation: 0, critter: true)
        ], [2, 0, 0]);
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout2);

        var decoded = await MapObjectPlacementDecoder.DecodeAsync(map, header, archive);

        Assert.True(decoded.IsComplete);
        Assert.Equal(2, decoded.Objects.Count);
        Assert.Equal(new[] { 0, 1 }, decoded.Objects.Select(item => item.RecordIndex));
        Assert.Equal(sceneryPid, decoded.Objects[0].RawPid);
        Assert.Null(decoded.Objects[0].UnsupportedReason);
        Assert.Equal(
            new[] { 0x11223344 }.Concat(ladderPayload),
            decoded.Objects[0].RawPayloadWords);
        Assert.Equal(0x01000001, decoded.Objects[1].RawPid);
        Assert.Equal(11, decoded.Objects[1].RawPayloadWords.Count);
    }

    [Theory]
    [InlineData(19, 3, 99)]
    [InlineData(20, 4, 99, 98)]
    public async Task DecodeAsync_RejectsTruncatedLadderPayload(
        int mapVersion,
        int sceneryType,
        int firstPayload,
        int? secondPayload = null)
    {
        const int sceneryPid = 0x02000001;
        var payloadWords = secondPayload is int second
            ? new[] { firstPayload, second }
            : [firstPayload];
        var archive = PrototypeArchive.Create(
            [("00000001.PRO", PrototypeArchive.CreateSceneryPrototype(sceneryPid, sceneryType))]);
        var map = MapFixture.Create(mapVersion, [
            MapFixture.ObjectRecord(
                sceneryPid,
                tile: 123,
                elevation: 2,
                extraWords: payloadWords)
        ], [0, 0, 1]);
        Array.Resize(ref map, map.Length - sizeof(int));
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout2);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => MapObjectPlacementDecoder.DecodeAsync(map, header, archive));

        var missingFieldIndex = mapVersion == 19 ? 0 : 1;
        Assert.Contains(
            $"scenery subtype {sceneryType} payload field {missingFieldIndex}",
            exception.Message);
        Assert.Contains("extends beyond", exception.Message);
    }

    [Fact]
    public async Task DecodeAsync_RejectsTruncatedDoorPayload()
    {
        const int sceneryPid = 0x02000001;
        var archive = PrototypeArchive.Create(
            [("00000001.PRO", PrototypeArchive.CreateSceneryPrototype(sceneryPid, sceneryType: 0))]);
        var map = MapFixture.Create(20, [
            MapFixture.ObjectRecord(sceneryPid, tile: 123, elevation: 0)
        ], [1, 0, 0]);
        Array.Resize(ref map, map.Length - 2 * sizeof(int));
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout2);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => MapObjectPlacementDecoder.DecodeAsync(map, header, archive));

        Assert.Contains("scenery subtype 0 payload field 0", exception.Message);
        Assert.Contains("extends beyond", exception.Message);
    }

    [Fact]
    public async Task DecodeAsync_StopsOnUnsupportedScenerySubtype()
    {
        const int sceneryPid = 0x02000001;
        var archive = PrototypeArchive.Create(
            [("00000001.PRO", PrototypeArchive.CreateSceneryPrototype(sceneryPid, sceneryType: 6))]);
        var map = MapFixture.Create(20, [
            MapFixture.ObjectRecord(sceneryPid, tile: 123, elevation: 0, extraWords: [99, 98]),
            MapFixture.ObjectRecord(pid: 0x01000001, tile: 456, elevation: 0, critter: true)
        ], [2, 0, 0]);
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout2);

        var decoded = await MapObjectPlacementDecoder.DecodeAsync(map, header, archive);

        Assert.False(decoded.IsComplete);
        Assert.Single(decoded.Objects);
        Assert.Contains("unsupported subtype 6", decoded.UnsupportedReason);
    }

    [Fact]
    public async Task DecodeAsync_RejectsSceneryPrototypeWithMismatchedPid()
    {
        const int mapPid = 0x02000001;
        var archive = PrototypeArchive.Create(
            [("00000001.PRO", PrototypeArchive.CreateSceneryPrototype(0x02000002, sceneryType: 5))]);

        var decoded = await DecodeSingleSceneryAsync(mapPid, archive);

        Assert.False(decoded.IsComplete);
        Assert.Contains("does not match MAP PID", decoded.UnsupportedReason);
        Assert.Null(Assert.Single(decoded.Objects).PrototypeFileName);
    }

    [Fact]
    public async Task DecodeAsync_StoresSceneryPrototypeFileNameFromOneBasedListIndex()
    {
        const int sceneryPid = 0x02000002;
        const string prototypeFileName = "Tree123.pro";
        var archive = PrototypeArchive.Create(
            [
                (prototypeFileName, PrototypeArchive.CreateSceneryPrototype(sceneryPid, sceneryType: 5))
            ],
            listContent: $"unused.pro\n{prototypeFileName}\n");

        var decoded = await DecodeSingleSceneryAsync(sceneryPid, archive);

        var placement = Assert.Single(decoded.Objects);
        Assert.True(decoded.IsComplete, decoded.UnsupportedReason);
        Assert.Equal(prototypeFileName, placement.PrototypeFileName);
        Assert.Contains($"Prototype file: {prototypeFileName}", placement.Details);
    }

    [Theory]
    [InlineData(0x02000000)]
    [InlineData(0x02000002)]
    public async Task DecodeAsync_RejectsInvalidOrOutOfRangeSceneryListIndex(int pid)
    {
        var archive = PrototypeArchive.Create(
            [("00000001.PRO", PrototypeArchive.CreateSceneryPrototype(0x02000001, sceneryType: 5))]);

        var decoded = await DecodeSingleSceneryAsync(pid, archive);

        Assert.False(decoded.IsComplete);
        Assert.Contains("list index", decoded.UnsupportedReason);
    }

    [Fact]
    public async Task DecodeAsync_ReportsMissingSceneryList()
    {
        var archive = new PrototypeArchive();

        var decoded = await DecodeSingleSceneryAsync(0x02000001, archive);

        Assert.Contains("scenery.lst is missing", decoded.UnsupportedReason);
    }

    [Fact]
    public async Task DecodeAsync_RejectsShortSceneryPrototype()
    {
        const int sceneryPid = 0x02000001;
        var prototype = PrototypeArchive.CreateSceneryPrototype(sceneryPid, sceneryType: 5);
        Array.Resize(ref prototype, 35);
        var archive = PrototypeArchive.Create([("00000001.PRO", prototype)]);

        var decoded = await DecodeSingleSceneryAsync(sceneryPid, archive);

        Assert.False(decoded.IsComplete);
        Assert.Contains("shorter than 36 bytes", decoded.UnsupportedReason);
    }

    [Fact]
    public async Task DecodeAsync_ReportsMissingSceneryPrototype()
    {
        var archive = PrototypeArchive.Create([], listContent: "00000001.PRO\n");

        var decoded = await DecodeSingleSceneryAsync(0x02000001, archive);

        Assert.False(decoded.IsComplete);
        Assert.Contains("prototype '00000001.PRO' is missing", decoded.UnsupportedReason);
        Assert.Null(Assert.Single(decoded.Objects).PrototypeFileName);
    }

    [Fact]
    public async Task DecodeAsync_RejectsUnsafeOrMalformedPrototypeListEntry()
    {
        const int sceneryPid = 0x02000001;
        var archive = PrototypeArchive.Create(
            [("../outside.PRO", PrototypeArchive.CreateSceneryPrototype(sceneryPid, sceneryType: 5))],
            listContent: "../outside.PRO\n");

        var decoded = await DecodeSingleSceneryAsync(sceneryPid, archive);

        Assert.Contains("unsafe or invalid filename", decoded.UnsupportedReason);
    }

    [Fact]
    public void Decode_ReportsTruncatedInventoryQuantityAsUnsupported()
    {
        var map = MapFixture.Create(19, [
            MapFixture.ObjectRecord(pid: 0x03000001, tile: 100, elevation: 2, inventoryLength: 1, inventoryCapacity: int.MaxValue)
        ], [0, 0, 1]);
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout1);

        var decoded = MapObjectPlacementDecoder.Decode(map, header);
        var placement = Assert.Single(decoded.Objects);

        Assert.False(decoded.IsComplete);
        Assert.Equal(int.MaxValue, placement.RawInventoryCapacity);
        Assert.Contains("quantity could not be read", decoded.UnsupportedReason);
    }

    [Fact]
    public async Task DecodeAsync_DecodesNestedInventoryBeforeFollowingTopLevelRecord()
    {
        const int sceneryPid = 0x02000001;
        var archive = PrototypeArchive.Create(
            [("00000001.PRO", PrototypeArchive.CreateSceneryPrototype(sceneryPid, sceneryType: 5))]);
        var parent = MapFixture.ObjectRecord(
            sceneryPid,
            tile: 100,
            elevation: 2,
            inventoryLength: 1,
            inventoryCapacity: 1);
        var inventory = AppendInventory(
            parent,
            (7, MapFixture.ObjectRecord(pid: 0x01000001, tile: 150, elevation: 2, critter: true)));
        var map = MapFixture.Create(20, [
            inventory,
            MapFixture.ObjectRecord(pid: 0x03000001, tile: 200, elevation: 2)
        ], [0, 0, 2]);
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout2);

        var decoded = await MapObjectPlacementDecoder.DecodeAsync(map, header, archive);

        Assert.True(decoded.IsComplete);
        Assert.Equal(2, decoded.TotalObjectCount);
        Assert.Equal(2, decoded.DecodedTopLevelObjectCount);
        Assert.Equal(3, decoded.Objects.Count);
        Assert.Equal(new[] { 0, 1, 2 }, decoded.Objects.Select(item => item.RecordIndex));
        Assert.Equal(0, decoded.Objects[1].ParentRecordIndex);
        Assert.Equal(1, decoded.Objects[1].InventoryDepth);
        Assert.Equal(7, decoded.Objects[1].InventoryQuantity);
        Assert.Null(decoded.Objects[2].ParentRecordIndex);
        Assert.Equal(0, decoded.Objects[2].InventoryDepth);
        Assert.Equal(0x03000001, decoded.Objects[2].RawPid);
        Assert.Equal((199, 1), (decoded.Objects[2].HexX, decoded.Objects[2].HexY));
        Assert.Equal(
            decoded.Objects[0].RecordOffset + inventory.Length,
            decoded.Objects[2].RecordOffset);
    }

    [Fact]
    public async Task DecodeAsync_AdvancesToNextElevationAfterNestedInventory()
    {
        var firstElevationTree = AppendInventory(
            MapFixture.ObjectRecord(
                pid: 0x01000001,
                tile: 100,
                elevation: 0,
                critter: true,
                inventoryLength: 1,
                inventoryCapacity: 1),
            (5, MapFixture.ObjectRecord(pid: 0x01000002, tile: 101, elevation: 0, critter: true)));
        var secondElevationObject = MapFixture.ObjectRecord(
            pid: 0x01000003,
            tile: 200,
            elevation: 1,
            critter: true);
        var map = MapFixture.Create(
            20,
            [firstElevationTree, secondElevationObject],
            [1, 1, 0]);
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout2);

        var decoded = await MapObjectPlacementDecoder.DecodeAsync(map, header);

        Assert.True(decoded.IsComplete, decoded.UnsupportedReason);
        Assert.Equal(2, decoded.TotalObjectCount);
        Assert.Equal(2, decoded.DecodedTopLevelObjectCount);
        Assert.Equal(new[] { 0, 1, 2 }, decoded.Objects.Select(item => item.RecordIndex));
        Assert.Equal(new[] { 0, 0, 1 }, decoded.Objects.Select(item => item.ElevationGroup));
        Assert.Equal(new int?[] { null, 0, null }, decoded.Objects.Select(item => item.ParentRecordIndex));
        Assert.Equal(new int?[] { null, 5, null }, decoded.Objects.Select(item => item.InventoryQuantity));
        Assert.Equal(
            firstElevationTree.Length + sizeof(int),
            decoded.Objects[2].RecordOffset - decoded.Objects[0].RecordOffset);
        Assert.Equal(0, decoded.UnparsedByteCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task DecodeAsync_AdvancesPastItemTypesWithoutSubtypePayload(int itemType)
    {
        var decoded = await DecodeItemFollowedByCritterAsync(itemType, []);

        AssertItemAndFollowingCritter(decoded, []);
    }

    [Fact]
    public async Task DecodeAsync_AdvancesPastWeaponItemAndPreservesTwoPayloadWords()
    {
        var payload = new[] { unchecked((int)0xA1B2C3D4), unchecked((int)0xE5F60718) };

        var decoded = await DecodeItemFollowedByCritterAsync(3, payload);

        AssertItemAndFollowingCritter(decoded, payload);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task DecodeAsync_AdvancesPastItemTypesWithOnePayloadWord(int itemType)
    {
        var payload = new[] { unchecked((int)0xA1B2C3D4) };

        var decoded = await DecodeItemFollowedByCritterAsync(itemType, payload);

        AssertItemAndFollowingCritter(decoded, payload);
    }

    [Fact]
    public async Task DecodeAsync_StoresItemPrototypeFileNameFromOneBasedListIndex()
    {
        const int itemPid = 0x00000002;
        const string prototypeFileName = "MetalArmor.pro";
        var archive = PrototypeArchive.CreateItems(
            [
                (prototypeFileName, PrototypeArchive.CreateItemPrototype(itemPid, itemType: 0))
            ],
            listContent: $"unused.pro\n{prototypeFileName}\n");

        var decoded = await DecodeSingleItemAsync(itemPid, archive);

        var placement = Assert.Single(decoded.Objects);
        Assert.True(decoded.IsComplete, decoded.UnsupportedReason);
        Assert.Equal(prototypeFileName, placement.PrototypeFileName);
        Assert.Contains($"Prototype file: {prototypeFileName}", placement.Details);
    }

    [Fact]
    public async Task DecodeAsync_RejectsTruncatedWeaponItemPayload()
    {
        const int itemPid = 0x00000001;
        var archive = PrototypeArchive.CreateItems(
            [("00000001.PRO", PrototypeArchive.CreateItemPrototype(itemPid, itemType: 3))]);
        var map = MapFixture.Create(20, [
            MapFixture.ObjectRecord(itemPid, tile: 123, elevation: 2, extraWords: [99, 98])
        ], [0, 0, 1]);
        Array.Resize(ref map, map.Length - sizeof(int));
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout2);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => MapObjectPlacementDecoder.DecodeAsync(map, header, archive));

        Assert.Contains("item subtype 3 payload field 1", exception.Message);
        Assert.Contains("extends beyond", exception.Message);
    }

    [Fact]
    public async Task DecodeAsync_ReportsMissingItemPrototypeList()
    {
        const int itemPid = 0x00000001;
        var archive = PrototypeArchive.CreateItems([], listContent: null);

        var decoded = await DecodeSingleItemAsync(itemPid, archive);

        Assert.False(decoded.IsComplete);
        Assert.Contains("proto/items/items.lst is missing", decoded.UnsupportedReason);
    }

    [Theory]
    [InlineData(0, "invalid zero list index")]
    [InlineData(2, "list index 2 is outside")]
    public async Task DecodeAsync_RejectsInvalidItemPrototypeListIndex(int pid, string expectedReason)
    {
        var archive = PrototypeArchive.CreateItems(
            [("00000001.PRO", PrototypeArchive.CreateItemPrototype(0x00000001, itemType: 0))]);

        var decoded = await DecodeSingleItemAsync(pid, archive);

        Assert.False(decoded.IsComplete);
        Assert.Contains(expectedReason, decoded.UnsupportedReason);
    }

    [Fact]
    public async Task DecodeAsync_ReportsMissingItemPrototype()
    {
        const int itemPid = 0x00000001;
        var archive = PrototypeArchive.CreateItems([], listContent: "00000001.PRO\n");

        var decoded = await DecodeSingleItemAsync(itemPid, archive);

        Assert.False(decoded.IsComplete);
        Assert.Contains("item prototype '00000001.PRO' is missing", decoded.UnsupportedReason);
        Assert.Null(Assert.Single(decoded.Objects).PrototypeFileName);
    }

    [Fact]
    public async Task DecodeAsync_RejectsShortItemPrototype()
    {
        const int itemPid = 0x00000001;
        var prototype = PrototypeArchive.CreateItemPrototype(itemPid, itemType: 0);
        Array.Resize(ref prototype, 35);
        var archive = PrototypeArchive.CreateItems([("00000001.PRO", prototype)]);

        var decoded = await DecodeSingleItemAsync(itemPid, archive);

        Assert.False(decoded.IsComplete);
        Assert.Contains("item prototype is shorter than 36 bytes", decoded.UnsupportedReason);
    }

    [Fact]
    public async Task DecodeAsync_RejectsItemPrototypeWithMismatchedPid()
    {
        const int itemPid = 0x00000001;
        var archive = PrototypeArchive.CreateItems(
            [("00000001.PRO", PrototypeArchive.CreateItemPrototype(0x00000002, itemType: 0))]);

        var decoded = await DecodeSingleItemAsync(itemPid, archive);

        Assert.False(decoded.IsComplete);
        Assert.Contains("item prototype PID", decoded.UnsupportedReason);
        Assert.Contains("does not match MAP PID", decoded.UnsupportedReason);
        Assert.Null(Assert.Single(decoded.Objects).PrototypeFileName);
    }

    [Fact]
    public async Task DecodeAsync_RejectsUnsupportedItemType()
    {
        const int itemPid = 0x00000001;
        var archive = PrototypeArchive.CreateItems(
            [("00000001.PRO", PrototypeArchive.CreateItemPrototype(itemPid, itemType: 7))]);

        var decoded = await DecodeSingleItemAsync(itemPid, archive);

        Assert.False(decoded.IsComplete);
        Assert.Contains("item prototype has unsupported subtype 7", decoded.UnsupportedReason);
        Assert.Null(Assert.Single(decoded.Objects).PrototypeFileName);
    }

    [Fact]
    public async Task DecodeAsync_ReportsMalformedItemPrototypeListEntry()
    {
        const int itemPid = 0x00000001;
        var archive = PrototypeArchive.CreateItems(
            [("../outside.PRO", PrototypeArchive.CreateItemPrototype(itemPid, itemType: 0))],
            listContent: "../outside.PRO\n");

        var decoded = await DecodeSingleItemAsync(itemPid, archive);

        Assert.False(decoded.IsComplete);
        Assert.Contains("item prototype list contains an unsafe or invalid filename", decoded.UnsupportedReason);
    }

    [Fact]
    public async Task DecodeAsync_StopsAtItemWithNestedInventory()
    {
        const int itemPid = 0x00000001;
        var archive = PrototypeArchive.CreateItems(
            [("00000001.PRO", PrototypeArchive.CreateItemPrototype(itemPid, itemType: 3))]);
        var parent = MapFixture.ObjectRecord(
            itemPid,
            tile: 123,
            elevation: 2,
            inventoryLength: 1,
            inventoryCapacity: 1,
            extraWords: [77, 88]);
        var inventory = AppendInventory(
            parent,
            (3, MapFixture.ObjectRecord(pid: 0x01000001, tile: 456, elevation: 2, critter: true)));
        var map = MapFixture.Create(20, [inventory], [0, 0, 1]);
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout2);

        var decoded = await MapObjectPlacementDecoder.DecodeAsync(map, header, archive);

        Assert.True(decoded.IsComplete);
        Assert.Equal(2, decoded.Objects.Count);
        Assert.Equal(itemPid, decoded.Objects[0].RawPid);
        Assert.Equal(new[] { 0x11223344, 77, 88 }, decoded.Objects[0].RawPayloadWords);
        Assert.Equal(0, decoded.Objects[1].ParentRecordIndex);
        Assert.Equal(3, decoded.Objects[1].InventoryQuantity);
    }

    [Fact]
    public async Task DecodeAsync_DecodesMultipleInventoryChildrenWithTheirQuantities()
    {
        var archive = CreateItemArchive(
            (1, 1),
            (2, 4),
            (3, 5));
        var parent = MapFixture.ObjectRecord(
            1,
            tile: 100,
            elevation: 0,
            inventoryLength: 2,
            inventoryCapacity: 2);
        var inventory = AppendInventory(
            parent,
            (2, MapFixture.ObjectRecord(2, tile: 101, elevation: 0, extraWords: [20])),
            (9, MapFixture.ObjectRecord(3, tile: 102, elevation: 0, extraWords: [30])));
        var map = MapFixture.Create(20, [inventory], [1, 0, 0]);
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout2);

        var decoded = await MapObjectPlacementDecoder.DecodeAsync(map, header, archive);

        Assert.True(decoded.IsComplete, decoded.UnsupportedReason);
        Assert.Equal(1, decoded.TotalObjectCount);
        Assert.Equal(1, decoded.DecodedTopLevelObjectCount);
        Assert.Equal(new[] { 1, 2, 3 }, decoded.Objects.Select(item => item.RawPid));
        Assert.Equal(new int?[] { null, 0, 0 }, decoded.Objects.Select(item => item.ParentRecordIndex));
        Assert.Equal(new int?[] { null, 2, 9 }, decoded.Objects.Select(item => item.InventoryQuantity));
        Assert.Equal(new[] { 0, 1, 1 }, decoded.Objects.Select(item => item.InventoryDepth));
        Assert.Equal(new[] { 0x11223344, 20 }, decoded.Objects[1].RawPayloadWords);
        Assert.Equal(new[] { 0x11223344, 30 }, decoded.Objects[2].RawPayloadWords);
    }

    [Fact]
    public async Task DecodeAsync_DecodesNestedContainerDepthFirstAndKeepsFollowingTopLevelOffset()
    {
        var archive = CreateItemArchive(
            (1, 1),
            (2, 1),
            (3, 3));
        var grandchild = MapFixture.ObjectRecord(3, tile: 303, elevation: 0, extraWords: [31, 32]);
        var child = AppendInventory(
            MapFixture.ObjectRecord(
                2,
                tile: 202,
                elevation: 0,
                inventoryLength: 1,
                inventoryCapacity: 1),
            (6, grandchild));
        var tree = AppendInventory(
            MapFixture.ObjectRecord(
                1,
                tile: 101,
                elevation: 0,
                inventoryLength: 1,
                inventoryCapacity: 1),
            (4, child));
        var followingRoot = MapFixture.ObjectRecord(0x03000001, tile: 39999, elevation: 0);
        var map = MapFixture.Create(20, [tree, followingRoot], [2, 0, 0]);
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout2);

        var decoded = await MapObjectPlacementDecoder.DecodeAsync(map, header, archive);

        Assert.True(decoded.IsComplete);
        Assert.Equal(2, decoded.TotalObjectCount);
        Assert.Equal(2, decoded.DecodedTopLevelObjectCount);
        Assert.Equal(4, decoded.Objects.Count);
        Assert.Equal(new[] { 0, 1, 2, 3 }, decoded.Objects.Select(item => item.RecordIndex));
        Assert.Equal(new int?[] { null, 0, 1, null }, decoded.Objects.Select(item => item.ParentRecordIndex));
        Assert.Equal(new[] { 0, 1, 2, 0 }, decoded.Objects.Select(item => item.InventoryDepth));
        Assert.Equal(new int?[] { null, 4, 6, null }, decoded.Objects.Select(item => item.InventoryQuantity));
        Assert.Equal(new[] { 0x11223344, 31, 32 }, decoded.Objects[2].RawPayloadWords);
        Assert.Equal(tree.Length + decoded.Objects[0].RecordOffset, decoded.Objects[3].RecordOffset);
        Assert.Equal(0, decoded.Objects[3].HexX);
        Assert.Equal(199, decoded.Objects[3].HexY);
    }

    [Fact]
    public async Task DecodeAsync_ReturnsPartialResultWhenInventoryQuantityIsTruncated()
    {
        var archive = CreateItemArchive((1, 1));
        var map = MapFixture.Create(20, [
            MapFixture.ObjectRecord(1, tile: 123, elevation: 2, inventoryLength: 1, inventoryCapacity: 1)
        ], [0, 0, 1]);
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout2);

        var decoded = await MapObjectPlacementDecoder.DecodeAsync(map, header, archive);

        Assert.False(decoded.IsComplete);
        Assert.Single(decoded.Objects);
        Assert.Contains("quantity could not be read", decoded.UnsupportedReason);
        Assert.Contains("extends beyond", decoded.UnsupportedReason);
        Assert.Equal(0, decoded.UnparsedByteCount);
    }

    [Fact]
    public async Task DecodeAsync_ReturnsExactPartialOffsetWhenChildRecordIsTruncated()
    {
        var archive = CreateItemArchive((1, 1));
        var parent = MapFixture.ObjectRecord(
            1,
            tile: 123,
            elevation: 2,
            inventoryLength: 1,
            inventoryCapacity: 1);
        var truncatedChild = MapFixture.ObjectRecord(0x03000001, tile: 456, elevation: 2)[..(3 * sizeof(int))];
        var serializedInventory = AppendInventory(parent, (5, truncatedChild))
            .Concat(new byte[] { 0xAA, 0xBB })
            .ToArray();
        var map = MapFixture.Create(20, [serializedInventory], [0, 0, 1]);
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout2);

        var decoded = await MapObjectPlacementDecoder.DecodeAsync(map, header, archive);

        Assert.False(decoded.IsComplete);
        Assert.Single(decoded.Objects);
        Assert.Contains("inventory child 0", decoded.UnsupportedReason);
        Assert.Contains("base field 3", decoded.UnsupportedReason);
        Assert.Equal(2, decoded.UnparsedByteCount);
    }

    [Theory]
    [InlineData(-1, 0, "negative inventory metadata")]
    [InlineData(5, 2, "exceeds capacity")]
    [InlineData(1001, 1001, "exceeds the supported limit 1000")]
    public async Task DecodeAsync_RejectsInvalidInventoryCounters(
        int inventoryLength,
        int inventoryCapacity,
        string expectedReason)
    {
        var map = MapFixture.Create(20, [
            MapFixture.ObjectRecord(
                0x03000001,
                tile: 123,
                elevation: 0,
                inventoryLength: inventoryLength,
                inventoryCapacity: inventoryCapacity)
        ], [1, 0, 0]);
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout2);

        var decoded = await MapObjectPlacementDecoder.DecodeAsync(map, header);

        Assert.False(decoded.IsComplete);
        Assert.Single(decoded.Objects);
        Assert.Contains(expectedReason, decoded.UnsupportedReason);
    }

    [Fact]
    public async Task DecodeAsync_StopsAtMaximumInventoryDepthWithoutReadingAnotherChild()
    {
        var archive = CreateItemArchive((1, 1));
        byte[] nested = MapFixture.ObjectRecord(
            1,
            tile: 108,
            elevation: 2,
            inventoryLength: 1,
            inventoryCapacity: 1);
        for (var depth = 7; depth >= 0; depth--)
        {
            nested = AppendInventory(
                MapFixture.ObjectRecord(
                    1,
                    tile: 100 + depth,
                    elevation: 2,
                    inventoryLength: 1,
                    inventoryCapacity: 1),
                (depth + 1, nested));
        }

        var map = MapFixture.Create(20, [nested], [0, 0, 1]);
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout2);

        var decoded = await MapObjectPlacementDecoder.DecodeAsync(map, header, archive);

        Assert.False(decoded.IsComplete);
        Assert.Equal(9, decoded.Objects.Count);
        Assert.Equal(8, decoded.Objects[^1].InventoryDepth);
        Assert.Contains("maximum supported depth 8", decoded.UnsupportedReason);
        Assert.Equal(0, decoded.UnparsedByteCount);
    }

    [Fact]
    public async Task DecodeAsync_StopsBeforeDecodedObjectGlobalLimitIsExceeded()
    {
        const int objectCountLimit = 100_000;
        var archive = CreateItemArchive((1, 1));
        var records = Enumerable.Range(0, objectCountLimit - 1)
            .Select(index => MapFixture.ObjectRecord(0x03000001, tile: index, elevation: 0))
            .ToList();
        records.Add(AppendInventory(
            MapFixture.ObjectRecord(
                0x03000001,
                tile: objectCountLimit - 1,
                elevation: 0,
                inventoryLength: 1,
                inventoryCapacity: 1),
            (1, MapFixture.ObjectRecord(0x03000001, tile: 0, elevation: 0))));
        var map = MapFixture.Create(20, records, [objectCountLimit, 0, 0]);
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout2);

        var decoded = await MapObjectPlacementDecoder.DecodeAsync(map, header, archive);

        Assert.False(decoded.IsComplete);
        Assert.Equal(objectCountLimit, decoded.TotalObjectCount);
        Assert.Equal(objectCountLimit, decoded.DecodedTopLevelObjectCount);
        Assert.Equal(objectCountLimit, decoded.Objects.Count);
        Assert.Contains("decoded object limit 100000", decoded.UnsupportedReason);
    }

    [Fact]
    public async Task DecodeAsync_StopsAtUnsupportedInventoryChildWithoutAdvancingToNextRoot()
    {
        var archive = CreateItemArchive((1, 1));
        var parent = MapFixture.ObjectRecord(
            1,
            tile: 100,
            elevation: 2,
            inventoryLength: 1,
            inventoryCapacity: 1);
        var tree = AppendInventory(
            parent,
            (8, MapFixture.ObjectRecord(2, tile: 200, elevation: 2)));
        var followingRoot = MapFixture.ObjectRecord(0x03000001, tile: 300, elevation: 2);
        var map = MapFixture.Create(20, [tree, followingRoot], [0, 0, 2]);
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout2);

        var decoded = await MapObjectPlacementDecoder.DecodeAsync(map, header, archive);

        Assert.False(decoded.IsComplete);
        Assert.Equal(2, decoded.Objects.Count);
        Assert.Equal(new[] { 1, 2 }, decoded.Objects.Select(item => item.RawPid));
        Assert.Equal(0, decoded.Objects[1].ParentRecordIndex);
        Assert.Contains("item PID list index 2 is outside", decoded.UnsupportedReason);
        Assert.Equal(followingRoot.Length, decoded.UnparsedByteCount);
    }

    [Fact]
    public void Decode_PreservesInvalidHexIndexAndMarksItUnplaced()
    {
        var map = MapFixture.Create(19, [
            MapFixture.ObjectRecord(pid: 0x03000001, tile: 40000, elevation: 0)
        ], [1, 0, 0]);
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout1);

        var placement = Assert.Single(MapObjectPlacementDecoder.Decode(map, header).Objects);

        Assert.Equal(40000, placement.RawHexIndex);
        Assert.Null(placement.HexX);
        Assert.Null(placement.HexY);
        Assert.Equal("40000 (invalid tile index)", placement.FormatHexPosition());
        Assert.Contains("outside", placement.Warning);
    }

    [Fact]
    public void Decode_PreservesNoTileSentinelAndLabelsItSeparatelyFromInvalidIndices()
    {
        var map = MapFixture.Create(19, [
            MapFixture.ObjectRecord(pid: 0x03000001, tile: -1, elevation: 0)
        ], [1, 0, 0]);
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout1);

        var placement = Assert.Single(MapObjectPlacementDecoder.Decode(map, header).Objects);

        Assert.Equal(-1, placement.RawHexIndex);
        Assert.Null(placement.HexX);
        Assert.Null(placement.HexY);
        Assert.Equal("-1 (no tile assigned)", placement.FormatHexPosition());
        Assert.Contains("No hex-grid tile is assigned", placement.Warning);
    }

    [Fact]
    public void Decode_ExtractsPidTypeFromUnsignedHighByte()
    {
        var map = MapFixture.Create(19, [
            MapFixture.ObjectRecord(pid: unchecked((int)0xFF000001u), tile: 100, elevation: 0)
        ], [1, 0, 0]);
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout1);

        var placement = Assert.Single(MapObjectPlacementDecoder.Decode(map, header).Objects);

        Assert.Equal(255, placement.PidTypeIdentifier);
        Assert.Contains("255", placement.UnsupportedReason);
    }

    [Fact]
    public void Decode_RejectsTruncatedObjectRecord()
    {
        var map = MapFixture.Create(19, [
            MapFixture.ObjectRecord(pid: 0x03000001, tile: 100, elevation: 2)
        ], [0, 0, 1]);
        Array.Resize(ref map, map.Length - sizeof(int));
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout1);

        var exception = Assert.Throws<InvalidDataException>(
            () => MapObjectPlacementDecoder.Decode(map, header));

        Assert.Contains("object record", exception.Message);
        Assert.Contains("extends beyond", exception.Message);
    }

    [Fact]
    public void Decode_RejectsTruncatedScriptExtent()
    {
        var map = MapFixture.Create(
            19,
            [],
            [0, 0, 0],
            scriptCounts: [1, 0, 0, 0, 0],
            scriptExtents: []);
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout1);

        Assert.Contains(
            "script list 0 extents",
            Assert.Throws<InvalidDataException>(
                () => MapObjectPlacementDecoder.Decode(map, header)).Message);
    }

    [Fact]
    public void Decode_RejectsNegativeAndInconsistentObjectCounts()
    {
        var negativeCounts = MapFixture.Create(19, [], [-1, 0, 0], totalObjectCount: -1);
        var negativeHeader = MapHeaderDecoder.Decode(negativeCounts, FalloutGame.Fallout1);
        Assert.Throws<InvalidDataException>(
            () => MapObjectPlacementDecoder.Decode(negativeCounts, negativeHeader));

        var inconsistentCounts = MapFixture.Create(19, [], [1, 0, 0], totalObjectCount: 0);
        var inconsistentHeader = MapHeaderDecoder.Decode(inconsistentCounts, FalloutGame.Fallout1);
        Assert.Contains(
            "smaller than",
            Assert.Throws<InvalidDataException>(
                () => MapObjectPlacementDecoder.Decode(inconsistentCounts, inconsistentHeader)).Message);
    }

    [Fact]
    public void Decode_RejectsHeaderMetadataThatDoesNotMatchEntry()
    {
        var map = MapFixture.Create(19, [], [0, 0, 0]);
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout1) with
        {
            TileDataOffset = long.MaxValue
        };

        Assert.Throws<InvalidDataException>(
            () => MapObjectPlacementDecoder.Decode(map, header));
    }

    private static PrototypeArchive CreateItemArchive(params (int Pid, int Type)[] prototypes)
    {
        var entries = prototypes.Select((prototype, index) =>
        {
            var name = $"inventory-{index + 1:D4}.PRO";
            return (name, PrototypeArchive.CreateItemPrototype(prototype.Pid, prototype.Type));
        }).ToArray();
        var listContent = string.Join('\n', entries.Select(entry => entry.name)) + "\n";
        return PrototypeArchive.CreateItems(entries, listContent);
    }

    private static byte[] AppendInventory(
        byte[] parentRecord,
        params (int Quantity, byte[] Record)[] children)
    {
        using var stream = new MemoryStream();
        stream.Write(parentRecord);
        Span<byte> quantityBytes = stackalloc byte[sizeof(int)];
        foreach (var (quantity, record) in children)
        {
            BinaryPrimitives.WriteInt32BigEndian(quantityBytes, quantity);
            stream.Write(quantityBytes);
            stream.Write(record);
        }

        return stream.ToArray();
    }

    private static async Task<MapObjectPlacementData> DecodeSingleSceneryAsync(
        int pid,
        IGameArchive archive)
    {
        var map = MapFixture.Create(
            20,
            [MapFixture.ObjectRecord(pid, tile: 123, elevation: 0)],
            [1, 0, 0]);
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout2);
        return await MapObjectPlacementDecoder.DecodeAsync(map, header, archive);
    }

    private static async Task<MapObjectPlacementData> DecodeSingleItemAsync(
        int pid,
        IGameArchive archive)
    {
        var map = MapFixture.Create(
            20,
            [MapFixture.ObjectRecord(pid, tile: 123, elevation: 0)],
            [1, 0, 0]);
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout2);
        return await MapObjectPlacementDecoder.DecodeAsync(map, header, archive);
    }

    private static async Task<MapObjectPlacementData> DecodeItemFollowedByCritterAsync(
        int itemType,
        IReadOnlyList<int> payload)
    {
        const int itemPid = 0x00000001;
        var archive = PrototypeArchive.CreateItems(
            [("00000001.PRO", PrototypeArchive.CreateItemPrototype(itemPid, itemType))]);
        var map = MapFixture.Create(20, [
            MapFixture.ObjectRecord(itemPid, tile: 123, elevation: 0, extraWords: payload),
            MapFixture.ObjectRecord(pid: 0x01000001, tile: 456, elevation: 0, critter: true)
        ], [2, 0, 0]);
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout2);
        return await MapObjectPlacementDecoder.DecodeAsync(map, header, archive);
    }

    private static void AssertItemAndFollowingCritter(
        MapObjectPlacementData decoded,
        IReadOnlyList<int> payload)
    {
        Assert.True(decoded.IsComplete);
        Assert.Equal(2, decoded.Objects.Count);
        Assert.Equal(0x00000001, decoded.Objects[0].RawPid);
        Assert.Equal(new[] { 0x11223344 }.Concat(payload), decoded.Objects[0].RawPayloadWords);
        Assert.Equal(0x01000001, decoded.Objects[1].RawPid);
        Assert.Equal(11, decoded.Objects[1].RawPayloadWords.Count);
        Assert.Equal(0, decoded.Objects[0].PidTypeIdentifier);
    }

    private sealed class PrototypeArchive : IGameArchive
    {
        private readonly Dictionary<string, byte[]> _entries;

        public PrototypeArchive(
            IReadOnlyList<ArchiveEntry> browserEntries,
            Dictionary<string, byte[]> entries)
        {
            BrowserEntries = browserEntries;
            _entries = entries;
        }

        public PrototypeArchive()
            : this([], new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase))
        {
        }

        public string Path => "synthetic-fallout2.dat";
        public FalloutGame Game => FalloutGame.Fallout2;
        public ArchiveFormat Format => ArchiveFormat.Dat2;
        public IReadOnlyList<ArchiveEntry> BrowserEntries { get; }

        public async Task CopyEntryToAsync(
            ArchiveEntry entry,
            Stream destination,
            CancellationToken cancellationToken = default)
        {
            var key = entry.Path.Replace('\\', '/');
            if (!_entries.TryGetValue(key, out var content))
            {
                throw new InvalidDataException($"Missing synthetic entry '{entry.Path}'.");
            }

            await destination.WriteAsync(content, cancellationToken);
        }

        public static PrototypeArchive Create(
            IReadOnlyList<(string Path, byte[] Content)> prototypes,
            string listContent = "00000001.PRO\n")
        {
            var contents = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["proto/scenery/scenery.lst"] = Encoding.ASCII.GetBytes(listContent)
            };
            foreach (var (path, content) in prototypes)
            {
                contents[$"proto/scenery/{path}"] = content;
            }

            var entries = contents.Select(pair =>
                new ArchiveEntry(pair.Key, pair.Value.Length, null, "synthetic-fallout2.dat")).ToArray();
            return new PrototypeArchive(entries, contents);
        }

        public static PrototypeArchive CreateItems(
            IReadOnlyList<(string Path, byte[] Content)> prototypes,
            string? listContent = "00000001.PRO\n")
        {
            var contents = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            if (listContent is not null)
            {
                contents["proto/items/items.lst"] = Encoding.ASCII.GetBytes(listContent);
            }

            foreach (var (path, content) in prototypes)
            {
                contents[$"proto/items/{path}"] = content;
            }

            var entries = contents.Select(pair =>
                new ArchiveEntry(pair.Key, pair.Value.Length, null, "synthetic-fallout2.dat")).ToArray();
            return new PrototypeArchive(entries, contents);
        }

        public static byte[] CreateSceneryPrototype(int pid, int sceneryType)
            => CreatePrototype(pid, sceneryType);

        public static byte[] CreateItemPrototype(int pid, int itemType)
            => CreatePrototype(pid, itemType);

        private static byte[] CreatePrototype(int pid, int type)
        {
            var data = new byte[45];
            BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0, sizeof(int)), pid);
            BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(32, sizeof(int)), type);
            return data;
        }
    }

    private static class MapFixture
    {
        public static byte[] Create(
            int version,
            IReadOnlyList<byte[]> objects,
            int[] elevationCounts,
            int? totalObjectCount = null,
            int[]? scriptCounts = null,
            IReadOnlyList<byte[]>? scriptExtents = null)
        {
            scriptCounts ??= new int[5];
            scriptExtents ??= [];
            var scriptSection = new List<byte>();
            var extentIndex = 0;
            for (var listIndex = 0; listIndex < 5; listIndex++)
            {
                WriteInt32(scriptSection, scriptCounts[listIndex]);
                var extentCount = (scriptCounts[listIndex] + 15) / 16;
                for (var index = 0; index < extentCount; index++)
                {
                    if (extentIndex >= scriptExtents.Count)
                    {
                        break;
                    }

                    scriptSection.AddRange(scriptExtents[extentIndex++]);
                }
            }

            var objectSection = new List<byte>();
            WriteInt32(objectSection, totalObjectCount ?? objects.Count);
            for (var elevation = 0; elevation < elevationCounts.Length; elevation++)
            {
                WriteInt32(objectSection, elevationCounts[elevation]);

                foreach (var record in objects.Where(record =>
                    BinaryPrimitives.ReadInt32BigEndian(record.AsSpan(10 * sizeof(int), sizeof(int))) == elevation))
                {
                    objectSection.AddRange(record);
                }
            }

            var data = new byte[MapHeaderDecoder.HeaderSize + scriptSection.Count + objectSection.Count];
            BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0), version);
            BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0x28), 0x0e);
            scriptSection.CopyTo(data, MapHeaderDecoder.HeaderSize);
            objectSection.CopyTo(data, MapHeaderDecoder.HeaderSize + scriptSection.Count);
            return data;
        }

        public static byte[] ObjectRecord(
            int pid,
            int tile,
            int elevation,
            int flags = 0,
            bool critter = false,
            int inventoryLength = 0,
            int inventoryCapacity = 0,
            IReadOnlyList<int>? extraWords = null)
        {
            extraWords ??= [];
            var words = new List<int>
            {
                101, tile, -2, 3, 4, 5, 6, 2,
                0x03000123, flags, elevation, pid,
                14, 15, 16, 0x71727374, -1, 18,
                inventoryLength, inventoryCapacity, unchecked((int)0xAABBCCDD)
            };

            if (critter)
            {
                words.AddRange(Enumerable.Range(0, 11).Select(index => unchecked((int)0xF0000001) + index));
            }
            else
            {
                words.Add(0x11223344);
                words.AddRange(extraWords);
            }

            var result = new byte[words.Count * sizeof(int)];
            for (var index = 0; index < words.Count; index++)
            {
                BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(index * sizeof(int)), words[index]);
            }

            return result;
        }

        public static byte[] CreateScriptExtent(int scriptId)
        {
            var bytes = new List<byte>();
            for (var script = 0; script < 16; script++)
            {
                WriteInt32(bytes, script == 0 ? scriptId : -1);
                WriteInt32(bytes, 0);
                if (script == 0)
                {
                    WriteInt32(bytes, 0);
                    WriteInt32(bytes, 0);
                }

                for (var field = 0; field < 14; field++)
                {
                    WriteInt32(bytes, 0);
                }
            }

            WriteInt32(bytes, 1);
            WriteInt32(bytes, -1);
            return bytes.ToArray();
        }

        private static void WriteInt32(List<byte> bytes, int value)
        {
            Span<byte> word = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32BigEndian(word, value);
            bytes.AddRange(word.ToArray());
        }
    }
}
