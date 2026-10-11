namespace FalloutAssetExplorer.Core;

public sealed class FalloutPaletteResolver
{
    public const string DefaultPaletteEntryPath = "COLOR.PAL";
    public const string DefaultPaletteFileName = "COLOR.PAL";

    private readonly SemaphoreSlim _resolutionGate = new(1, 1);
    private readonly Dictionary<string, FalloutPalette> _paletteCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IGameArchive> _archiveCache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<FalloutPaletteResolution> ResolveAsync(
        IGameArchive? openArchive,
        string? gameDataDirectory = null,
        CancellationToken cancellationToken = default)
    {
        await _resolutionGate.WaitAsync(cancellationToken);
        try
        {
            return await ResolveCoreAsync(openArchive, gameDataDirectory, cancellationToken);
        }
        catch (Exception exception) when (
            exception is InvalidDataException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return new FalloutPaletteResolution(
                null,
                null,
                $"Could not load the Fallout palette: {exception.Message} Showing diagnostic grayscale.");
        }
        finally
        {
            _resolutionGate.Release();
        }
    }

    private async Task<FalloutPaletteResolution> ResolveCoreAsync(
        IGameArchive? openArchive,
        string? gameDataDirectory,
        CancellationToken cancellationToken)
    {
        if (openArchive is not null)
        {
            var archiveMatches = FindDefaultPaletteEntries(openArchive);
            if (archiveMatches.Count > 1)
            {
                throw new InvalidDataException(
                    $"Archive '{openArchive.Path}' contains multiple root {DefaultPaletteFileName} entries.");
            }

            if (archiveMatches.Count == 1)
            {
                var palette = await ReadPaletteEntryAsync(openArchive, archiveMatches[0], cancellationToken);
                return new FalloutPaletteResolution(
                    palette,
                    $"{DefaultPaletteEntryPath} in {Path.GetFileName(openArchive.Path)}",
                    null);
            }
        }

        if (!string.IsNullOrWhiteSpace(gameDataDirectory))
        {
            if (!Directory.Exists(gameDataDirectory))
            {
                throw new DirectoryNotFoundException($"Game data directory '{gameDataDirectory}' does not exist.");
            }

            var configuredMaster = FindMasterArchive(gameDataDirectory, openArchive);
            var configuredDirectoryMatchesGame = openArchive is null || configuredMaster is null;
            if (configuredMaster is not null && openArchive is not null)
            {
                var configuredMasterArchive = await OpenArchiveAsync(configuredMaster, cancellationToken);
                configuredDirectoryMatchesGame = configuredMasterArchive.Game == openArchive.Game;
            }

            var configuredPalette = configuredDirectoryMatchesGame
                ? FindRootPaletteFile(gameDataDirectory)
                : null;
            if (configuredPalette is not null)
            {
                var palette = await ReadPaletteFileAsync(configuredPalette, cancellationToken);
                return new FalloutPaletteResolution(palette, Path.GetFullPath(configuredPalette), null);
            }

            if (configuredMaster is not null)
            {
                var palette = await ReadPaletteFromMasterArchiveAsync(
                    configuredMaster,
                    openArchive?.Game,
                    cancellationToken);
                if (palette is not null)
                {
                    return new FalloutPaletteResolution(
                        palette,
                        $"{DefaultPaletteEntryPath} in {Path.GetFileName(configuredMaster)}",
                        null);
                }
            }
        }

        if (openArchive is not null
            && string.Equals(Path.GetFileName(openArchive.Path), "CRITTER.DAT", StringComparison.OrdinalIgnoreCase))
        {
            var archiveDirectory = Path.GetDirectoryName(Path.GetFullPath(openArchive.Path));
            if (archiveDirectory is not null)
            {
                var adjacentMaster = FindMasterArchive(archiveDirectory, openArchive);
                if (adjacentMaster is not null)
                {
                    var palette = await ReadPaletteFromMasterArchiveAsync(
                        adjacentMaster,
                        openArchive.Game,
                        cancellationToken);
                    if (palette is not null)
                    {
                        return new FalloutPaletteResolution(
                            palette,
                            $"{DefaultPaletteEntryPath} in {Path.GetFileName(adjacentMaster)}",
                            null);
                    }
                }
            }
        }

        return new FalloutPaletteResolution(
            null,
            null,
            $"No root {DefaultPaletteFileName} was found in the open archive, configured game-data directory, or supported adjacent MASTER.DAT; showing diagnostic grayscale.");
    }

