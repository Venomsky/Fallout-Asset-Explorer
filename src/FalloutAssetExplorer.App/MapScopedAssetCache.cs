namespace FalloutAssetExplorer.App;

public sealed class MapScopedAssetCache<TAsset> : IDisposable
    where TAsset : class
{
    public const long DefaultByteLimit = 64L * 1024 * 1024;

    private readonly Dictionary<string, (TAsset Asset, long ByteSize)> _assets =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<TAsset> _disposeAsset;
    private readonly long _byteLimit;
    private bool _isDisposed;

    public MapScopedAssetCache(
        long byteLimit,
        Action<TAsset> disposeAsset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(byteLimit);
        ArgumentNullException.ThrowIfNull(disposeAsset);
        _byteLimit = byteLimit;
        _disposeAsset = disposeAsset;
    }

    public long CachedBytes { get; private set; }

    public bool TryGet(string key, out TAsset? asset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!_isDisposed && _assets.TryGetValue(NormalizeKey(key), out var cached))
        {
            asset = cached.Asset;
            return true;
        }

        asset = null;
        return false;
    }

    public bool TryAdd(string key, TAsset asset, long byteSize, out string? error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentOutOfRangeException.ThrowIfNegative(byteSize);
        var normalizedKey = NormalizeKey(key);

        if (_isDisposed)
        {
            error = "The map tile cache has been disposed.";
            return false;
        }

        if (_assets.ContainsKey(normalizedKey))
        {
            error = $"The map tile cache already contains '{key}'.";
            return false;
        }

        if (byteSize > _byteLimit - CachedBytes)
        {
            error = $"The map tile bitmap cache limit of {_byteLimit / (1024 * 1024)} MiB would be exceeded.";
            return false;
        }

        _assets.Add(normalizedKey, (asset, byteSize));
        CachedBytes += byteSize;
        error = null;
        return true;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        foreach (var cached in _assets.Values)
        {
            _disposeAsset(cached.Asset);
        }

        _assets.Clear();
        CachedBytes = 0;
    }

    private static string NormalizeKey(string key) =>
        key.Replace('\\', '/');
}
