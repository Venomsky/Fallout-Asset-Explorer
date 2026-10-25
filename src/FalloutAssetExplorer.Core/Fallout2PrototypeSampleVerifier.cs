using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

namespace FalloutAssetExplorer.Core;

public enum Fallout2PrototypeSampleStructureStatus
{
    PlausibleHeader,
    Incomplete,
    Unsupported,
    Rejected
}

public enum Fallout2PrototypeSampleIdentityComparison
{
    NotProvided,
    InvalidExpectedPid,
    UnableToCompare,
    MatchesReportedExpectedPid,
    DiffersFromReportedExpectedPid
}

public enum Fallout2PrototypeSampleMetadataStatus
{
    Verified,
    Reported,
    Unknown
}

public sealed record Fallout2PrototypeSampleProvenance(
    string? SourceReference = null,
    string? GameEditionVersion = null,
    string? RecordIdentity = null,
    string? AcquisitionMethod = null,
    string? ExpectedPidHex = null,
    string? ExpectedPidReference = null);

public sealed record Fallout2PrototypeSampleMetadataObservation(
    string Name,
    string? Value,
    Fallout2PrototypeSampleMetadataStatus Status);

public sealed record Fallout2PrototypeMaterialObservation(
    string RawBytesHex,
    uint BigEndianNumericValue);

public sealed record Fallout2PrototypeSampleVerification(
    string SelectedFileName,
    int FileLengthBytes,
    string? Sha256,
    Fallout2PrototypeSampleStructureStatus StructureStatus,
    string StructureNote,
    IReadOnlyList<string> ValidationMessages,
    string? RawPidBytesHex,
    uint? EmbeddedPid,
    int? PidType,
    uint? PrototypeIndex,
    int? Subtype,
    string? ExpectedPidHex,
    Fallout2PrototypeSampleIdentityComparison IdentityComparison,
    Fallout2PrototypeMaterialObservation? MaterialCandidate,
    IReadOnlyList<Fallout2PrototypeSampleMetadataObservation> Provenance);

public static class Fallout2PrototypeSampleVerifier
{
    public const int MaximumSampleFileSizeBytes = Fallout2PrototypeResolver.MaximumPrototypeSizeBytes;

    private const int PidFieldSizeBytes = sizeof(uint);
    private const int SubtypeOffset = 0x20;
    private const int SubtypeFieldEnd = SubtypeOffset + sizeof(int);
    private const int MaterialOffset = 0x24;
    private const int MaterialFieldEnd = MaterialOffset + sizeof(uint);

    public static Fallout2PrototypeSampleVerification Verify(
        string selectedFileName,
        ReadOnlySpan<byte> fileBytes,
        Fallout2PrototypeSampleProvenance? provenance = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedFileName);
        provenance ??= new Fallout2PrototypeSampleProvenance();

        var messages = new List<string>();
        var metadata = CreateMetadataObservations(provenance);
        var expectedPidHex = NormalizeExpectedPid(provenance.ExpectedPidHex);
        var identityComparison = CompareExpectedPid(
            provenance.ExpectedPidHex,
            embeddedPid: null);
        if (identityComparison == Fallout2PrototypeSampleIdentityComparison.InvalidExpectedPid)
        {
            messages.Add("The reported expected PID is not a valid 32-bit hexadecimal value.");
        }

        var sha256 = fileBytes.Length <= MaximumSampleFileSizeBytes
            ? Convert.ToHexString(SHA256.HashData(fileBytes))
            : null;
        var structureStatus = Fallout2PrototypeSampleStructureStatus.PlausibleHeader;
        string structureNote;

        if (fileBytes.Length > MaximumSampleFileSizeBytes)
        {
            structureStatus = Fallout2PrototypeSampleStructureStatus.Rejected;
            structureNote =
                $"The file exceeds the {MaximumSampleFileSizeBytes:N0}-byte inspection limit; no bytes were interpreted and no SHA-256 was calculated.";
            messages.Add(structureNote);
            return CreateResult(
                selectedFileName,
                fileBytes.Length,
                sha256,
                structureStatus,
                structureNote,
                messages,
                null,
                null,
                null,
                null,
                null,
                metadata,
                expectedPidHex,
                identityComparison);
        }

