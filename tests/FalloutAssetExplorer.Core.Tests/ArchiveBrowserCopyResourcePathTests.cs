using FalloutAssetExplorer.App.ViewModels;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class ArchiveBrowserCopyResourcePathTests
{
    [Fact]
    public void SelectedEntryPathToCopy_ReturnsExactSelectedPath()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        const string path = "Art\\Critters\\Hero.FRM";
        viewModel.SelectedEntry = new ArchiveEntry(path, 120, 80, "master.dat");

        Assert.True(viewModel.CanCopySelectedEntryPath);
        Assert.Equal(path, viewModel.SelectedEntryPathToCopy);
    }

    [Fact]
    public void SelectedEntryPathToCopy_PreservesBoundaryWhitespaceAndPathFormatting()
    {
        using var viewModel = new ArchiveBrowserViewModel();
        const string path = " art/backgrnd/BHOOD.FRM ";
        viewModel.SelectedEntry = new ArchiveEntry(path, 120, 80, "master.dat");

        Assert.Equal(path, viewModel.SelectedEntryPathToCopy);
    }

    [Fact]
    public void SelectedEntryPathToCopy_IsUnavailableWithoutSelection()
    {
        using var viewModel = new ArchiveBrowserViewModel();

        Assert.False(viewModel.CanCopySelectedEntryPath);
        Assert.Null(viewModel.SelectedEntryPathToCopy);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public void SelectedEntryPathToCopy_IsUnavailableForBlankPath(string path)
    {
        using var viewModel = new ArchiveBrowserViewModel();
        viewModel.SelectedEntry = new ArchiveEntry(path, 0, null, "master.dat");

        Assert.False(viewModel.CanCopySelectedEntryPath);
        Assert.Null(viewModel.SelectedEntryPathToCopy);
    }
}