    private async Task<FalloutPalette> ReadPaletteEntryAsync(
        IGameArchive archive,
        ArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        var key = CreateSourceKey(archive, entry);
        if (_paletteCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        if (entry.UncompressedSize != FalloutPalette.MinimalFileSize
            && entry.UncompressedSize != FalloutPalette.ExtendedFileSize)
        {
            throw new InvalidDataException(
                $"Palette entry '{entry.Path}' has unsupported length {entry.UncompressedSize}.");
        }

        using var output = new MemoryStream((int)entry.UncompressedSize);
        await archive.CopyEntryToAsync(entry, output, cancellationToken);
        var palette = FalloutPalette.Parse(output.GetBuffer().AsSpan(0, checked((int)output.Length)));
        _paletteCache[key] = palette;
        return palette;
    }

    private async Task<FalloutPalette?> ReadPaletteFromMasterArchiveAsync(
        string path,
        FalloutGame? requiredGame,
        CancellationToken cancellationToken)
    {
        var archive = await OpenArchiveAsync(path, cancellationToken);

        if (requiredGame is not null && archive.Game != requiredGame)
        {
            return null;
        }

        var entries = FindDefaultPaletteEntries(archive);
        if (entries.Count > 1)
        {
            throw new InvalidDataException(
                $"Archive '{archive.Path}' contains multiple root {DefaultPaletteFileName} entries.");
        }

        return entries.Count == 0
            ? null
            : await ReadPaletteEntryAsync(archive, entries[0], cancellationToken);
    }

    private async Task<IGameArchive> OpenArchiveAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        var archiveKey = CreateFileSourceKey(fullPath);
        if (_archiveCache.TryGetValue(archiveKey, out var archive))
        {
            return archive;
        }

        cancellationToken.ThrowIfCancellationRequested();
        archive = await Task.Run(
            () => FalloutArchive.Open(fullPath, cancellationToken),
            cancellationToken);
        _archiveCache[archiveKey] = archive;
        return archive;
    }

    private async Task<FalloutPalette> ReadPaletteFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        var key = CreateFileSourceKey(fullPath);
        if (_paletteCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var length = stream.Length;
        if (length != FalloutPalette.MinimalFileSize && length != FalloutPalette.ExtendedFileSize)
        {
            throw new InvalidDataException($"Palette file '{fullPath}' has unsupported length {length}.");
        }

        var data = new byte[(int)length];
        await stream.ReadExactlyAsync(data, cancellationToken);
        if (stream.Length != length)
        {
            throw new InvalidDataException($"Palette file '{fullPath}' changed while it was being read.");
        }

        var palette = FalloutPalette.Parse(data);
        _paletteCache[key] = palette;
        return palette;
    }

    private static string? FindRootPaletteFile(string directory)
    {
        var matches = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => string.Equals(
                Path.GetFileName(path),
                DefaultPaletteFileName,
                StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        if (matches.Length > 1)
        {
            throw new InvalidDataException(
                $"Game data directory '{directory}' contains multiple root {DefaultPaletteFileName} files.");
        }

        return matches.SingleOrDefault();
    }

    private static string? FindMasterArchive(string directory, IGameArchive? openArchive)
    {
        var matches = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => string.Equals(Path.GetFileName(path), "MASTER.DAT", StringComparison.OrdinalIgnoreCase))
            .Where(path => openArchive is null
                || !string.Equals(
                    Path.GetFullPath(path),
                    Path.GetFullPath(openArchive.Path),
                    StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        if (matches.Length > 1)
        {
            throw new InvalidDataException($"Directory '{directory}' contains multiple MASTER.DAT archives.");
        }

        return matches.SingleOrDefault();
    }

    private static List<ArchiveEntry> FindDefaultPaletteEntries(IGameArchive archive) =>
        archive.BrowserEntries
            .Where(entry => string.Equals(
                entry.Path,
                DefaultPaletteEntryPath,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static string CreateSourceKey(IGameArchive archive, ArchiveEntry entry) =>
        $"{CreateFileSourceKey(Path.GetFullPath(archive.Path))}|{archive.Format}|{entry.Path}|{entry.UncompressedSize}|{entry.CompressedSize}";

    private static string CreateFileSourceKey(string path)
    {
        var info = new FileInfo(path);
        return $"{Path.GetFullPath(path)}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
    }
}

public sealed record FalloutPaletteResolution(
    FalloutPalette? Palette,
    string? Source,
    string? Diagnostic);
