using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;

using MusicStrmExtract.Online;

namespace MusicStrmExtract.Providers
{
    /// <summary>
    /// 标准本地元数据读取器(ILocalMetadataProvider):Emby 在扫描/刷新 Audio 条目时调用。
    /// 只保留主路径·专辑轨道定位:文件名只解析数字轨号,扫描专辑文件夹得到本地轨号集合;
    /// 按艺人 + 专辑文件夹名查询 MB release,用轨号覆盖选择 media,再按轨号直接取 tracklist
    /// 数据(recording MBID/标题/艺人),整张专辑一次定位并缓存;不做远程探测、不做文件名文本匹配。
    /// 未命中的条目返回空结果,由 Emby 后续流程决定是否保持现状或做其它在线补全。
    /// </summary>
    public sealed class MusicStrmLocalProvider : ILocalMetadataProvider<Audio>
    {
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;

        public MusicStrmLocalProvider(ILogManager logManager, ILibraryManager libraryManager)
        {
            _logger = logManager.GetLogger("MusicStrmExtract");
            _libraryManager = libraryManager;
        }

        public string Name => "Music Strm Extract (目录)";

        /// <summary>strm 文件名数字轨号解析:"01 - 我的地盤.flac.strm" → 1;无数字前缀则 Number=-1。</summary>
        private static readonly Regex StrmNumberRegex = new Regex(
            @"^(\d{1,3})\s*[-_]\s*",
            RegexOptions.Compiled);

        /// <summary>按 strm 路径解析"艺人\专辑"文件夹结构(strm 直接位于专辑文件夹)。
        /// 返回 (专辑文件夹名, 艺人文件夹名);结构不符时对应项为 null。</summary>
        private static (string? AlbumFolder, string? ArtistFolder) GetFolderStructure(string strmPath)
        {
            var albumDir = Path.GetDirectoryName(strmPath);
            if (string.IsNullOrWhiteSpace(albumDir))
            {
                return (null, null);
            }

            var artistDir = Path.GetDirectoryName(albumDir);
            return (
                Path.GetFileName(albumDir),
                string.IsNullOrWhiteSpace(artistDir) ? null : Path.GetFileName(artistDir));
        }

        /// <summary>引擎不写 provider 的 Audio.Album 且 AlbumId 只读,此处对真实条目直写 Album/AlbumArtists
        /// (UpdateToRepository 实测有效),后续库扫描会按新值重新归组 MusicAlbum。</summary>
        private void SyncAlbumField(ItemInfo info, string? album, System.Collections.Generic.IEnumerable<string> albumArtists)
        {
            if (string.IsNullOrWhiteSpace(album))
            {
                return;
            }

            try
            {
                var real = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    Path = info.Path,
                    Limit = 1,
                    IncludeItemTypes = new[] { "Audio" }
                }).FirstOrDefault() as Audio;
                if (real is null
                    || string.IsNullOrWhiteSpace(real.Path)
                    || !real.Path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var changed = false;
                if (!string.Equals(real.Album, album, StringComparison.Ordinal))
                {
                    real.Album = album;
                    changed = true;
                }

                var wantedArtists = albumArtists.Where(a => !string.IsNullOrWhiteSpace(a)).ToArray();
                if (wantedArtists.Length > 0
                    && !real.AlbumArtists.SequenceEqual(wantedArtists, StringComparer.Ordinal))
                {
                    real.AlbumArtists = wantedArtists.ToArray();
                    changed = true;
                }

                if (changed)
                {
                    real.UpdateToRepository(ItemUpdateType.MetadataEdit);
                    _logger.Info($"[MusicStrmExtract] [LocalProvider] 直写真实条目 Album: Id={real.Id} Album='{real.Album}'");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[MusicStrmExtract] [LocalProvider] 直写 Album 失败: Path={info.Path} -> {ex.Message}");
            }
        }

        /// <summary>专辑定位结果缓存(键=专辑文件夹|艺人文件夹;TTL 30 分钟)。
        /// 含整张专辑的轨道映射——同专辑后续 strm 条目零请求直接命中。</summary>
        private static readonly ConcurrentDictionary<string, (DateTime CreatedUtc, AlbumSearchResult Value)> AlbumCache =
            new ConcurrentDictionary<string, (DateTime, AlbumSearchResult)>(StringComparer.Ordinal);

        private const int CacheMaxEntries = 500;

        /// <summary>写入前清理:TTL 过期项删除,避免"只增不清理";极端超上限整体清空兜底。</summary>
        private static void PruneCache<T>(ConcurrentDictionary<string, (DateTime CreatedUtc, T Value)> cache)
        {
            var now = DateTime.UtcNow;
            foreach (var kv in cache)
            {
                if (now - kv.Value.CreatedUtc > TimeSpan.FromMinutes(30))
                {
                    cache.TryRemove(kv.Key, out _);
                }
            }

            if (cache.Count > CacheMaxEntries)
            {
                cache.Clear();
            }
        }

        public async Task<MetadataResult<Audio>> GetMetadata(
            ItemInfo info,
            LibraryOptions libraryOptions,
            IDirectoryService directoryService,
            CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Audio>();
            if (info?.Path is null
                || !info.Path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase))
            {
                return result;
            }

            var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();

            // ===== 主路径:专辑轨道定位(艺人/专辑文件夹 → MB release tracklist;零远程探测)=====
            var (albumFolder, artistFolder) = GetFolderStructure(info.Path);
            if (!string.IsNullOrWhiteSpace(albumFolder)
                && await TryResolveByAlbumTrackAsync(info, albumFolder, artistFolder, config, result, cancellationToken).ConfigureAwait(false))
            {
                return result;
            }

