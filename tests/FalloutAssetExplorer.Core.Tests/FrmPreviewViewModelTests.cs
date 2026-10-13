using System.ComponentModel;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Media.Imaging;
using FalloutAssetExplorer.App;
using FalloutAssetExplorer.App.ViewModels;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class FrmPreviewViewModelTests
{
    public FrmPreviewViewModelTests()
    {
        AvaloniaTestEnvironment.EnsureInitialized();
    }

    [Fact]
    public void InitialAndClearedState_HasNoDisplayedFrameOrExportableSelection()
    {
        using var viewModel = new FrmPreviewViewModel();
        Assert.Null(viewModel.PreviewBitmap);
        Assert.False(viewModel.HasPreview);
        Assert.False(viewModel.CanPlay);
        Assert.False(viewModel.CanExportPng);
        Assert.False(viewModel.CanExportSpritesheet);
        Assert.Empty(viewModel.FrameOptions);
        Assert.Equal(0, viewModel.SelectedDirectionIndex);
        Assert.Equal(0, viewModel.SelectedFrameIndex);
        Assert.False(viewModel.IsCheckerboardBackground);

        var document = CreateDocument();
        viewModel.SetPreview(document, CreatePalette(), "art/critters/test.frm");

        Assert.NotNull(viewModel.PreviewBitmap);
        Assert.True(viewModel.HasPreview);
        Assert.True(viewModel.CanPlay);
        Assert.True(viewModel.CanExportPng);
        Assert.True(viewModel.CanExportSpritesheet);

        var displayedBitmap = viewModel.PreviewBitmap;
        viewModel.ClearPreview();

        Assert.Null(viewModel.PreviewBitmap);
        Assert.False(viewModel.HasPreview);
        Assert.False(viewModel.CanPlay);
        Assert.False(viewModel.CanExportPng);
        Assert.False(viewModel.CanExportSpritesheet);
        Assert.Empty(viewModel.FrameOptions);
        Assert.Equal(0, viewModel.SelectedDirectionIndex);
        Assert.Equal(0, viewModel.SelectedFrameIndex);
        Assert.Equal("Select a .FRM, .MAP, or .PRO entry to inspect.", viewModel.PreviewDetails);
        AssertBitmapDisposed(displayedBitmap!);
    }

    [Fact]
    public void SelectingDirectionAndFrame_UpdatesPresentationAndNotifications()
    {
        using var viewModel = new FrmPreviewViewModel();
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += OnPropertyChanged;
        viewModel.SetPreview(CreateDocument(), CreatePalette(), "art/critters/test.frm");
        changedProperties.Clear();

        var firstBitmap = viewModel.PreviewBitmap;
        viewModel.SelectedFrameIndex = 1;

        Assert.Equal(1, viewModel.SelectedFrameIndex);
        Assert.NotSame(firstBitmap, viewModel.PreviewBitmap);
        Assert.Contains(nameof(viewModel.SelectedFrameIndex), changedProperties);
        Assert.Contains(nameof(viewModel.PreviewBitmap), changedProperties);
        Assert.Contains(nameof(viewModel.HasPreview), changedProperties);
        Assert.Contains(nameof(viewModel.PreviewDetails), changedProperties);
        Assert.Contains(nameof(viewModel.CurrentFrameDisplay), changedProperties);
        Assert.Contains(nameof(viewModel.SuggestedPngFileName), changedProperties);
        Assert.Contains(nameof(viewModel.CanExportPng), changedProperties);
        Assert.EndsWith("_Frame02.png", viewModel.SuggestedPngFileName);

        changedProperties.Clear();
        viewModel.SelectedDirectionIndex = 1;

        Assert.Equal(1, viewModel.SelectedDirectionIndex);
        Assert.Equal(0, viewModel.SelectedFrameIndex);
        Assert.Contains(nameof(viewModel.SelectedDirectionIndex), changedProperties);
        Assert.Contains(nameof(viewModel.SelectedFrameIndex), changedProperties);

        void OnPropertyChanged(object? sender, PropertyChangedEventArgs args) =>
            changedProperties.Add(args.PropertyName);
    }

    [Fact]
    public void ReplacingFrameAndClearingPreview_DisposesOnlyBitmapsNoLongerDisplayed()
    {
        using var viewModel = new FrmPreviewViewModel();
        viewModel.SetPreview(CreateDocument(), CreatePalette(), "art/critters/test.frm");
        var firstBitmap = viewModel.PreviewBitmap!;

        viewModel.SelectedFrameIndex = 1;
        var secondBitmap = viewModel.PreviewBitmap!;

        Assert.NotSame(firstBitmap, secondBitmap);
        AssertBitmapDisposed(firstBitmap);
        AssertBitmapUsable(secondBitmap);

        viewModel.ClearPreview();

        AssertBitmapDisposed(secondBitmap);
    }

    [Fact]
    public async Task CheckerboardAndPngExport_UseFrmPresentationState()
    {
        using var viewModel = new FrmPreviewViewModel();
        viewModel.SetPreview(CreateDocument(), CreatePalette(), "art/critters/test.frm");

        Assert.False(viewModel.IsCheckerboardBackground);
        viewModel.IsCheckerboardBackground = true;
        Assert.True(viewModel.IsCheckerboardBackground);

        using var output = new MemoryStream();
        await viewModel.ExportSelectedFramePngAsync(output);

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, output.ToArray()[..8]);
        Assert.False(viewModel.IsPlaying);
    }

    [Fact]
    public async Task ExportSelectedFramePngAsync_NotifiesThatSpritesheetExportIsTemporarilyUnavailable()
    {
        using var viewModel = new FrmPreviewViewModel();
        viewModel.SetPreview(CreateDocument(), CreatePalette(), "art/critters/test.frm");
        var notifiedProperties = new List<string?>();
        viewModel.PropertyChanged += OnPropertyChanged;
        using var output = new BlockingWriteStream();

        var export = viewModel.ExportSelectedFramePngAsync(output);
        try
        {
            await output.WriteStarted;

            Assert.False(viewModel.CanExportPng);
            Assert.False(viewModel.CanExportSpritesheet);
            Assert.Contains(nameof(viewModel.CanExportSpritesheet), notifiedProperties);
        }
        finally
        {
            output.Release();
        }

        await export;
        Assert.True(viewModel.CanExportSpritesheet);

        void OnPropertyChanged(object? sender, PropertyChangedEventArgs args) =>
            notifiedProperties.Add(args.PropertyName);
    }

    [Fact]
    public void CanExportSpritesheet_RequiresDocumentAndResolvedPalette()
    {
        using var viewModel = new FrmPreviewViewModel();
        var notifiedProperties = new List<string?>();
        viewModel.PropertyChanged += OnPropertyChanged;

        viewModel.SetPreview(CreateDocument(), null, "art/critters/test.frm");
        Assert.False(viewModel.CanExportSpritesheet);
        Assert.Contains(nameof(viewModel.CanExportSpritesheet), notifiedProperties);

        notifiedProperties.Clear();
        viewModel.SetPreview(CreateDocument(), CreatePalette(), "art/critters/test.frm");

        Assert.True(viewModel.CanExportSpritesheet);
        Assert.Equal("test_spritesheet.png", viewModel.SuggestedSpritesheetPngFileName);
        Assert.Contains(nameof(viewModel.CanExportSpritesheet), notifiedProperties);

        notifiedProperties.Clear();
        viewModel.ClearPreview();
        Assert.False(viewModel.CanExportSpritesheet);
        Assert.Contains(nameof(viewModel.CanExportSpritesheet), notifiedProperties);

        void OnPropertyChanged(object? sender, PropertyChangedEventArgs args) =>
            notifiedProperties.Add(args.PropertyName);
    }

    [Fact]
    public async Task ExportSpritesheetPngAsync_WritesAllDirectionsWithArchiveEntryName()
    {
        using var viewModel = new FrmPreviewViewModel();
        viewModel.SetPreview(CreateDocument(), CreatePalette(), "art/critters/test.frm");
        using var output = new MemoryStream();

        await viewModel.ExportSpritesheetPngAsync(output);

        Assert.Equal(
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A },
            output.ToArray()[..8]);
        using var metadata = ReadSpritesheetMetadata(output.ToArray());
        var root = metadata.RootElement;
        Assert.Equal("test.frm", root.GetProperty("assetName").GetString());
        var directions = root.GetProperty("directions").EnumerateArray().ToArray();
        Assert.Equal(2, directions.Length);
        Assert.Equal(0, directions[0].GetProperty("index").GetInt32());
        Assert.Equal(2, directions[0].GetProperty("frames").GetArrayLength());
        Assert.Equal(1, directions[1].GetProperty("index").GetInt32());
        Assert.Equal(1, directions[1].GetProperty("frames").GetArrayLength());
        Assert.True(viewModel.CanExportSpritesheet);
    }

    [Fact]
    public async Task ExportSpritesheetPngAsync_ReportsDestinationWriteFailureAndRestoresAvailability()
    {
        using var viewModel = new FrmPreviewViewModel();
        viewModel.SetPreview(CreateDocument(), CreatePalette(), "art/critters/test.frm");

        await Assert.ThrowsAsync<IOException>(() =>
            viewModel.ExportSpritesheetPngAsync(new ThrowingWriteStream()));

        Assert.True(viewModel.CanExportSpritesheet);
        Assert.True(viewModel.CanExportPng);
    }

    [Fact]
    public async Task ExportSpritesheetPngAsync_RejectsMissingPalette()
    {
        using var viewModel = new FrmPreviewViewModel();
        viewModel.SetPreview(CreateDocument(), null, "art/critters/test.frm");

        Assert.False(viewModel.CanExportSpritesheet);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            viewModel.ExportSpritesheetPngAsync(new MemoryStream()));
    }

    [Fact]
    public void Dispose_ClearsOwnedBitmapAndRejectsFurtherPreviewState()
    {
        var viewModel = new FrmPreviewViewModel();
        viewModel.SetPreview(CreateDocument(), CreatePalette(), "art/critters/test.frm");
        var bitmap = viewModel.PreviewBitmap!;

        viewModel.Dispose();

        Assert.Null(viewModel.PreviewBitmap);
        Assert.False(viewModel.HasPreview);
        AssertBitmapDisposed(bitmap);
        Assert.Throws<ObjectDisposedException>(
            () => viewModel.SetPreview(CreateDocument(), CreatePalette(), "other.frm"));
        viewModel.Dispose();
    }

    private static FrmDocument CreateDocument() =>
        new(
            4,
            10,
            0,
            2,
            [
                new FrmDirection(
                    0,
                    "NE",
                    0,
                    0,
                    [
                        new FrmFrame(1, 1, 0, 0, [1]),
                        new FrmFrame(2, 1, 0, 0, [2, 3])
                    ]),
                new FrmDirection(
                    1,
                    "E",
                    0,
                    0,
                    [new FrmFrame(1, 1, 0, 0, [4])])
            ]);

    private static FalloutPalette CreatePalette() =>
        FalloutPalette.Parse(new byte[FalloutPalette.MinimalFileSize]);

    private static JsonDocument ReadSpritesheetMetadata(byte[] png)
    {
        var offset = 8;
        while (offset < png.Length)
        {
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, sizeof(uint))));
            var dataOffset = offset + 8;
            var chunkType = Encoding.ASCII.GetString(png, offset + sizeof(uint), 4);
            if (chunkType == "iTXt")
            {
                var keywordEnd = Array.IndexOf(png, (byte)0, dataOffset, length);
                var textOffset = checked(keywordEnd - dataOffset + 1 + 2);
                var languageEnd = Array.IndexOf(png, (byte)0, dataOffset + textOffset, length - textOffset);
                textOffset = checked(languageEnd - dataOffset + 1);
                var translatedEnd = Array.IndexOf(png, (byte)0, dataOffset + textOffset, length - textOffset);
                textOffset = checked(translatedEnd - dataOffset + 1);
                return JsonDocument.Parse(png.AsMemory(dataOffset + textOffset, length - textOffset));
            }

            offset = checked(offset + sizeof(uint) + 4 + length + sizeof(uint));
        }

        throw new InvalidDataException("PNG spritesheet has no iTXt metadata chunk.");
    }

    private static void AssertBitmapDisposed(Bitmap bitmap) =>
        Assert.Throws<NullReferenceException>(() => bitmap.Save(Stream.Null));

    private static void AssertBitmapUsable(Bitmap bitmap)
    {
        using var output = new MemoryStream();
        bitmap.Save(output);
        Assert.NotEmpty(output.ToArray());
    }

    private sealed class ThrowingWriteStream : MemoryStream
    {
        public override void Write(ReadOnlySpan<byte> buffer) =>
            throw new IOException("Simulated destination write failure.");

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new IOException("Simulated destination write failure.");
    }

    private sealed class BlockingWriteStream : MemoryStream
    {
        private readonly TaskCompletionSource<bool> _writeStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _release = new();

        public Task WriteStarted => _writeStarted.Task;

        public void Release() => _release.Set();

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _writeStarted.TrySetResult(true);
            _release.Wait();
            base.Write(buffer);
        }
    }
}

internal static class AvaloniaTestEnvironment
{
    private static readonly Lazy<bool> Initialized = new(Initialize);

    public static void EnsureInitialized() => _ = Initialized.Value;

    private static bool Initialize()
    {
        AppBuilder.Configure<global::FalloutAssetExplorer.App.App>()
            .UsePlatformDetect()
            .SetupWithoutStarting();
        return true;
    }
}