        if (!string.Equals(
                Path.GetExtension(selectedFileName),
                ".PRO",
                StringComparison.OrdinalIgnoreCase))
        {
            structureStatus = Fallout2PrototypeSampleStructureStatus.Rejected;
            structureNote = "Only a selected standalone .PRO file is accepted; the filename is not used as record identity.";
            messages.Add(structureNote);
            return CreateResult(
                selectedFileName,
                fileBytes.Length,
                sha256,
                structureStatus,
                structureNote,
                messages,
                null,
                null,
                null,
                null,
                null,
                metadata,
                expectedPidHex,
                identityComparison);
        }

        if (fileBytes.Length < PidFieldSizeBytes)
        {
            structureStatus = Fallout2PrototypeSampleStructureStatus.Incomplete;
            structureNote = "The file is shorter than the 4-byte PID field; no PID can be decoded.";
            messages.Add(structureNote);
            return CreateResult(
                selectedFileName,
                fileBytes.Length,
                sha256,
                structureStatus,
                structureNote,
                messages,
                null,
                null,
                null,
                null,
                null,
                metadata,
                expectedPidHex,
                identityComparison);
        }

        var rawPidBytesHex = Convert.ToHexString(fileBytes[..PidFieldSizeBytes]);
        var embeddedPid = BinaryPrimitives.ReadUInt32BigEndian(fileBytes[..PidFieldSizeBytes]);
        var pidType = (int)(embeddedPid >> 24);
        var prototypeIndex = embeddedPid & 0x00FF_FFFF;
        int? subtype = null;

        if (pidType is not (0 or 2))
        {
            structureStatus = Fallout2PrototypeSampleStructureStatus.Unsupported;
            structureNote =
                $"PID bytes are present and decoded using the current FAE convention, but PID type {pidType} is unsupported for structural checks.";
            messages.Add(structureNote);
        }
        else if (prototypeIndex == 0)
        {
            structureStatus = Fallout2PrototypeSampleStructureStatus.Unsupported;
            structureNote =
                "PID type is recognized, but prototype list index zero is invalid under the current FAE resolver convention.";
            messages.Add(structureNote);
        }
        else if (fileBytes.Length < SubtypeFieldEnd)
        {
            structureStatus = Fallout2PrototypeSampleStructureStatus.Incomplete;
            structureNote = "PID type is recognized, but the complete 4-byte subtype field at 0x20 is unavailable.";
            messages.Add(structureNote);
        }
        else
        {
            subtype = BinaryPrimitives.ReadInt32BigEndian(
                fileBytes.Slice(SubtypeOffset, sizeof(int)));
            var maximumSubtype = pidType == 0 ? 6 : 5;
            if (subtype is < 0 || subtype > maximumSubtype)
            {
                structureStatus = Fallout2PrototypeSampleStructureStatus.Unsupported;
                structureNote =
                    $"PID type {pidType} is recognized, but subtype {subtype} is outside the current FAE-supported range.";
                messages.Add(structureNote);
            }
            else
            {
                structureNote =
                    $"PID type and subtype are plausible under current FAE parsing conventions. Total subtype-specific length and release compatibility are not validated.";
            }
        }

        identityComparison = CompareExpectedPid(
            provenance.ExpectedPidHex,
            embeddedPid);
        if (identityComparison == Fallout2PrototypeSampleIdentityComparison.DiffersFromReportedExpectedPid)
        {
            messages.Add("The embedded PID differs from the user-reported expected PID.");
        }

        var materialCandidate = fileBytes.Length >= MaterialFieldEnd
            ? new Fallout2PrototypeMaterialObservation(
                Convert.ToHexString(fileBytes.Slice(MaterialOffset, sizeof(uint))),
                BinaryPrimitives.ReadUInt32BigEndian(
                    fileBytes.Slice(MaterialOffset, sizeof(uint))))
            : null;
        if (materialCandidate is null)
        {
            messages.Add(
                $"The file has fewer than {MaterialFieldEnd} bytes; the four-byte candidate at offset 0x{MaterialOffset:X} is unavailable.");
        }

