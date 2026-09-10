using System.Collections.Concurrent;
using MusicStrmExtract.Caching;

namespace MusicStrmExtract.Providers;

/// <summary>专辑目录扫描的缓存边界，减少同一专辑每个轨道重复枚举文件。</summary>
internal interface IAlbumDirectoryScanService
{
    AlbumDirectoryScan GetOrScan(string albumDir, Action<string>? warning = null);
}

internal sealed class AlbumDirectoryScanService : IAlbumDirectoryScanService
{
    private readonly TtlCache<AlbumDirectoryScan> _cache;
    private readonly Func<string, Action<string>?, AlbumDirectoryScan> _scan;
    private readonly ConcurrentDictionary<string, Lazy<AlbumDirectoryScan>> _inflight =
        new(StringComparer.Ordinal);

    public AlbumDirectoryScanService(TtlCache<AlbumDirectoryScan> cache)
        : this(cache, AlbumDirectoryScanner.Scan)
    {
    }

    internal AlbumDirectoryScanService(
        TtlCache<AlbumDirectoryScan> cache,
        Func<string, Action<string>?, AlbumDirectoryScan> scan)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _scan = scan ?? throw new ArgumentNullException(nameof(scan));
    }

    public AlbumDirectoryScan GetOrScan(string albumDir, Action<string>? warning = null)
    {
        if (string.IsNullOrWhiteSpace(albumDir))
            throw new ArgumentException("Album directory must not be empty.", nameof(albumDir));

        if (_cache.TryGet(albumDir, out var cached))
            return cached;

        var lazy = _inflight.GetOrAdd(
            albumDir,
            key => new Lazy<AlbumDirectoryScan>(
                () =>
                {
                    var scan = _scan(key, warning);
                    _cache.Set(key, scan);
                    return scan;
                },
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return lazy.Value;
        }
        finally
        {
            _ = _inflight.TryRemove(
                new KeyValuePair<string, Lazy<AlbumDirectoryScan>>(albumDir, lazy));
        }
    }
}
