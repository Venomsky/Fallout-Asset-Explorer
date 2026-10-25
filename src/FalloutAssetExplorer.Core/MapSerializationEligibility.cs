using System.Buffers.Binary;

namespace FalloutAssetExplorer.Core;

public enum MapSerializationEligibilityStatus
{
    Eligible,
    SourceIdentityMismatch,
    UnsupportedGameVersion,
    ParsingIncompleteOrFailed,
    UnconsumedTrailingBytes,
    UnsupportedObjectPayloadLayout
}

public sealed record MapSerializationEligibilityResult(
    MapSerializationEligibilityStatus Status,
    string Message)
{
    public bool IsEligible => Status == MapSerializationEligibilityStatus.Eligible;
}

public static class MapSerializationEligibility
{
    public static MapSerializationEligibilityResult Validate(
        MapSourceData source,
        FalloutGame game,
        MapHeaderMetadata? header,
        MapTileGridData? tileGrids,
        MapObjectPlacementData? objectPlacements,
        MapEditSession? editSession)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.Length < sizeof(int))
        {
            return Ineligible(
                MapSerializationEligibilityStatus.ParsingIncompleteOrFailed,
                "The MAP source is too short to contain a version field.");
        }

        var sourceVersion = BinaryPrimitives.ReadInt32BigEndian(source.Memory.Span);
        if (!MapHeaderDecoder.IsSupportedGameVersion(game, sourceVersion))
        {
            return Ineligible(
                MapSerializationEligibilityStatus.UnsupportedGameVersion,
                $"Fallout game/version {game}/{sourceVersion} is not supported.");
        }

        if (header is null)
        {
            return Ineligible(
                MapSerializationEligibilityStatus.ParsingIncompleteOrFailed,
                "MAP header parsing did not produce a result.");
        }

        if (header.SourceData is not { } headerSource
            || !ReferenceEquals(headerSource, source)
            || header.Provenance is not { } provenance
            || provenance.SourceSnapshotId != source.SnapshotId)
        {
            return Ineligible(
                MapSerializationEligibilityStatus.SourceIdentityMismatch,
                "The MAP header is not bound to this exact source snapshot.");
        }

        if (header.Game != game
            || header.Version != sourceVersion
            || provenance.Game != game
            || provenance.Version != sourceVersion)
        {
            return Ineligible(
                MapSerializationEligibilityStatus.UnsupportedGameVersion,
                "The MAP header and decode provenance do not match the source game/version.");
        }

        MapHeaderMetadata sourceHeader;
        try
        {
            sourceHeader = MapHeaderDecoder.Decode(source.Memory, game);
        }
        catch (InvalidDataException exception)
        {
            return Ineligible(
                MapSerializationEligibilityStatus.ParsingIncompleteOrFailed,
                $"The source MAP header could not be validated: {exception.Message}");
        }

        if (!HeadersMatch(header, sourceHeader))
        {
            return Ineligible(
                MapSerializationEligibilityStatus.ParsingIncompleteOrFailed,
                "The decoded MAP header metadata does not match the source bytes.");
        }

        if (tileGrids is null || objectPlacements is null)
        {
            return Ineligible(
                MapSerializationEligibilityStatus.ParsingIncompleteOrFailed,
                "MAP tile-grid or object parsing did not produce a result.");
        }

        if (tileGrids.Provenance is not { } gridProvenance
            || !ReferenceEquals(gridProvenance, provenance)
            || tileGrids.Header.Provenance is not { } gridHeaderProvenance
            || !ReferenceEquals(gridHeaderProvenance, provenance)
            || !ReferenceEquals(tileGrids.Header.SourceData, source)
            || objectPlacements.Provenance is not { } objectProvenance
            || !ReferenceEquals(objectProvenance, provenance)
            || !ReferenceEquals(objectPlacements.SourceData, source)
            || editSession is null
            || !ReferenceEquals(editSession.SourceData, source)
            || !ReferenceEquals(editSession.Provenance, provenance)
            || !ReferenceEquals(editSession.DecodedObjectPlacements, objectPlacements))
        {
            return Ineligible(
                MapSerializationEligibilityStatus.SourceIdentityMismatch,
                "The decoded MAP data and edit session do not share the same source-bound decode.");
        }

        if (tileGrids.Header.Game != game
            || tileGrids.Header.Version != sourceVersion
            || objectProvenance.Game != game
            || objectProvenance.Version != sourceVersion)
        {
            return Ineligible(
                MapSerializationEligibilityStatus.UnsupportedGameVersion,
                "The decoded MAP sections do not match the source game/version.");
        }

        if (objectPlacements.HasUnsupportedObjectPayload)
        {
            return Ineligible(
                MapSerializationEligibilityStatus.UnsupportedObjectPayloadLayout,
                objectPlacements.UnsupportedReason
                    ?? "The MAP contains an unsupported object payload layout.");
        }

        if (!objectPlacements.IsComplete
            || objectPlacements.UnsupportedReason is not null
            || header.EntryLength != source.Length
            || !HeadersMatch(tileGrids.Header, sourceHeader)
            || tileGrids.Grids.Count != header.PresentElevationCount
            || !tileGrids.Grids.Select(grid => grid.ElevationIndex).SequenceEqual(
                Enumerable.Range(0, MapHeaderDecoder.ElevationCount)
                    .Where(elevation => (header.Flags & (1 << (elevation + 1))) == 0))
            || tileGrids.Grids.Any(grid => grid.RawValues.Length != MapTileGridDecoder.ValuesPerGrid))
        {
            return Ineligible(
                MapSerializationEligibilityStatus.ParsingIncompleteOrFailed,
                objectPlacements.UnsupportedReason
                    ?? "The decoded MAP sections are incomplete or inconsistent with the source.");
        }

        if (objectPlacements.UnparsedByteCount != 0)
        {
            return Ineligible(
                MapSerializationEligibilityStatus.UnconsumedTrailingBytes,
                $"The MAP object parser left {objectPlacements.UnparsedByteCount} bytes unconsumed.");
        }

        return new MapSerializationEligibilityResult(
            MapSerializationEligibilityStatus.Eligible,
            "The source-bound MAP decode is complete for the supported game/version.");
    }

    private static MapSerializationEligibilityResult Ineligible(
        MapSerializationEligibilityStatus status,
        string message) =>
        new(status, message);

    private static bool HeadersMatch(
        MapHeaderMetadata actual,
        MapHeaderMetadata expected) =>
        actual.Game == expected.Game
        && actual.Version == expected.Version
        && actual.EntryLength == expected.EntryLength
        && actual.RawName is not null
        && expected.RawName is not null
        && actual.RawName.AsSpan().SequenceEqual(expected.RawName)
        && actual.EnteringTile == expected.EnteringTile
        && actual.EnteringElevation == expected.EnteringElevation
        && actual.EnteringRotation == expected.EnteringRotation
        && actual.LocalVariableCount == expected.LocalVariableCount
        && actual.ScriptIndex == expected.ScriptIndex
        && actual.Flags == expected.Flags
        && actual.Darkness == expected.Darkness
        && actual.GlobalVariableCount == expected.GlobalVariableCount
        && actual.MapIndex == expected.MapIndex
        && actual.LastVisitTimeRaw == expected.LastVisitTimeRaw
        && actual.ReservedHeaderData is not null
        && expected.ReservedHeaderData is not null
        && actual.ReservedHeaderData.AsSpan().SequenceEqual(expected.ReservedHeaderData)
        && actual.GlobalVariablesOffset == expected.GlobalVariablesOffset
        && actual.GlobalVariablesLength == expected.GlobalVariablesLength
        && actual.LocalVariablesOffset == expected.LocalVariablesOffset
        && actual.LocalVariablesLength == expected.LocalVariablesLength
        && actual.TileDataOffset == expected.TileDataOffset
        && actual.TileDataLength == expected.TileDataLength
        && actual.PresentElevationCount == expected.PresentElevationCount;
}
