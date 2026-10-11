using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using FalloutAssetExplorer.Core;
using Xunit;
using Xunit.Abstractions;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class ArchiveExtractionServiceTests
{
    private readonly ITestOutputHelper _output;

    public ArchiveExtractionServiceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task ExtractAsync_ExtractsSingleDat1EntryToPreservedRelativePath()
    {
        var payload = "Fallout DAT1 payload"u8.ToArray();
        using var archiveFile = new TemporaryArchive(Dat1Fixture.Create(
            "art",
            "hero.frm",
            Dat1Archive.RawEntryType,
            payload,
            payload.Length));

        var archive = Dat1Archive.Open(archiveFile.Path);
        using var destination = new TemporaryDirectory();

        var summary = await new ArchiveExtractionService().ExtractAsync(
            archive,
            archive.Entries,
            destination.Path);

        Assert.Equal(1, summary.Successful);
        Assert.Equal(0, summary.Failed);
        Assert.Equal(0, summary.Skipped);
        var outputPath = Path.Combine(destination.Path, "art", "hero.frm");
        Assert.Equal(payload, File.ReadAllBytes(outputPath));
    }

    [Fact]
    public async Task ExtractAsync_ExtractsMultipleDat2EntriesAndPreservesRelativePaths()
    {
        var first = new byte[] { 0x01, 0x02, 0x03 };
        var second = "fallout 2 payload"u8.ToArray();
        using var archiveFile = new TemporaryArchive(Dat2Fixture.Create(
            new Dat2FixtureEntry("art/hero.frm", false, first),
            new Dat2FixtureEntry("text/dialogue.msg", false, second)));

        var archive = Dat2Archive.Open(archiveFile.Path);
        using var destination = new TemporaryDirectory();

        var summary = await new ArchiveExtractionService().ExtractAsync(
            archive,
            archive.Entries,
            destination.Path);

        Assert.Equal(2, summary.Successful);
        Assert.Equal(first, File.ReadAllBytes(Path.Combine(destination.Path, "art", "hero.frm")));
        Assert.Equal(second, File.ReadAllBytes(Path.Combine(destination.Path, "text", "dialogue.msg")));
    }

    [Fact]
    public async Task ExtractAsync_SkipsExistingFilesWhenConfigured()
    {
        var payload = "hello"u8.ToArray();
        using var archiveFile = new TemporaryArchive(Dat1Fixture.Create(
            "art",
            "hero.frm",
            Dat1Archive.RawEntryType,
            payload,
            payload.Length));

        var archive = Dat1Archive.Open(archiveFile.Path);
        using var destination = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(destination.Path, "art"));
        var existing = Path.Combine(destination.Path, "art", "hero.frm");
        File.WriteAllBytes(existing, "existing"u8.ToArray());

        var summary = await new ArchiveExtractionService().ExtractAsync(
            archive,
            archive.Entries,
            destination.Path,
            new ExtractionOptions(OverwritePolicy.SkipExisting));

        Assert.Equal(1, summary.Skipped);
        Assert.Equal(0, summary.Successful);
        Assert.Equal("existing", Encoding.ASCII.GetString(File.ReadAllBytes(existing)));
    }

    [Fact]
    public async Task ExtractAsync_OverwritesExistingFilesWhenConfigured()
    {
        var payload = "replacement"u8.ToArray();
        using var archiveFile = new TemporaryArchive(Dat1Fixture.Create(
            ".",
            "hero.frm",
            Dat1Archive.RawEntryType,
            payload,
            payload.Length));

        var archive = Dat1Archive.Open(archiveFile.Path);
        using var destination = new TemporaryDirectory();
        var outputPath = Path.Combine(destination.Path, "hero.frm");
        File.WriteAllBytes(outputPath, "old"u8.ToArray());

        var summary = await new ArchiveExtractionService().ExtractAsync(
            archive,
            archive.Entries,
            destination.Path,
            new ExtractionOptions(OverwritePolicy.OverwriteExisting));

        Assert.Equal(1, summary.Successful);
        Assert.Equal(payload, File.ReadAllBytes(outputPath));
    }

    [Fact]
    public void ResolveOutputPath_RejectsTraversalAndAbsolutePaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "fae-path-test");
        Directory.CreateDirectory(root);

        Assert.Null(ArchiveExtractionService.ResolveOutputPath(root, "../escape.txt"));
        Assert.Null(ArchiveExtractionService.ResolveOutputPath(root, "/absolute/path.bin"));
        Assert.Null(ArchiveExtractionService.ResolveOutputPath(root, "C:/windows/system32/drivers/etc/hosts"));
        Assert.Null(ArchiveExtractionService.ResolveOutputPath(root, "\\\\server\\share\\evil.bin"));
        Assert.Null(ArchiveExtractionService.ResolveOutputPath(root, "folder/../../evil.bin"));

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task ExtractAsync_RejectsDuplicateNormalizedOutputPaths()
    {
        var first = new ArchiveEntry("art/hero.frm", 3, null, "archive.dat");
        var second = new ArchiveEntry("art\\hero.frm", 3, null, "archive.dat");
        using var destination = new TemporaryDirectory();

        var archive = new FakeArchive(
            [first, second],
            "archive.dat");

        var summary = await new ArchiveExtractionService().ExtractAsync(
            archive,
            archive.BrowserEntries,
            destination.Path);

        Assert.Equal(1, summary.Successful);
        Assert.Equal(1, summary.Skipped);
    }

    [Fact]
    public async Task ExtractAsync_CleansUpTemporaryFilesOnFailure()
    {
        var entry = new ArchiveEntry("art/hero.frm", 3, null, "archive.dat");
        using var destination = new TemporaryDirectory();
        var archive = new ThrowingArchive(entry);

        var summary = await new ArchiveExtractionService().ExtractAsync(
            archive,
            archive.BrowserEntries,
            destination.Path);

        Assert.Equal(1, summary.Failed);
        Assert.Contains("InvalidDataException", Assert.Single(summary.Results).Message);
        var tempFolder = Path.Combine(destination.Path, ".fae-extract");
        Assert.False(Directory.Exists(tempFolder) && Directory.EnumerateFiles(tempFolder, "*.tmp", SearchOption.AllDirectories).Any());
        Assert.False(File.Exists(Path.Combine(destination.Path, "art", "hero.frm")));
    }

    [Fact]
    public async Task ExtractAsync_CanCancelDuringOperation()
    {
        var entry = new ArchiveEntry("art/hero.frm", 3, null, "archive.dat");
        using var destination = new TemporaryDirectory();
        using var cts = new CancellationTokenSource();
        var archive = new CancelAwareArchive(entry, cts);

        cts.CancelAfter(50);
        var summary = await new ArchiveExtractionService().ExtractAsync(
            archive,
            archive.BrowserEntries,
            destination.Path,
            cancellationToken: cts.Token);

        Assert.True(summary.WasCancelled);
        Assert.Equal(1, summary.Cancelled);
    }

    [Fact]
    public async Task ExtractAsync_CancellationBeforeStartingReturnsCancelledSummary()
    {
        var entries = new[]
        {
            new ArchiveEntry("first.bin", 1, null, "archive.dat"),
            new ArchiveEntry("second.bin", 1, null, "archive.dat")
        };
        using var destination = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var summary = await new ArchiveExtractionService().ExtractAsync(
            new FakeArchive(entries, "archive.dat"),
            entries,
            destination.Path,
            cancellationToken: cancellation.Token);

        Assert.True(summary.WasCancelled);
        Assert.Equal(2, summary.TotalEntries);
        Assert.Equal(0, summary.Successful);
        Assert.Equal(0, summary.Skipped);
        Assert.Equal(0, summary.Failed);
        Assert.Equal(2, summary.Cancelled);
        Assert.All(summary.Results, result => Assert.Equal(ExtractionStatus.Cancelled, result.Status));
    }

    [Fact]
    public async Task ExtractAsync_CancellationBetweenEntriesKeepsCompletedOutputAndCountsRemainingEntries()
    {
        var entries = new[]
        {
            new ArchiveEntry("first.bin", 1, null, "archive.dat"),
            new ArchiveEntry("second.bin", 1, null, "archive.dat")
        };
        using var destination = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        var archive = new CancellingAfterFirstEntryArchive(entries);

        var summary = await new ArchiveExtractionService().ExtractAsync(
            archive,
            entries,
            destination.Path,
            progress: new CancelOnSuccessfulEntryProgress(cancellation),
            cancellationToken: cancellation.Token);

        Assert.True(summary.WasCancelled);
        Assert.Equal(1, summary.Successful);
        Assert.Equal(0, summary.Skipped);
        Assert.Equal(0, summary.Failed);
        Assert.Equal(1, summary.Cancelled);
        Assert.Equal([0x41], File.ReadAllBytes(Path.Combine(destination.Path, "first.bin")));
        Assert.False(File.Exists(Path.Combine(destination.Path, "second.bin")));
    }

    [Fact]
    public async Task ExtractAsync_CancellingOverwritePreservesExistingFileAndCleansTemporaryFile()
    {
        var entry = new ArchiveEntry("art/hero.frm", 8, null, "archive.dat");
        var originalContents = "original file contents"u8.ToArray();
        using var destination = new TemporaryDirectory();
        var outputDirectory = Path.Combine(destination.Path, "art");
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, "hero.frm");
        File.WriteAllBytes(outputPath, originalContents);
        using var cancellation = new CancellationTokenSource();

        var summary = await new ArchiveExtractionService().ExtractAsync(
            new PartialWriteThenCancelArchive(entry, cancellation),
            [entry],
            destination.Path,
            new ExtractionOptions(OverwritePolicy.OverwriteExisting),
            cancellationToken: cancellation.Token);

        Assert.True(summary.WasCancelled);
        Assert.Equal(0, summary.Successful);
        Assert.Equal(1, summary.Cancelled);
        Assert.True(File.Exists(outputPath));
        Assert.Equal(originalContents, File.ReadAllBytes(outputPath));
        var tempDirectory = Path.Combine(destination.Path, ".fae-extract");
        Assert.False(Directory.Exists(tempDirectory)
            && Directory.EnumerateFiles(tempDirectory, "*.tmp", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task ExtractAsync_RejectsDestinationSubdirectorySymbolicLink()
    {
        using var destination = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        var linkPath = Path.Combine(destination.Path, "linked");
        if (!TryCreateDirectoryLink(linkPath, outside.Path))
        {
            return;
        }

        try
        {
            var entry = new ArchiveEntry("linked/escaped.bin", 7, null, "archive.dat");
            var summary = await new ArchiveExtractionService().ExtractAsync(
                new FakeArchive([entry], "archive.dat"),
                [entry],
                destination.Path);

            Assert.Equal(0, summary.Successful);
            Assert.Equal(1, summary.Failed);
            Assert.Contains("symbolic link or filesystem junction", Assert.Single(summary.Results).Message);
            Assert.False(File.Exists(Path.Combine(outside.Path, "escaped.bin")));
        }
        finally
        {
            Directory.Delete(linkPath);
        }
    }

    [Fact]
    public async Task ExtractAsync_RejectsTemporaryDirectorySymbolicLink()
    {
        using var destination = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        if (!TryCreateDirectoryLink(Path.Combine(destination.Path, ".fae-extract"), outside.Path))
        {
            return;
        }

        var tempLinkPath = Path.Combine(destination.Path, ".fae-extract");
        try
        {
            var entry = new ArchiveEntry("escaped.bin", 7, null, "archive.dat");
            var summary = await new ArchiveExtractionService().ExtractAsync(
                new FakeArchive([entry], "archive.dat"),
                [entry],
                destination.Path);

            Assert.Equal(0, summary.Successful);
            Assert.Equal(1, summary.Failed);
            Assert.False(File.Exists(Path.Combine(outside.Path, "escaped.bin")));
            Assert.Empty(Directory.GetFiles(outside.Path));
        }
        finally
        {
            Directory.Delete(tempLinkPath);
        }
    }

    private static class Dat1Fixture
    {
        public static byte[] Create(
            string folder,
            string fileName,
            uint type,
            byte[] payload,
            long unpackedSize)
        {
            using var stream = new MemoryStream();
            WriteUInt32(stream, folder.Length == 0 ? 0u : 1u);
            WriteUInt32(stream, 0);
            WriteUInt32(stream, 0);
            WriteUInt32(stream, 0);

            WriteName(stream, folder);
            WriteUInt32(stream, 1u);
            WriteUInt32(stream, 0);
            WriteUInt32(stream, 0);
            WriteUInt32(stream, 0);

            WriteName(stream, fileName);
            WriteUInt32(stream, type);
            var offsetPosition = stream.Position;
            WriteUInt32(stream, 0);
            WriteUInt32(stream, checked((uint)unpackedSize));
            WriteUInt32(stream, checked((uint)payload.Length));

            var dataOffset = checked((uint)stream.Position);
            stream.Position = offsetPosition;
            WriteUInt32(stream, dataOffset);
            stream.Position = stream.Length;
            stream.Write(payload);
            return stream.ToArray();
        }

        private static void WriteName(Stream stream, string value)
        {
            var bytes = Encoding.Latin1.GetBytes(value);
            stream.WriteByte((byte)bytes.Length);
            stream.Write(bytes);
        }

        private static void WriteUInt32(Stream stream, uint value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
            stream.Write(buffer);
        }
    }

    private static class Dat2Fixture
    {
        public static byte[] Create(params Dat2FixtureEntry[] entries) =>
            Create(entries, []);

        public static byte[] Create(Dat2FixtureEntry[] entries, byte[] prefix)
        {
            using var data = new MemoryStream();
            data.Write(prefix);
            var offsets = new uint[entries.Length];
            for (var index = 0; index < entries.Length; index++)
            {
                offsets[index] = checked((uint)(data.Position - prefix.Length));
                data.Write(entries[index].Payload);
            }

            using var directory = new MemoryStream();
            WriteUInt32(directory, checked((uint)entries.Length));
            for (var index = 0; index < entries.Length; index++)
            {
                var entry = entries[index];
                var pathBytes = Encoding.ASCII.GetBytes(entry.Path);
                WriteUInt32(directory, checked((uint)pathBytes.Length));
                directory.Write(pathBytes);
                directory.WriteByte(entry.IsCompressed ? (byte)1 : (byte)0);
                WriteUInt32(directory, checked((uint)entry.Payload.Length));
                WriteUInt32(directory, checked((uint)entry.Payload.Length));
                WriteUInt32(directory, offsets[index]);
            }

            using var archive = new MemoryStream();
            data.Position = 0;
            data.CopyTo(archive);
            directory.Position = 0;
            directory.CopyTo(archive);
            WriteUInt32(archive, checked((uint)directory.Length));
            WriteUInt32(
                archive,
                checked((uint)(data.Length - prefix.Length + directory.Length + FooterSize)));
            return archive.ToArray();
        }

        private static void WriteUInt32(Stream stream, uint value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
            stream.Write(buffer);
        }

        private const int FooterSize = 8;
    }

    private sealed record Dat2FixtureEntry(string Path, bool IsCompressed, byte[] Payload);

    private sealed class TemporaryArchive : IDisposable
    {
        public TemporaryArchive(byte[] bytes)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"fae-{Guid.NewGuid():N}.dat");
            File.WriteAllBytes(Path, bytes);
        }

        public string Path { get; }

        public void Dispose() => File.Delete(Path);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"fae-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class FakeArchive : IGameArchive
    {
        public FakeArchive(IReadOnlyList<ArchiveEntry> entries, string path)
        {
            BrowserEntries = entries;
            Path = path;
        }

        public string Path { get; }
        public FalloutGame Game => FalloutGame.Fallout1;
        public ArchiveFormat Format => ArchiveFormat.Dat1;
        public IReadOnlyList<ArchiveEntry> BrowserEntries { get; }

        public Task CopyEntryToAsync(ArchiveEntry entry, Stream destination, CancellationToken cancellationToken = default)
        {
            var payload = "payload"u8.ToArray();
            destination.Write(payload);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingArchive : IGameArchive
    {
        public ThrowingArchive(params ArchiveEntry[] entries)
        {
            BrowserEntries = entries;
        }

        public string Path => "throwing.dat";
        public FalloutGame Game => FalloutGame.Fallout1;
        public ArchiveFormat Format => ArchiveFormat.Dat1;
        public IReadOnlyList<ArchiveEntry> BrowserEntries { get; }

        public Task CopyEntryToAsync(ArchiveEntry entry, Stream destination, CancellationToken cancellationToken = default)
            => throw new InvalidDataException("corrupt entry");
    }

    private sealed class CancelAwareArchive : IGameArchive
    {
        private readonly CancellationTokenSource _tokenSource;

        public CancelAwareArchive(ArchiveEntry entry, CancellationTokenSource tokenSource)
        {
            BrowserEntries = [entry];
            _tokenSource = tokenSource;
        }

        public string Path => "cancel.dat";
        public FalloutGame Game => FalloutGame.Fallout1;
        public ArchiveFormat Format => ArchiveFormat.Dat1;
        public IReadOnlyList<ArchiveEntry> BrowserEntries { get; }

        public async Task CopyEntryToAsync(ArchiveEntry entry, Stream destination, CancellationToken cancellationToken = default)
        {
            _tokenSource.Token.ThrowIfCancellationRequested();
            await Task.Delay(200, cancellationToken);
            destination.WriteByte(0x01);
        }
    }

    private sealed class PartialWriteThenCancelArchive : IGameArchive
    {
        private readonly CancellationTokenSource _cancellation;

        public PartialWriteThenCancelArchive(ArchiveEntry entry, CancellationTokenSource cancellation)
        {
            BrowserEntries = [entry];
            _cancellation = cancellation;
        }

        public string Path => "cancel.dat";
        public FalloutGame Game => FalloutGame.Fallout1;
        public ArchiveFormat Format => ArchiveFormat.Dat1;
        public IReadOnlyList<ArchiveEntry> BrowserEntries { get; }

        public async Task CopyEntryToAsync(ArchiveEntry entry, Stream destination, CancellationToken cancellationToken = default)
        {
            await destination.WriteAsync("partial"u8.ToArray(), cancellationToken);
            _cancellation.Cancel();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }

    private sealed class CancellingAfterFirstEntryArchive : IGameArchive
    {
        public CancellingAfterFirstEntryArchive(IReadOnlyList<ArchiveEntry> entries)
        {
            BrowserEntries = entries;
        }

        public string Path => "cancel-between.dat";
        public FalloutGame Game => FalloutGame.Fallout1;
        public ArchiveFormat Format => ArchiveFormat.Dat1;
        public IReadOnlyList<ArchiveEntry> BrowserEntries { get; }

        public Task CopyEntryToAsync(ArchiveEntry entry, Stream destination, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            destination.WriteByte(0x41);
            return Task.CompletedTask;
        }
    }

    private sealed class CancelOnSuccessfulEntryProgress(CancellationTokenSource cancellation) : IProgress<ExtractionProgress>
    {
        public void Report(ExtractionProgress value)
        {
            if (value.Message == "Extracted successfully.")
            {
                cancellation.Cancel();
            }
        }
    }

    private bool TryCreateDirectoryLink(string linkPath, string targetPath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                startInfo.ArgumentList.Add("/d");
                startInfo.ArgumentList.Add("/c");
                startInfo.ArgumentList.Add("mklink");
                startInfo.ArgumentList.Add("/J");
                startInfo.ArgumentList.Add(linkPath);
                startInfo.ArgumentList.Add(targetPath);

                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    _output.WriteLine("Link assertion not run: could not start the Windows junction command.");
                    return false;
                }

                var standardOutput = process.StandardOutput.ReadToEnd();
                var standardError = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    _output.WriteLine(
                        $"Link assertion not run: Windows could not create a directory junction. {standardOutput} {standardError}");
                    return false;
                }
            }
            else
            {
                Directory.CreateSymbolicLink(linkPath, targetPath);
            }

            return true;
        }
        catch (UnauthorizedAccessException exception)
        {
            _output.WriteLine($"Symbolic-link assertion not run: creating links is not permitted ({exception.Message}).");
            return false;
        }
        catch (PlatformNotSupportedException exception)
        {
            _output.WriteLine($"Symbolic-link assertion not run: directory links are unsupported ({exception.Message}).");
            return false;
        }
        catch (IOException exception)
        {
            _output.WriteLine($"Symbolic-link assertion not run: the filesystem cannot create the link ({exception.Message}).");
            return false;
        }
    }
}
