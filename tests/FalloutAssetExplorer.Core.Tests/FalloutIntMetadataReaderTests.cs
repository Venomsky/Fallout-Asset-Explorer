using System.Buffers.Binary;
using System.Runtime.InteropServices;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class FalloutIntMetadataReaderTests
{
    [Theory]
    [InlineData(FalloutGame.Fallout1)]
    [InlineData(FalloutGame.Fallout2)]
    public void Parse_AcceptsMinimalFileWithEmptyTables(FalloutGame game)
    {
        var result = FalloutIntMetadataReader.Parse(game, BuildFile([]));

        Assert.True(result.IsSuccess);
        Assert.Null(result.Diagnostic);
        Assert.NotNull(result.Metadata);
        Assert.Equal(game, result.Metadata.Game);
        Assert.Equal(0, result.Metadata.ProcedureCount);
        Assert.Empty(result.Metadata.Procedures);
        Assert.Equal(46, result.Metadata.IdentifierTableOffset);
        Assert.Equal(0, result.Metadata.IdentifierPayloadLength);
        Assert.Equal(50, result.Metadata.StaticStringTableOffset);
        Assert.Equal(0, result.Metadata.RawStaticStringTablePrefix);
        Assert.Equal(54, result.Metadata.StaticStringDataOffset);
        Assert.Equal(0, result.Metadata.StaticStringDataAvailableLength);
    }

    [Theory]
    [InlineData(FalloutGame.Fallout1)]
    [InlineData(FalloutGame.Fallout2)]
    public void Parse_ReadsSixBigEndianRawFieldsAndResolvesBoundedName(FalloutGame game)
    {
        var fields = new[]
        {
            6,
            0x01020300,
            0x11223344,
            int.MinValue,
            60,
            0x55667788
        };
        var identifiers = new byte[] { 0x80, 0xFF, (byte)'N', (byte)'A', (byte)'M', (byte)'E', 0 };
        var bytes = BuildFile([fields], identifiers, [0xFE, 0xFD]);

        var result = FalloutIntMetadataReader.Parse(game, bytes);

        Assert.True(result.IsSuccess);
        var procedure = Assert.Single(result.Metadata!.Procedures);
        Assert.Equal(46, procedure.RecordOffset);
        Assert.Equal(6, procedure.RawField0);
        Assert.Equal(0x01020300, procedure.RawField4);
        Assert.Equal(0x11223344, procedure.RawField8);
        Assert.Equal(int.MinValue, procedure.RawField12);
        Assert.Equal(60, procedure.RawField16);
        Assert.Equal(0x55667788, procedure.RawField20);
        Assert.Equal(new byte[] { (byte)'N', (byte)'A', (byte)'M', (byte)'E' }, procedure.NameBytes.ToArray());
        Assert.Equal(7, result.Metadata.IdentifierPayloadLength);
        Assert.Equal(2, result.Metadata.RawStaticStringTablePrefix);
    }

    [Fact]
    public void Parse_ExposesRawIdentifierTableIncludingPrefixAndNonAsciiBytes()
    {
        var bytes = BuildFile(
            [[6, 0, 0, 0, 60, 0]],
            [0x80, 0xFF, (byte)'x', 0]);

        var result = FalloutIntMetadataReader.Parse(FalloutGame.Fallout1, bytes);

        Assert.True(result.IsSuccess);
        var metadata = result.Metadata!;
        Assert.Equal(
            new byte[] { 0, 0, 0, 4, 0x80, 0xFF, (byte)'x', 0 },
            metadata.IdentifierTableBytes.ToArray());
        Assert.Equal(4, metadata.IdentifierPayloadLength);
        Assert.Equal(new byte[] { (byte)'x' }, Assert.Single(metadata.Procedures).NameBytes.ToArray());

        Array.Fill(bytes, (byte)0);
        Assert.Equal(
            new byte[] { 0, 0, 0, 4, 0x80, 0xFF, (byte)'x', 0 },
            metadata.IdentifierTableBytes.ToArray());
    }

    [Fact]
    public void PublicByteViews_UseReadOnlySpansAndNameBytesReturnsDetachedMemory()
    {
        var result = FalloutIntMetadataReader.Parse(
            FalloutGame.Fallout2,
            BuildFile([[4, 0, 0, 0, 60, 0]], "name\0"u8.ToArray(), [0x41, 0]));

        Assert.True(result.IsSuccess);
        var metadata = result.Metadata!;
        var procedure = Assert.Single(metadata.Procedures);
        var candidate = metadata.GetStaticStringCandidate(0);
        var identifierTableBefore = metadata.IdentifierTableBytes.ToArray();
        var nameBefore = procedure.NameBytes.ToArray();
        var candidateBefore = candidate.CandidateBytes.ToArray();

        Assert.Equal(
            typeof(ReadOnlySpan<byte>),
            typeof(FalloutIntMetadata).GetProperty(nameof(metadata.IdentifierTableBytes))!.PropertyType);
        Assert.Equal(
            typeof(ReadOnlyMemory<byte>),
            typeof(FalloutIntProcedureMetadata).GetProperty(nameof(procedure.NameBytes))!.PropertyType);
        Assert.Equal(
            typeof(ReadOnlySpan<byte>),
            typeof(FalloutIntStaticStringCandidate).GetProperty(nameof(candidate.CandidateBytes))!.PropertyType);

        MutateArrayRecoveredFromMemoryMarshal(procedure.NameBytes);

        Assert.Equal(identifierTableBefore, metadata.IdentifierTableBytes.ToArray());
        Assert.Equal(nameBefore, procedure.NameBytes.ToArray());
        Assert.Equal(candidateBefore, metadata.GetStaticStringCandidate(0).CandidateBytes.ToArray());
    }

    [Fact]
    public void Parse_LeavesField20RawForBothGameLayouts()
    {
        var bytes = BuildFile(
            [[4, 0, 0, 0, 60, 9]],
            "proc\0"u8.ToArray());

        var fallout1 = FalloutIntMetadataReader.Parse(FalloutGame.Fallout1, bytes);
        var fallout2 = FalloutIntMetadataReader.Parse(FalloutGame.Fallout2, bytes);

        Assert.Equal(9, Assert.Single(fallout1.Metadata!.Procedures).RawField20);
        Assert.Equal(9, Assert.Single(fallout2.Metadata!.Procedures).RawField20);
    }

    [Fact]
    public void Parse_AllowsNameOffsetsWithinTheWholeIdentifierTable()
    {
        var bytes = BuildFile(
            [[0, 0, 0, 0, 60, 0]],
            [(byte)'x', 0]);

        var result = FalloutIntMetadataReader.Parse(FalloutGame.Fallout2, bytes);

        Assert.True(result.IsSuccess);
        Assert.Empty(Assert.Single(result.Metadata!.Procedures).NameBytes.ToArray());
    }

    [Fact]
    public void Parse_ReportsTruncatedProcedureCount()
    {
        var result = FalloutIntMetadataReader.Parse(FalloutGame.Fallout1, new byte[42]);

        AssertDiagnostic(result, FalloutIntMetadataDiagnosticCode.TruncatedProcedureCount);
    }

    [Fact]
    public void Parse_ReportsTruncatedProcedureRecord()
    {
        var bytes = new byte[46 + 23];
        WriteInt32(bytes, 42, 1);

        var result = FalloutIntMetadataReader.Parse(FalloutGame.Fallout2, bytes);

        AssertDiagnostic(result, FalloutIntMetadataDiagnosticCode.TruncatedProcedureTable);
    }

    [Theory]
    [InlineData(-1, FalloutIntMetadataDiagnosticCode.InvalidProcedureCount)]
    [InlineData(int.MaxValue, FalloutIntMetadataDiagnosticCode.TooManyProcedures)]
    public void Parse_RejectsNegativeAndExtremeProcedureCounts(
        int count,
        FalloutIntMetadataDiagnosticCode code)
    {
        var bytes = new byte[46];
        WriteInt32(bytes, 42, count);

        var result = FalloutIntMetadataReader.Parse(FalloutGame.Fallout1, bytes);

        AssertDiagnostic(result, code);
    }

    [Fact]
    public void Parse_ReportsTruncatedIdentifierSizePrefix()
    {
        var result = FalloutIntMetadataReader.Parse(
            FalloutGame.Fallout1,
            new byte[46]);

        AssertDiagnostic(result, FalloutIntMetadataDiagnosticCode.TruncatedIdentifierTableHeader);
    }

    [Fact]
    public void Parse_ReportsTruncatedIdentifierPayload()
    {
        var bytes = new byte[52];
        WriteInt32(bytes, 42, 0);
        WriteInt32(bytes, 46, 3);

        var result = FalloutIntMetadataReader.Parse(FalloutGame.Fallout2, bytes);

        AssertDiagnostic(result, FalloutIntMetadataDiagnosticCode.TruncatedIdentifierTable);
    }

    [Fact]
    public void Parse_ReportsTruncatedStaticStringPrefix()
    {
        var missingPrefix = BuildFile([], []);
        Array.Resize(ref missingPrefix, 50);

        AssertDiagnostic(
            FalloutIntMetadataReader.Parse(FalloutGame.Fallout1, missingPrefix),
            FalloutIntMetadataDiagnosticCode.TruncatedStaticStringTablePrefix);
    }

    [Fact]
    public void Parse_RejectsNegativeIdentifierTableLength()
    {
        var identifiers = BuildFile([], []);
        WriteInt32(identifiers, 46, -1);

        AssertDiagnostic(
            FalloutIntMetadataReader.Parse(FalloutGame.Fallout1, identifiers),
            FalloutIntMetadataDiagnosticCode.InvalidIdentifierTableSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void Parse_PreservesRawStaticStringPrefixWithoutAssumingItsMeaning(int rawPrefix)
    {
        var bytes = BuildFile([], [], [0xAA, 0x00, 0xBB]);
        WriteInt32(bytes, 50, rawPrefix);

        var result = FalloutIntMetadataReader.Parse(FalloutGame.Fallout2, bytes);

        Assert.True(result.IsSuccess);
        Assert.Equal(rawPrefix, result.Metadata!.RawStaticStringTablePrefix);
        Assert.Equal(54, result.Metadata.StaticStringDataOffset);
        Assert.Equal(3, result.Metadata.StaticStringDataAvailableLength);
        var candidate = result.Metadata.GetStaticStringCandidate(0);
        Assert.Equal(
            FalloutIntStaticStringCandidateStatus.NullTerminatedBeforeEndOfFile,
            candidate.Status);
        Assert.Equal(new byte[] { 0xAA }, candidate.CandidateBytes.ToArray());
        Assert.False(candidate.StaticStringTableBoundaryIsKnown);
    }

    [Fact]
    public void GetStaticStringCandidate_ReturnsBytesBeforeNulWithoutDecoding()
    {
        var bytes = BuildFile([], [], [0x41, 0x80, 0, 0x42]);

        var result = FalloutIntMetadataReader.Parse(FalloutGame.Fallout1, bytes);

        Assert.True(result.IsSuccess);
        var candidate = result.Metadata!.GetStaticStringCandidate(0);
        Assert.Equal(
            FalloutIntStaticStringCandidateStatus.NullTerminatedBeforeEndOfFile,
            candidate.Status);
        Assert.Equal(new byte[] { 0x41, 0x80 }, candidate.CandidateBytes.ToArray());
        Assert.False(candidate.StaticStringTableBoundaryIsKnown);

        Array.Fill(bytes, (byte)0);
        Assert.Equal(new byte[] { 0x41, 0x80 }, candidate.CandidateBytes.ToArray());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(int.MaxValue)]
    public void GetStaticStringCandidate_ReportsInvalidOffsets(int offset)
    {
        var result = FalloutIntMetadataReader.Parse(
            FalloutGame.Fallout2,
            BuildFile([], [], [0x41, 0x42]));

        Assert.True(result.IsSuccess);
        var candidate = result.Metadata!.GetStaticStringCandidate(offset);
        Assert.Equal(offset, candidate.Offset);
        Assert.Equal(FalloutIntStaticStringCandidateStatus.InvalidOffset, candidate.Status);
        Assert.Empty(candidate.CandidateBytes.ToArray());
        Assert.False(candidate.StaticStringTableBoundaryIsKnown);
    }

    [Fact]
    public void GetStaticStringCandidate_ReportsUnterminatedBytesThroughEndOfFile()
    {
        var result = FalloutIntMetadataReader.Parse(
            FalloutGame.Fallout2,
            BuildFile([], [], [0x41, 0x80]));

        Assert.True(result.IsSuccess);
        var candidate = result.Metadata!.GetStaticStringCandidate(1);
        Assert.Equal(
            FalloutIntStaticStringCandidateStatus.UnterminatedAtEndOfFile,
            candidate.Status);
        Assert.Equal(new byte[] { 0x80 }, candidate.CandidateBytes.ToArray());
        Assert.False(candidate.StaticStringTableBoundaryIsKnown);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void Parse_RejectsNameOffsetsOutsideTheIdentifierTable(int nameOffset)
    {
        var bytes = BuildFile(
            [[nameOffset, 0, 0, 0, 60, 0]],
            "name\0"u8.ToArray());

        var result = FalloutIntMetadataReader.Parse(FalloutGame.Fallout2, bytes);

        AssertDiagnostic(result, FalloutIntMetadataDiagnosticCode.InvalidNameOffset);
    }

    [Fact]
    public void Parse_RejectsNamesWithoutANulBeforeTableEnd()
    {
        var bytes = BuildFile(
            [[4, 0, 0, 0, 60, 0]],
            "name"u8.ToArray());

        var result = FalloutIntMetadataReader.Parse(FalloutGame.Fallout1, bytes);

        AssertDiagnostic(result, FalloutIntMetadataDiagnosticCode.UnterminatedName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void Parse_RejectsNonImportedProcedureBodyOffsetsOutsideFile(int bodyOffset)
    {
        var bytes = BuildFile(
            [[4, 0, 0, 0, bodyOffset, 0]],
            "name\0"u8.ToArray());

        var result = FalloutIntMetadataReader.Parse(FalloutGame.Fallout1, bytes);

        AssertDiagnostic(result, FalloutIntMetadataDiagnosticCode.InvalidProcedureBodyOffset);
    }

    [Fact]
    public void Parse_ValidatesConditionOffsetOnlyForConditionalProcedures()
    {
        var bytes = BuildFile(
            [[4, 0x02, 0, int.MaxValue, 60, 0]],
            "name\0"u8.ToArray());

        var result = FalloutIntMetadataReader.Parse(FalloutGame.Fallout2, bytes);

        AssertDiagnostic(result, FalloutIntMetadataDiagnosticCode.InvalidConditionOffset);
    }

    [Fact]
    public void Parse_PreservesInputAndBoundsStaticStringDataAtEndOfFile()
    {
        var bytes = BuildFile(
            [[4, 0, 0, 0, 60, 0]],
            "name\0"u8.ToArray()).Concat(new byte[] { 0xAA, 0xBB }).ToArray();
        var original = bytes.ToArray();

        var first = FalloutIntMetadataReader.Parse(FalloutGame.Fallout2, bytes);
        var second = FalloutIntMetadataReader.Parse(FalloutGame.Fallout2, bytes);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Metadata!.ProcedureCount, second.Metadata!.ProcedureCount);
        Assert.Equal(
            first.Metadata.Procedures[0].NameBytes.ToArray(),
            second.Metadata.Procedures[0].NameBytes.ToArray());
        Assert.Equal(
            first.Metadata.Procedures[0].RawField20,
            second.Metadata.Procedures[0].RawField20);
        Assert.Equal(2, first.Metadata!.StaticStringDataAvailableLength);
        Assert.Equal(original, bytes);
    }

    private static void AssertDiagnostic(
        FalloutIntMetadataReadResult result,
        FalloutIntMetadataDiagnosticCode expectedCode)
    {
        Assert.False(result.IsSuccess);
        Assert.Null(result.Metadata);
        Assert.Equal(expectedCode, result.Diagnostic?.Code);
        Assert.True(result.Diagnostic!.ByteOffset >= 0);
        Assert.False(string.IsNullOrWhiteSpace(result.Diagnostic.Message));
    }

    private static void MutateArrayRecoveredFromMemoryMarshal(ReadOnlyMemory<byte> memory)
    {
        Assert.True(MemoryMarshal.TryGetArray(memory, out var segment));
        Assert.NotNull(segment.Array);
        segment.Array![segment.Offset] ^= 0xFF;
    }

    private static byte[] BuildFile(
        int[][] procedures,
        byte[]? identifierPayload = null,
        byte[]? staticStringPayload = null)
    {
        identifierPayload ??= [];
        staticStringPayload ??= [];
        using var stream = new MemoryStream();
        stream.Write(new byte[42]);
        WriteInt32(stream, procedures.Length);
        foreach (var procedure in procedures)
        {
            if (procedure.Length != 6)
            {
                throw new ArgumentException("Each synthetic procedure must contain six fields.", nameof(procedures));
            }

            foreach (var field in procedure)
            {
                WriteInt32(stream, field);
            }
        }

        WriteInt32(stream, identifierPayload.Length);
        stream.Write(identifierPayload);
        WriteInt32(stream, staticStringPayload.Length);
        stream.Write(staticStringPayload);
        return stream.ToArray();
    }

    private static void WriteInt32(byte[] destination, int offset, int value) =>
        BinaryPrimitives.WriteInt32BigEndian(destination.AsSpan(offset, sizeof(int)), value);

    private static void WriteInt32(Stream destination, int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        destination.Write(buffer);
    }
}
