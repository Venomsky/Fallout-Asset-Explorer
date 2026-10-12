using FalloutAssetExplorer.App;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class MapScopedAssetCacheTests
{
    [Fact]
    public void TryGet_ReusesCaseInsensitiveResourcePathAndDisposeReleasesCachedAsset()
    {
        var disposedCount = 0;
        using var cache = new MapScopedAssetCache<TestAsset>(8, asset => asset.Dispose());
        var asset = new TestAsset(() => disposedCount++);

        Assert.True(cache.TryAdd("art/tiles/road.frm", asset, 8, out var error));
        Assert.Null(error);
        Assert.True(cache.TryGet("ART\\TILES\\ROAD.FRM", out var reused));
        Assert.Same(asset, reused);
        Assert.Equal(8, cache.CachedBytes);

        cache.Dispose();

        Assert.Equal(1, disposedCount);
        Assert.Equal(0, cache.CachedBytes);
        Assert.False(cache.TryGet("art/tiles/road.frm", out _));
    }

    [Fact]
    public void TryAdd_RejectsAssetsThatExceedMapCacheBudgetWithoutTakingOwnership()
    {
        using var cache = new MapScopedAssetCache<TestAsset>(4, asset => asset.Dispose());
        var asset = new TestAsset(() => { });

        var added = cache.TryAdd("art/tiles/large.frm", asset, 5, out var error);

        Assert.False(added);
        Assert.Contains("limit", error);
        Assert.Equal(0, cache.CachedBytes);
        asset.Dispose();
    }

    [Fact]
    public void TryAdd_RejectsDuplicatePathsWithoutReplacingCachedAsset()
    {
        var firstDisposeCount = 0;
        var secondDisposeCount = 0;
        using var cache = new MapScopedAssetCache<TestAsset>(8, asset => asset.Dispose());
        var first = new TestAsset(() => firstDisposeCount++);
        var second = new TestAsset(() => secondDisposeCount++);
        Assert.True(cache.TryAdd("art/tiles/one.frm", first, 1, out _));

        var added = cache.TryAdd("ART/TILES/ONE.FRM", second, 1, out var error);

        Assert.False(added);
        Assert.Contains("already contains", error);
        Assert.True(cache.TryGet("art/tiles/one.frm", out var cached));
        Assert.Same(first, cached);
        cache.Dispose();
        second.Dispose();
        Assert.Equal(1, firstDisposeCount);
        Assert.Equal(1, secondDisposeCount);
    }

    private sealed class TestAsset(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
