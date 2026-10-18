using System.Buffers.Binary;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class MapEditSessionSourceBindingTests
{
    [Fact]
    public void SourceBoundSession_RejectsHeaderFromAnotherDecodeOfSameSnapshot()
    {
        var decoded = DecodeMap(
            CreateMap(19, [CreateObject(0x03000001, 0, 123)]),
            FalloutGame.Fallout1);
        var secondHeaderDecode = MapHeaderDecoder.Decode(decoded.Source, decoded.Game);

        Assert.Throws<ArgumentException>(
            () => new MapEditSession(secondHeaderDecode, decoded.ObjectPlacements));
    }

    [Fact]
    public void SourceBoundSessionRetainsDecodeIdentityAndEditsDoNotChangeSourceBytes()
    {
        var bytes = CreateMap(
            19,
            [CreateObject(0x03000001, 0, 123, inventoryLength: 1, inventoryCapacity: 1)],
            [7],
            [CreateObject(0x01000001, 0, 456, critter: true)]);
        var decoded = DecodeMap(bytes, FalloutGame.Fallout1);
        var originalBytes = decoded.Source.GetRange(0, decoded.Source.Length).ToArray();
        var parent = decoded.EditSession.Objects[0].Placement;
        var child = decoded.EditSession.Objects[1].Placement;
        var parentId = new MapEditObjectId(decoded.EditSession.SessionId, parent.RecordIndex);
        var childId = new MapEditObjectId(decoded.EditSession.SessionId, child.RecordIndex);

        Assert.Same(decoded.Header.Provenance, decoded.EditSession.Provenance);
        Assert.Same(decoded.ObjectPlacements.Provenance, decoded.EditSession.Provenance);
        Assert.True(decoded.EditSession.TrySetHexTileIndex(parentId, 456, out var hexError));
        Assert.True(decoded.EditSession.TrySetInventoryQuantity(childId, 12, out var quantityError));
        Assert.Null(hexError);
        Assert.Null(quantityError);
        Assert.True(decoded.EditSession.IsDirty);
        Assert.Equal(originalBytes, decoded.Source.GetRange(0, decoded.Source.Length).ToArray());
    }

    private static DecodedMap DecodeMap(byte[] bytes, FalloutGame game)
    {
        var source = new MapSourceData(bytes);
        var header = MapHeaderDecoder.Decode(source, game);
        var objectPlacements = MapObjectPlacementDecoder.Decode(source, header);
        var editSession = new MapEditSession(header, objectPlacements);
        return new DecodedMap(source, game, header, objectPlacements, editSession);
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
        MapObjectPlacementData ObjectPlacements,
        MapEditSession EditSession);
}
