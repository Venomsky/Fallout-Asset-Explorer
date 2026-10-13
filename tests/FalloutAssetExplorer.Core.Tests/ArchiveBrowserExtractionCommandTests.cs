using System.Buffers.Binary;
using System.ComponentModel;
using System.Reflection;
using System.Text;
using Avalonia;
using FalloutAssetExplorer.App.ViewModels;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class ArchiveBrowserExtractionCommandTests
{
    [Fact]
    public async Task ExtractionAvailability_TracksArchiveSelectionAndFiltering()
    {
        using var firstArchive = new TemporaryArchive(CreateDat1Archive("ART/CRITTERS/HAPOWRAG.FRM"));
        using var emptyArchive = new TemporaryArchive(CreateEmptyDat1Archive());
        using var viewModel = new ArchiveBrowserViewModel();
        var notifiedProperties = new List<string?>();
        viewModel.PropertyChanged += OnPropertyChanged;

        Assert.False(viewModel.CanExtractSelected);
        Assert.False(viewModel.CanExtractVisible);
        Assert.False(viewModel.CanExtractAll);

        await viewModel.LoadArchiveAsync(firstArchive.Path);

        Assert.True(viewModel.CanExtractAll);
        Assert.True(viewModel.CanExtractVisible);
        Assert.False(viewModel.CanExtractSelected);
        Assert.Contains(nameof(viewModel.CanExtractAll), notifiedProperties);

        notifiedProperties.Clear();
        viewModel.UpdateSelectionCount(1);

        Assert.True(viewModel.CanExtractSelected);
        Assert.Contains(nameof(viewModel.CanExtractSelected), notifiedProperties);

        notifiedProperties.Clear();
        viewModel.SelectedExtension = ".MSG";

        Assert.False(viewModel.CanExtractVisible);
        Assert.True(viewModel.CanExtractAll);
        Assert.Contains(nameof(viewModel.CanExtractVisible), notifiedProperties);

        viewModel.SelectedExtension = ArchiveEntryFilter.AllExtensions;
        Assert.True(viewModel.CanExtractVisible);

        await viewModel.LoadArchiveAsync(emptyArchive.Path);

        Assert.False(viewModel.CanExtractSelected);
        Assert.False(viewModel.CanExtractVisible);
        Assert.False(viewModel.CanExtractAll);

        void OnPropertyChanged(object? sender, PropertyChangedEventArgs args) =>
            notifiedProperties.Add(args.PropertyName);
    }

    [Fact]
    public async Task PreviewSelectedEntryAsync_LeavesUnsupportedEntriesBrowsableAndExtractable()
    {
        AvaloniaTestEnvironment.EnsureInitialized();
        using var archiveFile = new TemporaryArchive(CreateDat1Archive("ART/PROTO/TEST.PRO"));
        using var viewModel = new ArchiveBrowserViewModel();

        await viewModel.LoadArchiveAsync(archiveFile.Path);
        viewModel.FrmPreview.SetPreview(
            new FrmDocument(
                4,
                10,
                0,
                1,
                [new FrmDirection(0, "NE", 0, 0, [new FrmFrame(1, 1, 0, 0, [1])])]),
            FalloutPalette.Parse(new byte[FalloutPalette.MinimalFileSize]),
            "ART/CRITTERS/STALE.FRM");
        Assert.NotNull(viewModel.FrmPreview.PreviewBitmap);
        viewModel.SelectedEntry = Assert.Single(viewModel.Entries);
        await viewModel.PreviewSelectedEntryAsync();

        Assert.Equal("Select a .FRM or .MAP entry to inspect.", viewModel.PreviewMessage);
        Assert.Null(viewModel.FrmPreview.PreviewBitmap);
        Assert.False(viewModel.FrmPreview.HasPreview);
        Assert.Single(viewModel.VisibleEntries);
        Assert.True(viewModel.CanExtractAll);
    }

    [Fact]
    public async Task PreviewSelectedEntryAsync_ClearsFrmImageBeforeMapRoute()
    {
        AvaloniaTestEnvironment.EnsureInitialized();
        using var archiveFile = new TemporaryArchive(CreateDat1Archive("MAPS/TEST.MAP"));
        using var viewModel = new ArchiveBrowserViewModel();

        await viewModel.LoadArchiveAsync(archiveFile.Path);
        viewModel.FrmPreview.SetPreview(
            new FrmDocument(
                4,
                10,
                0,
                1,
                [new FrmDirection(0, "NE", 0, 0, [new FrmFrame(1, 1, 0, 0, [1])])]),
            FalloutPalette.Parse(new byte[FalloutPalette.MinimalFileSize]),
            "ART/CRITTERS/STALE.FRM");
        Assert.NotNull(viewModel.FrmPreview.PreviewBitmap);

        viewModel.SelectedEntry = Assert.Single(viewModel.Entries);
        await viewModel.PreviewSelectedEntryAsync();

        Assert.Null(viewModel.FrmPreview.PreviewBitmap);
        Assert.False(viewModel.FrmPreview.HasPreview);
        Assert.StartsWith("Could not inspect MAP:", viewModel.PreviewMessage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreviewSelectedEntryAsync_StaleFrmReadCannotReplaceNewerPreviewOrCancellation(
        bool completeNewerPreviewBeforeReleasingStaleRead)
    {
        var synchronizationContext = SynchronizationContext.Current;
        AvaloniaTestEnvironment.EnsureInitialized();
        SynchronizationContext.SetSynchronizationContext(synchronizationContext);
        using var archive = new ControlledArchive();
        var firstFrm = archive.AddEntry(
            "ART/CRITTERS/FIRST.FRM",
            FrmFixture.Create([[new FrmFixtureFrame(1, 1, 0, 0, [11])]], framesPerSecond: 11));
        var secondFrm = archive.AddEntry(
            "ART/CRITTERS/SECOND.FRM",
            FrmFixture.Create(
            [[
                new FrmFixtureFrame(1, 1, 0, 0, [22]),
                new FrmFixtureFrame(1, 1, 0, 0, [23])
            ]],
            framesPerSecond: 17));
        archive.AddEntry("COLOR.PAL", new byte[FalloutPalette.MinimalFileSize]);
        var firstRead = archive.BlockEntry(firstFrm.Path);
        var secondRead = archive.BlockEntry(secondFrm.Path);
        using var viewModel = new ArchiveBrowserViewModel();
        SetPrivateField(viewModel, "_archive", archive);
        viewModel.SetEntries(archive.BrowserEntries);

        Task? firstPreview = null;
        Task? secondPreview = null;
        try
        {
            viewModel.SelectedEntry = firstFrm;
            firstPreview = viewModel.PreviewSelectedEntryAsync();
            await firstRead.WaitStartedAsync();

            viewModel.SelectedEntry = secondFrm;
            secondPreview = viewModel.PreviewSelectedEntryAsync();
            await secondRead.WaitStartedAsync();

            var secondCancellation = GetPrivateField<CancellationTokenSource?>(
                viewModel,
                "_previewCancellation");
            Assert.NotNull(secondCancellation);

            if (!completeNewerPreviewBeforeReleasingStaleRead)
            {
                firstRead.Release();
                await firstPreview.WaitAsync(TimeSpan.FromSeconds(10));

                Assert.Same(
                    secondCancellation,
                    GetPrivateField<CancellationTokenSource?>(viewModel, "_previewCancellation"));
                Assert.Equal("FRM Preview", viewModel.PreviewTitle);
                Assert.Equal("Reading and decoding FRM...", viewModel.PreviewMessage);
                Assert.Null(viewModel.FrmPreview.PreviewBitmap);
            }

            secondRead.Release();
            await secondPreview.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal("FRM Preview", viewModel.PreviewTitle);
            Assert.Equal(secondFrm.Path, viewModel.PreviewMessage);
            Assert.Equal(
                "Fallout 2 COLOR.PAL applied; visual color match is not verified.",
                viewModel.PaletteStatus);
            Assert.Equal("SECOND_NE_Frame01.png", viewModel.FrmPreview.SuggestedPngFileName);
            Assert.Contains("FRM rate metadata 17 FPS", viewModel.FrmPreview.PreviewDetails);
            Assert.Equal("Frame 1 / 2", viewModel.FrmPreview.CurrentFrameDisplay);
            Assert.Equal(new PixelSize(1, 1), viewModel.FrmPreview.PreviewBitmap?.PixelSize);

            if (completeNewerPreviewBeforeReleasingStaleRead)
            {
                var publishedBitmap = viewModel.FrmPreview.PreviewBitmap;
                var publishedTitle = viewModel.PreviewTitle;
                var publishedMessage = viewModel.PreviewMessage;
                var publishedPaletteStatus = viewModel.PaletteStatus;
                var publishedDetails = viewModel.FrmPreview.PreviewDetails;
                var publishedFileName = viewModel.FrmPreview.SuggestedPngFileName;
                var publishedFrameDisplay = viewModel.FrmPreview.CurrentFrameDisplay;
                var publishedCancellation = GetPrivateField<CancellationTokenSource?>(
                    viewModel,
                    "_previewCancellation");
                Assert.Null(publishedCancellation);

                firstRead.Release();
                await firstPreview.WaitAsync(TimeSpan.FromSeconds(10));

                Assert.Same(publishedBitmap, viewModel.FrmPreview.PreviewBitmap);
                Assert.Equal(publishedTitle, viewModel.PreviewTitle);
                Assert.Equal(publishedMessage, viewModel.PreviewMessage);
                Assert.Equal(publishedPaletteStatus, viewModel.PaletteStatus);
                Assert.Equal(publishedDetails, viewModel.FrmPreview.PreviewDetails);
                Assert.Equal(publishedFileName, viewModel.FrmPreview.SuggestedPngFileName);
                Assert.Equal(publishedFrameDisplay, viewModel.FrmPreview.CurrentFrameDisplay);
                Assert.Null(GetPrivateField<CancellationTokenSource?>(
                    viewModel,
                    "_previewCancellation"));

                using var bitmapOutput = new MemoryStream();
                viewModel.FrmPreview.PreviewBitmap!.Save(bitmapOutput);
                Assert.NotEmpty(bitmapOutput.ToArray());
            }
        }
        finally
        {
            firstRead.Release();
            secondRead.Release();
            if (firstPreview is not null)
            {
                await firstPreview.WaitAsync(TimeSpan.FromSeconds(10));
            }

            if (secondPreview is not null)
            {
                await secondPreview.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrototypeDiagnostics_StaleSuccessOrExpectedIoCannotReplaceNewerSelection(
        bool failFirstPrototypeRead)
    {
        using var archive = new ControlledArchive();
        archive.AddEntry("proto/items/items.lst", Encoding.ASCII.GetBytes("first.pro\nsecond.pro\n"));
        var firstPid = 0x00000001;
        var secondPid = 0x00000002;
        var firstPrototype = archive.AddEntry(
            "proto/items/first.pro",
            CreateItemPrototype(firstPid));
        var secondPrototype = archive.AddEntry(
            "proto/items/second.pro",
            CreateItemPrototype(secondPid));
        var firstRead = archive.BlockEntry(firstPrototype.Path, failWithIo: failFirstPrototypeRead);
        var secondRead = archive.BlockEntry(secondPrototype.Path);
        var resolver = new Fallout2PrototypeResolver(archive);

        using var viewModel = new ArchiveBrowserViewModel();
        var firstPlacement = CreateItemPlacement(1, firstPid);
        var secondPlacement = CreateItemPlacement(2, secondPid);
        viewModel.MapObjectPlacements.Add(firstPlacement);
        viewModel.MapObjectPlacements.Add(secondPlacement);
        viewModel.MapInspector.SelectedMapObject = firstPlacement;
        SetPrivateField(viewModel, "_mapPrototypeResolver", resolver);

        using var firstCancellation = new CancellationTokenSource();
        SetPrivateField(viewModel, "_mapObjectPrototypeDiagnosticCancellation", firstCancellation);
        viewModel.MapInspector.SetMapObjectPrototypeDiagnostic(
            "Loading prototype diagnostics...",
            $"Selected object PID: 0x{unchecked((uint)firstPid):X8}",
            string.Empty,
            string.Empty,
            []);
        var firstDiagnostic = StartPrototypeDiagnosticLoad(
            viewModel,
            firstPlacement,
            resolver,
            firstCancellation);
        try
        {
            await firstRead.WaitStartedAsync();
            var secondDiagnosticReady = WaitForPrototypeFileAsync(
                viewModel.MapInspector,
                "second.pro");

            viewModel.MapInspector.SelectedMapObject = secondPlacement;
            await secondRead.WaitStartedAsync();

            var secondCancellation = GetPrivateField<CancellationTokenSource?>(
                viewModel,
                "_mapObjectPrototypeDiagnosticCancellation");
            Assert.NotNull(secondCancellation);

            firstRead.Release();
            await firstDiagnostic.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Same(
                secondCancellation,
                GetPrivateField<CancellationTokenSource?>(
                    viewModel,
                    "_mapObjectPrototypeDiagnosticCancellation"));
            Assert.Equal("Loading prototype diagnostics...", viewModel.MapInspector.MapObjectPrototypeDiagnosticStatus);
            Assert.Contains("0x00000002", viewModel.MapInspector.MapObjectPrototypeDiagnosticDetails);
            Assert.DoesNotContain("first.pro", viewModel.MapInspector.MapObjectPrototypeDiagnosticDetails);
            Assert.Equal(failFirstPrototypeRead, firstRead.FailedWithIo);

            secondRead.Release();
            await secondDiagnosticReady.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Contains("Verified file: second.pro", viewModel.MapInspector.MapObjectPrototypeDiagnosticDetails);
            Assert.Contains("Prototype PID: 0x00000002", viewModel.MapInspector.MapObjectPrototypeDiagnosticDetails);
            Assert.NotEmpty(viewModel.MapInspector.MapObjectPrototypeItemFieldsDisplay);
            Assert.NotEmpty(viewModel.MapInspector.MapObjectPrototypeRawByteLines);
        }
        finally
        {
            firstRead.Release();
            secondRead.Release();
            await firstDiagnostic.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static byte[] CreateEmptyDat1Archive()
    {
        byte[] archive = new byte[16];
        return archive;
    }

    private static MapObjectPlacement CreateItemPlacement(int recordIndex, int rawPid) =>
        new(
            RecordIndex: recordIndex,
            RecordOffset: 0,
            ElevationGroup: 0,
            RawObjectId: 0,
            RawHexIndex: 20_099,
            HexX: 100,
            HexY: 100,
            RawOffsetX: 0,
            RawOffsetY: 0,
            RawScreenX: 0,
            RawScreenY: 0,
            RawFrame: 0,
            RawRotation: 0,
            RawFid: 0,
            RawObjectFlags: 0,
            StoredElevation: 0,
            RawPid: rawPid,
            PidTypeIdentifier: 0,
            RawCid: 0,
            RawLightDistance: 0,
            RawLightIntensity: 0,
            RawField74: 0,
            RawScriptId: 0,
            RawScriptField: 0,
            RawInventoryLength: 0,
            RawInventoryCapacity: 0,
            RawInventoryPointer: 0,
            RawPayloadWords: [],
            Warning: null,
            UnsupportedReason: null);

    private static byte[] CreateItemPrototype(int pid)
    {
        var bytes = new byte[0x45];
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(0, sizeof(int)), pid);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(0x20, sizeof(int)), 5);
        return bytes;
    }

    private static Task StartPrototypeDiagnosticLoad(
        ArchiveBrowserViewModel viewModel,
        MapObjectPlacement placement,
        Fallout2PrototypeResolver resolver,
        CancellationTokenSource cancellation)
    {
        var method = typeof(ArchiveBrowserViewModel).GetMethod(
            "LoadMapObjectPrototypeDiagnosticAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<Task>(
            method.Invoke(viewModel, [placement, resolver, cancellation]));
    }

    private static async Task WaitForPrototypeFileAsync(
        MapInspectorViewModel inspector,
        string fileName)
    {
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void OnPropertyChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (inspector.MapObjectPrototypeDiagnosticDetails.Contains(
                    $"Verified file: {fileName}",
                    StringComparison.OrdinalIgnoreCase))
            {
                completion.TrySetResult(true);
            }
        }

        inspector.PropertyChanged += OnPropertyChanged;
        try
        {
            OnPropertyChanged(inspector, new PropertyChangedEventArgs(
                nameof(inspector.MapObjectPrototypeDiagnosticDetails)));
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            inspector.PropertyChanged -= OnPropertyChanged;
        }
    }

    private static T GetPrivateField<T>(ArchiveBrowserViewModel viewModel, string name)
    {
        var field = typeof(ArchiveBrowserViewModel).GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (T)field.GetValue(viewModel)!;
    }

    private static void SetPrivateField<T>(
        ArchiveBrowserViewModel viewModel,
        string name,
        T value)
    {
        var field = typeof(ArchiveBrowserViewModel).GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(viewModel, value);
    }

    private static byte[] CreateDat1Archive(string entryPath)
    {
        var separator = entryPath.LastIndexOf('/');
        var folder = separator < 0 ? "." : entryPath[..separator];
        var fileName = separator < 0 ? entryPath : entryPath[(separator + 1)..];
        var payload = new byte[] { 0x46, 0x52, 0x4D };
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
        Span<byte> buffer = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private sealed class TemporaryArchive : IDisposable
    {
        public TemporaryArchive(byte[] content)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"fae-command-test-{Guid.NewGuid():N}.dat");
            File.WriteAllBytes(Path, content);
        }

        public string Path { get; }

        public void Dispose() => File.Delete(Path);
    }

    private sealed class ControlledArchive : IGameArchive, IDisposable
    {
        private readonly Dictionary<string, (ArchiveEntry Entry, byte[] Bytes)> _entries =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ReadGate> _readGates =
            new(StringComparer.OrdinalIgnoreCase);

        public ControlledArchive()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"fae-controlled-{Guid.NewGuid():N}.dat");
            File.WriteAllBytes(Path, []);
        }

        public string Path { get; }

        public FalloutGame Game => FalloutGame.Fallout2;

        public ArchiveFormat Format => ArchiveFormat.Dat2;

        public IReadOnlyList<ArchiveEntry> BrowserEntries =>
            _entries.Values.Select(value => value.Entry).ToArray();

        public ArchiveEntry AddEntry(string path, byte[] bytes)
        {
            var entry = new ArchiveEntry(path, bytes.Length, null, Path);
            _entries.Add(path, (entry, bytes));
            return entry;
        }

        public ReadGate BlockEntry(string path, bool failWithIo = false)
        {
            var gate = new ReadGate(failWithIo);
            _readGates.Add(path, gate);
            return gate;
        }

        public async Task CopyEntryToAsync(
            ArchiveEntry entry,
            Stream destination,
            CancellationToken cancellationToken = default)
        {
            if (!_entries.TryGetValue(entry.Path, out var item))
            {
                throw new IOException($"Unexpected synthetic archive entry: {entry.Path}");
            }

            if (_readGates.TryGetValue(entry.Path, out var gate))
            {
                gate.Started.TrySetResult(true);
                await gate.Released.Task.ConfigureAwait(false);
                if (gate.FailWithIo)
                {
                    gate.FailedWithIo = true;
                    throw new IOException($"Synthetic I/O failure for {entry.Path}.");
                }
            }
            await destination.WriteAsync(item.Bytes, CancellationToken.None);
        }

        public void Dispose() => File.Delete(Path);

        public sealed class ReadGate(bool failWithIo)
        {
            public TaskCompletionSource<bool> Started { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> Released { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public bool FailWithIo { get; } = failWithIo;

            public bool FailedWithIo { get; set; }

            public Task WaitStartedAsync() => Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

            public void Release() => Released.TrySetResult(true);
        }
    }
}