            return result;
        }

        // ==================== 主路径:专辑轨道定位 ====================

        private async Task<bool> TryResolveByAlbumTrackAsync(
            ItemInfo info,
            string albumFolder,
            string? artistFolder,
            PluginConfiguration config,
            MetadataResult<Audio> result,
            CancellationToken ct)
        {
            var albumDir = Path.GetDirectoryName(info.Path);
            if (string.IsNullOrWhiteSpace(albumDir))
            {
                return false;
            }

            var selfNumber = ParseStrmFileName(info.Path);
            if (selfNumber <= 0)
            {
                return false; // 本文件无轨号,无法按轨取数
            }

            var localTracks = ScanAlbumTracks(albumDir);
            if (localTracks.Count == 0)
            {
                return false;
            }

            AlbumSearchResult album;
            try
            {
                album = await GetAlbumTrackMapAsync(
                    $"{albumFolder}|{artistFolder}", albumFolder, artistFolder, localTracks, config, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                // MB 不可达/超时:不写缓存、不产生结果(条目保持现状)
                _logger.Warn($"[MusicStrmExtract] [LocalProvider] 专辑定位 MB 不可达: '{albumFolder}' -> {ex.Message}");
                return false;
            }

            if (!album.Found)
            {
                return false;
            }

            var track = album.Tracks.FirstOrDefault(t => t.Number == selfNumber);
            if (track is null)
            {
                return false;
            }

            // 命中:数据全部来自 MB 专辑 tracklist(recording MBID 真实无脏)
            var albumArtists = !string.IsNullOrWhiteSpace(album.ArtistName)
                ? new[] { album.ArtistName! }
                : Array.Empty<string>();
            var trackArtists = track.Artists.Count > 0 ? track.Artists.ToArray() : albumArtists;

            var item = new Audio
            {
                Name = (track.Title ?? Path.GetFileNameWithoutExtension(info.Path)).Trim(),
                Album = album.Title,
                ProductionYear = album.Year,
                IndexNumber = track.Number,
                Artists = trackArtists,
                AlbumArtists = albumArtists
            };

            SetProviderId(item, "MusicBrainzTrack", track.RecordingMbid);
            SetProviderId(item, "MusicBrainzAlbum", album.ReleaseMbid);
            SetProviderId(item, "MusicBrainzArtist", track.ArtistMbid ?? album.AlbumArtistMbid);
            SetProviderId(item, "MusicBrainzAlbumArtist", album.AlbumArtistMbid);
            SetProviderId(item, "MusicBrainzReleaseGroup", album.ReleaseGroupMbid);

            result.Item = item;
            result.HasMetadata = true;
            if (!string.IsNullOrWhiteSpace(album.Title))
            {
                SyncAlbumField(info, album.Title, albumArtists);
            }

            _logger.Info($"[MusicStrmExtract] [LocalProvider] 专辑轨道定位: '{albumFolder}' 轨 {track.Number} '{track.Title}' recordingMBID={track.RecordingMbid}");
            return true;
        }

        private async Task<AlbumSearchResult> GetAlbumTrackMapAsync(
            string key,
            string albumFolder,
            string? artistFolder,
            List<int> localTracks,
            PluginConfiguration config,
            CancellationToken ct)
        {
            if (AlbumCache.TryGetValue(key, out var entry)
                && DateTime.UtcNow - entry.CreatedUtc < TimeSpan.FromMinutes(30))
            {
                return entry.Value;
            }

            using var api = new MusicBrainzApi(
                string.IsNullOrWhiteSpace(config.MusicBrainzBaseUrl) ? null : config.MusicBrainzBaseUrl);
            var search = new AlbumSearch(api);
            var result = await search.SearchForTrackMapAsync(albumFolder, artistFolder, localTracks, ct).ConfigureAwait(false);
            PruneCache(AlbumCache);
            AlbumCache[key] = (DateTime.UtcNow, result);
            _logger.Info($"[MusicStrmExtract] [LocalProvider] 专辑定位: '{albumFolder}' -> " +
                (result.Found
                    ? $"'{result.Title}' releaseMBID={result.ReleaseMbid} 轨数={result.Tracks.Count}"
                    : "无命中/轨号覆盖未通过"));
            return result;
        }

        /// <summary>解析 strm 文件名数字前缀 → 轨号;无数字前缀时返回 0。</summary>
        private static int ParseStrmFileName(string filePath)
        {
            var name = Path.GetFileName(filePath);
            if (name.EndsWith(".strm", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - ".strm".Length);
            }

            var m = StrmNumberRegex.Match(name);
            if (!m.Success)
            {
                return 0;
            }

            if (m.Groups[1].Success
                && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                return number;
            }

            return 0;
        }

        /// <summary>扫描专辑文件夹内全部 .strm,构建本地轨号集合(按轨号升序、去重,不读取文件名标题)。</summary>
        private static List<int> ScanAlbumTracks(string albumDir)
        {
            var tracks = new List<int>();
            var seen = new HashSet<int>();
            try
            {
                foreach (var f in Directory.EnumerateFiles(albumDir))
                {
                    if (!f.EndsWith(".strm", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var number = ParseStrmFileName(f);
                    if (number <= 0 || !seen.Add(number))
                    {
                        continue;
                    }

                    tracks.Add(number);
                }
            }
            catch (Exception)
            {
                return tracks;
            }

            tracks.Sort();
            return tracks;
        }

        private static void SetProviderId(Audio item, string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            item.ProviderIds[key] = value.Trim();
        }
    }
}
