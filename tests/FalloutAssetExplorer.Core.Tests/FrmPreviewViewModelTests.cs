using System.ComponentModel;
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

        var displayedBitmap = viewModel.PreviewBitmap;
        viewModel.ClearPreview();

        Assert.Null(viewModel.PreviewBitmap);
        Assert.False(viewModel.HasPreview);
        Assert.False(viewModel.CanPlay);
        Assert.False(viewModel.CanExportPng);
        Assert.Empty(viewModel.FrameOptions);
        Assert.Equal(0, viewModel.SelectedDirectionIndex);
        Assert.Equal(0, viewModel.SelectedFrameIndex);
        Assert.Equal("Select a .FRM or .MAP entry to inspect.", viewModel.PreviewDetails);
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

    private static void AssertBitmapDisposed(Bitmap bitmap) =>
        Assert.Throws<NullReferenceException>(() => bitmap.Save(Stream.Null));

    private static void AssertBitmapUsable(Bitmap bitmap)
    {
        using var output = new MemoryStream();
        bitmap.Save(output);
        Assert.NotEmpty(output.ToArray());
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
