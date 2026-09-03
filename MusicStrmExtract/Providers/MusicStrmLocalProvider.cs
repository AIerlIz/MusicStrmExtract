using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;

using MusicStrmExtract.Metadata;
using MusicStrmExtract.Online;
using MusicStrmExtract.Probing;

namespace MusicStrmExtract.Providers
{
    /// <summary>
    /// 标准本地元数据读取器(ILocalMetadataProvider):Emby 在扫描/刷新 Audio 条目时调用。
    /// 对 .strm 条目读取目标 URL 并用 ffprobe 远程探测内嵌标签,返回 MetadataResult 交由
    /// Emby 刷新引擎合并与持久化(不再自行写库),随后引擎原生完成专辑/艺术家组织。
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

        public string Name => "Music Strm Extract (探测)";

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

        /// <summary>在线补全结果缓存(键=标题|艺术家|专辑|MBID;TTL 30 分钟),避免重复刷新反复请求 MusicBrainz。</summary>
        private static readonly ConcurrentDictionary<string, (DateTime CreatedUtc, OnlineMetadata Value)> OnlineCache =
            new ConcurrentDictionary<string, (DateTime, OnlineMetadata)>(StringComparer.Ordinal);

        /// <summary>专辑名搜索缓存(键=专辑文件夹|艺人文件夹;TTL 30 分钟)。</summary>
        private static readonly ConcurrentDictionary<string, (DateTime CreatedUtc, AlbumSearchResult Value)> AlbumCache =
            new ConcurrentDictionary<string, (DateTime, AlbumSearchResult)>(StringComparer.Ordinal);

        private async Task<AlbumSearchResult> GetAlbumSearchAsync(
            string key,
            string albumFolder,
            string? artistFolder,
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
            var result = await search.SearchAsync(albumFolder, artistFolder, ct).ConfigureAwait(false);
            AlbumCache[key] = (DateTime.UtcNow, result);
            _logger.Info($"[MusicStrmExtract] [LocalProvider] 专辑名搜索: '{albumFolder}' -> " + (result.Found ? $"'{result.Title}' MBID={result.ReleaseMbid}" : "无命中"));
            return result;
        }

