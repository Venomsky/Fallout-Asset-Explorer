using System.Text;

namespace FalloutAssetExplorer.Core;

public sealed record FalloutFidArtAssetResolution(
    bool Success,
    FalloutFid Fid,
    string? ResourceName,
    string? ResourcePath,
    ArchiveEntry? Entry,
    string? Error);

public static class FalloutFidArtAssetResolver
{
    private const int MaximumListSize = 1024 * 1024;
    private const int MaximumResourceNameLength = 12;

    public static async Task<FalloutFidArtAssetResolution> ResolveAsync(
        IGameArchive archive,
        int rawFid,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archive);
        var fid = FalloutFidDecoder.Decode(rawFid);
        if (!TryGetArtListDirectory(fid.ArtType, out var directory, out var unsupportedReason))
        {
            return Failure(fid, unsupportedReason!);
        }

        var listPath = $"art/{directory}/{directory}.lst";
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
                listPath,
                entriesByPath,
                duplicatePaths,
                out var listEntry,
                out var listError)
            || listEntry is null)
        {
            return Failure(fid, listError!);
        }

        if (listEntry.UncompressedSize is < 0 or > MaximumListSize)
        {
            return Failure(fid, $"The art list '{listPath}' is outside the supported size limit.");
        }

        byte[] listBytes;
        try
        {
            await using var output = new MemoryStream(checked((int)listEntry.UncompressedSize));
            await archive.CopyEntryToAsync(listEntry, output, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (output.Length != listEntry.UncompressedSize)
            {
                return Failure(
                    fid,
                    $"The art list '{listPath}' declares {listEntry.UncompressedSize} bytes but {output.Length} bytes were read.");
            }

            listBytes = output.ToArray();
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return Failure(fid, $"The art list '{listPath}' could not be read: {exception.Message}");
        }

        if (!TryReadListNames(
                listBytes,
                fid.ArtType == 4,
                out var names,
                out var listFormatError))
        {
            return Failure(fid, $"The art list '{listPath}' {listFormatError}.");
        }

        if ((uint)fid.ArtListIndex >= (uint)names.Count)
        {
            return Failure(
                fid,
                $"Art-list index {fid.ArtListIndex} is outside '{listPath}' ({names.Count} entries).");
        }

        var resourceName = names[fid.ArtListIndex];
        if (!IsSafeResourceName(resourceName))
        {
            return Failure(
                fid,
                $"Art-list entry {fid.ArtListIndex} in '{listPath}' is empty, malformed, or unsafe.");
        }

        var resourcePath = $"art/{directory}/{resourceName}";
        if (!TryFindUniqueEntry(
                resourcePath,
                entriesByPath,
                duplicatePaths,
                out var resourceEntry,
                out var resourceError)
            || resourceEntry is null)
        {
            return Failure(fid, resourceError!);
        }

        return new FalloutFidArtAssetResolution(
            true,
            fid,
            resourceName,
            resourcePath,
            resourceEntry,
            null);
    }

    private static bool TryGetArtListDirectory(
        int artType,
        out string? directory,
        out string? error)
    {
        directory = artType switch
        {
            0 => "items",
            2 => "scenery",
            3 => "walls",
            4 => "tiles",
            5 => "misc",
            6 => "intrface",
            7 => "inven",
            9 => "backgrnd",
            10 => "skilldex",
            _ => null
        };
        error = artType switch
        {
            1 => "Critter FIDs require special animation, alias, and split-file handling.",
            8 => "Head FIDs require special animation and weapon filename handling.",
            _ when directory is null => $"FID art type {artType} is not supported.",
            _ => null
        };
        return directory is not null;
    }

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

    private static bool TryReadListNames(
        byte[] bytes,
        bool useCeTileListSemantics,
        out IReadOnlyList<string> names,
        out string? error)
    {
        if (bytes.Any(value => value > 0x7f))
        {
            names = [];
            error = "contains non-ASCII data";
            return false;
        }

        if (useCeTileListSemantics)
        {
            names = Fallout2TileAssetResolver.ReadCeListNames(bytes);
            error = null;
            return true;
        }

        var result = new List<string>();
        var start = 0;
        while (start < bytes.Length)
        {
            var end = Array.IndexOf(bytes, (byte)'\n', start);
            if (end < 0)
            {
                end = bytes.Length;
            }

            var line = bytes.AsSpan(start, end - start);
            if (!line.IsEmpty && line[^1] == (byte)'\r')
            {
                line = line[..^1];
            }

            result.Add(Encoding.ASCII.GetString(line));
            start = end + 1;
        }

        names = result;
        error = null;
        return true;
    }

    private static bool IsSafeResourceName(string resourceName) =>
        resourceName.Length is > 0 and <= MaximumResourceNameLength
        && resourceName is not "." and not ".."
        && !resourceName.Any(character => char.IsControl(character) || char.IsWhiteSpace(character))
        && resourceName.IndexOfAny(['/', '\\', ':']) < 0;

    private static FalloutFidArtAssetResolution Failure(FalloutFid fid, string error) =>
        new(false, fid, null, null, null, error);
}
