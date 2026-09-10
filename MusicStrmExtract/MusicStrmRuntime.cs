using MediaBrowser.Model.Logging;
using MusicStrmExtract.Caching;
using MusicStrmExtract.Online;
using MusicStrmExtract.Providers;

namespace MusicStrmExtract;

/// <summary>
/// 插件进程级运行时依赖。当前只承载历史共享缓存，后续可继续接收 Provider 和 UI 服务。
/// </summary>
internal static class MusicStrmRuntime
{
    private const int CacheMaxEntries = 500;

    private static readonly TtlCache<AlbumSearchResult> s_albumCache =
        new(TimeSpan.FromMinutes(30), CacheMaxEntries);

    public static IAlbumResolutionService CreateAlbumResolutionService(ILogger logger)
    {
        return new AlbumTrackMapLocator(logger, s_albumCache);
    }
}