        private async Task<OnlineMetadata> GetOnlineAsync(
            TrackMetadata md,
            PluginConfiguration config,
            CancellationToken ct)
        {
            var key = $"{md.Title}|{string.Join("/", md.Artists)}|{md.Album}|{md.MusicBrainzTrackId}";
            if (OnlineCache.TryGetValue(key, out var entry)
                && DateTime.UtcNow - entry.CreatedUtc < TimeSpan.FromMinutes(30))
            {
                _logger.Info($"[MusicStrmExtract] [LocalProvider] 在线缓存命中: {md.Title}");
                return entry.Value;
            }

            _logger.Info($"[MusicStrmExtract] [LocalProvider] 在线解析开始(无缓存): {md.Title}");
            using var resolver = new OnlineResolver(
                string.IsNullOrWhiteSpace(config.MusicBrainzBaseUrl) ? null : config.MusicBrainzBaseUrl);
            var online = await resolver.ResolveAsync(md, ct).ConfigureAwait(false);
            _logger.Info($"[MusicStrmExtract] [LocalProvider] 在线解析完成: kind={online.Kind}");
            OnlineCache[key] = (DateTime.UtcNow, online);
            return online;
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

            var url = ReadStrmUrl(info.Path);
            if (string.IsNullOrEmpty(url))
            {
                return result;
            }

            var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
            var ffprobePath = FfprobeRunner.Locate(config.FfprobePath);
            if (string.IsNullOrEmpty(ffprobePath))
            {
                return result;
            }

            var runner = new FfprobeRunner(ffprobePath, config.ProbeTimeoutSeconds, config.ExtraHeaders);
            var probe = await ProbeCache.ProbeAsync(runner, info.Path, url, cancellationToken).ConfigureAwait(false);
            if (probe is null || !probe.HasTags)
            {
                _logger.Warn($"[MusicStrmExtract] [LocalProvider] 探测无标签: {info.Path}");
                return result;
            }

            var md = TagParser.Parse(probe.Tags);
            md.HasEmbeddedCover = probe.HasEmbeddedCover;
            if (md.IsEmpty)
            {
                return result;
            }

            // 在线补全合并进本地读取器流程(实测 Emby 4.9 刷新 Audio 条目不调用 IRemoteMetadataProvider 的
            // GetMetadata 阶段,在线抓取主要由专辑/艺术家级联;此处探测后直接补全,结果仍交由引擎持久化)
            if (config.EnableOnlineMetadata)
            {
                var online = await GetOnlineAsync(md, config, cancellationToken).ConfigureAwait(false);
                var (final, _, kind, note) = MergePolicy.Merge(md, online);
                if (online.Kind != OnlineMatchKind.None)
                {
                    _logger.Info($"[MusicStrmExtract] [LocalProvider] 在线补全: {md.Title} | kind={kind} | {note}");
                }
                else
                {
                    // 在线无结果:内嵌 MBID 大概率脏(已在解析中被 404/无结果验证),不写回,避免污染 ProviderIds
                    _logger.Info($"[MusicStrmExtract] [LocalProvider] 在线无结果: {md.Title} -> 仅保留内嵌文本字段,不写内嵌 MBID");
                    final.MusicBrainzTrackId = null;
                    final.MusicBrainzAlbumId = null;
                    final.MusicBrainzArtistId = null;
                    final.MusicBrainzAlbumArtistId = null;
                    final.MusicBrainzReleaseGroupId = null;
                }

                md = final;
            }

            // 专辑/艺人组织:按专辑名搜索 MB(用户约定:专辑实体名 = MB 官方专辑名)。
            // 搜索命中且可信 -> md.Album = MB 官方专辑名并写专辑级 MBID(供内置抓取器补详情/封面);
            // 未命中 -> 回退专辑文件夹名(与文件系统一致,避免悬浮专辑)。
            var (albumFolder, artistFolder) = GetFolderStructure(info.Path);
            if (config.EnableOnlineMetadata && !string.IsNullOrWhiteSpace(albumFolder))
            {
                var albumKey = $"{albumFolder}|{artistFolder}";
                var album = await GetAlbumSearchAsync(albumKey, albumFolder, artistFolder, config, cancellationToken).ConfigureAwait(false);
                if (album.Found && !string.IsNullOrWhiteSpace(album.Title))
                {
                    md.Album = album.Title;
                    md.MusicBrainzAlbumId = album.ReleaseMbid;
                    md.MusicBrainzReleaseGroupId = album.ReleaseGroupMbid;
                    if (!string.IsNullOrWhiteSpace(album.ArtistName))
                    {
                        // 统一艺人名:Artists/AlbumArtists 都用 MB artist-credit 名,
                        // 避免内嵌 "Jay Chou (周杰倫)" 等变体导致 MusicArtist 实体分裂
                        md.Artists.Clear();
                        md.Artists.Add(album.ArtistName);
                        md.AlbumArtists.Clear();
                        md.AlbumArtists.Add(album.ArtistName);
                    }

                    // 封面:不下载——专辑级 MBID(Album/ReleaseGroup)已写入,Mb 封面由 Emby 内置
                    // MusicBrainz 抓取器在实体刷新时从 Cover Art Archive 刮削挂载(用户约定)
                }
                else
                {
                    _logger.Warn($"[MusicStrmExtract] [LocalProvider] 专辑名搜索无命中: '{albumFolder}' -> 使用文件夹名作为专辑名");
                    md.Album = albumFolder;
                }
            }
            else if (!string.IsNullOrWhiteSpace(albumFolder))
            {
                md.Album = albumFolder;
            }

            if (md.IsEmpty)
            {
                return result;
            }

            // 引擎不采用 provider 返回的 Audio.Album(实测)且 AlbumId 只读;
            // 对真实条目直写最终(文件夹为准)的 Album/AlbumArtists,便于库扫描归组/后续清理实体
            if (config.EnableOnlineMetadata && !string.IsNullOrWhiteSpace(md.Album))
            {
                SyncAlbumField(info, md.Album, md.AlbumArtists);
            }

            var item = new Audio
            {
                Name = string.IsNullOrWhiteSpace(md.Title)
                    ? Path.GetFileNameWithoutExtension(info.Path)
                    : md.Title.Trim()
            };
            ApplyFields(item, md);

            result.Item = item;
            result.HasMetadata = true;
            _logger.Info($"[MusicStrmExtract] [LocalProvider] 探测完成: Id={item.Id} Name='{item.Name}' Album='{item.Album}'");
            return result;
        }

        private static void ApplyFields(Audio item, TrackMetadata md)
        {
            if (!string.IsNullOrWhiteSpace(md.Album))
            {
                item.Album = md.Album;
            }

            if (md.Artists.Count > 0)
            {
                item.Artists = md.Artists.ToArray();
            }

            if (md.AlbumArtists.Count > 0)
            {
                item.AlbumArtists = md.AlbumArtists.ToArray();
            }

            if (md.Genres.Count > 0)
            {
                item.Genres = md.Genres.ToArray();
            }

            if (md.Composers.Count > 0)
            {
                item.Composers = md.Composers.Select(c => new MediaBrowser.Model.Dto.LinkedItemInfo { Name = c }).ToArray();
            }

            if (md.Year is int year)
            {
                item.ProductionYear = year;
            }

            if (md.IndexNumber is int index)
            {
                item.IndexNumber = index;
            }

            if (md.ParentIndexNumber is int disc)
            {
                item.ParentIndexNumber = disc;
            }

            SetProviderId(item, "MusicBrainzAlbum", md.MusicBrainzAlbumId);
            SetProviderId(item, "MusicBrainzTrack", md.MusicBrainzTrackId);
            SetProviderId(item, "MusicBrainzArtist", md.MusicBrainzArtistId);
            SetProviderId(item, "MusicBrainzAlbumArtist", md.MusicBrainzAlbumArtistId);
            SetProviderId(item, "MusicBrainzReleaseGroup", md.MusicBrainzReleaseGroupId);
        }

        private static void SetProviderId(Audio item, string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            item.ProviderIds[key] = value.Trim();
        }

        private static string? ReadStrmUrl(string strmPath)
        {
            foreach (var line in File.ReadLines(strmPath))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0)
                {
                    return trimmed;
                }
            }

            return null;
        }
    }
}