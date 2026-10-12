using System.Buffers.Binary;
using System.Text;

namespace FalloutAssetExplorer.Core;

public sealed record Fallout2PrototypeDiagnostic(
    int RequestedPid,
    string Status,
    string? VerifiedFileName,
    string? ListedFileName,
    int? PrototypePid,
    int? Subtype,
    long? FileSize,
    ReadOnlyMemory<byte> RawBytes)
{
    public bool IsVerified => VerifiedFileName is not null;

    public bool HasSupportedSubtype { get; init; }
}

public sealed class Fallout2PrototypeResolver
{
    private const int MaximumPrototypeListSize = 16 * 1024 * 1024;
    private const int MaximumPrototypeSize = 1024 * 1024;
    private const int MinimumPrototypeSize = 36;
    private const int MaximumCachedPrototypeBytes = 8 * 1024 * 1024;

    private readonly IGameArchive? _archive;
    private readonly SemaphoreSlim _listLoadLock = new(1, 1);
    private readonly object _cacheLock = new();
    private readonly Dictionary<int, PrototypeListResult> _prototypeLists = [];
    private readonly Dictionary<int, PrototypeLocation> _prototypeLocations = [];
    private readonly Dictionary<int, LinkedListNode<CachedPrototype>> _prototypeCache = [];
    private readonly LinkedList<CachedPrototype> _prototypeCacheLru = [];
    private int _cachedPrototypeBytes;

    public Fallout2PrototypeResolver(IGameArchive? archive)
    {
        _archive = archive;
    }

    public async Task<Fallout2PrototypeDiagnostic> ResolveAsync(
        int pid,
        CancellationToken cancellationToken = default)
    {
        var pidType = (int)((uint)pid >> 24);
        if (_archive is null
            || _archive.Game != FalloutGame.Fallout2
            || _archive.Format != ArchiveFormat.Dat2)
        {
            return CreateFailure(pid, "the active Fallout 2 DAT2 archive is unavailable");
        }

        if (pidType is not (0 or 2))
        {
            return CreateFailure(pid, "prototype lookup is supported only for Item and Scenery PIDs");
        }

        var location = await ResolveLocationAsync(pid, pidType, cancellationToken);
        if (location.Error is not null)
        {
            return CreateFailure(pid, location.Error, location.FileName, location.FileSize);
        }

        if (location.Entry is null || location.FileName is null || location.FileSize is null)
        {
            return CreateFailure(pid, "the prototype entry could not be resolved", location.FileName);
        }

        var prototypeBytes = TryGetCachedPrototype(pid);
        if (prototypeBytes is null)
        {
            try
            {
                var bytes = await ReadArchiveEntryAsync(
                    _archive,
                    location.Entry,
                    checked((int)location.FileSize.Value),
                    cancellationToken);
                CachePrototype(pid, bytes);
                prototypeBytes = bytes;
            }
            catch (Exception exception) when (
                exception is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                return CreateFailure(
                    pid,
                    $"the {(pidType == 0 ? "item" : "scenery")} prototype could not be read",
                    location.FileName,
                    location.FileSize);
            }
        }

        return ValidatePrototype(pid, pidType, location.FileName, location.FileSize.Value, prototypeBytes.Value);
    }

