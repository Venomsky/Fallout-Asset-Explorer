using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class Fallout2PrototypeResolverTests
{
    [Fact]
    public async Task ResolveAsync_ReturnsRawBytesSizeAndVerifiedHeader()
    {
        const int pid = 0x02000001;
        var prototypeBytes = CreatePrototype(pid, subtype: 5);
        var archive = CreateArchive(
            "scenery",
            "scenery.lst",
            "tree.pro\n",
            ("tree.pro", prototypeBytes));
        var resolver = new Fallout2PrototypeResolver(archive);

        var result = await resolver.ResolveAsync(pid);

        Assert.Equal("tree.pro", result.VerifiedFileName);
        Assert.Equal(pid, result.PrototypePid);
        Assert.Equal(5, result.Subtype);
        Assert.Equal(prototypeBytes.Length, result.FileSize);
        Assert.Equal(prototypeBytes, result.RawBytes.ToArray());
        Assert.True(result.HasSupportedSubtype);
    }

    [Fact]
    public async Task ResolveAsync_ReportsPrototypePidThatDoesNotMatchSelectedObject()
    {
        const int requestedPid = 0x02000001;
        const int prototypePid = 0x02000002;
        var archive = CreateArchive(
            "scenery",
            "scenery.lst",
            "tree.pro\n",
            ("tree.pro", CreatePrototype(prototypePid, subtype: 5)));

        var result = await new Fallout2PrototypeResolver(archive).ResolveAsync(requestedPid);

        Assert.Null(result.VerifiedFileName);
        Assert.Equal("tree.pro", result.ListedFileName);
        Assert.Equal(prototypePid, result.PrototypePid);
        Assert.Contains("does not match MAP PID", result.Status);
        Assert.NotEmpty(result.RawBytes.ToArray());
    }

    [Fact]
    public async Task ResolveAsync_ReportsMissingPrototypeWithoutRawData()
    {
        var archive = CreateArchive("items", "items.lst", "missing.pro\n");

        var result = await new Fallout2PrototypeResolver(archive).ResolveAsync(0x00000001);

        Assert.Contains("prototype 'missing.pro' is missing", result.Status);
        Assert.Null(result.VerifiedFileName);
        Assert.Empty(result.RawBytes.ToArray());
    }

    [Fact]
    public async Task ResolveAsync_ReportsUnsupportedSubtypeAndPreservesRawFile()
    {
        const int pid = 0x00000001;
        var prototypeBytes = CreatePrototype(pid, subtype: 7);
        var archive = CreateArchive(
            "items",
            "items.lst",
            "unsupported.pro\n",
            ("unsupported.pro", prototypeBytes));

        var result = await new Fallout2PrototypeResolver(archive).ResolveAsync(pid);

        Assert.Equal(7, result.Subtype);
        Assert.Equal("unsupported.pro", result.VerifiedFileName);
        Assert.False(result.HasSupportedSubtype);
        Assert.Contains("unsupported subtype 7", result.Status);
        Assert.Equal(prototypeBytes, result.RawBytes.ToArray());
    }

    [Fact]
    public async Task ResolveAsync_ReportsTruncatedPrototypeAndReturnsAvailableBytes()
    {
        const int pid = 0x02000001;
        var truncatedPrototype = CreatePrototype(pid, subtype: 5)[..35];
        var archive = CreateArchive(
            "scenery",
            "scenery.lst",
            "short.pro\n",
            ("short.pro", truncatedPrototype));

        var result = await new Fallout2PrototypeResolver(archive).ResolveAsync(pid);

        Assert.Contains("shorter than 36 bytes", result.Status);
        Assert.Equal(pid, result.PrototypePid);
        Assert.Null(result.Subtype);
        Assert.Equal(truncatedPrototype.Length, result.FileSize);
        Assert.Equal(truncatedPrototype, result.RawBytes.ToArray());
    }

    [Fact]
    public async Task ResolveAsync_ReportsEntryLengthMismatchWithoutThrowing()
    {
        const int pid = 0x00000001;
        var bytes = CreatePrototype(pid, subtype: 5);
        var archive = CreateArchiveWithEntrySize(
            "items",
            "items.lst",
            "item.pro\n",
            ("item.pro", bytes, (long)bytes.Length + 1));

        var result = await new Fallout2PrototypeResolver(archive).ResolveAsync(pid);

        Assert.Contains("prototype could not be read", result.Status);
        Assert.Empty(result.RawBytes.ToArray());
    }

    [Fact]
    public async Task ResolveAsync_KeepsResultsStableAcrossSelectionsAndReusesCachedReads()
    {
        const int firstPid = 0x02000001;
        const int secondPid = 0x02000002;
        var firstBytes = CreatePrototype(firstPid, subtype: 0);
        var secondBytes = CreatePrototype(secondPid, subtype: 5);
        var archive = CreateArchive(
            "scenery",
            "scenery.lst",
            "door.pro\ngeneric.pro\n",
            ("door.pro", firstBytes),
            ("generic.pro", secondBytes));
        var resolver = new Fallout2PrototypeResolver(archive);

        var first = await resolver.ResolveAsync(firstPid);
        var second = await resolver.ResolveAsync(secondPid);
        var firstAgain = await resolver.ResolveAsync(firstPid);

        Assert.Equal("door.pro", firstAgain.VerifiedFileName);
        Assert.Equal(firstPid, firstAgain.PrototypePid);
        Assert.Equal(0, firstAgain.Subtype);
        Assert.Equal(firstBytes, first.RawBytes.ToArray());
        Assert.Equal(firstBytes, firstAgain.RawBytes.ToArray());
        Assert.Equal("generic.pro", second.VerifiedFileName);
        Assert.Equal(secondPid, second.PrototypePid);
        Assert.Equal(5, second.Subtype);
        Assert.Equal(1, archive.ReadCount("proto/scenery/scenery.lst"));
        Assert.Equal(1, archive.ReadCount("proto/scenery/door.pro"));
        Assert.Equal(1, archive.ReadCount("proto/scenery/generic.pro"));
    }

    private static InMemoryArchive CreateArchive(
        string directory,
        string listFileName,
        string listContents,
        params (string FileName, byte[] Bytes)[] prototypes) =>
        CreateArchiveWithEntrySize(
            directory,
            listFileName,
            listContents,
            prototypes.Select(prototype => (
                prototype.FileName,
                prototype.Bytes,
                (long?)null)).ToArray());

    private static InMemoryArchive CreateArchiveWithEntrySize(
        string directory,
        string listFileName,
        string listContents,
        params (string FileName, byte[] Bytes, long? DeclaredSize)[] prototypes)
    {
        var entries = new Dictionary<string, (ArchiveEntry Entry, byte[] Bytes)>(
            StringComparer.OrdinalIgnoreCase);

        AddEntry($"proto/{directory}/{listFileName}", Encoding.ASCII.GetBytes(listContents));
        foreach (var (fileName, bytes, declaredSize) in prototypes)
        {
            AddEntry($"proto/{directory}/{fileName}", bytes, declaredSize);
        }

        return new InMemoryArchive(entries.Values.Select(item => item.Entry).ToArray(), entries);

        void AddEntry(string path, byte[] bytes, long? declaredSize = null)
        {
            entries.Add(
                path,
                (new ArchiveEntry(path, declaredSize ?? bytes.Length, null, "synthetic-fallout2.dat"), bytes));
        }
    }

    private static byte[] CreatePrototype(int pid, int subtype)
    {
        var bytes = new byte[45];
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(0, sizeof(int)), pid);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(32, sizeof(int)), subtype);
        return bytes;
    }

    private sealed class InMemoryArchive(
        IReadOnlyList<ArchiveEntry> browserEntries,
        Dictionary<string, (ArchiveEntry Entry, byte[] Bytes)> entries) : IGameArchive
    {
        private readonly ConcurrentDictionary<string, int> _readCounts =
            new(StringComparer.OrdinalIgnoreCase);

        public string Path => "synthetic-fallout2.dat";
        public FalloutGame Game => FalloutGame.Fallout2;
        public ArchiveFormat Format => ArchiveFormat.Dat2;
        public IReadOnlyList<ArchiveEntry> BrowserEntries => browserEntries;

        public int ReadCount(string path) =>
            _readCounts.TryGetValue(path, out var count) ? count : 0;

        public async Task CopyEntryToAsync(
            ArchiveEntry entry,
            Stream destination,
            CancellationToken cancellationToken = default)
        {
            if (!entries.TryGetValue(entry.Path, out var item))
            {
                throw new InvalidDataException($"Synthetic entry '{entry.Path}' does not exist.");
            }

            _readCounts.AddOrUpdate(entry.Path, 1, (_, count) => count + 1);
            await destination.WriteAsync(item.Bytes, cancellationToken);
        }
    }
}
