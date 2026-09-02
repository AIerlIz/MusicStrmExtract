using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;

namespace MusicStrmExtract.Processing
{
    /// <summary>
    /// 把同专辑 .strm 音轨已写回的元数据(经库扫描生成的 MusicAlbum 实体)补全到专辑条目:
    /// MusicBrainzAlbum / MusicBrainzReleaseGroup ProviderId、缺失的年份;补到 MBID 时刷新该专辑
    /// 以触发 Emby 的 MusicBrainz 刮削器拉取完整专辑详情。幂等:已有值跳过。
    /// </summary>
    public sealed class AlbumUpdater
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;

        public AlbumUpdater(ILibraryManager libraryManager, ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _logger = logManager.GetLogger("MusicStrmExtract");
        }

        public async Task<int> RunAsync(CancellationToken cancellationToken)
        {
            // 1) strm 支撑且已具备专辑归属的 Audio(仅音乐库)
            var musicLocations = _libraryManager.GetVirtualFolders()
                .Where(v => string.Equals(v.CollectionType, "music", StringComparison.OrdinalIgnoreCase))
                .SelectMany(v => v.Locations ?? Array.Empty<string>())
                .ToList();
            if (musicLocations.Count == 0)
            {
                return 0;
            }

            var strmAudios = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    Recursive = true,
                    IncludeItemTypes = new[] { "Audio" }
                })
                .OfType<Audio>()
                .Where(a => !string.IsNullOrEmpty(a.Path)
                            && a.Path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase)
                            && musicLocations.Any(root => a.Path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                            && !string.IsNullOrWhiteSpace(a.Album))
                .ToList();

            // 2) 按专辑名聚合组内 MBID/年份(取组内非空首值)
            var groupMbAlbum = new Dictionary<string, string>(StringComparer.Ordinal);
            var groupMbReleaseGroup = new Dictionary<string, string>(StringComparer.Ordinal);
            var groupYear = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var audio in strmAudios)
            {
                var albumName = audio.Album.Trim();
                if (string.IsNullOrEmpty(albumName))
                {
                    continue;
                }

                if (!groupMbAlbum.ContainsKey(albumName))
                {
                    groupMbAlbum[albumName] = GetProvider(audio, "MusicBrainzAlbum");
                }
                else if (string.IsNullOrEmpty(groupMbAlbum[albumName]))
                {
                    groupMbAlbum[albumName] = GetProvider(audio, "MusicBrainzAlbum");
                }

                if (!groupMbReleaseGroup.ContainsKey(albumName))
                {
                    groupMbReleaseGroup[albumName] = GetProvider(audio, "MusicBrainzReleaseGroup");
                }
                else if (string.IsNullOrEmpty(groupMbReleaseGroup[albumName]))
                {
                    groupMbReleaseGroup[albumName] = GetProvider(audio, "MusicBrainzReleaseGroup");
                }

                if (!groupYear.ContainsKey(albumName) && audio.ProductionYear is int year)
                {
                    groupYear[albumName] = year;
                }
            }

            // 3) 拉取库中 MusicAlbum 实体并补写
            var albums = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    Recursive = true,
                    IncludeItemTypes = new[] { "MusicAlbum" }
                })
                .OfType<MusicAlbum>()
                .ToList();

            var updated = 0;
            foreach (var albumName in groupMbAlbum.Keys.Union(groupYear.Keys))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var album = albums.FirstOrDefault(a =>
                    string.Equals(a.Name, albumName, StringComparison.Ordinal));
                if (album is null)
                {
                    // 名称不完全一致时退而求其次:忽略(虚拟专辑项一般与 Audio.Album 同源)
                    continue;
                }

                var changed = false;
                var wroteAlbumMbid = false;

                if (groupMbAlbum.TryGetValue(albumName, out var mbAlbum)
                    && !string.IsNullOrWhiteSpace(mbAlbum))
                {
                    var alreadySet = album.ProviderIds.TryGetValue("MusicBrainzAlbum", out var existing)
                                     && string.Equals(existing, mbAlbum, StringComparison.Ordinal);
                    if (!alreadySet)
                    {
                        album.ProviderIds["MusicBrainzAlbum"] = mbAlbum;
                        changed = true;
                        wroteAlbumMbid = true;
                    }
                }

                if (groupMbReleaseGroup.TryGetValue(albumName, out var mbRg)
                    && !string.IsNullOrWhiteSpace(mbRg)
                    && (!album.ProviderIds.TryGetValue("MusicBrainzReleaseGroup", out var existingRg)
                        || !string.Equals(existingRg, mbRg, StringComparison.Ordinal)))
                {
                    album.ProviderIds["MusicBrainzReleaseGroup"] = mbRg;
                    changed = true;
                }

                if (groupYear.TryGetValue(albumName, out var year) && album.ProductionYear is null)
                {
                    album.ProductionYear = year;
                    changed = true;
                }

                if (!changed)
                {
                    continue;
                }

                try
                {
                    album.UpdateToRepository(ItemUpdateType.MetadataEdit);
                    updated++;
                    _logger.Info($"[MusicStrmExtract] 已补写专辑条目: Id={album.Id} Name='{album.Name}' Year={album.ProductionYear} ProviderIds=[{string.Join(", ", album.ProviderIds.Select(kv => kv.Key + "=" + kv.Value))}]");
                }
                catch (Exception ex)
                {
                    _logger.Error($"[MusicStrmExtract] 补写专辑失败: {album.Name} -> {ex.Message}");
                    continue;
                }

                // 4) 补到 MusicBrainzAlbum ID 时刷新该专辑,让 Emby MusicBrainz 抓取器取回完整专辑详情
                if (wroteAlbumMbid)
                {
                    try
                    {
                        await album.RefreshMetadata(cancellationToken).ConfigureAwait(false);
                        _logger.Info($"[MusicStrmExtract] 已刷新专辑(触发 MusicBrainz 抓取): {album.Name}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn($"[MusicStrmExtract] 刷新专辑失败(网络/抓取器异常,不影响已写字段): {album.Name} -> {ex.Message}");
                    }
                }
            }

            return updated;
        }

        private static string? GetProvider(Audio audio, string key)
        {
            return audio.ProviderIds.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : null;
        }
    }
}
