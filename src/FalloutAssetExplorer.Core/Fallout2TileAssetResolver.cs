using System.Text;

namespace FalloutAssetExplorer.Core;

public sealed record Fallout2TileAssetResolution(
    bool Success,
    int FloorId,
    string? ResourceName,
    string? ResourcePath,
    ArchiveEntry? Entry,
    string? Error);

public static class Fallout2TileAssetResolver
{
    public const string TileListPath = "art/tiles/tiles.lst";

    private const int MaximumFloorId = 0x0fff;
    private const int MaximumListSize = 1024 * 1024;
    private const int CeLineBufferSize = 200;

    public static async Task<Fallout2TileAssetResolution> ResolveFloorAsync(
        IGameArchive archive,
        int floorId,
        CancellationToken cancellationToken = default)
    {
        var results = await ResolveFloorsAsync(archive, [floorId], cancellationToken);
        return results[floorId];
    }

    public static async Task<IReadOnlyDictionary<int, Fallout2TileAssetResolution>> ResolveFloorsAsync(
        IGameArchive archive,
        IEnumerable<int> floorIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(floorIds);

        var requestedIds = floorIds.Distinct().ToArray();
        var results = new Dictionary<int, Fallout2TileAssetResolution>(requestedIds.Length);
        if (requestedIds.Length == 0)
        {
            return results;
        }

        if (archive.Game != FalloutGame.Fallout2 || archive.Format != ArchiveFormat.Dat2)
        {
            foreach (var floorId in requestedIds)
            {
                results.Add(floorId, Failure(floorId, "A Fallout 2 DAT2 archive is required."));
            }

            return results;
        }

        var validIds = new List<int>(requestedIds.Length);
        foreach (var floorId in requestedIds)
        {
            if (floorId is < 0 or > MaximumFloorId)
            {
                results.Add(floorId, Failure(floorId, $"Floor ID must be between 0 and {MaximumFloorId}."));
            }
            else
            {
                validIds.Add(floorId);
            }
        }

        if (validIds.Count == 0)
        {
            return results;
        }

        var entriesByPath = new Dictionary<string, ArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        var duplicatePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.BrowserEntries)
        {
            var normalizedPath = NormalizeArchivePath(entry.Path);
            if (!entriesByPath.TryAdd(normalizedPath, entry))
            {
                duplicatePaths.Add(normalizedPath);
            }
        }

        if (!TryFindUniqueEntry(
                TileListPath,
                entriesByPath,
                duplicatePaths,
                out var listEntry,
                out var listError)
            || listEntry is null)
        {
            foreach (var floorId in validIds)
            {
                results.Add(floorId, Failure(floorId, listError!));
            }

            return results;
        }

        if (listEntry.UncompressedSize is < 0 or > MaximumListSize)
        {
            var error = $"The tile list exceeds the {MaximumListSize}-byte size limit.";
            foreach (var floorId in validIds)
            {
                results.Add(floorId, Failure(floorId, error));
            }

            return results;
        }

        byte[] listBytes;
        try
        {
            await using var output = new MemoryStream(checked((int)listEntry.UncompressedSize));
            await archive.CopyEntryToAsync(listEntry, output, cancellationToken);
            if (output.Length > MaximumListSize)
            {
                var error = $"The tile list exceeds the {MaximumListSize}-byte size limit.";
                foreach (var requestedId in validIds)
                {
                    results.Add(requestedId, Failure(requestedId, error));
                }

                return results;
            }

            listBytes = output.ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            foreach (var requestedId in validIds)
            {
                results.Add(requestedId, Failure(requestedId, "The tile list could not be read from the archive."));
            }

            return results;
        }

        if (listBytes.Any(value => value > 0x7f))
        {
            const string error = "The tile list contains non-ASCII data that cannot be matched to DAT2 paths.";
            foreach (var requestedId in validIds)
            {
                results.Add(requestedId, Failure(requestedId, error));
            }

            return results;
        }

        var names = ReadCeListNames(listBytes);
        foreach (var floorId in validIds)
        {
            if ((uint)floorId >= (uint)names.Count)
            {
                results.Add(floorId, Failure(floorId, $"Floor ID {floorId} is outside the tile list."));
                continue;
            }

            var resourceName = names[floorId];
            if (!IsSafeResourceName(resourceName))
            {
                results.Add(floorId, Failure(floorId, $"Tile list entry {floorId} is empty or is not a safe resource name."));
                continue;
            }

            var resourcePath = $"art/tiles/{resourceName}";
            if (!TryFindUniqueEntry(
                    resourcePath,
                    entriesByPath,
                    duplicatePaths,
                    out var assetEntry,
                    out var assetError))
            {
                results.Add(floorId, Failure(floorId, assetError!));
                continue;
            }

            results.Add(
                floorId,
                new Fallout2TileAssetResolution(
                    true,
                    floorId,
                    resourceName,
                    resourcePath,
                    assetEntry,
                    null));
        }

        return results;
    }

    private static Fallout2TileAssetResolution Failure(int floorId, string error) =>
        new(false, floorId, null, null, null, error);

    private static bool TryFindUniqueEntry(
        string expectedPath,
        IReadOnlyDictionary<string, ArchiveEntry> entriesByPath,
        IReadOnlySet<string> duplicatePaths,
        out ArchiveEntry? entry,
        out string? error)
    {
        var normalizedPath = NormalizeArchivePath(expectedPath);
        if (duplicatePaths.Contains(normalizedPath))
        {
            entry = null;
            error = $"The archive contains multiple entries matching '{expectedPath}'.";
            return false;
        }

        if (!entriesByPath.TryGetValue(normalizedPath, out entry))
        {
            error = $"The archive entry '{expectedPath}' is missing.";
            return false;
        }

        error = null;
        return true;
    }

    private static string NormalizeArchivePath(string path) =>
        path.Replace('\\', '/');

    internal static List<string> ReadCeListNames(byte[] bytes)
    {
        var names = new List<string>();
        var offset = 0;
        while (offset < bytes.Length)
        {
            var end = offset;
            var readLimit = Math.Min(bytes.Length, offset + CeLineBufferSize - 1);
            while (end < readLimit && bytes[end] != (byte)'\n')
            {
                end++;
            }

            var hasLineFeed = end < readLimit && bytes[end] == (byte)'\n';
            var line = bytes.AsSpan(offset, end - offset);
            var separator = line.IndexOfAny(" ,;\r\t\n"u8);
            if (separator >= 0)
            {
                line = line[..separator];
            }

            names.Add(Encoding.ASCII.GetString(line[..Math.Min(line.Length, 12)]));
            offset = hasLineFeed ? end + 1 : end;
        }

        return names;
    }

    private static bool IsSafeResourceName(string resourceName) =>
        resourceName.Length > 0
        && resourceName is not "." and not ".."
        && !resourceName.Any(char.IsControl)
        && resourceName.IndexOfAny(['/', '\\', ':']) < 0;
}
