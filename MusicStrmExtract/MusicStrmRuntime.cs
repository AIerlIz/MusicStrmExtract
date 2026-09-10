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
    private static readonly TimeSpan s_albumScanTtl = TimeSpan.FromSeconds(30);

    private static readonly TtlCache<AlbumSearchResult> s_albumCache =
        new(TimeSpan.FromMinutes(30), CacheMaxEntries);

    private static readonly TtlCache<AlbumDirectoryScan> s_albumScanCache =
        new(s_albumScanTtl, CacheMaxEntries);

    private static readonly ResolutionDiagnosticsStore s_diagnostics = new();

    private static readonly PluginCacheManager s_cacheManager =
        new(s_albumCache, s_albumScanCache);

    private static readonly MusicBrainzSourceCheckService s_sourceCheckService =
        new(baseUrl => new MusicBrainzApi(baseUrl));

    public static PluginCacheManager CacheManager => s_cacheManager;

    public static ResolutionDiagnosticsStore Diagnostics => s_diagnostics;

    public static MusicBrainzSourceCheckService SourceCheckService => s_sourceCheckService;

    public static IAlbumResolutionService CreateAlbumResolutionService(ILogger logger)
    {
        return new AlbumTrackMapLocator(
            logger,
            s_albumCache,
            baseUrl => new MusicBrainzApi(baseUrl),
            s_diagnostics);
    }

    public static IAlbumDirectoryScanService CreateAlbumDirectoryScanService()
    {
        return new AlbumDirectoryScanService(s_albumScanCache);
    }
}