        return CreateResult(
            selectedFileName,
            fileBytes.Length,
            sha256,
            structureStatus,
            structureNote,
            messages,
            rawPidBytesHex,
            embeddedPid,
            pidType,
            prototypeIndex,
            subtype,
            metadata,
            expectedPidHex,
            identityComparison,
            materialCandidate);
    }

    private static Fallout2PrototypeSampleVerification CreateResult(
        string selectedFileName,
        int fileLengthBytes,
        string? sha256,
        Fallout2PrototypeSampleStructureStatus structureStatus,
        string structureNote,
        IReadOnlyList<string> validationMessages,
        string? rawPidBytesHex,
        uint? embeddedPid,
        int? pidType,
        uint? prototypeIndex,
        int? subtype,
        IReadOnlyList<Fallout2PrototypeSampleMetadataObservation> metadata,
        string? expectedPidHex = null,
        Fallout2PrototypeSampleIdentityComparison identityComparison =
            Fallout2PrototypeSampleIdentityComparison.NotProvided,
        Fallout2PrototypeMaterialObservation? materialCandidate = null) =>
        new(
            Path.GetFileName(selectedFileName),
            fileLengthBytes,
            sha256,
            structureStatus,
            structureNote,
            Array.AsReadOnly(validationMessages.ToArray()),
            rawPidBytesHex,
            embeddedPid,
            pidType,
            prototypeIndex,
            subtype,
            expectedPidHex,
            identityComparison,
            materialCandidate,
            Array.AsReadOnly(metadata.ToArray()));

    private static IReadOnlyList<Fallout2PrototypeSampleMetadataObservation> CreateMetadataObservations(
        Fallout2PrototypeSampleProvenance provenance) =>
        Array.AsReadOnly(
        [
            CreateMetadataObservation("Source URL/repository and immutable revision", provenance.SourceReference),
            CreateMetadataObservation("Game edition/version", provenance.GameEditionVersion),
            CreateMetadataObservation("Reported original record path/identifier", provenance.RecordIdentity),
            CreateMetadataObservation("Acquisition/extraction method", provenance.AcquisitionMethod),
            CreateMetadataObservation("Reported expected PID", provenance.ExpectedPidHex),
            CreateMetadataObservation("Expected PID reference", provenance.ExpectedPidReference)
        ]);

    private static Fallout2PrototypeSampleMetadataObservation CreateMetadataObservation(
        string name,
        string? value)
    {
        var normalizedValue = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return new Fallout2PrototypeSampleMetadataObservation(
            name,
            normalizedValue,
            normalizedValue is null
                ? Fallout2PrototypeSampleMetadataStatus.Unknown
                : Fallout2PrototypeSampleMetadataStatus.Reported);
    }

    private static string? NormalizeExpectedPid(string? expectedPidHex)
    {
        if (!TryParseExpectedPid(expectedPidHex, out var expectedPid))
        {
            return null;
        }

        return $"0x{expectedPid:X8}";
    }

    private static Fallout2PrototypeSampleIdentityComparison CompareExpectedPid(
        string? expectedPidHex,
        uint? embeddedPid)
    {
        if (string.IsNullOrWhiteSpace(expectedPidHex))
        {
            return Fallout2PrototypeSampleIdentityComparison.NotProvided;
        }

        if (!TryParseExpectedPid(expectedPidHex, out var expectedPid))
        {
            return Fallout2PrototypeSampleIdentityComparison.InvalidExpectedPid;
        }

        if (embeddedPid is null)
        {
            return Fallout2PrototypeSampleIdentityComparison.UnableToCompare;
        }

        return expectedPid == embeddedPid.Value
            ? Fallout2PrototypeSampleIdentityComparison.MatchesReportedExpectedPid
            : Fallout2PrototypeSampleIdentityComparison.DiffersFromReportedExpectedPid;
    }

    private static bool TryParseExpectedPid(string? value, out uint expectedPid)
    {
        expectedPid = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[2..];
        }

        return normalized.Length is > 0 and <= 8
            && uint.TryParse(
                normalized,
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out expectedPid);
    }
}
