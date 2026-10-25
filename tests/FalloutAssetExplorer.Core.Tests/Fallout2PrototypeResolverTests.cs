using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class Fallout2PrototypeResolverTests
{
    [Fact]
    public async Task ResolveAsync_ReturnsRawBytesSizeAndVerifiedHeader()
    {
        const int pid = 0x02000001;
        var prototypeBytes = CreatePrototype(pid, subtype: 5);
        var archive = CreateArchive(
            "scenery",
            "scenery.lst",
            "tree.pro\n",
            ("tree.pro", prototypeBytes));
        var resolver = new Fallout2PrototypeResolver(archive);

        var result = await resolver.ResolveAsync(pid);

        Assert.Equal("tree.pro", result.VerifiedFileName);
        Assert.Equal(pid, result.PrototypePid);
        Assert.Equal(5, result.Subtype);
        Assert.Equal(prototypeBytes.Length, result.FileSize);
        Assert.Equal(prototypeBytes, result.RawBytes.ToArray());
        Assert.True(result.HasSupportedSubtype);
        Assert.True(result.HasExpectedSize);
    }

    [Fact]
    public async Task InspectStandaloneEntryAsync_ParsesItemWithoutPrototypeListVerification()
    {
        const int pid = 0x00000005;
        var prototypeBytes = CreateItemPrototype(pid, subtype: 3);
        WriteInt32(prototypeBytes, 0x14, unchecked((int)0xA1B2C3D4));
        var archive = CreateArchive(
            "items",
            "items.lst",
            string.Empty,
            ("direct.pro", prototypeBytes));
        var entry = Assert.Single(archive.BrowserEntries, item => item.Path.EndsWith("direct.pro"));

        var result = await new Fallout2PrototypeResolver(archive)
            .InspectStandaloneEntryAsync(entry);

        Assert.Equal(entry.Path, result.EntryPath);
        Assert.Equal(archive.Path, result.ArchivePath);
        Assert.Equal(entry.ArchiveLocation, result.ArchiveLocation);
        Assert.Equal(entry.CompressedSize, result.CompressedSize);
        Assert.Equal(prototypeBytes.Length, result.ActualSize);
        Assert.True(result.IsReadComplete);
        Assert.Equal(PrototypeInspectionOutcome.Success, result.Outcome);
        Assert.Contains("Selected directly from the archive", result.Provenance);
        Assert.Equal("Item", result.PidTypeName);
        Assert.Equal("Weapon", result.SubtypeName);
        Assert.Equal(pid, result.PrototypePid);
        Assert.True(result.HasSupportedSubtype);
        Assert.Equal(prototypeBytes, result.RawBytes.ToArray());
        Assert.Contains(result.ItemFields, field => field.Name == "flags" && field.Value == unchecked((int)0xA1B2C3D4));
        Assert.Empty(result.SceneryFields);
        Assert.Equal(0, archive.ReadCount("proto/items/items.lst"));
        Assert.Equal(1, archive.ReadCount(entry.Path));
    }

    [Fact]
    public async Task InspectStandaloneEntryAsync_ParsesSceneryFields()
    {
        const int pid = 0x02000005;
        var prototypeBytes = CreateSceneryPrototype(pid, subtype: 5);
        WriteInt32(prototypeBytes, 0x29, 0x12345678);
        var archive = CreateArchive(
            "scenery",
            "scenery.lst",
            string.Empty,
            ("direct.pro", prototypeBytes));
        var entry = Assert.Single(archive.BrowserEntries, item => item.Path.EndsWith("direct.pro"));

        var result = await new Fallout2PrototypeResolver(archive)
            .InspectStandaloneEntryAsync(entry);

        Assert.Equal("Scenery", result.PidTypeName);
        Assert.Equal("Generic", result.SubtypeName);
        Assert.True(result.HasSupportedSubtype);
        Assert.Equal(PrototypeInspectionOutcome.Success, result.Outcome);
        Assert.True(result.IsReadComplete);
        Assert.Equal("Prototype PID type and subtype recognized.", result.Status);
        Assert.Equal(prototypeBytes.Length, result.ActualSize);
        var genericField = Assert.Single(
            result.SceneryFields,
            field => field.Name == "generic.field_0");
        Assert.Equal(0x12345678, genericField.Value);
        Assert.Equal(0x29, genericField.Offset);
        Assert.Equal(sizeof(int), genericField.SizeInBytes);
        Assert.Equal("int32 BE", genericField.StorageType);
        Assert.Equal(
            new byte[] { 0x12, 0x34, 0x56, 0x78 },
            result.RawBytes.Slice(0x29, sizeof(int)).ToArray());
        Assert.Empty(result.ItemFields);
        Assert.Equal(prototypeBytes, result.RawBytes.ToArray());
    }

    [Fact]
    public async Task InspectStandaloneEntryAsync_ReportsShortGenericPayloadAndPreservesAvailableData()
    {
        const int pid = 0x02000005;
        var prototypeBytes = CreateSceneryPrototype(pid, subtype: 5, size: 0x2C);
        prototypeBytes[0x28] = 0x30;
        var archive = CreateArchive(
            "scenery",
            "scenery.lst",
            string.Empty,
            ("short-generic.pro", prototypeBytes));
        var entry = Assert.Single(
            archive.BrowserEntries,
            item => item.Path.EndsWith("short-generic.pro"));

        var result = await new Fallout2PrototypeResolver(archive)
            .InspectStandaloneEntryAsync(entry);

        Assert.Equal(PrototypeInspectionOutcome.Partial, result.Outcome);
        Assert.True(result.IsReadComplete);
        Assert.Equal((long?)44, result.ActualSize);
        Assert.Contains("shorter than 45 bytes for subtype 5", result.Status);
        Assert.True(result.HasSupportedSubtype);
        var field34 = Assert.Single(result.SceneryFields, field => field.Name == "field_34");
        Assert.Equal(0x30, field34.Value);
        Assert.Equal(0x28, field34.Offset);
        Assert.DoesNotContain(result.SceneryFields, field => field.Name == "generic.field_0");
        Assert.Equal(prototypeBytes, result.RawBytes.ToArray());
    }

    [Fact]
    public async Task InspectStandaloneEntryAsync_PreservesUnknownPidTypeBytes()
    {
        var prototypeBytes = CreatePrototype(unchecked((int)0x01000001), subtype: 0);
        var archive = CreateArchive(
            "items",
            "items.lst",
            string.Empty,
            ("unknown-type.pro", prototypeBytes));
        var entry = Assert.Single(archive.BrowserEntries, item => item.Path.EndsWith("unknown-type.pro"));

        var result = await new Fallout2PrototypeResolver(archive)
            .InspectStandaloneEntryAsync(entry);

        Assert.Equal(1, result.PidType);
        Assert.Null(result.PidTypeName);
        Assert.Null(result.Subtype);
        Assert.False(result.HasSupportedSubtype);
        Assert.Contains("Unsupported Fallout 2 PRO PID type 1", result.Status);
        Assert.Equal(prototypeBytes, result.RawBytes.ToArray());
    }

    [Fact]
    public async Task InspectStandaloneEntryAsync_PreservesUnknownSubtypeBytes()
    {
        const int pid = 0x00000001;
        var prototypeBytes = CreatePrototype(pid, subtype: 7);
        var archive = CreateArchive(
            "items",
            "items.lst",
            string.Empty,
            ("unknown-subtype.pro", prototypeBytes));
        var entry = Assert.Single(archive.BrowserEntries, item => item.Path.EndsWith("unknown-subtype.pro"));

        var result = await new Fallout2PrototypeResolver(archive)
            .InspectStandaloneEntryAsync(entry);

        Assert.Equal(7, result.Subtype);
        Assert.Null(result.SubtypeName);
        Assert.False(result.HasSupportedSubtype);
        Assert.Contains("unsupported subtype 7", result.Status);
        Assert.Equal(prototypeBytes, result.RawBytes.ToArray());
    }

    [Fact]
    public async Task InspectStandaloneEntryAsync_PreservesTruncatedHeaderBytes()
    {
        byte[] prototypeBytes = [0x00, 0x01, 0x02];
        var archive = CreateArchive(
            "items",
            "items.lst",
            string.Empty,
            ("truncated.pro", prototypeBytes));
        var entry = Assert.Single(archive.BrowserEntries, item => item.Path.EndsWith("truncated.pro"));

        var result = await new Fallout2PrototypeResolver(archive)
            .InspectStandaloneEntryAsync(entry);

        Assert.Null(result.PrototypePid);
        Assert.Null(result.PidType);
        Assert.Contains("shorter than the 4-byte PID field", result.Status);
        Assert.Equal(prototypeBytes, result.RawBytes.ToArray());
    }

    [Fact]
    public async Task InspectStandaloneEntryAsync_ReportsTruncatedItemPayloadAndOmitsIncompleteFields()
    {
        const int pid = 0x00000001;
        var prototypeBytes = CreateItemPrototype(pid, subtype: 6, size: 0x3C);
        var archive = CreateArchive(
            "items",
            "items.lst",
            string.Empty,
            ("short-item.pro", prototypeBytes));
        var entry = Assert.Single(archive.BrowserEntries, item => item.Path.EndsWith("short-item.pro"));

        var result = await new Fallout2PrototypeResolver(archive)
            .InspectStandaloneEntryAsync(entry);

        Assert.Contains("shorter than 61 bytes for subtype 6", result.Status);
        Assert.True(result.HasSupportedSubtype);
        Assert.DoesNotContain(result.ItemFields, field => field.Name == "keyCode");
        Assert.Equal(prototypeBytes, result.RawBytes.ToArray());
    }

    [Fact]
    public async Task InspectStandaloneEntryAsync_ReportsLongItemAndSceneryTrailingBytes()
    {
        const int itemPid = 0x00000001;
        var itemBytes = CreateItemPrototype(itemPid, subtype: 6, size: 0x3E);
        var itemArchive = CreateArchive(
            "items",
            "items.lst",
            string.Empty,
            ("long-item.pro", itemBytes));
        var itemEntry = Assert.Single(itemArchive.BrowserEntries, item => item.Path.EndsWith("long-item.pro"));

        var itemResult = await new Fallout2PrototypeResolver(itemArchive)
            .InspectStandaloneEntryAsync(itemEntry);

        Assert.Contains("longer than expected 61 bytes", itemResult.Status);
        Assert.Contains(itemResult.ItemFields, field => field.Name == "keyCode");
        Assert.Equal(itemBytes, itemResult.RawBytes.ToArray());

        const int sceneryPid = 0x02000001;
        var sceneryBytes = CreateSceneryPrototype(sceneryPid, subtype: 5, size: 0x2D + 24);
        var sceneryArchive = CreateArchive(
            "scenery",
            "scenery.lst",
            string.Empty,
            ("trailing-scenery.pro", sceneryBytes));
        var sceneryEntry = Assert.Single(
            sceneryArchive.BrowserEntries,
            item => item.Path.EndsWith("trailing-scenery.pro"));

        var sceneryResult = await new Fallout2PrototypeResolver(sceneryArchive)
            .InspectStandaloneEntryAsync(sceneryEntry);

        Assert.Contains("longer than expected 45 bytes for subtype 5", sceneryResult.Status);
        Assert.Contains("trailing bytes are preserved without interpretation", sceneryResult.Status);
        Assert.True(sceneryResult.HasSupportedSubtype);
        Assert.Equal(PrototypeInspectionOutcome.Partial, sceneryResult.Outcome);
        Assert.Equal(sceneryBytes.Length, sceneryResult.FileSize);
        Assert.Equal(sceneryBytes, sceneryResult.RawBytes.ToArray());
    }

    [Fact]
    public async Task InspectStandaloneEntryAsync_DoesNotDecodeFallout1AndPreservesRawBytes()
    {
        const int pid = 0x00000001;
        var prototypeBytes = CreateItemPrototype(pid, subtype: 3);
        var archive = CreateArchiveWithEntrySize(
            "items",
            "items.lst",
            string.Empty,
            FalloutGame.Fallout1,
            ArchiveFormat.Dat1,
            ("direct.pro", prototypeBytes, (long?)null));
        var entry = Assert.Single(archive.BrowserEntries, item => item.Path.EndsWith("direct.pro"));

        var result = await new Fallout2PrototypeResolver(archive)
            .InspectStandaloneEntryAsync(entry);

        Assert.Contains("only for Fallout 2 DAT2", result.Status);
        Assert.Equal(FalloutGame.Fallout1, result.Game);
        Assert.Null(result.PrototypePid);
        Assert.Null(result.Subtype);
        Assert.False(result.HasSupportedSubtype);
        Assert.Empty(result.ItemFields);
        Assert.Equal(prototypeBytes, result.RawBytes.ToArray());
    }

    [Fact]
    public async Task InspectStandaloneEntryAsync_ReadsEntryAtLimitAndRejectsLargerMetadataBeforeRead()
    {
        const int pid = 0x00000001;
        var maximumSizeBytes = Fallout2PrototypeResolver.MaximumPrototypeSizeBytes;
        var atLimitBytes = CreateItemPrototype(pid, subtype: 6, size: maximumSizeBytes);
        var atLimitArchive = CreateArchive(
            "items",
            "items.lst",
            string.Empty,
            ("at-limit.pro", atLimitBytes));
        var atLimitEntry = Assert.Single(
            atLimitArchive.BrowserEntries,
            item => item.Path.EndsWith("at-limit.pro"));

        var atLimitResult = await new Fallout2PrototypeResolver(atLimitArchive)
            .InspectStandaloneEntryAsync(atLimitEntry);

        Assert.Equal(1, atLimitArchive.ReadCount(atLimitEntry.Path));
        Assert.Contains("longer than expected 61 bytes", atLimitResult.Status);
        Assert.Equal(maximumSizeBytes, atLimitResult.RawBytes.Length);

        var overLimitArchive = CreateArchiveWithEntrySize(
            "items",
            "items.lst",
            string.Empty,
            ("over-limit.pro", CreateItemPrototype(pid, subtype: 6), (long?)maximumSizeBytes + 1));
        var overLimitEntry = Assert.Single(
            overLimitArchive.BrowserEntries,
            item => item.Path.EndsWith("over-limit.pro"));

        var overLimitResult = await new Fallout2PrototypeResolver(overLimitArchive)
            .InspectStandaloneEntryAsync(overLimitEntry);

        Assert.Equal(0, overLimitArchive.ReadCount(overLimitEntry.Path));
        Assert.Contains("1 MiB preview limit", overLimitResult.Status);
        Assert.Empty(overLimitResult.RawBytes.ToArray());
    }

    [Fact]
    public async Task InspectStandaloneEntryAsync_RejectsNegativeSizeMetadataBeforeRead()
    {
        var archive = CreateArchiveWithEntrySize(
            "items",
            "items.lst",
            string.Empty,
            ("negative-size.pro", CreatePrototype(1, subtype: 6), -1));
        var entry = Assert.Single(
            archive.BrowserEntries,
            item => item.Path.EndsWith("negative-size.pro"));

        var result = await new Fallout2PrototypeResolver(archive)
            .InspectStandaloneEntryAsync(entry);

        Assert.Equal(0, archive.ReadCount(entry.Path));
        Assert.Contains("negative uncompressed size", result.Status);
        Assert.Contains("The entry was not read.", result.Status);
    }

    [Fact]
    public async Task ResolveAsync_DecodesObservedGenericSceneryByteAndRawWordAtExactOffsets()
    {
        const int pid = 0x020002F5;
        byte[] prototypeBytes = Convert.FromHexString(
            "020002F5000127B4020002FF00000000000000002000001008002000FFFFFFFF000000050000000730CCCCCCCC");
        Assert.Equal(45, prototypeBytes.Length);

        var listContents = string.Join(
            '\n',
            Enumerable.Repeat("unused.pro", 756).Append("00000758.pro"));
        var archive = CreateArchive(
            "scenery",
            "scenery.lst",
            listContents,
            ("00000758.pro", prototypeBytes));

        var result = await new Fallout2PrototypeResolver(archive).ResolveAsync(pid);

        Assert.Equal("00000758.pro", result.VerifiedFileName);
        Assert.Equal(pid, result.PrototypePid);
        Assert.Equal(5, result.Subtype);
        Assert.Equal((long?)45, result.FileSize);
        Assert.Equal("Prototype PID type and subtype recognized.", result.Status);
        Assert.Equal(prototypeBytes, result.RawBytes.ToArray());

        var fields = result.SceneryFields.ToDictionary(field => field.Name);
        Assert.Equal(7, fields["material"].Value);
        Assert.Equal(0x24, fields["material"].Offset);
        Assert.Equal(0x30, fields["field_34"].Value);
        Assert.Equal(0x28, fields["field_34"].Offset);
        Assert.Equal(sizeof(byte), fields["field_34"].SizeInBytes);
        Assert.Equal("uint8", fields["field_34"].StorageType);
        Assert.Equal(
            "Uninterpreted byte; sound ID is not confirmed.",
            fields["field_34"].Interpretation);
        Assert.Equal(unchecked((int)0xCCCCCCCC), fields["generic.field_0"].Value);
        Assert.Equal(0x29, fields["generic.field_0"].Offset);
        Assert.Equal(sizeof(int), fields["generic.field_0"].SizeInBytes);
        Assert.Equal("int32 BE", fields["generic.field_0"].StorageType);
        Assert.Equal(
            new byte[] { 0xCC, 0xCC, 0xCC, 0xCC },
            result.RawBytes.Slice(0x29, sizeof(int)).ToArray());
        Assert.Contains(
            "meaning is not confirmed",
            fields["generic.field_0"].Interpretation ?? string.Empty);
    }

    [Fact]
    public async Task ResolveAsync_DecodesSceneryCommonFieldsAtExactOffsetsAsBigEndian()
    {
        const int pid = 0x02000001;
        var prototypeBytes = CreateSceneryPrototype(pid, subtype: 0);
        WriteInt32(prototypeBytes, 0x04, 0x01020304);
        WriteInt32(prototypeBytes, 0x08, unchecked((int)0xFEA1B2C3));
        WriteInt32(prototypeBytes, 0x0C, int.MinValue);
        WriteInt32(prototypeBytes, 0x10, int.MaxValue);
        WriteInt32(prototypeBytes, 0x14, unchecked((int)0xA1B2C3D4));
        WriteInt32(prototypeBytes, 0x18, -1);
        WriteInt32(prototypeBytes, 0x1C, -1);
        WriteInt32(prototypeBytes, 0x24, 7);
        prototypeBytes[0x28] = 0xA5;
        WriteInt32(prototypeBytes, 0x29, unchecked((int)0x80000001));
        WriteInt32(prototypeBytes, 0x2D, int.MaxValue);
        var archive = CreateArchive(
            "scenery",
            "scenery.lst",
            "door.pro\n",
            ("door.pro", prototypeBytes));

        var result = await new Fallout2PrototypeResolver(archive).ResolveAsync(pid);

        var fields = result.SceneryFields.ToDictionary(field => field.Name);
        Assert.Equal(prototypeBytes, result.RawBytes.ToArray());
        Assert.Equal(0x01020304, fields["messageId"].Value);
        Assert.Equal(0x04, fields["messageId"].Offset);
        Assert.Equal(sizeof(int), fields["messageId"].SizeInBytes);
        Assert.Equal("int32 BE", fields["messageId"].StorageType);
        Assert.Equal(unchecked((int)0xFEA1B2C3), fields["fid"].Value);
        Assert.True(fields["fid"].DisplayAsHex);
        Assert.Equal(int.MinValue, fields["lightDistance"].Value);
        Assert.Equal(int.MaxValue, fields["lightIntensity"].Value);
        Assert.Equal(unchecked((int)0xA1B2C3D4), fields["flags"].Value);
        Assert.True(fields["flags"].IsFlags);
        Assert.Equal(-1, fields["sid"].Value);
        Assert.Equal("No script", fields["sid"].Interpretation);
        Assert.Equal("Leather", fields["material"].Interpretation);
        Assert.Equal(0xA5, fields["field_34"].Value);
        Assert.Equal(0x28, fields["field_34"].Offset);
        Assert.Equal(sizeof(byte), fields["field_34"].SizeInBytes);
        Assert.Equal("uint8", fields["field_34"].StorageType);
        Assert.Equal(
            "Uninterpreted byte; sound ID is not confirmed.",
            fields["field_34"].Interpretation);
        Assert.Equal(unchecked((int)0x80000001), fields["openFlags"].Value);
        Assert.True(fields["openFlags"].IsFlags);
        Assert.Equal(int.MaxValue, fields["keyCode"].Value);
    }

    [Theory]
    [InlineData(0, "openFlags", "keyCode", 0x2D)]
    [InlineData(1, "stairs.field_0", "stairs.field_4", 0x2D)]
    [InlineData(2, "elevator.type", "elevator.level", 0x2D)]
    [InlineData(3, "ladder.field_0", null, 0)]
    [InlineData(4, "ladder.field_0", null, 0)]
    [InlineData(5, "generic.field_0", null, 0)]
    public async Task ResolveAsync_DecodesScenerySubtypeWordsAtVerifiedOffsets(
        int subtype,
        string firstFieldName,
        string? secondFieldName,
        int secondFieldOffset)
    {
        const int pid = 0x02000001;
        var prototypeBytes = CreateSceneryPrototype(pid, subtype);
        WriteInt32(prototypeBytes, 0x29, 0x12345678);
        if (secondFieldName is not null)
        {
            WriteInt32(prototypeBytes, secondFieldOffset, unchecked((int)0x89ABCDEF));
        }

        var archive = CreateArchive(
            "scenery",
            "scenery.lst",
            "object.pro\n",
            ("object.pro", prototypeBytes));

        var result = await new Fallout2PrototypeResolver(archive).ResolveAsync(pid);

        Assert.True(result.HasSupportedSubtype);
        Assert.Equal("Prototype PID type and subtype recognized.", result.Status);
        var byteField = Assert.Single(result.SceneryFields, field => field.Name == "field_34");
        Assert.Equal(0x28, byteField.Offset);
        Assert.Equal(sizeof(byte), byteField.SizeInBytes);
        Assert.Equal("uint8", byteField.StorageType);
        var firstField = Assert.Single(result.SceneryFields, field => field.Name == firstFieldName);
        Assert.Equal(0x29, firstField.Offset);
        Assert.Equal(0x12345678, firstField.Value);
        if (secondFieldName is not null)
        {
            var secondField = Assert.Single(result.SceneryFields, field => field.Name == secondFieldName);
            Assert.Equal(secondFieldOffset, secondField.Offset);
            Assert.Equal(unchecked((int)0x89ABCDEF), secondField.Value);
        }
    }

    [Theory]
    [InlineData(0, 0x81)]
    [InlineData(1, 0x41)]
    [InlineData(2, 0x7D)]
    [InlineData(3, 0x7A)]
    [InlineData(4, 0x51)]
    [InlineData(5, 0x45)]
    [InlineData(6, 0x3D)]
    public async Task ResolveAsync_DecodesConfirmedItemFieldsAtExactOffsetsAsBigEndian(
        int subtype,
        int expectedSize)
    {
        const int pid = 0x00000001;
        var prototypeBytes = CreateItemPrototype(pid, subtype);
        var expectedFields = GetExpectedItemFields(subtype);
        var expectedValues = new Dictionary<int, int>();
        for (var index = 0; index < expectedFields.Length; index++)
        {
            var field = expectedFields[index];
            var value = index switch
            {
                0 => int.MinValue,
                1 => int.MaxValue,
                2 => 0x01020304,
                _ => unchecked((int)(0xA0B0C000u + (uint)index))
            };
            if (field.SizeInBytes == sizeof(byte))
            {
                var byteValue = unchecked((byte)value);
                expectedValues[field.Offset] = byteValue;
                prototypeBytes[field.Offset] = byteValue;
            }
            else
            {
                expectedValues[field.Offset] = value;
                WriteInt32(prototypeBytes, field.Offset, value);
            }
        }

        var archive = CreateArchive(
            "items",
            "items.lst",
            "item.pro\n",
            ("item.pro", prototypeBytes));

        var result = await new Fallout2PrototypeResolver(archive).ResolveAsync(pid);

        Assert.True(result.HasSupportedSubtype);
        Assert.Equal(expectedSize, result.FileSize);
        Assert.Equal("Prototype PID, subtype, and Item size validated.", result.Status);
        Assert.Equal(prototypeBytes, result.RawBytes.ToArray());
        Assert.Equal(
            expectedFields,
            result.ItemFields
                .Select(field => (field.Name, field.Offset, field.SizeInBytes))
                .ToArray());
        Assert.All(result.ItemFields, field =>
        {
            Assert.Equal(expectedValues[field.Offset], field.Value);
            Assert.Equal(
                field.SizeInBytes == sizeof(byte) ? "uint8" : "int32 BE",
                field.StorageType);
        });
        Assert.Equal(
            "Uninterpreted byte; sound ID is not confirmed.",
            result.ItemFields.Single(field => field.Name == "field_80").Interpretation);
    }

    [Theory]
    [InlineData(0, 0x81)]
    [InlineData(1, 0x41)]
    [InlineData(2, 0x7D)]
    [InlineData(3, 0x7A)]
    [InlineData(4, 0x51)]
    [InlineData(5, 0x45)]
    [InlineData(6, 0x3D)]
    public async Task ResolveAsync_ReportsTruncatedItemAndOmitsIncompleteFields(
        int subtype,
        int expectedSize)
    {
        const int pid = 0x00000001;
        var truncatedSize = expectedSize - 1;
        var prototypeBytes = CreateItemPrototype(pid, subtype, truncatedSize);
        var expectedFields = GetExpectedItemFields(subtype);
        for (var index = 0; index < expectedFields.Length; index++)
        {
            var field = expectedFields[index];
            if (field.Offset + field.SizeInBytes > prototypeBytes.Length)
            {
                continue;
            }

            var value = unchecked((int)(0x10203040u + (uint)index));
            if (field.SizeInBytes == sizeof(byte))
            {
                prototypeBytes[field.Offset] = unchecked((byte)value);
            }
            else
            {
                WriteInt32(prototypeBytes, field.Offset, value);
            }
        }

        var archive = CreateArchive(
            "items",
            "items.lst",
            "short.pro\n",
            ("short.pro", prototypeBytes));

        var result = await new Fallout2PrototypeResolver(archive).ResolveAsync(pid);

        Assert.Contains(
            $"shorter than {expectedSize} bytes for subtype {subtype}",
            result.Status);
        Assert.DoesNotContain(
            result.ItemFields,
            field => field.Name == expectedFields[^1].Name);
        Assert.All(
            result.ItemFields,
            field => Assert.True(field.Offset + field.SizeInBytes <= prototypeBytes.Length));
        Assert.Equal(prototypeBytes, result.RawBytes.ToArray());
    }

    [Fact]
    public async Task ResolveAsync_ReportsItemPrototypeLongerThanSubtypeSize()
    {
        const int pid = 0x00000001;
        var prototypeBytes = CreateItemPrototype(pid, subtype: 6, size: 0x3E);
        var archive = CreateArchive(
            "items",
            "items.lst",
            "long.pro\n",
            ("long.pro", prototypeBytes));

        var result = await new Fallout2PrototypeResolver(archive).ResolveAsync(pid);

        Assert.Contains("longer than expected 61 bytes for subtype 6 (found 62)", result.Status);
        Assert.Contains(result.ItemFields, field => field.Name == "keyCode");
    }

    [Fact]
    public async Task ResolveAsync_ReportsItemPrototypePidMismatchWithoutInterpretingFields()
    {
        const int requestedPid = 0x00000001;
        const int prototypePid = 0x00000002;
        var prototypeBytes = CreateItemPrototype(prototypePid, subtype: 6);
        var archive = CreateArchive(
            "items",
            "items.lst",
            "wrong-pid.pro\n",
            ("wrong-pid.pro", prototypeBytes));

        var result = await new Fallout2PrototypeResolver(archive).ResolveAsync(requestedPid);

        Assert.Null(result.VerifiedFileName);
        Assert.Equal(prototypePid, result.PrototypePid);
        Assert.Contains("does not match MAP PID", result.Status);
        Assert.Empty(result.ItemFields);
        Assert.Equal(prototypeBytes, result.RawBytes.ToArray());
    }

    [Fact]
    public async Task ResolveAsync_ReportsTruncatedSceneryAndOmitsIncompleteWords()
    {
        const int pid = 0x02000001;
        var prototypeBytes = CreateSceneryPrototype(pid, subtype: 2, size: 0x30);
        WriteInt32(prototypeBytes, 0x29, 42);
        prototypeBytes[0x2D] = 0x01;
        prototypeBytes[0x2E] = 0x02;
        prototypeBytes[0x2F] = 0x03;
        var archive = CreateArchive(
            "scenery",
            "scenery.lst",
            "short.pro\n",
            ("short.pro", prototypeBytes));

        var result = await new Fallout2PrototypeResolver(archive).ResolveAsync(pid);

        Assert.Contains("shorter than 49 bytes for subtype 2", result.Status);
        Assert.True(result.HasSupportedSubtype);
        Assert.False(result.HasExpectedSize);
        Assert.Equal(48, result.FileSize);
        Assert.Contains(result.SceneryFields, field => field.Name == "elevator.type" && field.Value == 42);
        Assert.Contains(result.SceneryFields, field => field.Name == "field_34");
        Assert.DoesNotContain(result.SceneryFields, field => field.Name == "elevator.level");
        Assert.Equal(prototypeBytes, result.RawBytes.ToArray());
    }

    [Fact]
    public async Task ResolveAsync_ReportsSceneryTrailingBytesWithoutInterpretingThem()
    {
        const int pid = 0x02000001;
        const int nominalSize = 0x2D;
        var prototypeBytes = CreateSceneryPrototype(pid, subtype: 5, size: nominalSize + 24);
        WriteInt32(prototypeBytes, 0x29, 0x11223344);
        for (var index = 0; index < 6; index++)
        {
            WriteInt32(prototypeBytes, nominalSize + index * sizeof(int), 0x10203040 + index);
        }

        var archive = CreateArchive(
            "scenery",
            "scenery.lst",
            "generic.pro\n",
            ("generic.pro", prototypeBytes));

        var result = await new Fallout2PrototypeResolver(archive).ResolveAsync(pid);

        Assert.True(result.HasSupportedSubtype);
        Assert.False(result.HasExpectedSize);
        Assert.Contains("longer than expected 45 bytes for subtype 5", result.Status);
        Assert.Contains("trailing bytes are preserved without interpretation", result.Status);
        Assert.Equal(nominalSize + 24, result.FileSize);
        Assert.Contains(
            result.SceneryFields,
            field => field.Name == "generic.field_0" && field.Value != 0);
        Assert.DoesNotContain(result.SceneryFields, field => field.Offset >= nominalSize);
        Assert.Equal(prototypeBytes, result.RawBytes.ToArray());
    }

    [Theory]
    [InlineData(0, "Glass")]
    [InlineData(7, "Leather")]
    public async Task ResolveAsync_MapsOnlyConfirmedSceneryMaterialBoundaries(
        int material,
        string expectedName)
    {
        const int pid = 0x02000001;
        var prototypeBytes = CreateSceneryPrototype(pid, subtype: 5);
        WriteInt32(prototypeBytes, 0x24, material);
        var archive = CreateArchive(
            "scenery",
            "scenery.lst",
            "object.pro\n",
            ("object.pro", prototypeBytes));

        var result = await new Fallout2PrototypeResolver(archive).ResolveAsync(pid);

        var field = Assert.Single(result.SceneryFields, item => item.Name == "material");
        Assert.Equal(material, field.Value);
        Assert.Equal(expectedName, field.Interpretation);
    }

    [Fact]
    public async Task ResolveAsync_ReportsPrototypePidThatDoesNotMatchSelectedObject()
    {
        const int requestedPid = 0x02000001;
        const int prototypePid = 0x02000002;
        var archive = CreateArchive(
            "scenery",
            "scenery.lst",
            "tree.pro\n",
            ("tree.pro", CreatePrototype(prototypePid, subtype: 5)));

        var result = await new Fallout2PrototypeResolver(archive).ResolveAsync(requestedPid);

        Assert.Null(result.VerifiedFileName);
        Assert.Equal("tree.pro", result.ListedFileName);
        Assert.Equal(prototypePid, result.PrototypePid);
        Assert.Contains("does not match MAP PID", result.Status);
        Assert.NotEmpty(result.RawBytes.ToArray());
    }

    [Fact]
    public async Task ResolveAsync_ReportsMissingPrototypeWithoutRawData()
    {
        var archive = CreateArchive("items", "items.lst", "missing.pro\n");

        var result = await new Fallout2PrototypeResolver(archive).ResolveAsync(0x00000001);

        Assert.Contains("prototype 'missing.pro' is missing", result.Status);
        Assert.Null(result.VerifiedFileName);
        Assert.Empty(result.RawBytes.ToArray());
    }

    [Fact]
    public async Task ResolveAsync_ReportsUnsupportedSubtypeAndPreservesRawFile()
    {
        const int pid = 0x00000001;
        var prototypeBytes = CreatePrototype(pid, subtype: 7);
        var archive = CreateArchive(
            "items",
            "items.lst",
            "unsupported.pro\n",
            ("unsupported.pro", prototypeBytes));

        var result = await new Fallout2PrototypeResolver(archive).ResolveAsync(pid);

        Assert.Equal(7, result.Subtype);
        Assert.Equal("unsupported.pro", result.VerifiedFileName);
        Assert.False(result.HasSupportedSubtype);
        Assert.Contains("unsupported subtype 7", result.Status);
        Assert.Equal(prototypeBytes, result.RawBytes.ToArray());
    }

    [Fact]
    public async Task ResolveAsync_ReportsTruncatedPrototypeAndReturnsAvailableBytes()
    {
        const int pid = 0x02000001;
        var truncatedPrototype = CreatePrototype(pid, subtype: 5)[..35];
        var archive = CreateArchive(
            "scenery",
            "scenery.lst",
            "short.pro\n",
            ("short.pro", truncatedPrototype));

        var result = await new Fallout2PrototypeResolver(archive).ResolveAsync(pid);

        Assert.Contains("shorter than 36 bytes", result.Status);
        Assert.Equal(pid, result.PrototypePid);
        Assert.Null(result.Subtype);
        Assert.Equal(truncatedPrototype.Length, result.FileSize);
        Assert.Equal(truncatedPrototype, result.RawBytes.ToArray());
    }

    [Fact]
    public async Task ResolveAsync_ReportsEntryLengthMismatchWithoutThrowing()
    {
        const int pid = 0x00000001;
        var bytes = CreatePrototype(pid, subtype: 5);
        var archive = CreateArchiveWithEntrySize(
            "items",
            "items.lst",
            "item.pro\n",
            ("item.pro", bytes, (long)bytes.Length + 1));

        var result = await new Fallout2PrototypeResolver(archive).ResolveAsync(pid);

        Assert.Contains("prototype could not be read", result.Status);
        Assert.Empty(result.RawBytes.ToArray());
    }

    [Fact]
    public async Task ResolveAsync_KeepsResultsStableAcrossSelectionsAndReusesCachedReads()
    {
        const int firstPid = 0x02000001;
        const int secondPid = 0x02000002;
        var firstBytes = CreateSceneryPrototype(firstPid, subtype: 0);
        var secondBytes = CreatePrototype(secondPid, subtype: 5);
        var archive = CreateArchive(
            "scenery",
            "scenery.lst",
            "door.pro\ngeneric.pro\n",
            ("door.pro", firstBytes),
            ("generic.pro", secondBytes));
        var resolver = new Fallout2PrototypeResolver(archive);

        var first = await resolver.ResolveAsync(firstPid);
        var second = await resolver.ResolveAsync(secondPid);
        var firstAgain = await resolver.ResolveAsync(firstPid);

        Assert.Equal("door.pro", firstAgain.VerifiedFileName);
        Assert.Equal(firstPid, firstAgain.PrototypePid);
        Assert.Equal(0, firstAgain.Subtype);
        Assert.Equal(firstBytes, first.RawBytes.ToArray());
        Assert.Equal(firstBytes, firstAgain.RawBytes.ToArray());
        Assert.Equal("generic.pro", second.VerifiedFileName);
        Assert.Equal(secondPid, second.PrototypePid);
        Assert.Equal(5, second.Subtype);
        Assert.Equal(1, archive.ReadCount("proto/scenery/scenery.lst"));
        Assert.Equal(1, archive.ReadCount("proto/scenery/door.pro"));
        Assert.Equal(1, archive.ReadCount("proto/scenery/generic.pro"));
    }

    private static InMemoryArchive CreateArchive(
        string directory,
        string listFileName,
        string listContents,
        params (string FileName, byte[] Bytes)[] prototypes) =>
        CreateArchiveWithEntrySize(
            directory,
            listFileName,
            listContents,
            prototypes.Select(prototype => (
                prototype.FileName,
                prototype.Bytes,
                (long?)null)).ToArray());

    private static InMemoryArchive CreateArchiveWithEntrySize(
        string directory,
        string listFileName,
        string listContents,
        params (string FileName, byte[] Bytes, long? DeclaredSize)[] prototypes) =>
        CreateArchiveWithEntrySize(
            directory,
            listFileName,
            listContents,
            FalloutGame.Fallout2,
            ArchiveFormat.Dat2,
            prototypes);

    private static InMemoryArchive CreateArchiveWithEntrySize(
        string directory,
        string listFileName,
        string listContents,
        FalloutGame game,
        ArchiveFormat format,
        params (string FileName, byte[] Bytes, long? DeclaredSize)[] prototypes)
    {
        var entries = new Dictionary<string, (ArchiveEntry Entry, byte[] Bytes)>(
            StringComparer.OrdinalIgnoreCase);

        AddEntry($"proto/{directory}/{listFileName}", Encoding.ASCII.GetBytes(listContents));
        foreach (var (fileName, bytes, declaredSize) in prototypes)
        {
            AddEntry($"proto/{directory}/{fileName}", bytes, declaredSize);
        }

        return new InMemoryArchive(
            entries.Values.Select(item => item.Entry).ToArray(),
            entries,
            game,
            format);

        void AddEntry(string path, byte[] bytes, long? declaredSize = null)
        {
            entries.Add(
                path,
                (new ArchiveEntry(path, declaredSize ?? bytes.Length, null, "synthetic-fallout2.dat"), bytes));
        }
    }

    private static byte[] CreatePrototype(int pid, int subtype)
    {
        var bytes = new byte[45];
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(0, sizeof(int)), pid);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(32, sizeof(int)), subtype);
        return bytes;
    }

    private static byte[] CreateSceneryPrototype(int pid, int subtype, int? size = null)
    {
        var bytes = new byte[size ?? (subtype <= 2 ? 0x31 : 0x2D)];
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(0, sizeof(int)), pid);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(32, sizeof(int)), subtype);
        return bytes;
    }

    private static byte[] CreateItemPrototype(int pid, int subtype, int? size = null)
    {
        var bytes = new byte[size ?? GetExpectedItemSize(subtype)];
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(0, sizeof(int)), pid);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(32, sizeof(int)), subtype);
        return bytes;
    }

    private static int GetExpectedItemSize(int subtype) =>
        subtype switch
        {
            0 => 0x81,
            1 => 0x41,
            2 => 0x7D,
            3 => 0x7A,
            4 => 0x51,
            5 => 0x45,
            6 => 0x3D,
            _ => throw new ArgumentOutOfRangeException(nameof(subtype))
        };

    private static (string Name, int Offset, int SizeInBytes)[] GetExpectedItemFields(int subtype)
    {
        var fields = new List<(string Name, int Offset, int SizeInBytes)>
        {
            ("messageId", 0x04, sizeof(int)),
            ("fid", 0x08, sizeof(int)),
            ("lightDistance", 0x0C, sizeof(int)),
            ("lightIntensity", 0x10, sizeof(int)),
            ("flags", 0x14, sizeof(int)),
            ("extendedFlags", 0x18, sizeof(int)),
            ("sid", 0x1C, sizeof(int)),
            ("material", 0x24, sizeof(int)),
            ("size", 0x28, sizeof(int)),
            ("weight", 0x2C, sizeof(int)),
            ("cost", 0x30, sizeof(int)),
            ("inventoryFid", 0x34, sizeof(int)),
            ("field_80", 0x38, sizeof(byte))
        };

        switch (subtype)
        {
            case 0:
                fields.Add(("armorClass", 0x39, sizeof(int)));
                AddExpectedArray(fields, "damageResistance", 0x3D, 7);
                AddExpectedArray(fields, "damageThreshold", 0x59, 7);
                fields.Add(("perk", 0x75, sizeof(int)));
                fields.Add(("maleFid", 0x79, sizeof(int)));
                fields.Add(("femaleFid", 0x7D, sizeof(int)));
                break;
            case 1:
                fields.Add(("maxSize", 0x39, sizeof(int)));
                fields.Add(("openFlags", 0x3D, sizeof(int)));
                break;
            case 2:
                AddExpectedArray(fields, "stat", 0x39, 3);
                AddExpectedArray(fields, "amount", 0x45, 3);
                fields.Add(("duration1", 0x51, sizeof(int)));
                AddExpectedArray(fields, "amount1", 0x55, 3);
                fields.Add(("duration2", 0x61, sizeof(int)));
                AddExpectedArray(fields, "amount2", 0x65, 3);
                fields.Add(("addictionChance", 0x71, sizeof(int)));
                fields.Add(("withdrawalEffect", 0x75, sizeof(int)));
                fields.Add(("withdrawalOnset", 0x79, sizeof(int)));
                break;
            case 3:
                fields.AddRange(
                [
                    ("animationCode", 0x39, sizeof(int)),
                    ("minDamage", 0x3D, sizeof(int)),
                    ("maxDamage", 0x41, sizeof(int)),
                    ("damageType", 0x45, sizeof(int)),
                    ("maxRange1", 0x49, sizeof(int)),
                    ("maxRange2", 0x4D, sizeof(int)),
                    ("projectilePid", 0x51, sizeof(int)),
                    ("minStrength", 0x55, sizeof(int)),
                    ("actionPointCost1", 0x59, sizeof(int)),
                    ("actionPointCost2", 0x5D, sizeof(int)),
                    ("criticalFailureType", 0x61, sizeof(int)),
                    ("perk", 0x65, sizeof(int)),
                    ("rounds", 0x69, sizeof(int)),
                    ("caliber", 0x6D, sizeof(int)),
                    ("ammoTypePid", 0x71, sizeof(int)),
                    ("ammoCapacity", 0x75, sizeof(int)),
                    ("soundCode", 0x79, sizeof(byte))
                ]);
                break;
            case 4:
                fields.AddRange(
                [
                    ("caliber", 0x39, sizeof(int)),
                    ("quantity", 0x3D, sizeof(int)),
                    ("armorClassModifier", 0x41, sizeof(int)),
                    ("damageResistanceModifier", 0x45, sizeof(int)),
                    ("damageMultiplier", 0x49, sizeof(int)),
                    ("damageDivisor", 0x4D, sizeof(int))
                ]);
                break;
            case 5:
                fields.AddRange(
                [
                    ("powerTypePid", 0x39, sizeof(int)),
                    ("powerType", 0x3D, sizeof(int)),
                    ("charges", 0x41, sizeof(int))
                ]);
                break;
            case 6:
                fields.Add(("keyCode", 0x39, sizeof(int)));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(subtype));
        }

        return fields.ToArray();
    }

    private static void AddExpectedArray(
        List<(string Name, int Offset, int SizeInBytes)> fields,
        string name,
        int offset,
        int count)
    {
        for (var index = 0; index < count; index++)
        {
            fields.Add(($"{name}[{index}]", offset + index * sizeof(int), sizeof(int)));
        }
    }

    private static void WriteInt32(byte[] bytes, int offset, int value) =>
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(offset, sizeof(int)), value);

    private sealed class InMemoryArchive(
        IReadOnlyList<ArchiveEntry> browserEntries,
        Dictionary<string, (ArchiveEntry Entry, byte[] Bytes)> entries,
        FalloutGame game,
        ArchiveFormat format) : IGameArchive
    {
        private readonly ConcurrentDictionary<string, int> _readCounts =
            new(StringComparer.OrdinalIgnoreCase);

        public string Path => "synthetic-fallout2.dat";
        public FalloutGame Game => game;
        public ArchiveFormat Format => format;
        public IReadOnlyList<ArchiveEntry> BrowserEntries => browserEntries;

        public int ReadCount(string path) =>
            _readCounts.TryGetValue(path, out var count) ? count : 0;

        public async Task CopyEntryToAsync(
            ArchiveEntry entry,
            Stream destination,
            CancellationToken cancellationToken = default)
        {
            if (!entries.TryGetValue(entry.Path, out var item))
            {
                throw new InvalidDataException($"Synthetic entry '{entry.Path}' does not exist.");
            }

            _readCounts.AddOrUpdate(entry.Path, 1, (_, count) => count + 1);
            await destination.WriteAsync(item.Bytes, cancellationToken);
        }
    }
}