    private async Task<PrototypeLocation> ResolveLocationAsync(
        int pid,
        int pidType,
        CancellationToken cancellationToken)
    {
        lock (_cacheLock)
        {
            if (_prototypeLocations.TryGetValue(pid, out var cachedLocation))
            {
                return cachedLocation;
            }
        }

        var directory = pidType == 0 ? "items" : "scenery";
        var listFileName = pidType == 0 ? "items.lst" : "scenery.lst";
        var category = pidType == 0 ? "item" : "scenery";
        var listResult = await GetPrototypeListAsync(
            pidType,
            directory,
            listFileName,
            category,
            cancellationToken);
        if (listResult.Lines is null)
        {
            return CacheLocation(pid, new PrototypeLocation(null, null, null, listResult.Error));
        }

        var listIndex = pid & 0x00ffffff;
        if (listIndex == 0)
        {
            return CacheLocation(
                pid,
                new PrototypeLocation(null, null, null, $"the {category} PID has an invalid zero list index"));
        }

        var lineIndex = listIndex - 1;
        if ((uint)lineIndex >= listResult.Lines.Length
            || string.IsNullOrWhiteSpace(listResult.Lines[lineIndex]))
        {
            return CacheLocation(
                pid,
                new PrototypeLocation(
                    null,
                    null,
                    null,
                    $"{category} PID list index {listIndex} is outside the prototype list"));
        }

        var prototypeName = listResult.Lines[lineIndex];
        if (prototypeName.IndexOfAny(['/', '\\', ':']) >= 0
            || prototypeName is "." or ".."
            || !string.Equals(Path.GetExtension(prototypeName), ".PRO", StringComparison.OrdinalIgnoreCase))
        {
            return CacheLocation(
                pid,
                new PrototypeLocation(
                    prototypeName,
                    null,
                    null,
                    $"the {category} prototype list contains an unsafe or invalid filename"));
        }

        var entry = FindArchiveEntry(_archive!, $"proto/{directory}/{prototypeName}");
        if (entry is null)
        {
            return CacheLocation(
                pid,
                new PrototypeLocation(
                    prototypeName,
                    null,
                    null,
                    $"the {category} prototype '{prototypeName}' is missing"));
        }

        if (!TryReadEntrySize(entry, MaximumPrototypeSize, out var size, out var sizeError))
        {
            return CacheLocation(
                pid,
                new PrototypeLocation(
                    prototypeName,
                    null,
                    entry.UncompressedSize,
                    $"the {category} prototype {sizeError}"));
        }

        return CacheLocation(pid, new PrototypeLocation(prototypeName, entry, size, null));
    }

    private async Task<PrototypeListResult> GetPrototypeListAsync(
        int pidType,
        string directory,
        string listFileName,
        string category,
        CancellationToken cancellationToken)
    {
        lock (_cacheLock)
        {
            if (_prototypeLists.TryGetValue(pidType, out var cachedList))
            {
                return cachedList;
            }
        }

        await _listLoadLock.WaitAsync(cancellationToken);
        try
        {
            lock (_cacheLock)
            {
                if (_prototypeLists.TryGetValue(pidType, out var cachedList))
                {
                    return cachedList;
                }
            }

            var result = await ReadPrototypeListAsync(
                _archive!,
                directory,
                listFileName,
                category,
                cancellationToken);
            lock (_cacheLock)
            {
                _prototypeLists[pidType] = result;
            }

            return result;
        }
        finally
        {
            _listLoadLock.Release();
        }
    }

