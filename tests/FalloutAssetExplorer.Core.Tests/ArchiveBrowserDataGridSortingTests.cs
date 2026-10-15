using System.ComponentModel;
using Avalonia.Collections;
using Avalonia.Controls;
using FalloutAssetExplorer.App.ViewModels;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class ArchiveBrowserDataGridSortingTests
{
    [Fact]
    public void RefreshVisibleEntries_PreservesPathSortInDataGridView()
    {
        AvaloniaTestEnvironment.EnsureInitialized();

        using var viewModel = new ArchiveBrowserViewModel();
        viewModel.Entries.Add(new ArchiveEntry("maps\\zeta.frm", 40, 30, "master.dat"));
        viewModel.Entries.Add(new ArchiveEntry("art\\beta.frm", 20, 15, "master.dat"));
        viewModel.Entries.Add(new ArchiveEntry("maps\\alphabet.frm", 30, 22, "master.dat"));
        viewModel.Entries.Add(new ArchiveEntry("art\\alpha.frm", 10, 8, "master.dat"));
        viewModel.SearchText = ".frm";

        var dataGrid = new DataGrid
        {
            ItemsSource = viewModel.VisibleEntries
        };
        var collectionView = dataGrid.CollectionView;
        Assert.NotNull(collectionView);
        Assert.True(collectionView.CanSort);
        var pathComparer = Comparer<ArchiveEntry>.Create(
            (left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));
        collectionView.SortDescriptions.Add(
            new DataGridComparerSortDescription(pathComparer, ListSortDirection.Ascending));

        viewModel.SearchText = "alpha";

        Assert.Equal(
            ["art\\alpha.frm", "maps\\alphabet.frm"],
            collectionView.Cast<ArchiveEntry>().Select(entry => entry.Path));

        viewModel.SearchText = string.Empty;

        Assert.Equal(
            ["art\\alpha.frm", "art\\beta.frm", "maps\\alphabet.frm", "maps\\zeta.frm"],
            collectionView.Cast<ArchiveEntry>().Select(entry => entry.Path));
        Assert.Single(collectionView.SortDescriptions);
    }
}
