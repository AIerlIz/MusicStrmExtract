using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Model.Logging;

using MusicStrmExtract.Caching;
using MusicStrmExtract.Online;

namespace MusicStrmExtract.Providers
{
    /// <summary>
    /// 专辑定位结果的网络读取与缓存入口。
    /// 一次专辑定位包含整张专辑的轨道映射;同专辑后续 strm 条目零请求直接命中缓存。
    /// </summary>
    internal sealed class AlbumTrackMapLocator
    {
        private readonly ILogger _logger;
        private readonly TtlCache<AlbumSearchResult> _cache;

        public AlbumTrackMapLocator(ILogger logger, TtlCache<AlbumSearchResult> cache)
        {
            _logger = logger;
            _cache = cache;
        }

        public async Task<AlbumSearchResult> GetOrSearchAsync(
            string cacheKey,
            string albumFolder,
            string? artistFolder,
            IReadOnlyList<LocalDisc> localDiscs,
            PluginConfiguration config,
            CancellationToken ct)
        {
            if (_cache.TryGet(cacheKey, out var cached))
            {
                return cached;
            }

            using var api = new MusicBrainzApi(
                string.IsNullOrWhiteSpace(config.MusicBrainzBaseUrl) ? null : config.MusicBrainzBaseUrl);
            var coverArt = new CoverArtClient(config.CoverArtBaseUrl);
            var search = new AlbumSearch(api, coverArt);
            var result = await search.SearchForTrackMapAsync(albumFolder, artistFolder, localDiscs, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            _cache.Set(cacheKey, result);
            _logger.Info($"[MusicStrmExtract] [LocalProvider] 专辑定位: '{albumFolder}' -> " +
                (result.Found
                    ? $"'{result.Title}' releaseMBID={result.ReleaseMbid} 碟数={result.Medias.Count} 轨数={result.Medias.Sum(m => m.Tracks.Count)}"
                    : "无命中/碟轨覆盖未通过"));
            return result;
        }

        public static string BuildCacheKey(
            string albumFolder,
            string? artistFolder,
            IReadOnlyList<LocalDisc> localDiscs,
            PluginConfiguration config)
        {
            // 对碟组按 DiscNumber 和 TrackNumbers 排序,保证目录枚举非确定性下缓存 Key 稳定
            var layout = string.Join("|", localDiscs
                .OrderBy(d => d.DiscNumber ?? int.MaxValue)
                .Select(d =>
                    (d.DiscNumber?.ToString(CultureInfo.InvariantCulture) ?? "_")
                    + ":"
                    + string.Join("-", d.TrackNumbers.OrderBy(n => n))));
            var musicBrainzSource = string.IsNullOrWhiteSpace(config.MusicBrainzBaseUrl)
                ? "official"
                : config.MusicBrainzBaseUrl.Trim().TrimEnd('/');
            var coverArtSource = string.IsNullOrWhiteSpace(config.CoverArtBaseUrl)
                ? "official"
                : config.CoverArtBaseUrl.Trim().TrimEnd('/');

            // 服务地址也进 key:切换镜像后不应继续命中旧镜像缓存的专辑定位结果。
            return $"{albumFolder}|{artistFolder}|{layout}|{musicBrainzSource}|{coverArtSource}";
        }
    }
}