    private static async Task<PrototypeListResult> ReadPrototypeListAsync(
        IGameArchive archive,
        string directory,
        string listFileName,
        string category,
        CancellationToken cancellationToken)
    {
        var listPath = $"proto/{directory}/{listFileName}";
        var listEntry = FindArchiveEntry(archive, listPath);
        if (listEntry is null)
        {
            return new PrototypeListResult(null, $"{listPath} is missing");
        }

        if (!TryReadEntrySize(listEntry, MaximumPrototypeListSize, out var listSize, out var sizeError))
        {
            return new PrototypeListResult(null, $"the {category} prototype list {sizeError}");
        }

        byte[] listBytes;
        try
        {
            listBytes = await ReadArchiveEntryAsync(archive, listEntry, listSize, cancellationToken);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return new PrototypeListResult(
                null,
                $"the {category} prototype list could not be read");
        }

        if (listBytes.Any(value => value > 0x7f))
        {
            return new PrototypeListResult(null, $"the {category} prototype list contains non-ASCII data");
        }

        var lines = new List<string>();
        using var reader = new StringReader(Encoding.ASCII.GetString(listBytes));
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty);
        }

        return new PrototypeListResult([.. lines], null);
    }

    private static Fallout2PrototypeDiagnostic ValidatePrototype(
        int requestedPid,
        int pidType,
        string fileName,
        long fileSize,
        ReadOnlyMemory<byte> prototypeBytes)
    {
        var category = pidType == 0 ? "item" : "scenery";
        if (prototypeBytes.Length < sizeof(int))
        {
            return new Fallout2PrototypeDiagnostic(
                requestedPid,
                $"the {category} prototype is shorter than the 4-byte PID field",
                null,
                fileName,
                null,
                null,
                fileSize,
                prototypeBytes);
        }

        var prototypePid = BinaryPrimitives.ReadInt32BigEndian(prototypeBytes.Span);
        if (prototypePid != requestedPid || (uint)prototypePid >> 24 != (uint)pidType)
        {
            return new Fallout2PrototypeDiagnostic(
                requestedPid,
                $"the {category} prototype PID 0x{prototypePid:X8} does not match MAP PID 0x{requestedPid:X8}",
                null,
                fileName,
                prototypePid,
                null,
                fileSize,
                prototypeBytes);
        }

        if (prototypeBytes.Length < MinimumPrototypeSize)
        {
            return new Fallout2PrototypeDiagnostic(
                requestedPid,
                $"the {category} prototype is shorter than {MinimumPrototypeSize} bytes",
                fileName,
                fileName,
                prototypePid,
                null,
                fileSize,
                prototypeBytes);
        }

        var subtype = BinaryPrimitives.ReadInt32BigEndian(
            prototypeBytes.Span.Slice(32, sizeof(int)));
        var maximumSubtype = pidType == 0 ? 6 : 5;
        if (subtype < 0 || subtype > maximumSubtype)
        {
            return new Fallout2PrototypeDiagnostic(
                requestedPid,
                $"the {category} prototype has unsupported subtype {subtype}",
                fileName,
                fileName,
                prototypePid,
                subtype,
                fileSize,
                prototypeBytes);
        }

        return new Fallout2PrototypeDiagnostic(
            requestedPid,
            "Prototype PID and subtype validated.",
            fileName,
            fileName,
            prototypePid,
            subtype,
            fileSize,
            prototypeBytes)
        {
            HasSupportedSubtype = true
        };
    }

    private PrototypeLocation CacheLocation(int pid, PrototypeLocation location)
    {
        lock (_cacheLock)
        {
            _prototypeLocations[pid] = location;
        }

        return location;
    }

    private ReadOnlyMemory<byte>? TryGetCachedPrototype(int pid)
    {
        lock (_cacheLock)
        {
            if (!_prototypeCache.TryGetValue(pid, out var node))
            {
                return null;
            }

            _prototypeCacheLru.Remove(node);
            _prototypeCacheLru.AddFirst(node);
            return node.Value.Bytes;
        }
    }

    private void CachePrototype(int pid, byte[] bytes)
    {
        if (bytes.Length > MaximumCachedPrototypeBytes)
        {
            return;
        }

        lock (_cacheLock)
        {
            if (_prototypeCache.Remove(pid, out var previous))
            {
                _prototypeCacheLru.Remove(previous);
                _cachedPrototypeBytes -= previous.Value.Bytes.Length;
            }

            while (_cachedPrototypeBytes + bytes.Length > MaximumCachedPrototypeBytes
                && _prototypeCacheLru.Last is { } last)
            {
                _prototypeCache.Remove(last.Value.Pid);
                _cachedPrototypeBytes -= last.Value.Bytes.Length;
                _prototypeCacheLru.RemoveLast();
            }

            var node = _prototypeCacheLru.AddFirst(new CachedPrototype(pid, bytes));
            _prototypeCache.Add(pid, node);
            _cachedPrototypeBytes += bytes.Length;
        }
    }

    private static Fallout2PrototypeDiagnostic CreateFailure(
        int pid,
        string status,
        string? listedFileName = null,
        long? fileSize = null) =>
        new(pid, status, null, listedFileName, null, null, fileSize, ReadOnlyMemory<byte>.Empty);

    private static ArchiveEntry? FindArchiveEntry(IGameArchive archive, string expectedPath)
    {
        ArchiveEntry? match = null;
        foreach (var entry in archive.BrowserEntries)
        {
            if (!string.Equals(
                    entry.Path.Replace('\\', '/'),
                    expectedPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (match is not null)
            {
                return null;
            }

            match = entry;
        }

        return match;
    }

    private static bool TryReadEntrySize(
        ArchiveEntry entry,
        int maximumSize,
        out int size,
        out string error)
    {
        if (entry.UncompressedSize < 0 || entry.UncompressedSize > maximumSize)
        {
            size = 0;
            error = $"has an invalid size of {entry.UncompressedSize} bytes";
            return false;
        }

        size = (int)entry.UncompressedSize;
        error = string.Empty;
        return true;
    }

    private static async Task<byte[]> ReadArchiveEntryAsync(
        IGameArchive archive,
        ArchiveEntry entry,
        int expectedSize,
        CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream(expectedSize);
        await archive.CopyEntryToAsync(entry, stream, cancellationToken);
        if (stream.Length != expectedSize)
        {
            throw new InvalidDataException(
                $"Archive entry '{entry.Path}' declared {expectedSize} bytes but yielded {stream.Length}.");
        }

        return stream.ToArray();
    }

    private sealed record PrototypeListResult(string[]? Lines, string? Error);

    private sealed record PrototypeLocation(
        string? FileName,
        ArchiveEntry? Entry,
        long? FileSize,
        string? Error);

    private sealed record CachedPrototype(int Pid, byte[] Bytes);
}
