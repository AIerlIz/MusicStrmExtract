using MusicStrmExtract.Caching;
using MusicStrmExtract.Online;

namespace MusicStrmExtract.Providers;

/// <summary>管理插件自有缓存，不触碰 Emby 媒体库数据。</summary>
internal sealed class PluginCacheManager
{
    private readonly object _gate = new();
    private readonly TtlCache<AlbumSearchResult> _albumResolutionCache;
    private readonly TtlCache<AlbumDirectoryScan> _albumDirectoryScanCache;

    public PluginCacheManager(
        TtlCache<AlbumSearchResult> albumResolutionCache,
        TtlCache<AlbumDirectoryScan> albumDirectoryScanCache)
    {
        _albumResolutionCache = albumResolutionCache ?? throw new ArgumentNullException(nameof(albumResolutionCache));
        _albumDirectoryScanCache = albumDirectoryScanCache ?? throw new ArgumentNullException(nameof(albumDirectoryScanCache));
    }

    public int AlbumResolutionCount
    {
        get
        {
            lock (_gate)
                return _albumResolutionCache.Count;
        }
    }

    public int AlbumDirectoryScanCount
    {
        get
        {
            lock (_gate)
                return _albumDirectoryScanCache.Count;
        }
    }

    public string GetStatus()
    {
        lock (_gate)
        {
            return $"专辑定位缓存 {_albumResolutionCache.Count} 条，目录扫描缓存 {_albumDirectoryScanCache.Count} 条。";
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _albumResolutionCache.Clear();
            _albumDirectoryScanCache.Clear();
        }
    }
}
