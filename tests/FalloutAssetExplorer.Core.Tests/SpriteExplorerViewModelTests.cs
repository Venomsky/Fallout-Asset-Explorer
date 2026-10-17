using System.Buffers.Binary;
using System.Text;
using Avalonia.Controls;
using FalloutAssetExplorer.App;
using FalloutAssetExplorer.App.ViewModels;
using FalloutAssetExplorer.App.Views;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class SpriteExplorerViewModelTests
{
    public SpriteExplorerViewModelTests()
    {
        AvaloniaTestEnvironment.EnsureInitialized();
    }

    [Fact]
    public void InitialState_ReportsMissingSourceWithoutFilterResults()
    {
        using var viewModel = new SpriteExplorerViewModel();

        Assert.Empty(viewModel.FrmEntries);
        Assert.Empty(viewModel.VisibleEntries);
        Assert.False(viewModel.IsFilterResultEmpty);
        Assert.Contains("Open a Fallout archive", viewModel.StatusMessage);
    }

    [Fact]
    public void MainWindow_ExposesArchiveBrowserAndSpriteExplorerTabs()
    {
        AvaloniaTestEnvironment.RunOnUiThread(() =>
        {
            using var viewModel = new ArchiveBrowserViewModel();
            var window = new MainWindow
            {
                DataContext = viewModel
            };

            try
            {
                var tabs = Assert.IsType<TabControl>(window.FindControl<TabControl>("WorkspaceTabs"));
                Assert.Equal(2, tabs.Items.Count);
                Assert.Equal("Archive Browser", Assert.IsType<TabItem>(tabs.Items[0]).Header);
                Assert.Equal("Sprite Explorer", Assert.IsType<TabItem>(tabs.Items[1]).Header);

                tabs.SelectedIndex = 1;
                Assert.Equal(1, viewModel.SelectedWorkspaceIndex);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task OpenSourceAsync_ReportsArchiveWithoutFrmEntries()
    {
        var archive = new MemoryGameArchive("empty.dat");
        archive.AddEntry("text/readme.msg", "{1}{}{text}"u8.ToArray());
        using var viewModel = new SpriteExplorerViewModel((_, _) => archive);

        await viewModel.OpenSourceAsync(archive.Path);

        Assert.Empty(viewModel.FrmEntries);
        Assert.Empty(viewModel.VisibleEntries);
        Assert.False(viewModel.IsFilterResultEmpty);
        Assert.Contains("contains no .FRM entries", viewModel.StatusMessage);
    }

    [Fact]
    public async Task SearchAndLocationFilters_OnlyShowMatchingFrmEntries()
    {
        var archive = new MemoryGameArchive("filter.dat");
        archive.AddEntry(
            FalloutPaletteResolver.DefaultPaletteEntryPath,
            new byte[FalloutPalette.MinimalFileSize]);
        var critter = archive.AddEntry(
            "art/critters/hero.frm",
            CreateFrm());
        critter = archive.SetLocation(critter, "CRITTER.DAT");
        var scenery = archive.AddEntry(
            "art/scenery/hero.frm",
            CreateFrm());
        scenery = archive.SetLocation(scenery, "MASTER.DAT");
        archive.AddEntry("art/critters/tree.pro", [1]);
        using var viewModel = new SpriteExplorerViewModel((_, _) => archive);

        await viewModel.OpenSourceAsync(archive.Path);

        Assert.Equal(2, viewModel.FrmEntries.Count);
        Assert.Equal(2, viewModel.VisibleEntries.Count);
        Assert.Contains(scenery, viewModel.FrmEntries);
        Assert.Contains("CRITTER.DAT", viewModel.ArchiveLocationOptions);
        Assert.Contains("MASTER.DAT", viewModel.ArchiveLocationOptions);

        viewModel.SearchText = "hero";
        viewModel.SelectedArchiveLocation = "CRITTER.DAT";

        Assert.Same(critter, Assert.Single(viewModel.VisibleEntries));
        Assert.False(viewModel.IsFilterResultEmpty);

        viewModel.SearchText = "missing";
        Assert.Empty(viewModel.VisibleEntries);
        Assert.True(viewModel.IsFilterResultEmpty);

    }

    [Fact]
    public async Task SelectingFrm_PublishesPreviewAndDirectionFrameState()
    {
        var archive = new MemoryGameArchive("preview.dat");
        archive.AddEntry(
            FalloutPaletteResolver.DefaultPaletteEntryPath,
            new byte[FalloutPalette.MinimalFileSize]);
        var entry = archive.AddEntry(
            "art/critters/animated.frm",
            FrmFixture.Create(
            [
                [
                    new FrmFixtureFrame(1, 1, 0, 0, [1]),
                    new FrmFixtureFrame(2, 1, 0, 0, [2, 3])
                ],
                [
                    new FrmFixtureFrame(1, 1, 0, 0, [4]),
                    new FrmFixtureFrame(3, 1, 0, 0, [5, 6, 7])
                ]
            ]));
        using var viewModel = new SpriteExplorerViewModel((_, _) => archive);

        await viewModel.OpenSourceAsync(archive.Path, entry.Path);

        Assert.Equal(entry.Path, viewModel.PreviewEntryPath);
        Assert.True(viewModel.Preview.HasPreview);
        Assert.True(viewModel.PaletteStatus.Contains("COLOR.PAL", StringComparison.Ordinal), viewModel.PaletteStatus);
        Assert.Contains("Version 3", viewModel.Preview.PreviewDetails);

        viewModel.Preview.SelectedDirectionIndex = 1;
        Assert.Equal(1, viewModel.Preview.SelectedDirectionIndex);
        Assert.Equal(0, viewModel.Preview.SelectedFrameIndex);

        viewModel.Preview.SelectedFrameIndex = 1;
        Assert.Equal(1, viewModel.Preview.SelectedFrameIndex);
        Assert.Contains("Direction E | 2/2", viewModel.Preview.PreviewDetails);
        Assert.Contains("Frame 3 x 1", viewModel.Preview.PreviewDetails);
        Assert.Equal(3, viewModel.Preview.PreviewBitmap!.PixelSize.Width);
    }

    [Fact]
    public async Task SpriteSheetMode_RendersDirectionBlocksAndSynchronizesFrameSelection()
    {
        var archive = new MemoryGameArchive("sprite-sheet.dat");
        archive.AddEntry(
            FalloutPaletteResolver.DefaultPaletteEntryPath,
            new byte[FalloutPalette.MinimalFileSize]);
        var entry = archive.AddEntry("art/critters/sheet.frm", CreateFrm());
        using var viewModel = new SpriteExplorerViewModel((_, _) => archive);
        await viewModel.OpenSourceAsync(archive.Path, entry.Path);
        viewModel.Preview.SetPreview(
            new FrmDocument(
                4,
                12,
                0,
                1,
                [
                    new FrmDirection(0, "NE", 0, 0,
                    [
                        new FrmFrame(1, 1, 0, 0, [1]),
                        new FrmFrame(2, 1, 0, 0, [2, 3])
                    ]),
                    new FrmDirection(1, "E", 0, 0, [new FrmFrame(1, 2, 0, 0, [4, 5])])
                ]),
            FalloutPalette.Parse(new byte[FalloutPalette.MinimalFileSize]),
            entry.Path);

        viewModel.IsSpriteSheetMode = true;
        await viewModel.SpriteSheetRenderTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(viewModel.IsSpriteSheetMode);
        Assert.False(viewModel.IsAnimationMode);
        Assert.Equal(2, viewModel.SpriteSheetLayout!.Placements.Count);
        Assert.NotNull(viewModel.DisplayBitmap);
        Assert.Equal(viewModel.SpriteSheetLayout.Width, viewModel.DisplayBitmap!.PixelSize.Width);

        viewModel.IsAllDirectionsSpriteSheet = true;
        await viewModel.SpriteSheetRenderTask.WaitAsync(TimeSpan.FromSeconds(10));

        var layout = Assert.IsType<FrmSpritesheetLayout>(viewModel.SpriteSheetLayout);
        Assert.Equal(3, layout.Placements.Count);
        var firstDirection = layout.Placements.Values
            .Where(placement => placement.Frame.Key.DirectionIndex == 0)
            .ToArray();
        var secondDirection = Assert.Single(
            layout.Placements.Values,
            placement => placement.Frame.Key.DirectionIndex == 1);
        var firstDirectionBottom = firstDirection.Max(
            placement => placement.Rectangle.Y + placement.Rectangle.Height);
        Assert.True(secondDirection.Rectangle.Y >= firstDirectionBottom + FrmPreviewViewModel.SpriteSheetDirectionGap);

        viewModel.Preview.SelectFrameByDirectionIndex(1, 0);
        Assert.Equal(new FrmSpritesheetFrameKey(1, 0), viewModel.SelectedSpriteSheetFrameKey);
        Assert.Single(viewModel.Preview.FrameOptions);

        viewModel.IsSpriteSheetMode = false;
        Assert.True(viewModel.IsAnimationMode);
        Assert.Same(viewModel.Preview.PreviewBitmap, viewModel.DisplayBitmap);
        Assert.Equal(1, viewModel.Preview.SelectedDirectionIndex);
        Assert.Equal(0, viewModel.Preview.SelectedFrameIndex);
    }

    [Fact]
    public async Task SpriteSheetHitTester_MapsZoomedAndPannedCoordinatesToFrame()
    {
        var archive = new MemoryGameArchive("sprite-sheet-hit-test.dat");
        archive.AddEntry(
            FalloutPaletteResolver.DefaultPaletteEntryPath,
            new byte[FalloutPalette.MinimalFileSize]);
        var entry = archive.AddEntry(
            "art/critters/hit-test.frm",
            FrmFixture.Create(
            [
                [
                    new FrmFixtureFrame(2, 1, 0, 0, [1, 2]),
                    new FrmFixtureFrame(1, 1, 0, 0, [3])
                ]
            ]));
        using var viewModel = new SpriteExplorerViewModel((_, _) => archive);
        viewModel.Zoom.SetViewportSize(300, 240);
        await viewModel.OpenSourceAsync(archive.Path, entry.Path);
        viewModel.IsSpriteSheetMode = true;
        await viewModel.SpriteSheetRenderTask.WaitAsync(TimeSpan.FromSeconds(10));

        var layout = Assert.IsType<FrmSpritesheetLayout>(viewModel.SpriteSheetLayout);
        var target = layout.Placements[new FrmSpritesheetFrameKey(0, 1)];
        viewModel.Zoom.SetViewportSize(2, 1);
        viewModel.Zoom.Set100Percent();
        Assert.True(viewModel.Zoom.CanPan);
        viewModel.Zoom.ZoomBy(2, 1, 0.5);
        viewModel.Zoom.PanBy(-1, 0);
        var x = (target.Rectangle.X + 0.5) * viewModel.Zoom.Zoom + viewModel.Zoom.OffsetX;
        var y = (target.Rectangle.Y + 0.5) * viewModel.Zoom.Zoom + viewModel.Zoom.OffsetY;

        var hit = SpriteSheetHitTester.HitTest(layout, viewModel.Zoom, x, y);

        Assert.Equal(target.Frame.Key, hit?.Frame.Key);
    }

    [Fact]
    public async Task ExportActions_WriteCurrentFrameAndSelectedOrSeparatedDirections()
    {
        var archive = new MemoryGameArchive("export.dat");
        archive.AddEntry(
            FalloutPaletteResolver.DefaultPaletteEntryPath,
            new byte[FalloutPalette.MinimalFileSize]);
        var entry = archive.AddEntry(
            "art/critters/export.frm",
            FrmFixture.Create(
            [
                [
                    new FrmFixtureFrame(1, 1, 0, 0, [1]),
                    new FrmFixtureFrame(1, 1, 0, 0, [2])
                ],
                [
                    new FrmFixtureFrame(2, 1, -1, 2, [2, 0]),
                    new FrmFixtureFrame(1, 2, 3, -4, [3, 1])
                ]
            ]));
        using var viewModel = new SpriteExplorerViewModel((_, _) => archive);
        using var destination = new TemporaryDirectory();

        await viewModel.OpenSourceAsync(archive.Path, entry.Path);

        Assert.True(viewModel.CanExportCurrentFrame);
        Assert.True(viewModel.CanExportCurrentDirection);
        Assert.True(viewModel.CanExportAllDirections);
        var framePath = Path.Combine(destination.Path, "frame.png");
        await viewModel.ExportCurrentFramePngAsync(framePath);
        AssertPngFile(framePath);

        viewModel.Preview.SelectedDirectionIndex = 1;
        var directionPath = Path.Combine(destination.Path, "direction.png");
        await viewModel.ExportCurrentDirectionSpritesheetPngAsync(directionPath);
        AssertPngFile(directionPath);

        var allDirectionsPath = Path.Combine(destination.Path, "all-directions.png");
        await viewModel.ExportAllDirectionsSpritesheetPngAsync(allDirectionsPath);
        AssertPngFile(allDirectionsPath);

        viewModel.IsSpriteSheetMode = true;
        await viewModel.SpriteSheetRenderTask.WaitAsync(TimeSpan.FromSeconds(10));
        var visibleDirectionPath = Path.Combine(destination.Path, "visible-direction.png");
        await viewModel.ExportVisibleSpriteSheetPngAsync(
            visibleDirectionPath,
            allDirections: false,
            selectedDirectionIndex: viewModel.Preview.SelectedDirectionIndex);
        AssertPngFile(visibleDirectionPath);

        viewModel.IsAllDirectionsSpriteSheet = true;
        await viewModel.SpriteSheetRenderTask.WaitAsync(TimeSpan.FromSeconds(10));
        var visibleAllDirectionsPath = Path.Combine(destination.Path, "visible-all-directions.png");
        await viewModel.ExportVisibleSpriteSheetPngAsync(
            visibleAllDirectionsPath,
            allDirections: true,
            selectedDirectionIndex: viewModel.Preview.SelectedDirectionIndex);
        AssertPngFile(visibleAllDirectionsPath);

        Assert.Contains("Exported PNG", viewModel.StatusMessage);
        Assert.False(viewModel.IsExporting);
    }

    [Fact]
    public async Task SelectingAnotherEntry_CancelsAndRejectsStalePreview()
    {
        var archive = new MemoryGameArchive("selection.dat");
        archive.AddEntry(
            FalloutPaletteResolver.DefaultPaletteEntryPath,
            new byte[FalloutPalette.MinimalFileSize]);
        var slowEntry = archive.AddEntry("art/critters/slow.frm", CreateFrm());
        var fastEntry = archive.AddEntry("art/critters/fast.frm", CreateFrm());
        var readGate = archive.BlockEntry(slowEntry.Path);
        using var viewModel = new SpriteExplorerViewModel((_, _) => archive);
        await viewModel.OpenSourceAsync(archive.Path);

        var slowPreview = viewModel.SelectEntryAsync(slowEntry);
        await readGate.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await viewModel.SelectEntryAsync(fastEntry).WaitAsync(TimeSpan.FromSeconds(10));
        await slowPreview.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(fastEntry.Path, viewModel.SelectedEntry?.Path);
        Assert.Equal(fastEntry.Path, viewModel.PreviewEntryPath);
        Assert.Contains("Preview loaded", viewModel.StatusMessage);
    }

    [Fact]
    public async Task SelectingAnotherFrm_ResetsZoomAndFitsTheNewFrame()
    {
        var archive = new MemoryGameArchive("zoom-reset.dat");
        archive.AddEntry(
            FalloutPaletteResolver.DefaultPaletteEntryPath,
            new byte[FalloutPalette.MinimalFileSize]);
        var firstEntry = archive.AddEntry("art/critters/first.frm", CreateFrm());
        var secondEntry = archive.AddEntry(
            "art/critters/second.frm",
            FrmFixture.Create([[new FrmFixtureFrame(2, 1, 0, 0, [1, 2])]]));
        using var viewModel = new SpriteExplorerViewModel((_, _) => archive);
        viewModel.Zoom.SetViewportSize(100, 100);
        await viewModel.OpenSourceAsync(archive.Path, firstEntry.Path);

        viewModel.Zoom.Set100Percent();
        Assert.False(viewModel.Zoom.IsFit);
        await viewModel.SelectEntryAsync(secondEntry);

        Assert.True(viewModel.Zoom.IsFit);
        Assert.Equal(2, viewModel.Preview.PreviewBitmap!.PixelSize.Width);
        Assert.Equal(34, viewModel.Zoom.OffsetY);
        Assert.Equal(secondEntry.Path, viewModel.PreviewEntryPath);
    }

    [Fact]
    public async Task Dispose_CancelsPendingPreviewAndDoesNotPublishIt()
    {
        var archive = new MemoryGameArchive("dispose.dat");
        archive.AddEntry(
            FalloutPaletteResolver.DefaultPaletteEntryPath,
            new byte[FalloutPalette.MinimalFileSize]);
        var entry = archive.AddEntry("art/critters/pending.frm", CreateFrm());
        var readGate = archive.BlockEntry(entry.Path);
        var viewModel = new SpriteExplorerViewModel((_, _) => archive);
        await viewModel.OpenSourceAsync(archive.Path);

        var preview = viewModel.SelectEntryAsync(entry);
        await readGate.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        viewModel.Dispose();
        var statusAfterDispose = viewModel.StatusMessage;
        await preview.WaitAsync(TimeSpan.FromSeconds(10));
        await archive.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Null(viewModel.Preview.PreviewBitmap);
        Assert.Null(viewModel.PreviewEntryPath);
        Assert.Equal(statusAfterDispose, viewModel.StatusMessage);
    }

    [Fact]
    public async Task Dispose_ClearsSpriteSheetBitmapAndLayout()
    {
        var archive = new MemoryGameArchive("dispose-sheet.dat");
        archive.AddEntry(
            FalloutPaletteResolver.DefaultPaletteEntryPath,
            new byte[FalloutPalette.MinimalFileSize]);
        var entry = archive.AddEntry("art/critters/sheet.frm", CreateFrm());
        var viewModel = new SpriteExplorerViewModel((_, _) => archive);
        await viewModel.OpenSourceAsync(archive.Path, entry.Path);
        viewModel.IsSpriteSheetMode = true;
        await viewModel.SpriteSheetRenderTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(viewModel.SpriteSheetLayout);
        Assert.NotNull(viewModel.DisplayBitmap);

        viewModel.Dispose();

        Assert.Null(viewModel.SpriteSheetLayout);
        Assert.Null(viewModel.DisplayBitmap);
        await archive.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task SelectingFrm_ReadFailureIsReported()
    {
        var archive = new MemoryGameArchive("read-error");
        var entry = archive.AddEntry("art/critters/unreadable.frm", CreateFrm());
        archive.FailEntry(entry.Path);
        using var viewModel = new SpriteExplorerViewModel((_, _) => archive);

        await viewModel.OpenSourceAsync(archive.Path, entry.Path);

        Assert.Null(viewModel.PreviewEntryPath);
        Assert.Contains("Could not preview FRM", viewModel.StatusMessage);
        Assert.Contains("Synthetic read failure", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ArchiveBrowserNavigation_OpensSelectedFrmInSeparateExplorerSource()
    {
        var frmBytes = CreateFrm();
        using var archiveFile = new TemporaryArchive(
            CreateDat1Archive("ART/CRITTERS/NAVIGATED.FRM", frmBytes));
        using var browser = new ArchiveBrowserViewModel();
        await browser.LoadArchiveAsync(archiveFile.Path);
        var entry = Assert.Single(browser.Entries);
        browser.SelectedEntry = entry;

        Assert.True(browser.CanOpenSelectedFrmInSpriteExplorer);
        await browser.OpenSelectedFrmInSpriteExplorerAsync();

        Assert.Equal(1, browser.SelectedWorkspaceIndex);
        Assert.Equal(Path.GetFullPath(archiveFile.Path), browser.SpriteExplorer.SourceArchiveDisplay);
        Assert.Equal(entry.Path, browser.SpriteExplorer.PreviewEntryPath);
        Assert.True(browser.SpriteExplorer.Preview.HasPreview);
    }

    private static byte[] CreateFrm() =>
        FrmFixture.Create([[new FrmFixtureFrame(1, 1, 0, 0, [1])]]);

    private static void AssertPngFile(string path)
    {
        var signature = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        Assert.Equal(signature, File.ReadAllBytes(path)[..8]);
    }

    private static byte[] CreateDat1Archive(string entryPath, byte[] payload)
    {
        var separator = entryPath.LastIndexOf('/');
        var folder = separator < 0 ? "." : entryPath[..separator];
        var fileName = separator < 0 ? entryPath : entryPath[(separator + 1)..];
        using var stream = new MemoryStream();

        WriteUInt32BigEndian(stream, 1);
        stream.Write(new byte[12]);
        WriteName(stream, folder);
        WriteUInt32BigEndian(stream, 1);
        stream.Write(new byte[12]);
        WriteName(stream, fileName);
        WriteUInt32BigEndian(stream, Dat1Archive.RawEntryType);
        var offsetPosition = stream.Position;
        WriteUInt32BigEndian(stream, 0);
        WriteUInt32BigEndian(stream, checked((uint)payload.Length));
        WriteUInt32BigEndian(stream, checked((uint)payload.Length));

        var payloadOffset = checked((uint)stream.Position);
        stream.Position = offsetPosition;
        WriteUInt32BigEndian(stream, payloadOffset);
        stream.Position = stream.Length;
        stream.Write(payload);
        return stream.ToArray();
    }

    private static void WriteName(Stream stream, string value)
    {
        var bytes = Encoding.Latin1.GetBytes(value);
        stream.WriteByte(checked((byte)bytes.Length));
        stream.Write(bytes);
    }

    private static void WriteUInt32BigEndian(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private sealed class TemporaryArchive : IDisposable
    {
        public TemporaryArchive(byte[] bytes)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"fae-sprite-source-{Guid.NewGuid():N}.dat");
            File.WriteAllBytes(Path, bytes);
        }

        public string Path { get; }

        public void Dispose() => File.Delete(Path);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"fae-sprite-export-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class MemoryGameArchive : IGameArchive, IDisposable
    {
        private readonly List<(ArchiveEntry Entry, byte[] Bytes)> _entries = [];
        private readonly Dictionary<string, ReadGate> _readGates = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _failedEntries = new(StringComparer.OrdinalIgnoreCase);
        private readonly TaskCompletionSource _disposed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public MemoryGameArchive(string name)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"fae-sprite-{name}-{Guid.NewGuid():N}.dat");
            File.WriteAllBytes(Path, []);
        }

        public string Path { get; }

        public FalloutGame Game => FalloutGame.Fallout2;

        public ArchiveFormat Format => ArchiveFormat.Dat2;

        public IReadOnlyList<ArchiveEntry> BrowserEntries => _entries.Select(item => item.Entry).ToArray();

        public TaskCompletionSource Disposed => _disposed;

        public ArchiveEntry AddEntry(string entryPath, byte[] bytes)
        {
            var entry = new ArchiveEntry(entryPath, bytes.LongLength, null, "source archive");
            _entries.Add((entry, bytes));
            return entry;
        }

        public ArchiveEntry SetLocation(ArchiveEntry entry, string location)
        {
            var index = _entries.FindIndex(item => ReferenceEquals(item.Entry, entry));
            Assert.True(index >= 0);
            var replacement = entry with { ArchiveLocation = location };
            _entries[index] = (replacement, _entries[index].Bytes);
            return replacement;
        }

        public ReadGate BlockEntry(string entryPath)
        {
            var gate = new ReadGate();
            _readGates.Add(entryPath, gate);
            return gate;
        }

        public void FailEntry(string entryPath) => _failedEntries.Add(entryPath);

        public async Task CopyEntryToAsync(
            ArchiveEntry entry,
            Stream destination,
            CancellationToken cancellationToken = default)
        {
            var item = _entries.FirstOrDefault(item => ReferenceEquals(item.Entry, entry));
            if (item.Entry is null)
            {
                throw new ArgumentException("The entry does not belong to this archive.", nameof(entry));
            }

            if (_failedEntries.Contains(entry.Path))
            {
                throw new IOException($"Synthetic read failure for {entry.Path}.");
            }

            if (_readGates.TryGetValue(entry.Path, out var gate))
            {
                gate.Started.TrySetResult();
                await gate.Blocked.Task.WaitAsync(cancellationToken);
            }

            await destination.WriteAsync(item.Bytes, cancellationToken);
        }

        public void Dispose()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }

            _disposed.TrySetResult();
        }
    }

    private sealed class ReadGate
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Blocked { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
