using System.Buffers.Binary;
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
    public void Decode_ReportsNestedInventoryAsUnsupportedWithoutFollowingUntrustedCounts()
    {
        var map = MapFixture.Create(19, [
            MapFixture.ObjectRecord(pid: 0x03000001, tile: 100, elevation: 0, inventoryLength: 1, inventoryCapacity: int.MaxValue)
        ], [1, 0, 0]);
        var header = MapHeaderDecoder.Decode(map, FalloutGame.Fallout1);

        var decoded = MapObjectPlacementDecoder.Decode(map, header);
        var placement = Assert.Single(decoded.Objects);

        Assert.False(decoded.IsComplete);
        Assert.Equal(int.MaxValue, placement.RawInventoryCapacity);
        Assert.Contains("nested inventory", decoded.UnsupportedReason);
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
        Assert.Contains("outside", placement.Warning);
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
