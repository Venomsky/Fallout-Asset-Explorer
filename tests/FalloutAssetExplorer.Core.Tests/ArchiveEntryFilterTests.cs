using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class ArchiveEntryFilterTests
{
    private static readonly ArchiveEntry[] Entries =
    [
        new("art\\critters\\hero.frm", 120, 80, "master.dat"),
        new("maps\\vault.map", 240, null, "master.dat"),
        new("data\\protos\\items.pro", 48, 32, "critter.dat")
    ];

    [Fact]
    public void Apply_FiltersSearchCaseInsensitively()
    {
        var result = ArchiveEntryFilter.Apply(Entries, "HERO", ArchiveEntryFilter.AllExtensions);

        Assert.Single(result);
        Assert.Equal("art\\critters\\hero.frm", result[0].Path);
    }

    [Fact]
    public void Apply_FiltersByExtension()
    {
        var result = ArchiveEntryFilter.Apply(Entries, null, ".MAP");

        Assert.Single(result);
        Assert.Equal(".MAP", result[0].Extension);
    }

    [Fact]
    public void Apply_CombinesSearchAndExtensionFilters()
    {
        var result = ArchiveEntryFilter.Apply(Entries, "items", ".PRO");

        Assert.Single(result);
        Assert.Equal("data\\protos\\items.pro", result[0].Path);
    }

    [Fact]
    public void Extension_IsUppercaseForMixedCasePaths()
    {
        var entry = new ArchiveEntry("art\\critters\\Hero.FrM", 120, 80, "master.dat");

        Assert.Equal(".FRM", entry.Extension);
    }
}
