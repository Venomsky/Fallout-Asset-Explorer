using FalloutAssetExplorer.Core;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class Fallout2MapArchiveDiagnosticTests(ITestOutputHelper output)
{
    private const int MaximumReportedStopReasons = 10;
    private const int HiddenObjectFlag = 0x00000001;
    private const int FlatObjectFlag = 0x00000008;

    [Fallout2MasterDatFact]
    public async Task DecodeKnownMapsFromFallout2MasterArchive()
    {
        var archivePath = Environment.GetEnvironmentVariable("FALLOUT2_MASTER_DAT");
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            throw new InvalidOperationException(
                "FALLOUT2_MASTER_DAT became unavailable after test discovery.");
        }

        var archive = Dat2Archive.Open(archivePath);
        foreach (var mapPath in new[] { "maps/arbridge.map", "maps/arcaves.map" })
        {
            var entry = archive.BrowserEntries.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Path.Replace('\\', '/'),
                    mapPath,
                    StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                throw new XunitException(
                    $"Archive '{archive.Path}' does not contain required MAP entry '{mapPath}'.");
            }

            using var mapStream = new MemoryStream();
            await archive.CopyEntryToAsync(entry, mapStream);
            var mapBytes = mapStream.ToArray();
            var header = MapHeaderDecoder.Decode(mapBytes, FalloutGame.Fallout2);
            var decoded = await MapObjectPlacementDecoder.DecodeAsync(mapBytes, header, archive);
            var stoppingRecord = decoded.Objects.LastOrDefault(
                placement => placement.UnsupportedReason is not null);

            output.WriteLine($"Archive: {archive.Path}");
            output.WriteLine($"MAP entry: {entry.Path}");
            output.WriteLine($"Declared records: {decoded.TotalObjectCount}");
            output.WriteLine($"Decoded records: {decoded.Objects.Count}");
            output.WriteLine(
                stoppingRecord is null
                    ? "First unsupported record: none"
                    : $"First unsupported record: index {stoppingRecord.RecordIndex}, "
                        + $"PID 0x{stoppingRecord.RawPid:X8}, type {stoppingRecord.ObjectTypeDisplay}");
            output.WriteLine($"Stop reason: {decoded.UnsupportedReason ?? "none"}");
            output.WriteLine($"Unparsed byte count: {decoded.UnparsedByteCount}");
        }
    }

    [Fallout2MasterDatFact]
    public async Task AuditAllMapsInFallout2MasterArchive()
    {
        var archivePath = Environment.GetEnvironmentVariable("FALLOUT2_MASTER_DAT");
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            throw new InvalidOperationException(
                "FALLOUT2_MASTER_DAT became unavailable after test discovery.");
        }

        var archive = Dat2Archive.Open(archivePath);
        var mapEntries = archive.BrowserEntries
            .Where(entry => string.Equals(
                Path.GetExtension(entry.Path),
                ".map",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var results = new List<MapAuditResult>();
        var excludedEntryCount = 0;
        long decodedObjectCount = 0;
        long hiddenObjectCount = 0;
        long flatObjectCount = 0;
        var flagCountsByPidType =
            new Dictionary<int, (string TypeName, long ObjectCount, long HiddenCount, long FlatCount)>();

        foreach (var entry in mapEntries)
        {
            var normalizedPath = entry.Path.Replace('\\', '/');
            if (!IsValidArchivePath(normalizedPath))
            {
                excludedEntryCount++;
                results.Add(MapAuditResult.Excluded(normalizedPath, "entry path is not a safe relative archive path"));
                continue;
            }

            if (entry.UncompressedSize < 0)
            {
                results.Add(MapAuditResult.ArchiveReadFailure(
                    normalizedPath,
                    $"invalid uncompressed size {entry.UncompressedSize}"));
                continue;
            }

            using var mapStream = new MemoryStream();
            try
            {
                await archive.CopyEntryToAsync(entry, mapStream);
            }
            catch (Exception exception) when (IsRecoverableEntryException(exception))
            {
                results.Add(MapAuditResult.ArchiveReadFailure(
                    normalizedPath,
                    $"{exception.GetType().Name}: {exception.Message}"));
                continue;
            }

            if (!mapStream.TryGetBuffer(out var buffer))
            {
                results.Add(MapAuditResult.ArchiveReadFailure(
                    normalizedPath,
                    "the in-memory MAP buffer could not be accessed without copying"));
                continue;
            }

            var mapBytes = new ReadOnlyMemory<byte>(
                buffer.Array!,
                buffer.Offset,
                checked((int)mapStream.Length));
            MapHeaderMetadata header;
            try
            {
                header = MapHeaderDecoder.Decode(mapBytes, FalloutGame.Fallout2);
            }
            catch (Exception exception) when (IsRecoverableEntryException(exception))
            {
                results.Add(MapAuditResult.HeaderRejected(
                    normalizedPath,
                    $"{exception.GetType().Name}: {exception.Message}"));
                continue;
            }

            try
            {
                var decoded = await MapObjectPlacementDecoder.DecodeAsync(mapBytes, header, archive);
                WriteObjectDiagnostics(normalizedPath, decoded.Objects);
                decodedObjectCount += decoded.Objects.Count;
                hiddenObjectCount += decoded.Objects.LongCount(
                    placement => (placement.RawObjectFlags & HiddenObjectFlag) != 0);
                flatObjectCount += decoded.Objects.LongCount(
                    placement => (placement.RawObjectFlags & FlatObjectFlag) != 0);
                foreach (var typeGroup in decoded.Objects.GroupBy(placement => placement.PidTypeIdentifier))
                {
                    var typeName = typeGroup.First().ObjectTypeDisplay;
                    var current = flagCountsByPidType.GetValueOrDefault(
                        typeGroup.Key,
                        (typeName, 0, 0, 0));
                    flagCountsByPidType[typeGroup.Key] = (
                        typeName,
                        current.ObjectCount + typeGroup.LongCount(),
                        current.HiddenCount + typeGroup.LongCount(
                            placement => (placement.RawObjectFlags & HiddenObjectFlag) != 0),
                        current.FlatCount + typeGroup.LongCount(
                            placement => (placement.RawObjectFlags & FlatObjectFlag) != 0));
                }

                var stoppingRecord = decoded.Objects.FirstOrDefault(
                    placement => placement.UnsupportedReason is not null);
                results.Add(MapAuditResult.Decoded(
                    normalizedPath,
                    header.Version,
                    decoded.TotalObjectCount,
                    decoded.DecodedTopLevelObjectCount,
                    decoded.Objects.Count,
                    decoded.IsComplete,
                    decoded.UnsupportedReason,
                    decoded.UnparsedByteCount,
                    stoppingRecord));
            }
            catch (Exception exception) when (IsRecoverableEntryException(exception))
            {
                results.Add(MapAuditResult.DecodeFailure(
                    normalizedPath,
                    header.Version,
                    $"{exception.GetType().Name}: {exception.Message}"));
            }
        }

        foreach (var result in results)
        {
            WriteMapAuditResult(result);
        }

        var headerRejectedCount = results.Count(result => result.Outcome == MapAuditOutcome.HeaderRejected);
        var archiveReadFailureCount = results.Count(result => result.Outcome == MapAuditOutcome.ArchiveReadFailure);
        var completeCount = results.Count(result => result.Outcome == MapAuditOutcome.Decoded && result.IsComplete == true);
        var incompleteCount = results.Count(result => result.Outcome == MapAuditOutcome.Decoded && result.IsComplete == false);
        var decodeFailureCount = results.Count(result => result.Outcome == MapAuditOutcome.DecodeFailure);

        output.WriteLine("MAP audit summary");
        output.WriteLine($"Archive: {archive.Path}");
        output.WriteLine($"MAP entries matching the extension: {mapEntries.Length}");
        output.WriteLine($"Excluded entries with invalid paths: {excludedEntryCount}");
        output.WriteLine($"Candidates: {mapEntries.Length - excludedEntryCount}");
        output.WriteLine($"Rejected headers: {headerRejectedCount}");
        output.WriteLine($"Archive read failures: {archiveReadFailureCount}");
        output.WriteLine($"Complete decodings: {completeCount}");
        output.WriteLine($"Incomplete decodings: {incompleteCount}");
        output.WriteLine($"Structural decoder failures: {decodeFailureCount}");
        output.WriteLine("Most frequent stop reasons:");

        var commonReasons = results
            .Where(result => result.Outcome == MapAuditOutcome.Decoded
                && result.IsComplete == false
                && !string.IsNullOrWhiteSpace(result.UnsupportedReason))
            .GroupBy(result => result.UnsupportedReason!, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Take(MaximumReportedStopReasons)
            .ToArray();
        if (commonReasons.Length == 0)
        {
            output.WriteLine("  none");
        }
        else
        {
            foreach (var reason in commonReasons)
            {
                output.WriteLine($"  {reason.Count()}x {reason.Key}");
            }
        }

        output.WriteLine(
            "The DAT2 copy API rejects entries larger than 512 MiB; MAP decoding requires the current entry in memory.");

        var decodedMapCount = results.Count(result => result.Outcome == MapAuditOutcome.Decoded);
        output.WriteLine("Global object flag summary");
        output.WriteLine($"MAP entries found: {mapEntries.Length}");
        output.WriteLine(
            $"MAPs successfully decoded (complete + incomplete): {FormatRate(decodedMapCount, mapEntries.Length)}");
        output.WriteLine(
            $"Complete MAP decodings: {FormatRate(completeCount, mapEntries.Length)}");
        output.WriteLine(
            $"Incomplete MAP decodings: {FormatRate(incompleteCount, mapEntries.Length)}");
        output.WriteLine(
            $"MAPs not successfully decoded: {FormatRate(mapEntries.Length - decodedMapCount, mapEntries.Length)}");
        output.WriteLine($"Successfully decoded objects (including partial MAPs): {decodedObjectCount}");
        output.WriteLine(
            $"OBJECT_HIDDEN (0x{HiddenObjectFlag:X2}): {FormatRate(hiddenObjectCount, decodedObjectCount)}");
        output.WriteLine(
            $"OBJECT_FLAT (0x{FlatObjectFlag:X2}): {FormatRate(flatObjectCount, decodedObjectCount)}");
        output.WriteLine("OBJECT_HIDDEN/OBJECT_FLAT by PID type (object count and flag rates):");
        if (flagCountsByPidType.Count == 0)
        {
            output.WriteLine("  none (no decoded objects)");
        }
        else
        {
            foreach (var type in flagCountsByPidType.OrderBy(pair => pair.Key))
            {
                output.WriteLine(
                    $"  {type.Value.TypeName} (PID type {type.Key}), objects {type.Value.ObjectCount}: "
                    + $"OBJECT_HIDDEN {FormatRate(type.Value.HiddenCount, type.Value.ObjectCount)}, "
                    + $"OBJECT_FLAT {FormatRate(type.Value.FlatCount, type.Value.ObjectCount)}");
            }
        }
    }

    private void WriteObjectDiagnostics(
        string mapPath,
        IReadOnlyList<MapObjectPlacement> placements)
    {
        var objectCount = placements.Count;
        var elevationPairs = placements
            .GroupBy(placement => (placement.ElevationGroup, placement.StoredElevation))
            .OrderBy(group => group.Key.ElevationGroup)
            .ThenBy(group => group.Key.StoredElevation)
            .ToArray();
        var nonzeroOffsetCount = placements.Count(
            placement => placement.RawOffsetX != 0 || placement.RawOffsetY != 0);
        var hiddenCount = placements.Count(
            placement => (placement.RawObjectFlags & HiddenObjectFlag) != 0);
        var flatCount = placements.Count(
            placement => (placement.RawObjectFlags & FlatObjectFlag) != 0);
        var unassignedTileCount = placements.Count(placement => placement.RawHexIndex == -1);
        var outOfRangeTileIndices = placements
            .Where(placement => placement.RawHexIndex != -1
                && (uint)placement.RawHexIndex >= MapObjectPlacementDecoder.HexGridTileCount)
            .GroupBy(placement => placement.RawHexIndex)
            .OrderBy(group => group.Key)
            .ToArray();
        var outOfRangeTileCount = outOfRangeTileIndices.Sum(group => group.Count());

        output.WriteLine($"Object diagnostics: {mapPath}");
        output.WriteLine(
            $"Decoded object records: {objectCount} (denominator for record statistics; includes inventory records)");
        output.WriteLine(
            $"ElevationGroup/StoredElevation mismatches: "
            + $"{FormatRate(placements.Count(placement => placement.ElevationGroup != placement.StoredElevation), objectCount)}");
        output.WriteLine("ElevationGroup/StoredElevation pairs (count / all decoded records):");
        if (elevationPairs.Length == 0)
        {
            output.WriteLine("  none");
        }
        else
        {
            foreach (var pair in elevationPairs)
            {
                output.WriteLine(
                    $"  {pair.Key.ElevationGroup}/{pair.Key.StoredElevation}: "
                    + $"{FormatRate(pair.Count(), objectCount)}");
            }
        }

        WriteDistribution("RawOffsetX", placements.Select(placement => placement.RawOffsetX), objectCount);
        WriteDistribution("RawOffsetY", placements.Select(placement => placement.RawOffsetY), objectCount);
        output.WriteLine(
            $"Objects with either nonzero raw offset: {FormatRate(nonzeroOffsetCount, objectCount)}");
        output.WriteLine(
            $"OBJECT_HIDDEN (RawObjectFlags & 0x{HiddenObjectFlag:X8}): {FormatRate(hiddenCount, objectCount)}");
        output.WriteLine(
            $"OBJECT_FLAT (RawObjectFlags & 0x{FlatObjectFlag:X8}): {FormatRate(flatCount, objectCount)}");
        output.WriteLine("OBJECT_HIDDEN/OBJECT_FLAT by PID type (count / records of that type):");
        foreach (var typeGroup in placements
            .GroupBy(placement => placement.PidTypeIdentifier)
            .OrderBy(group => group.Key))
        {
            var typeName = typeGroup.First().ObjectTypeDisplay;
            var typeCount = typeGroup.Count();
            var typeHiddenCount = typeGroup.Count(
                placement => (placement.RawObjectFlags & HiddenObjectFlag) != 0);
            var typeFlatCount = typeGroup.Count(
                placement => (placement.RawObjectFlags & FlatObjectFlag) != 0);
            output.WriteLine(
                $"  {typeName} (PID type {typeGroup.Key}): "
                + $"OBJECT_HIDDEN {FormatRate(typeHiddenCount, typeCount)}, "
                + $"OBJECT_FLAT {FormatRate(typeFlatCount, typeCount)}");
        }

        output.WriteLine(
            $"RawHexIndex == -1 (no tile assigned): {FormatRate(unassignedTileCount, objectCount)}");
        output.WriteLine(
            $"Out-of-range RawHexIndex values (excluding -1): {FormatRate(outOfRangeTileCount, objectCount)}");
        foreach (var invalidIndex in outOfRangeTileIndices)
        {
            output.WriteLine($"  RawHexIndex {invalidIndex.Key}: {invalidIndex.Count()}");
        }
    }

    private void WriteDistribution(string fieldName, IEnumerable<int> values, int denominator)
    {
        output.WriteLine($"{fieldName} distribution (count / all decoded records):");
        var distribution = values
            .GroupBy(value => value)
            .OrderBy(group => group.Key)
            .ToArray();
        if (distribution.Length == 0)
        {
            output.WriteLine("  none");
            return;
        }

        foreach (var value in distribution)
        {
            output.WriteLine($"  {value.Key}: {FormatRate(value.Count(), denominator)}");
        }
    }

    private static string FormatRate(long count, long denominator)
    {
        var percentage = denominator == 0 ? 0 : (double)count * 100 / denominator;
        return $"{count}/{denominator} ({percentage:F2}%)";
    }

    private void WriteMapAuditResult(MapAuditResult result)
    {
        output.WriteLine($"MAP: {result.Path}");
        output.WriteLine($"Outcome: {result.Outcome}");
        if (result.Outcome == MapAuditOutcome.Excluded)
        {
            output.WriteLine($"Exclusion reason: {result.Error}");
            return;
        }

        if (result.Outcome == MapAuditOutcome.ArchiveReadFailure
            || result.Outcome == MapAuditOutcome.HeaderRejected
            || result.Outcome == MapAuditOutcome.DecodeFailure)
        {
            output.WriteLine($"Reason: {result.Error}");
            if (result.Version is int failedVersion)
            {
                output.WriteLine($"Version: {failedVersion}");
            }

            return;
        }

        output.WriteLine($"Version: {result.Version}");
        output.WriteLine($"Declared top-level objects: {result.DeclaredTopLevelCount}");
        output.WriteLine($"Decoded top-level objects: {result.DecodedTopLevelCount}");
        output.WriteLine($"Decoded objects total: {result.DecodedObjectCount}");
        output.WriteLine($"IsComplete: {result.IsComplete}");
        output.WriteLine($"UnsupportedReason: {result.UnsupportedReason ?? "none"}");
        output.WriteLine($"UnparsedByteCount: {result.UnparsedByteCount}");
        if (result.StoppingRecord is { } record)
        {
            output.WriteLine(
                $"Stopping record: index {record.RecordIndex}, PID 0x{record.RawPid:X8}, "
                + $"type {record.ObjectTypeDisplay}, elevation {record.ElevationGroup}, "
                + $"parent {record.ParentRecordIndex?.ToString() ?? "none"}, "
                + $"depth {record.InventoryDepth}, quantity {record.InventoryQuantity?.ToString() ?? "none"}");
        }
        else if (result.IsComplete == false)
        {
            output.WriteLine("Stopping record: not available from the decoded records.");
        }
    }

    private static bool IsValidArchivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path.StartsWith("/", StringComparison.Ordinal)
            || path.Contains(':'))
        {
            return false;
        }

        return path.Split('/').All(segment => segment.Length > 0 && segment is not "." and not "..");
    }

    private static bool IsRecoverableEntryException(Exception exception) =>
        exception is not OutOfMemoryException
            and not StackOverflowException
            and not AccessViolationException;

    private enum MapAuditOutcome
    {
        Excluded,
        ArchiveReadFailure,
        HeaderRejected,
        Decoded,
        DecodeFailure
    }

    private sealed record MapAuditResult(
        string Path,
        MapAuditOutcome Outcome,
        int? Version,
        int? DeclaredTopLevelCount,
        int? DecodedTopLevelCount,
        int? DecodedObjectCount,
        bool? IsComplete,
        string? UnsupportedReason,
        int? UnparsedByteCount,
        string? Error,
        MapObjectPlacement? StoppingRecord)
    {
        public static MapAuditResult Excluded(string path, string reason) =>
            new(path, MapAuditOutcome.Excluded, null, null, null, null, null, null, null, reason, null);

        public static MapAuditResult ArchiveReadFailure(string path, string reason) =>
            new(path, MapAuditOutcome.ArchiveReadFailure, null, null, null, null, null, null, null, reason, null);

        public static MapAuditResult HeaderRejected(string path, string reason) =>
            new(path, MapAuditOutcome.HeaderRejected, null, null, null, null, null, null, null, reason, null);

        public static MapAuditResult Decoded(
            string path,
            int version,
            int declaredTopLevelCount,
            int decodedTopLevelCount,
            int decodedObjectCount,
            bool isComplete,
            string? unsupportedReason,
            int unparsedByteCount,
            MapObjectPlacement? stoppingRecord) =>
            new(
                path,
                MapAuditOutcome.Decoded,
                version,
                declaredTopLevelCount,
                decodedTopLevelCount,
                decodedObjectCount,
                isComplete,
                unsupportedReason,
                unparsedByteCount,
                null,
                stoppingRecord);

        public static MapAuditResult DecodeFailure(string path, int version, string reason) =>
            new(path, MapAuditOutcome.DecodeFailure, version, null, null, null, null, null, null, reason, null);
    }

    public sealed class Fallout2MasterDatFactAttribute : FactAttribute
    {
        public Fallout2MasterDatFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FALLOUT2_MASTER_DAT")))
            {
                Skip = "Set FALLOUT2_MASTER_DAT to a Fallout 2 master.dat path to run real-MAP diagnostics.";
            }
        }
    }
}
