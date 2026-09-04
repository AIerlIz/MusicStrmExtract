using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
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

        /// <summary>按 strm 路径解析"艺人\专辑\碟"文件夹结构。
        /// 返回 (专辑文件夹名, 艺人文件夹名, 专辑实际目录, 碟号);strm 直接在专辑目录时碟号为 null,
        /// 位于 "Album/Disc N/" 时解析出碟号并上移一级专辑目录。</summary>
        private static (string? AlbumFolder, string? ArtistFolder, string? AlbumDir, int? DiscNumber) GetFolderStructure(string strmPath)
        {
            var fileDir = Path.GetDirectoryName(strmPath);
            if (string.IsNullOrWhiteSpace(fileDir))
            {
                return (null, null, null, null);
            }

            var discNumber = StrmFileParser.ParseDiscFolderName(Path.GetFileName(fileDir));
            if (discNumber is not null && !string.IsNullOrWhiteSpace(Path.GetDirectoryName(fileDir)))
            {
                var albumDir = Path.GetDirectoryName(fileDir)!;
                var artistDir = Path.GetDirectoryName(albumDir);
                return (
                    Path.GetFileName(albumDir),
                    string.IsNullOrWhiteSpace(artistDir) ? null : Path.GetFileName(artistDir),
                    albumDir,
                    discNumber);
            }

            var artistDir2 = Path.GetDirectoryName(fileDir);
            return (
                Path.GetFileName(fileDir),
                string.IsNullOrWhiteSpace(artistDir2) ? null : Path.GetFileName(artistDir2),
                fileDir,
                null);
        }

        /// <summary>引擎合并时可能被 RemoteProvider(基于库中旧 MBID 的在线结果)覆盖,这里把本地
        /// 目录定位得到的最终字段直写真实条目(UpdateToRepository 实测有效),保证本地结果优先。</summary>
        private void SyncRepositoryItem(ItemInfo info, Audio item)
        {
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
                if (!string.IsNullOrWhiteSpace(item.Album)
                    && !string.Equals(real.Album, item.Album, StringComparison.Ordinal))
                {
                    real.Album = item.Album;
                    changed = true;
                }

                var wantedArtists = item.AlbumArtists.Where(a => !string.IsNullOrWhiteSpace(a)).ToArray();
                if (wantedArtists.Length > 0
                    && !real.AlbumArtists.SequenceEqual(wantedArtists, StringComparer.Ordinal))
                {
                    real.AlbumArtists = wantedArtists.ToArray();
                    changed = true;
                }

                if (!string.IsNullOrWhiteSpace(item.Name)
                    && !string.Equals(real.Name, item.Name, StringComparison.Ordinal))
                {
                    real.Name = item.Name;
                    changed = true;
                }

                if (item.ProductionYear != real.ProductionYear)
                {
                    real.ProductionYear = item.ProductionYear;
                    changed = true;
                }

                if (item.IndexNumber != real.IndexNumber)
                {
                    real.IndexNumber = item.IndexNumber;
                    changed = true;
                }

                if (item.ParentIndexNumber != real.ParentIndexNumber)
                {
                    real.ParentIndexNumber = item.ParentIndexNumber;
                    changed = true;
                }

                foreach (var kv in item.ProviderIds)
                {
                    if (!real.ProviderIds.TryGetValue(kv.Key, out var current) || current != kv.Value)
                    {
                        real.ProviderIds[kv.Key] = kv.Value;
                        changed = true;
                    }
                }

                if (changed)
                {
                    real.UpdateToRepository(ItemUpdateType.MetadataEdit);
                    _logger.Info($"[MusicStrmExtract] [LocalProvider] 直写真实条目: Id={real.Id} Name='{real.Name}' Album='{real.Album}' MBTrack={item.ProviderIds.GetValueOrDefault("MusicBrainzTrack")}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[MusicStrmExtract] [LocalProvider] 直写条目失败: Path={info.Path} -> {ex.Message}");
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
            var (albumFolder, artistFolder, albumDir, discNumber) = GetFolderStructure(info.Path);
            if (!string.IsNullOrWhiteSpace(albumFolder)
                && !string.IsNullOrWhiteSpace(albumDir)
                && await TryResolveByAlbumTrackAsync(
                    info, albumFolder, artistFolder, albumDir, discNumber, config, result, cancellationToken).ConfigureAwait(false))
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
            string albumDir,
            int? folderDisc,
            PluginConfiguration config,
            MetadataResult<Audio> result,
            CancellationToken ct)
        {
            var (fileDisc, rawTrackNumber, isCommentary) = StrmFileParser.ParseFileName(info.Path);
            if (rawTrackNumber <= 0)
            {
                return false; // 本文件无轨号,无法按轨取数
            }

            var (localDiscs, rawTracks) = ScanAlbumDiscs(albumDir);
            if (localDiscs.Count == 0)
            {
                return false;
            }

            AlbumSearchResult album;
            try
            {
                album = await GetAlbumTrackMapAsync(
                    BuildAlbumCacheKey(albumFolder, artistFolder, localDiscs),
                    albumFolder,
                    artistFolder,
                    localDiscs,
                    config,
                    ct).ConfigureAwait(false);
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

            var mapping = AlbumSearch.MapLocalDiscsToMedias(localDiscs, album.Medias);
            if (mapping is null)
            {
                return false;
            }

            var group = localDiscs.FirstOrDefault(d => d.DiscNumber == (folderDisc ?? fileDisc));
            if (group is null || !mapping.TryGetValue(group, out var media))
            {
                return false;
            }

            var rawRefs = rawTracks.TryGetValue(group.DiscNumber ?? 0, out var refs)
                ? refs
                : new List<(int Number, bool IsCommentary)>();
            var selfNumber = StrmFileParser.MapCommentaryTrackNumber(
                rawTrackNumber,
                isCommentary,
                rawRefs.Where(r => r.IsCommentary).Select(r => r.Number).ToArray(),
                rawRefs.Where(r => !r.IsCommentary).Select(r => r.Number).ToArray());
            if (selfNumber <= 0)
            {
                return false;
            }

            var track = media.Tracks.FirstOrDefault(t => t.Number == selfNumber);
            if (track is null)
            {
                return false;
            }

            // 命中:数据全部来自 MB 专辑 tracklist(recording MBID 真实无脏)
            var albumArtists = !string.IsNullOrWhiteSpace(album.ArtistName)
                ? new[] { album.ArtistName! }
                : Array.Empty<string>();
            var trackArtists = track.Artists.Count > 0 ? track.Artists.ToArray() : albumArtists;

            var displayName = (track.Title ?? Path.GetFileNameWithoutExtension(info.Path)).Trim();
            if (isCommentary)
            {
                displayName += " (Commentary)";
            }

            var item = new Audio
            {
                Name = displayName,
                Album = album.Title,
                ProductionYear = album.Year,
                IndexNumber = track.Number,
                ParentIndexNumber = group.DiscNumber is not null || localDiscs.Count > 1 ? media.Position : (int?)null,
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
            SyncRepositoryItem(info, item);

            _logger.Info($"[MusicStrmExtract] [LocalProvider] 专辑轨道定位: '{albumFolder}' 碟 {media.Position} 轨 {track.Number} '{track.Title}' recordingMBID={track.RecordingMbid}");
            return true;
        }

        private async Task<AlbumSearchResult> GetAlbumTrackMapAsync(
            string key,
            string albumFolder,
            string? artistFolder,
            List<LocalDisc> localDiscs,
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
            var result = await search.SearchForTrackMapAsync(albumFolder, artistFolder, localDiscs, ct).ConfigureAwait(false);
            PruneCache(AlbumCache);
            AlbumCache[key] = (DateTime.UtcNow, result);
            _logger.Info($"[MusicStrmExtract] [LocalProvider] 专辑定位: '{albumFolder}' -> " +
                (result.Found
                    ? $"'{result.Title}' releaseMBID={result.ReleaseMbid} 碟数={result.Medias.Count} 轨数={result.Medias.Sum(m => m.Tracks.Count)}"
                    : "无命中/碟轨覆盖未通过"));
            return result;
        }

        private static string BuildAlbumCacheKey(string albumFolder, string? artistFolder, List<LocalDisc> localDiscs)
        {
            var layout = string.Join("|", localDiscs.Select(d =>
                (d.DiscNumber?.ToString(CultureInfo.InvariantCulture) ?? "_")
                + ":"
                + string.Join("-", d.TrackNumbers)));
            return $"{albumFolder}|{artistFolder}|{layout}";
        }

        /// <summary>
        /// 扫描专辑目录上的 .strm(含 Disc N 子目录),构建本地碟组。
        /// 评论轨与正式轨先按原始轨号收集,再做评论轨归一化,避免 1..26 交错轨号破坏 release 覆盖校验。
        /// </summary>
        private static (List<LocalDisc> Discs, Dictionary<int, List<(int Number, bool IsCommentary)>> RawTracks) ScanAlbumDiscs(string albumDir)
        {
            // 解析结果中碟号只会是 null 或正整数,用 0 作为无碟号的字典键。
            var rawGroups = new Dictionary<int, List<(int Number, bool IsCommentary)>>();
            var seen = new HashSet<(int Disc, int Track, bool Commentary)>();

            void AddTrack(int? disc, int number, bool isCommentary)
            {
                var key = disc ?? 0;
                if (number <= 0 || !seen.Add((key, number, isCommentary)))
                {
                    return;
                }

                if (!rawGroups.TryGetValue(key, out var list))
                {
                    list = new List<(int Number, bool IsCommentary)>();
                    rawGroups.Add(key, list);
                }

                list.Add((number, isCommentary));
            }

            try
            {
                foreach (var f in Directory.EnumerateFiles(albumDir))
                {
                    if (!f.EndsWith(".strm", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var (disc, number, isCommentary) = StrmFileParser.ParseFileName(f);
                    AddTrack(disc, number, isCommentary);
                }

                foreach (var sub in Directory.EnumerateDirectories(albumDir))
                {
                    var disc = StrmFileParser.ParseDiscFolderName(Path.GetFileName(sub));
                    if (disc is null)
                    {
                        continue;
                    }

                    foreach (var f in Directory.EnumerateFiles(sub))
                    {
                        if (!f.EndsWith(".strm", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var (_, number, isCommentary) = StrmFileParser.ParseFileName(f);
                        AddTrack(disc, number, isCommentary);
                    }
                }
            }
            catch (Exception)
            {
                // 目录读取失败时返回已收集到的碟组
            }

            var result = new List<LocalDisc>();
            foreach (var kv in rawGroups)
            {
                var raw = kv.Value;
                var commentaryNumbers = raw.Where(r => r.IsCommentary).Select(r => r.Number).ToArray();
                var regularNumbers = raw.Where(r => !r.IsCommentary).Select(r => r.Number).ToArray();
                var group = new LocalDisc { DiscNumber = kv.Key == 0 ? null : kv.Key };
                group.TrackNumbers.AddRange(raw
                    .Select(r => StrmFileParser.MapCommentaryTrackNumber(r.Number, r.IsCommentary, commentaryNumbers, regularNumbers))
                    .Where(n => n > 0)
                    .Distinct());
                result.Add(group);
            }

            result.Sort((a, b) =>
            {
                var an = a.DiscNumber ?? int.MaxValue;
                var bn = b.DiscNumber ?? int.MaxValue;
                return an.CompareTo(bn);
            });
            foreach (var g in result)
            {
                g.TrackNumbers.Sort();
            }

            return (result, rawGroups);
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
