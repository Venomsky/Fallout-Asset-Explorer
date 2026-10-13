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

    public bool HasExpectedSize { get; init; }

    public IReadOnlyList<Fallout2PrototypeField> SceneryFields { get; init; } = [];

    public IReadOnlyList<Fallout2PrototypeField> ItemFields { get; init; } = [];
}

public enum PrototypeInspectionOutcome
{
    Success,
    Partial,
    Error
}

public sealed record Fallout2StandalonePrototypeInspection(
    string EntryPath,
    string Status,
    FalloutGame? Game,
    ArchiveFormat? Format,
    int? PrototypePid,
    int? PidType,
    int? Subtype,
    long? FileSize,
    ReadOnlyMemory<byte> RawBytes)
{
    public string? ArchivePath { get; init; }

    public string? ArchiveLocation { get; init; }

    public long? CompressedSize { get; init; }

    public long? ActualSize { get; init; }

    public bool IsReadComplete { get; init; }

    public PrototypeInspectionOutcome Outcome { get; init; } = PrototypeInspectionOutcome.Error;

    public string Provenance =>
        "Selected directly from the archive; not verified through a MAP PID or prototype list.";

    public string? PidTypeName { get; init; }

    public string? SubtypeName { get; init; }

    public bool HasSupportedSubtype { get; init; }

    public IReadOnlyList<Fallout2PrototypeField> SceneryFields { get; init; } = [];

    public IReadOnlyList<Fallout2PrototypeField> ItemFields { get; init; } = [];
}

public sealed record Fallout2PrototypeField(
    string Name,
    int Offset,
    int SizeInBytes,
    int Value,
    bool IsFlags = false,
    bool DisplayAsHex = false,
    string? Interpretation = null,
    string StorageType = "int32 BE");

public sealed class Fallout2PrototypeResolver
{
    private const int MaximumPrototypeListSize = 16 * 1024 * 1024;
    public const int MaximumPrototypeSizeBytes = 1024 * 1024;
    private const int MinimumPrototypeSize = 36;
    private const int SceneryTwoWordPrototypeSize = 0x31;
    private const int SceneryOneWordPrototypeSize = 0x2D;
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

    public async Task<Fallout2StandalonePrototypeInspection> InspectStandaloneEntryAsync(
        ArchiveEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!string.Equals(entry.Extension, ".PRO", StringComparison.OrdinalIgnoreCase))
        {
            return CreateStandaloneInspection(
                entry,
                "The selected archive entry is not a PRO file.",
                _archive?.Game,
                _archive?.Format,
                null,
                null,
                null,
                entry.UncompressedSize,
                ReadOnlyMemory<byte>.Empty);
        }

        if (_archive is null)
        {
            return CreateStandaloneInspection(
                entry,
                "The selected archive is unavailable.",
                null,
                null,
                null,
                null,
                null,
                entry.UncompressedSize,
                ReadOnlyMemory<byte>.Empty);
        }

        if (!TryReadEntrySize(
                entry,
                MaximumPrototypeSizeBytes,
                out var size,
                out _))
        {
            var sizeMessage = entry.UncompressedSize < 0
                ? $"the archive reports a negative uncompressed size of {entry.UncompressedSize} bytes"
                : $"the entry declares {entry.UncompressedSize:N0} bytes, exceeding the 1 MiB preview limit";
            return CreateStandaloneInspection(
                entry,
                $"PRO preview rejected: {sizeMessage}. The entry was not read.",
                _archive.Game,
                _archive.Format,
                null,
                null,
                null,
                entry.UncompressedSize,
                ReadOnlyMemory<byte>.Empty);
        }

