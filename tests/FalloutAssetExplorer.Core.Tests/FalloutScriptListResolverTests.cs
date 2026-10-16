using System.Text;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class FalloutScriptListResolverTests
{
    [Fact]
    public async Task ResolveAsync_MapsZeroBasedSlotsToCandidatePathsWithoutCheckingTargetEntries()
    {
        var archive = CreateArchive(
            FalloutGame.Fallout2,
            ArchiveFormat.Dat2,
            ListEntry("first.int\nmiddle.int\nlast.int"));

        var first = await FalloutScriptListResolver.ResolveAsync(archive, 0);
        var middle = await FalloutScriptListResolver.ResolveAsync(archive, 1);
        var last = await FalloutScriptListResolver.ResolveAsync(archive, 2);

        AssertResolution(first, 0, "first");
        Assert.Equal("scripts/first.int", first.IntPath);
        Assert.Equal("dialog/first.msg", first.MessagePath);
        AssertResolution(middle, 1, "middle");
        AssertResolution(last, 2, "last");
        Assert.Equal(3, archive.GetCopyCount(FalloutScriptListResolver.ScriptListPath));
    }

    [Fact]
    public async Task ResolveAsync_PreservesBlankAndCommentSlots()
    {
        var archive = CreateArchive(
            FalloutGame.Fallout2,
            ArchiveFormat.Dat2,
            ListEntry("first.int\n\n# local_vars=3\nlast.int\n"));

        var blankSlot = await FalloutScriptListResolver.ResolveAsync(archive, 1);
        var commentSlot = await FalloutScriptListResolver.ResolveAsync(archive, 2);
        var lastSlot = await FalloutScriptListResolver.ResolveAsync(archive, 3);

        AssertDiagnostic(blankSlot, FalloutScriptListDiagnosticCode.InvalidScriptName);
        AssertDiagnostic(commentSlot, FalloutScriptListDiagnosticCode.InvalidScriptName);
        AssertResolution(lastSlot, 3, "last");
    }

    [Theory]
    [InlineData(-1, FalloutScriptListDiagnosticCode.InvalidSlot)]
    [InlineData(1, FalloutScriptListDiagnosticCode.SlotOutOfRange)]
    [InlineData(int.MaxValue, FalloutScriptListDiagnosticCode.SlotOutOfRange)]
    public async Task ResolveAsync_ReportsInvalidOrOutOfRangeSlots(
        int slot,
        FalloutScriptListDiagnosticCode expectedCode)
    {
        var archive = CreateArchive(
            FalloutGame.Fallout2,
            ArchiveFormat.Dat2,
            ListEntry("only.int\n"));

        var result = await FalloutScriptListResolver.ResolveAsync(archive, slot);

        AssertDiagnostic(result, expectedCode);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsOutOfRangeForAnEmptyScriptList()
    {
        var archive = CreateArchive(
            FalloutGame.Fallout2,
            ArchiveFormat.Dat2,
            ListEntry(string.Empty));

        var result = await FalloutScriptListResolver.ResolveAsync(archive, 0);

        AssertDiagnostic(result, FalloutScriptListDiagnosticCode.SlotOutOfRange);
    }

    [Theory]
    [InlineData(FalloutGame.Fallout1, ArchiveFormat.Dat1)]
    [InlineData(FalloutGame.Fallout2, ArchiveFormat.Dat2)]
    public async Task ResolveAsync_TreatsCrLfAsOneLineEnding(FalloutGame game, ArchiveFormat format)
    {
        var archive = CreateArchive(
            game,
            format,
            ListEntry("first.int\r\nsecond.int\r\n"));

        var result = await FalloutScriptListResolver.ResolveAsync(archive, 1);

        AssertResolution(result, 1, "second");
    }

    [Fact]
    public async Task ResolveAsync_RejectsNonAsciiBytesInTheSelectedName()
    {
        var archive = CreateArchive(
            FalloutGame.Fallout2,
            ArchiveFormat.Dat2,
            new EntryData(
                FalloutScriptListResolver.ScriptListPath,
                Encoding.UTF8.GetBytes("café.int\n")));

        var result = await FalloutScriptListResolver.ResolveAsync(archive, 0);

        AssertDiagnostic(result, FalloutScriptListDiagnosticCode.InvalidScriptName);
    }

    [Fact]
    public async Task ResolveAsync_DoesNotCreateAnExtraSlotForATerminalLineFeed()
    {
        var archive = CreateArchive(
            FalloutGame.Fallout2,
            ArchiveFormat.Dat2,
            ListEntry("only.int\n"));

        var result = await FalloutScriptListResolver.ResolveAsync(archive, 1);

        AssertDiagnostic(result, FalloutScriptListDiagnosticCode.SlotOutOfRange);
    }

    [Theory]
    [InlineData(FalloutGame.Fallout1, ArchiveFormat.Dat1)]
    [InlineData(FalloutGame.Fallout2, ArchiveFormat.Dat2)]
    public async Task ResolveAsync_PreservesSlotsWhenLongLinesAreSplitByTheCeReadBuffer(
        FalloutGame game,
        ArchiveFormat format)
    {
        var archive = CreateArchive(
            game,
            format,
            ListEntry($"{new string('x', 260)} .int\nsecond.int\n"));

        var continuation = await FalloutScriptListResolver.ResolveAsync(archive, 1);
        var secondScript = await FalloutScriptListResolver.ResolveAsync(archive, 2);

        AssertDiagnostic(continuation, FalloutScriptListDiagnosticCode.InvalidScriptName);
        AssertResolution(secondScript, 2, "second");
    }

    [Theory]
    [InlineData("../outside.int")]
    [InlineData("sub/name.int")]
    [InlineData("sub\\name.int")]
    [InlineData("/absolute.int")]
    [InlineData("\\absolute.int")]
    [InlineData("C:\\absolute.int")]
    [InlineData("bad name.int")]
    [InlineData("bad*.int")]
    [InlineData(".int")]
    public async Task ResolveAsync_RejectsUnsafeOrMalformedNames(string line)
    {
        var archive = CreateArchive(
            FalloutGame.Fallout2,
            ArchiveFormat.Dat2,
            ListEntry($"{line}\n"));

        var result = await FalloutScriptListResolver.ResolveAsync(archive, 0);

        AssertDiagnostic(result, FalloutScriptListDiagnosticCode.InvalidScriptName);
    }

    [Fact]
    public async Task ResolveAsync_UsesFallout1FirstDotAndFallout2LowercaseIntMarkerRules()
    {
        var fallout1 = CreateArchive(
            FalloutGame.Fallout1,
            ArchiveFormat.Dat1,
            ListEntry("unit.other\n"));
        var fallout2 = CreateArchive(
            FalloutGame.Fallout2,
            ArchiveFormat.Dat2,
            ListEntry("unit.INT\n"));

        var fallout1Result = await FalloutScriptListResolver.ResolveAsync(fallout1, 0);
        var fallout2Result = await FalloutScriptListResolver.ResolveAsync(fallout2, 0);

        AssertResolution(fallout1Result, 0, "unit");
        AssertDiagnostic(fallout2Result, FalloutScriptListDiagnosticCode.InvalidScriptName);
    }

    [Fact]
    public async Task ResolveAsync_PreservesSafeScriptNameCaseAndAllowsFallout2NameDots()
    {
        var archive = CreateArchive(
            FalloutGame.Fallout2,
            ArchiveFormat.Dat2,
            ListEntry("My.Unit.int # local_vars=2\n"));

        var result = await FalloutScriptListResolver.ResolveAsync(archive, 0);

        AssertResolution(result, 0, "My.Unit");
        Assert.Equal("scripts/My.Unit.int", result.IntPath);
        Assert.Equal("dialog/My.Unit.msg", result.MessagePath);
    }

    [Fact]
    public async Task ResolveAsync_KeepsRepeatedNamesInDistinctSlots()
    {
        var archive = CreateArchive(
            FalloutGame.Fallout2,
            ArchiveFormat.Dat2,
            ListEntry("same.int\nsame.int\n"));

        var first = await FalloutScriptListResolver.ResolveAsync(archive, 0);
        var second = await FalloutScriptListResolver.ResolveAsync(archive, 1);

        AssertResolution(first, 0, "same");
        AssertResolution(second, 1, "same");
        Assert.Equal(first.IntPath, second.IntPath);
        Assert.Equal(first.MessagePath, second.MessagePath);
    }

    [Fact]
    public async Task ResolveAsync_EnforcesTheConfirmedGameSpecificNameLimits()
    {
        var fallout1FourteenCharacters = CreateArchive(
            FalloutGame.Fallout1,
            ArchiveFormat.Dat1,
            ListEntry("12345678901234.anything\n"));
        var fallout1FifteenCharacters = CreateArchive(
            FalloutGame.Fallout1,
            ArchiveFormat.Dat1,
            ListEntry("123456789012345.anything\n"));
        var fallout1LongerThanFifteen = CreateArchive(
            FalloutGame.Fallout1,
            ArchiveFormat.Dat1,
            ListEntry("12345678901234567890.anything\n"));
        var fallout2Maximum = CreateArchive(
            FalloutGame.Fallout2,
            ArchiveFormat.Dat2,
            ListEntry("1234567890123.int\n"));
        var fallout2TooLong = CreateArchive(
            FalloutGame.Fallout2,
            ArchiveFormat.Dat2,
            ListEntry("12345678901234.int\n"));

        var fallout1FourteenResult = await FalloutScriptListResolver.ResolveAsync(fallout1FourteenCharacters, 0);
        var fallout1FifteenResult = await FalloutScriptListResolver.ResolveAsync(fallout1FifteenCharacters, 0);
        var fallout1LongerResult = await FalloutScriptListResolver.ResolveAsync(fallout1LongerThanFifteen, 0);
        var fallout2MaximumResult = await FalloutScriptListResolver.ResolveAsync(fallout2Maximum, 0);
        var fallout2TooLongResult = await FalloutScriptListResolver.ResolveAsync(fallout2TooLong, 0);

        AssertResolution(fallout1FourteenResult, 0, "12345678901234");
        AssertResolution(fallout1FifteenResult, 0, "123456789012345");
        AssertResolution(fallout1LongerResult, 0, "123456789012345");
        Assert.Equal("scripts/123456789012345.int", fallout1FifteenResult.IntPath);
        Assert.Equal("dialog/123456789012345.msg", fallout1FifteenResult.MessagePath);
        Assert.Equal(fallout1FifteenResult.IntPath, fallout1LongerResult.IntPath);
        Assert.Equal(fallout1FifteenResult.MessagePath, fallout1LongerResult.MessagePath);
        AssertResolution(fallout2MaximumResult, 0, "1234567890123");
        AssertDiagnostic(fallout2TooLongResult, FalloutScriptListDiagnosticCode.InvalidScriptName);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsExplicitDiagnosticsForMissingAndAmbiguousLists()
    {
        var missing = CreateArchive(FalloutGame.Fallout1, ArchiveFormat.Dat1);
        var ambiguous = CreateArchive(
            FalloutGame.Fallout1,
            ArchiveFormat.Dat1,
            ListEntry("one.int\n"),
            ListEntry("two.int\n", path: "SCRIPTS\\SCRIPTS.LST"));

        var missingResult = await FalloutScriptListResolver.ResolveAsync(missing, 0);
        var ambiguousResult = await FalloutScriptListResolver.ResolveAsync(ambiguous, 0);

        AssertDiagnostic(missingResult, FalloutScriptListDiagnosticCode.ScriptListMissing);
        AssertDiagnostic(ambiguousResult, FalloutScriptListDiagnosticCode.ScriptListAmbiguous);
    }

    [Fact]
    public async Task ResolveAsync_RejectsMismatchedAndOversizedScriptLists()
    {
        var mismatched = CreateArchive(
            FalloutGame.Fallout2,
            ArchiveFormat.Dat2,
            ListEntry("only.int\n", declaredSize: 5));
        var oversized = CreateArchive(
            FalloutGame.Fallout2,
            ArchiveFormat.Dat2,
            new EntryData(
                FalloutScriptListResolver.ScriptListPath,
                [],
                DeclaredSize: 1024 * 1024 + 1));

        var mismatchResult = await FalloutScriptListResolver.ResolveAsync(mismatched, 0);
        var oversizedResult = await FalloutScriptListResolver.ResolveAsync(oversized, 0);

        AssertDiagnostic(mismatchResult, FalloutScriptListDiagnosticCode.ScriptListLengthMismatch);
        AssertDiagnostic(oversizedResult, FalloutScriptListDiagnosticCode.ScriptListTooLarge);
        Assert.Equal(0, oversized.GetCopyCount(FalloutScriptListResolver.ScriptListPath));
    }

    [Fact]
    public async Task ResolveAsync_BoundsActualBytesWrittenWhenDeclaredSizeIsSmall()
    {
        var oversizedBytes = new byte[1024 * 1024 + 1];
        var archive = CreateArchive(
            FalloutGame.Fallout2,
            ArchiveFormat.Dat2,
            new EntryData(
                FalloutScriptListResolver.ScriptListPath,
                oversizedBytes,
                DeclaredSize: 0));

        var result = await FalloutScriptListResolver.ResolveAsync(archive, 0);

        AssertDiagnostic(result, FalloutScriptListDiagnosticCode.ScriptListTooLarge);
    }

    [Fact]
    public async Task ResolveAsync_IsDeterministicAndDoesNotModifyListBytes()
    {
        var bytes = Encoding.ASCII.GetBytes("one.int\ntwo.int\n");
        var original = bytes.ToArray();
        var archive = CreateArchive(
            FalloutGame.Fallout1,
            ArchiveFormat.Dat1,
            new EntryData(FalloutScriptListResolver.ScriptListPath, bytes));

        var first = await FalloutScriptListResolver.ResolveAsync(archive, 1);
        var second = await FalloutScriptListResolver.ResolveAsync(archive, 1);

        Assert.Equal(first, second);
        Assert.Equal(original, bytes);
        AssertResolution(first, 1, "two");
    }

    [Fact]
    public async Task ResolveAsync_RejectsMismatchedArchiveGameAndFormat()
    {
        var archive = CreateArchive(
            FalloutGame.Fallout1,
            ArchiveFormat.Dat2,
            ListEntry("one.int\n"));

        var result = await FalloutScriptListResolver.ResolveAsync(archive, 0);

        AssertDiagnostic(result, FalloutScriptListDiagnosticCode.UnsupportedArchive);
        Assert.Equal(0, archive.GetCopyCount(FalloutScriptListResolver.ScriptListPath));
    }

    [Fact]
    public async Task ResolveAsync_ThrowsForNullArchive()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => FalloutScriptListResolver.ResolveAsync(null!, 0));
    }

    private static void AssertResolution(
        FalloutScriptListResolution result,
        int slot,
        string scriptName)
    {
        Assert.True(result.Success);
        Assert.Equal(slot, result.Slot);
        Assert.Equal(scriptName, result.ScriptName);
        Assert.NotNull(result.IntPath);
        Assert.NotNull(result.MessagePath);
        Assert.Null(result.Diagnostic);
    }

    private static void AssertDiagnostic(
        FalloutScriptListResolution result,
        FalloutScriptListDiagnosticCode code)
    {
        Assert.False(result.Success);
        Assert.Null(result.ScriptName);
        Assert.Null(result.IntPath);
        Assert.Null(result.MessagePath);
        Assert.Equal(code, result.Diagnostic?.Code);
        Assert.False(string.IsNullOrWhiteSpace(result.Diagnostic?.Message));
    }

    private static InMemoryArchive CreateArchive(
        FalloutGame game,
        ArchiveFormat format,
        params EntryData[] entries) =>
        new(game, format, entries);

    private static EntryData ListEntry(
        string contents,
        long? declaredSize = null,
        string path = FalloutScriptListResolver.ScriptListPath) =>
        new(path, Encoding.ASCII.GetBytes(contents), declaredSize);

    private sealed record EntryData(string Path, byte[] Bytes, long? DeclaredSize = null);

    private sealed class InMemoryArchive : IGameArchive
    {
        private readonly Dictionary<ArchiveEntry, byte[]> _contents;
        private readonly Dictionary<string, int> _copyCounts = new(StringComparer.OrdinalIgnoreCase);

        public InMemoryArchive(FalloutGame game, ArchiveFormat format, IReadOnlyList<EntryData> entries)
        {
            Game = game;
            Format = format;

            var contents = new List<KeyValuePair<ArchiveEntry, byte[]>>(entries.Count);
            for (var index = 0; index < entries.Count; index++)
            {
                var data = entries[index];
                var entry = new ArchiveEntry(
                    data.Path,
                    data.DeclaredSize ?? data.Bytes.Length,
                    null,
                    $"test-{index}.dat");
                contents.Add(new KeyValuePair<ArchiveEntry, byte[]>(entry, data.Bytes));
            }

            _contents = contents.ToDictionary(item => item.Key, item => item.Value);
            BrowserEntries = contents.Select(item => item.Key).ToArray();
        }

        public string Path => "test.dat";

        public FalloutGame Game { get; }

        public ArchiveFormat Format { get; }

        public IReadOnlyList<ArchiveEntry> BrowserEntries { get; }

        public int GetCopyCount(string path) =>
            _copyCounts.TryGetValue(path.Replace('\\', '/'), out var count) ? count : 0;

        public Task CopyEntryToAsync(
            ArchiveEntry entry,
            Stream destination,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedPath = entry.Path.Replace('\\', '/');
            _copyCounts[normalizedPath] = GetCopyCount(normalizedPath) + 1;
            return destination.WriteAsync(_contents[entry], cancellationToken).AsTask();
        }
    }
}
