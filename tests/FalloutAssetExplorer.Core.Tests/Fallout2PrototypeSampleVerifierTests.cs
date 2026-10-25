using System.Buffers.Binary;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class Fallout2PrototypeSampleVerifierTests
{
    [Fact]
    public void Verify_WhenTooShortForCandidateField_DoesNotReadPartialBytes()
    {
        var bytes = CreateSample(length: 0x27);

        var result = Fallout2PrototypeSampleVerifier.Verify("sample.PRO", bytes);

        Assert.Null(result.MaterialCandidate);
        Assert.Contains(result.ValidationMessages, message => message.Contains("candidate at offset 0x24"));
    }

    [Fact]
    public void Verify_WhenExactlyLongEnough_SeparatesRawBytesAndBigEndianNumericView()
    {
        var bytes = CreateSample(length: 0x28);
        bytes[0x24] = 0x01;
        bytes[0x25] = 0x02;
        bytes[0x26] = 0x03;
        bytes[0x27] = 0x04;

        var result = Fallout2PrototypeSampleVerifier.Verify("sample.PRO", bytes);

        Assert.Equal(Fallout2PrototypeSampleStructureStatus.PlausibleHeader, result.StructureStatus);
        Assert.Contains("Total subtype-specific length", result.StructureNote);
        Assert.Contains("release compatibility are not validated", result.StructureNote);
        Assert.NotNull(result.MaterialCandidate);
        Assert.Equal("01020304", result.MaterialCandidate.RawBytesHex);
        Assert.Equal(0x01020304u, result.MaterialCandidate.BigEndianNumericValue);
    }

    [Theory]
    [InlineData(0x00000002u, 0)]
    [InlineData(0x02000002u, 2)]
    public void Verify_ReportsCandidateBytesForSupportedPidTypesWithoutAssigningMeaning(
        uint pid,
        int expectedPidType)
    {
        var bytes = CreateSample(pid: pid);
        bytes[0x24] = 0xA1;
        bytes[0x25] = 0xB2;
        bytes[0x26] = 0xC3;
        bytes[0x27] = 0xD4;

        var result = Fallout2PrototypeSampleVerifier.Verify("sample.PRO", bytes);

        Assert.Equal(Fallout2PrototypeSampleStructureStatus.PlausibleHeader, result.StructureStatus);
        Assert.Equal(expectedPidType, result.PidType);
        Assert.NotNull(result.MaterialCandidate);
        Assert.Equal("A1B2C3D4", result.MaterialCandidate.RawBytesHex);
        Assert.Equal(0xA1B2C3D4u, result.MaterialCandidate.BigEndianNumericValue);
    }

    [Fact]
    public void Verify_RejectsUnsupportedPidTypeForStructuralChecks()
    {
        var result = Fallout2PrototypeSampleVerifier.Verify(
            "unsupported-type.PRO",
            CreateSample(pid: 0x01000002));

        Assert.Equal(Fallout2PrototypeSampleStructureStatus.Unsupported, result.StructureStatus);
        Assert.Equal(1, result.PidType);
        Assert.Contains("PID type 1 is unsupported for structural checks", result.StructureNote);
    }

    [Fact]
    public void Verify_DecodesPidAndReportsMismatchWithUserSuppliedExpectedPid()
    {
        var bytes = CreateSample(pid: 0x00000003);
        var provenance = new Fallout2PrototypeSampleProvenance(
            RecordIdentity: "proto/ITEMS/00000002.PRO",
            ExpectedPidHex: "0x00000002",
            ExpectedPidReference: "User-provided record report");

        var result = Fallout2PrototypeSampleVerifier.Verify("00000002.PRO", bytes, provenance);

        Assert.Equal("00000003", result.RawPidBytesHex);
        Assert.Equal(0x00000003u, result.EmbeddedPid);
        Assert.Equal(0, result.PidType);
        Assert.Equal(3u, result.PrototypeIndex);
        Assert.Equal(
            Fallout2PrototypeSampleIdentityComparison.DiffersFromReportedExpectedPid,
            result.IdentityComparison);
        Assert.Contains(result.ValidationMessages, message => message.Contains("user-reported expected PID"));
    }

    [Fact]
    public void Verify_RejectsZeroPrototypeIndexUnderCurrentResolverConvention()
    {
        var result = Fallout2PrototypeSampleVerifier.Verify(
            "zero-index.PRO",
            CreateSample(pid: 0x00000000));

        Assert.Equal(Fallout2PrototypeSampleStructureStatus.Unsupported, result.StructureStatus);
        Assert.Equal(0u, result.PrototypeIndex);
        Assert.Contains(result.ValidationMessages, message => message.Contains("index zero is invalid"));
    }

    [Fact]
    public void Verify_WithShortPidAndValidExpectedPid_ReportsUnableToCompareAndPreservesNormalizedValue()
    {
        var provenance = new Fallout2PrototypeSampleProvenance(ExpectedPidHex: "0x2");

        var result = Fallout2PrototypeSampleVerifier.Verify(
            "short.PRO",
            [0x00, 0x00, 0x02],
            provenance);

        Assert.Equal(Fallout2PrototypeSampleIdentityComparison.UnableToCompare, result.IdentityComparison);
        Assert.Equal("0x00000002", result.ExpectedPidHex);
        Assert.Null(result.EmbeddedPid);
    }

    [Fact]
    public void Verify_WithShortPidAndInvalidExpectedPid_ReportsInvalidInput()
    {
        var provenance = new Fallout2PrototypeSampleProvenance(ExpectedPidHex: "not-hex");

        var result = Fallout2PrototypeSampleVerifier.Verify(
            "short.PRO",
            [0x00, 0x00, 0x02],
            provenance);

        Assert.Equal(Fallout2PrototypeSampleIdentityComparison.InvalidExpectedPid, result.IdentityComparison);
        Assert.Null(result.ExpectedPidHex);
        Assert.Contains(result.ValidationMessages, message => message.Contains("not a valid 32-bit hexadecimal value"));
    }

    [Fact]
    public void Verify_ReportsExpectedPidMatchWithoutTreatingItAsVerifiedRecordIdentity()
    {
        var bytes = CreateSample(pid: 0x00000002);
        var provenance = new Fallout2PrototypeSampleProvenance(
            ExpectedPidHex: "00000002",
            ExpectedPidReference: "User-provided reference");

        var result = Fallout2PrototypeSampleVerifier.Verify("different-name.PRO", bytes, provenance);

        Assert.Equal(
            Fallout2PrototypeSampleIdentityComparison.MatchesReportedExpectedPid,
            result.IdentityComparison);
        Assert.Equal("different-name.PRO", result.SelectedFileName);
        Assert.Null(result.Provenance.Single(item => item.Name.Contains("record path")).Value);
    }

    [Fact]
    public void Verify_DoesNotInferRecordIdentityFromFilename()
    {
        var result = Fallout2PrototypeSampleVerifier.Verify(
            "00000002.PRO",
            CreateSample(pid: 0x00000003));

        Assert.Equal(0x00000003u, result.EmbeddedPid);
        Assert.Equal(
            Fallout2PrototypeSampleIdentityComparison.NotProvided,
            result.IdentityComparison);
        Assert.Null(result.Provenance.Single(item => item.Name.Contains("record path")).Value);
    }

    [Fact]
    public void Verify_MarksMissingProvenanceUnknownAndSuppliedMetadataReported()
    {
        var bytes = CreateSample();

        var unknown = Fallout2PrototypeSampleVerifier.Verify("sample.PRO", bytes);
        Assert.All(
            unknown.Provenance,
            item => Assert.Equal(Fallout2PrototypeSampleMetadataStatus.Unknown, item.Status));

        var reported = Fallout2PrototypeSampleVerifier.Verify(
            "sample.PRO",
            bytes,
            new Fallout2PrototypeSampleProvenance(
                SourceReference: "https://example.invalid/repo@immutable-revision",
                GameEditionVersion: "reported release",
                RecordIdentity: "reported path",
                AcquisitionMethod: "reported extraction"));
        Assert.All(
            reported.Provenance.Where(item => item.Value is not null),
            item => Assert.Equal(Fallout2PrototypeSampleMetadataStatus.Reported, item.Status));
        Assert.All(
            reported.Provenance.Where(item => item.Value is null),
            item => Assert.Equal(Fallout2PrototypeSampleMetadataStatus.Unknown, item.Status));
    }

    [Fact]
    public void Verify_ComputesDeterministicSha256ForSuppliedBytes()
    {
        var result = Fallout2PrototypeSampleVerifier.Verify("sample.PRO", "abc"u8);

        Assert.Equal(3, result.FileLengthBytes);
        Assert.Equal(
            "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD",
            result.Sha256);
        Assert.Equal(Fallout2PrototypeSampleStructureStatus.Incomplete, result.StructureStatus);
    }

    [Fact]
    public void Verify_DoesNotModifyOriginalSampleBytes()
    {
        var bytes = CreateSample(length: 0x28);
        bytes[0x24] = 0xDE;
        bytes[0x25] = 0xAD;
        bytes[0x26] = 0xBE;
        bytes[0x27] = 0xEF;
        var original = bytes.ToArray();

        _ = Fallout2PrototypeSampleVerifier.Verify("sample.PRO", bytes);

        Assert.Equal(original, bytes);
    }

    [Fact]
    public void Verify_RejectsNonProExtensionWithoutUsingFilenameAsIdentity()
    {
        var result = Fallout2PrototypeSampleVerifier.Verify(
            "00000002.DAT",
            CreateSample());

        Assert.Equal(Fallout2PrototypeSampleStructureStatus.Rejected, result.StructureStatus);
        Assert.Null(result.EmbeddedPid);
        Assert.Null(result.MaterialCandidate);
        Assert.NotNull(result.Sha256);
    }

    [Fact]
    public void Verify_RejectsSamplesAboveTheInspectionLimit()
    {
        var bytes = new byte[Fallout2PrototypeSampleVerifier.MaximumSampleFileSizeBytes + 1];

        var result = Fallout2PrototypeSampleVerifier.Verify("large.PRO", bytes);

        Assert.Equal(Fallout2PrototypeSampleStructureStatus.Rejected, result.StructureStatus);
        Assert.Null(result.Sha256);
        Assert.Null(result.EmbeddedPid);
        Assert.Contains(result.ValidationMessages, message => message.Contains("inspection limit"));
    }

    private static byte[] CreateSample(
        int length = 0x28,
        uint pid = 0x00000002,
        int subtype = 0)
    {
        var bytes = new byte[length];
        if (length >= sizeof(uint))
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0, sizeof(uint)), pid);
        }

        if (length >= 0x24)
        {
            BinaryPrimitives.WriteInt32BigEndian(
                bytes.AsSpan(0x20, sizeof(int)),
                subtype);
        }

        return bytes;
    }
}