        var readResult = await ReadStandaloneEntryAsync(
            _archive,
            entry,
            size,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var rawBytes = readResult.RawBytes;
        if (!readResult.IsComplete)
        {
            return CreateStandaloneInspection(
                entry,
                readResult.Error!,
                _archive.Game,
                _archive.Format,
                null,
                null,
                null,
                entry.UncompressedSize,
                rawBytes,
                actualSize: readResult.ActualSize);
        }

        if (_archive.Game != FalloutGame.Fallout2
            || _archive.Format != ArchiveFormat.Dat2)
        {
            return CreateStandaloneInspection(
                entry,
                $"Semantic PRO inspection is supported only for Fallout 2 DAT2 archives; "
                    + $"this entry is {_archive.Game} {_archive.Format}. Raw bytes are shown without decoding.",
                _archive.Game,
                _archive.Format,
                null,
                null,
                null,
                size,
                rawBytes,
                actualSize: readResult.ActualSize,
                isReadComplete: true,
                outcome: PrototypeInspectionOutcome.Partial);
        }

        if (rawBytes.Length < sizeof(int))
        {
            return CreateStandaloneInspection(
                entry,
                "The Fallout 2 PRO entry is shorter than the 4-byte PID field.",
                _archive.Game,
                _archive.Format,
                null,
                null,
                null,
                size,
                rawBytes,
                actualSize: readResult.ActualSize,
                isReadComplete: true,
                outcome: PrototypeInspectionOutcome.Partial);
        }

        var prototypePid = BinaryPrimitives.ReadInt32BigEndian(rawBytes);
        var pidType = (int)((uint)prototypePid >> 24);
        if (pidType is not (0 or 2))
        {
            return CreateStandaloneInspection(
                entry,
                $"Unsupported Fallout 2 PRO PID type {pidType}; only Item (0) and Scenery (2) are decoded.",
                _archive.Game,
                _archive.Format,
                prototypePid,
                pidType,
                null,
                size,
                rawBytes,
                actualSize: readResult.ActualSize,
                isReadComplete: true,
                outcome: PrototypeInspectionOutcome.Partial);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var diagnostic = await Task.Run(
            () => ValidatePrototype(
                prototypePid,
                pidType,
                entry.Path,
                size,
                rawBytes),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return CreateStandaloneInspection(
            entry,
            diagnostic.Status,
            _archive.Game,
            _archive.Format,
            diagnostic.PrototypePid,
            pidType,
            diagnostic.Subtype,
            size,
            diagnostic.RawBytes,
            diagnostic.HasSupportedSubtype,
            diagnostic.ItemFields,
            diagnostic.SceneryFields,
            actualSize: readResult.ActualSize,
            isReadComplete: true,
            outcome: diagnostic.HasSupportedSubtype && diagnostic.HasExpectedSize
                ? PrototypeInspectionOutcome.Success
                : PrototypeInspectionOutcome.Partial);
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

        if (!TryReadEntrySize(entry, MaximumPrototypeSizeBytes, out var size, out var sizeError))
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

    private Fallout2StandalonePrototypeInspection CreateStandaloneInspection(
        ArchiveEntry entry,
        string status,
        FalloutGame? game,
        ArchiveFormat? format,
        int? prototypePid,
        int? pidType,
        int? subtype,
        long? fileSize,
        ReadOnlyMemory<byte> rawBytes,
        bool hasSupportedSubtype = false,
        IReadOnlyList<Fallout2PrototypeField>? itemFields = null,
        IReadOnlyList<Fallout2PrototypeField>? sceneryFields = null,
        long? actualSize = null,
        bool isReadComplete = false,
        PrototypeInspectionOutcome outcome = PrototypeInspectionOutcome.Error)
    {
        return new Fallout2StandalonePrototypeInspection(
            entry.Path,
            status,
            game,
            format,
            prototypePid,
            pidType,
            subtype,
            fileSize,
            rawBytes)
        {
            PidTypeName = pidType switch
            {
                0 => "Item",
                2 => "Scenery",
                _ => null
            },
            SubtypeName = (pidType, subtype) switch
            {
                (0, 0) => "Armor",
                (0, 1) => "Container",
                (0, 2) => "Drug",
                (0, 3) => "Weapon",
                (0, 4) => "Ammo",
                (0, 5) => "Misc",
                (0, 6) => "Key",
                (2, 0) => "Door",
                (2, 1) => "Stairs",
                (2, 2) => "Elevator",
                (2, 3) => "Ladder Up",
                (2, 4) => "Ladder Down",
                (2, 5) => "Generic",
                _ => null
            },
            HasSupportedSubtype = hasSupportedSubtype,
            ItemFields = itemFields ?? [],
            SceneryFields = sceneryFields ?? [],
            ArchivePath = _archive?.Path,
            ArchiveLocation = entry.ArchiveLocation,
            CompressedSize = entry.CompressedSize,
            ActualSize = actualSize,
            IsReadComplete = isReadComplete,
            Outcome = outcome
        };
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

        if (pidType == 0)
        {
            var expectedItemSize = GetExpectedItemSize(subtype);
            var itemFields = ReadItemFields(prototypeBytes.Span, subtype);
            var itemStatus = prototypeBytes.Length switch
            {
                var size when size < expectedItemSize =>
                    $"the item prototype is shorter than {expectedItemSize} bytes for subtype {subtype} (found {size})",
                var size when size > expectedItemSize =>
                    $"the item prototype is longer than expected {expectedItemSize} bytes for subtype {subtype} (found {size})",
                _ => "Prototype PID, subtype, and Item size validated."
            };

            return new Fallout2PrototypeDiagnostic(
                requestedPid,
                itemStatus,
                fileName,
                fileName,
                prototypePid,
                subtype,
                fileSize,
                prototypeBytes)
            {
                HasSupportedSubtype = true,
                HasExpectedSize = prototypeBytes.Length == expectedItemSize,
                ItemFields = itemFields
            };
        }

        var expectedScenerySize = subtype <= 2
            ? SceneryTwoWordPrototypeSize
            : SceneryOneWordPrototypeSize;
        var sceneryFields = ReadSceneryFields(prototypeBytes.Span, subtype);
        var status = prototypeBytes.Length < expectedScenerySize
            ? $"the scenery prototype is shorter than {expectedScenerySize} bytes for subtype {subtype}"
            : "Prototype PID type and subtype recognized.";

        return new Fallout2PrototypeDiagnostic(
            requestedPid,
            status,
            fileName,
            fileName,
            prototypePid,
            subtype,
            fileSize,
            prototypeBytes)
        {
            HasSupportedSubtype = true,
            HasExpectedSize = prototypeBytes.Length >= expectedScenerySize,
            SceneryFields = sceneryFields
        };
    }

    private static IReadOnlyList<Fallout2PrototypeField> ReadSceneryFields(
        ReadOnlySpan<byte> prototypeBytes,
        int subtype)
    {
        var fields = new List<Fallout2PrototypeField>();
        AddInt32Field(fields, prototypeBytes, "messageId", 0x04);
        AddInt32Field(fields, prototypeBytes, "fid", 0x08, displayAsHex: true);
        AddInt32Field(fields, prototypeBytes, "lightDistance", 0x0C);
        AddInt32Field(fields, prototypeBytes, "lightIntensity", 0x10);
        AddInt32Field(fields, prototypeBytes, "flags", 0x14, isFlags: true);
        AddInt32Field(fields, prototypeBytes, "extendedFlags", 0x18, isFlags: true);
        AddInt32Field(
            fields,
            prototypeBytes,
            "sid",
            0x1C,
            interpretationResolver: value => value == -1 ? "No script" : null);
        AddInt32Field(
            fields,
            prototypeBytes,
            "material",
            0x24,
            interpretationResolver: GetMaterialName);
        AddUInt8Field(
            fields,
            prototypeBytes,
            "field_34",
            0x28,
            "Uninterpreted byte; sound ID is not confirmed.");

        switch (subtype)
        {
            case 0:
                AddInt32Field(fields, prototypeBytes, "openFlags", 0x29, isFlags: true);
                AddInt32Field(fields, prototypeBytes, "keyCode", 0x2D);
                break;
            case 1:
                AddInt32Field(fields, prototypeBytes, "stairs.field_0", 0x29);
                AddInt32Field(fields, prototypeBytes, "stairs.field_4", 0x2D);
                break;
            case 2:
                AddInt32Field(fields, prototypeBytes, "elevator.type", 0x29);
                AddInt32Field(fields, prototypeBytes, "elevator.level", 0x2D);
                break;
            case 3:
                AddInt32Field(fields, prototypeBytes, "ladder.field_0", 0x29);
                break;
            case 4:
                AddInt32Field(fields, prototypeBytes, "ladder.field_0", 0x29);
                break;
            case 5:
                AddInt32Field(
                    fields,
                    prototypeBytes,
                    "generic.field_0",
                    0x29,
                    interpretationResolver: _ => "Uninterpreted raw field; meaning is not confirmed.");
                break;
        }

        return fields;
    }

    private static IReadOnlyList<Fallout2PrototypeField> ReadItemFields(
        ReadOnlySpan<byte> prototypeBytes,
        int subtype)
    {
        var fields = new List<Fallout2PrototypeField>();
        AddInt32Field(fields, prototypeBytes, "messageId", 0x04);
        AddInt32Field(fields, prototypeBytes, "fid", 0x08, displayAsHex: true);
        AddInt32Field(fields, prototypeBytes, "lightDistance", 0x0C);
        AddInt32Field(fields, prototypeBytes, "lightIntensity", 0x10);
        AddInt32Field(fields, prototypeBytes, "flags", 0x14, isFlags: true);
        AddInt32Field(fields, prototypeBytes, "extendedFlags", 0x18, isFlags: true);
        AddInt32Field(
            fields,
            prototypeBytes,
            "sid",
            0x1C,
            interpretationResolver: value => value == -1 ? "No script" : null);
        AddInt32Field(fields, prototypeBytes, "material", 0x24, interpretationResolver: GetMaterialName);
        AddInt32Field(fields, prototypeBytes, "size", 0x28);
        AddInt32Field(fields, prototypeBytes, "weight", 0x2C);
        AddInt32Field(fields, prototypeBytes, "cost", 0x30);
        AddInt32Field(fields, prototypeBytes, "inventoryFid", 0x34, displayAsHex: true);
        AddUInt8Field(
            fields,
            prototypeBytes,
            "field_80",
            0x38,
            "Uninterpreted byte; sound ID is not confirmed.");

        switch (subtype)
        {
            case 0:
                AddInt32Field(fields, prototypeBytes, "armorClass", 0x39);
                AddInt32ArrayFields(fields, prototypeBytes, "damageResistance", 0x3D, 7);
                AddInt32ArrayFields(fields, prototypeBytes, "damageThreshold", 0x59, 7);
                AddInt32Field(fields, prototypeBytes, "perk", 0x75);
                AddInt32Field(fields, prototypeBytes, "maleFid", 0x79, displayAsHex: true);
                AddInt32Field(fields, prototypeBytes, "femaleFid", 0x7D, displayAsHex: true);
                break;
            case 1:
                AddInt32Field(fields, prototypeBytes, "maxSize", 0x39);
                AddInt32Field(fields, prototypeBytes, "openFlags", 0x3D, isFlags: true);
                break;
            case 2:
                AddInt32ArrayFields(fields, prototypeBytes, "stat", 0x39, 3);
                AddInt32ArrayFields(fields, prototypeBytes, "amount", 0x45, 3);
                AddInt32Field(fields, prototypeBytes, "duration1", 0x51);
                AddInt32ArrayFields(fields, prototypeBytes, "amount1", 0x55, 3);
                AddInt32Field(fields, prototypeBytes, "duration2", 0x61);
                AddInt32ArrayFields(fields, prototypeBytes, "amount2", 0x65, 3);
                AddInt32Field(fields, prototypeBytes, "addictionChance", 0x71);
                AddInt32Field(fields, prototypeBytes, "withdrawalEffect", 0x75);
                AddInt32Field(fields, prototypeBytes, "withdrawalOnset", 0x79);
                break;
            case 3:
                AddInt32Field(fields, prototypeBytes, "animationCode", 0x39);
                AddInt32Field(fields, prototypeBytes, "minDamage", 0x3D);
                AddInt32Field(fields, prototypeBytes, "maxDamage", 0x41);
                AddInt32Field(fields, prototypeBytes, "damageType", 0x45);
                AddInt32Field(fields, prototypeBytes, "maxRange1", 0x49);
                AddInt32Field(fields, prototypeBytes, "maxRange2", 0x4D);
                AddInt32Field(fields, prototypeBytes, "projectilePid", 0x51, displayAsHex: true);
                AddInt32Field(fields, prototypeBytes, "minStrength", 0x55);
                AddInt32Field(fields, prototypeBytes, "actionPointCost1", 0x59);
                AddInt32Field(fields, prototypeBytes, "actionPointCost2", 0x5D);
                AddInt32Field(fields, prototypeBytes, "criticalFailureType", 0x61);
                AddInt32Field(fields, prototypeBytes, "perk", 0x65);
                AddInt32Field(fields, prototypeBytes, "rounds", 0x69);
                AddInt32Field(fields, prototypeBytes, "caliber", 0x6D);
                AddInt32Field(fields, prototypeBytes, "ammoTypePid", 0x71, displayAsHex: true);
                AddInt32Field(fields, prototypeBytes, "ammoCapacity", 0x75);
                AddUInt8Field(fields, prototypeBytes, "soundCode", 0x79);
                break;
            case 4:
                AddInt32Field(fields, prototypeBytes, "caliber", 0x39);
                AddInt32Field(fields, prototypeBytes, "quantity", 0x3D);
                AddInt32Field(fields, prototypeBytes, "armorClassModifier", 0x41);
                AddInt32Field(fields, prototypeBytes, "damageResistanceModifier", 0x45);
                AddInt32Field(fields, prototypeBytes, "damageMultiplier", 0x49);
                AddInt32Field(fields, prototypeBytes, "damageDivisor", 0x4D);
                break;
            case 5:
                AddInt32Field(fields, prototypeBytes, "powerTypePid", 0x39, displayAsHex: true);
                AddInt32Field(fields, prototypeBytes, "powerType", 0x3D);
                AddInt32Field(fields, prototypeBytes, "charges", 0x41);
                break;
            case 6:
                AddInt32Field(fields, prototypeBytes, "keyCode", 0x39);
                break;
        }

        return fields;
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

    private static void AddInt32ArrayFields(
        List<Fallout2PrototypeField> fields,
        ReadOnlySpan<byte> prototypeBytes,
        string name,
        int offset,
        int count)
    {
        for (var index = 0; index < count; index++)
        {
            AddInt32Field(fields, prototypeBytes, $"{name}[{index}]", offset + index * sizeof(int));
        }
    }

    private static void AddInt32Field(
        List<Fallout2PrototypeField> fields,
        ReadOnlySpan<byte> prototypeBytes,
        string name,
        int offset,
        bool isFlags = false,
        bool displayAsHex = false,
        Func<int, string?>? interpretationResolver = null)
    {
        if (prototypeBytes.Length - offset < sizeof(int))
        {
            return;
        }

        var value = BinaryPrimitives.ReadInt32BigEndian(
            prototypeBytes.Slice(offset, sizeof(int)));
        fields.Add(new Fallout2PrototypeField(
            name,
            offset,
            sizeof(int),
            value,
            isFlags,
            displayAsHex,
            interpretationResolver?.Invoke(value)));
    }

    private static void AddUInt8Field(
        List<Fallout2PrototypeField> fields,
        ReadOnlySpan<byte> prototypeBytes,
        string name,
        int offset,
        string? interpretation = null)
    {
        if ((uint)offset >= (uint)prototypeBytes.Length)
        {
            return;
        }

        fields.Add(new Fallout2PrototypeField(
            name,
            offset,
            sizeof(byte),
            prototypeBytes[offset],
            Interpretation: interpretation,
            StorageType: "uint8"));
    }

    private static string? GetMaterialName(int value) =>
        value switch
        {
            0 => "Glass",
            1 => "Metal",
            2 => "Plastic",
            3 => "Wood",
            4 => "Dirt",
            5 => "Stone",
            6 => "Cement",
            7 => "Leather",
            _ => null
        };

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

    private static async Task<StandaloneEntryReadResult> ReadStandaloneEntryAsync(
        IGameArchive archive,
        ArchiveEntry entry,
        int expectedSize,
        CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream(expectedSize);
        try
        {
            await archive.CopyEntryToAsync(entry, stream, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = stream.ToArray();
            if (stream.Length != expectedSize)
            {
                return new StandaloneEntryReadResult(
                    bytes,
                    stream.Length,
                    false,
                    $"Could not read standalone PRO entry: Archive entry '{entry.Path}' "
                        + $"declared {expectedSize} bytes but yielded {stream.Length}.");
            }

            return new StandaloneEntryReadResult(bytes, stream.Length, true, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidDataException
            or IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException)
        {
            return new StandaloneEntryReadResult(
                stream.ToArray(),
                stream.Length,
                false,
                $"Could not read standalone PRO entry: {exception.Message}");
        }
    }

    private sealed record StandaloneEntryReadResult(
        byte[] RawBytes,
        long ActualSize,
        bool IsComplete,
        string? Error);

    private sealed record PrototypeListResult(string[]? Lines, string? Error);

    private sealed record PrototypeLocation(
        string? FileName,
        ArchiveEntry? Entry,
        long? FileSize,
        string? Error);

    private sealed record CachedPrototype(int Pid, byte[] Bytes);
}
