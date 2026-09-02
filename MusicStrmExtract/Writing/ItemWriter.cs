using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Logging;

using MusicStrmExtract.Metadata;

namespace MusicStrmExtract.Writing
{
    /// <summary>
    /// 把合并后的元数据写回 Emby 的 Audio 条目(名称/专辑/艺术家/年份/曲号/流派/作曲/ProviderIds),
    /// 并下载在线封面到 strm 所在专辑目录(cover.jpg)。
    /// </summary>
    public sealed class ItemWriter : IDisposable
    {
        private const string CoverFileName = "cover.jpg";

        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;
        private readonly HttpClient _http;

        public ItemWriter(ILibraryManager libraryManager, ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _logger = logManager.GetLogger("MusicStrmExtract");
            _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
            _http.Timeout = TimeSpan.FromSeconds(30);
        }

        /// <summary>把 final 元数据应用到 item;返回是否有字段级变化。</summary>
        public async Task<bool> ApplyToAudioAsync(
            Audio item,
            TrackMetadata final,
            string strmPath,
            string? coverUrl,
            bool writeCover,
            CancellationToken cancellationToken)
        {
            var changed = false;

            if (!string.IsNullOrWhiteSpace(final.Title)
                && !string.Equals(item.Name, final.Title.Trim(), StringComparison.Ordinal))
            {
                item.Name = final.Title.Trim();
                changed = true;
            }

            if (final.Year is int year && item.ProductionYear != year)
            {
                item.ProductionYear = year;
                changed = true;
            }

            if (final.IndexNumber is int track && item.IndexNumber != track)
            {
                item.IndexNumber = track;
                changed = true;
            }

            if (final.ParentIndexNumber is int disc && item.ParentIndexNumber != disc)
            {
                item.ParentIndexNumber = disc;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(final.Album)
                && !string.Equals(item.Album, final.Album, StringComparison.Ordinal))
            {
                item.Album = final.Album;
                changed = true;
            }

            if (final.Artists.Count > 0 && !SameSet(item.Artists, final.Artists))
            {
                item.Artists = final.Artists.ToArray();
                changed = true;
            }

            if (final.AlbumArtists.Count > 0 && !SameSet(item.AlbumArtists, final.AlbumArtists))
            {
                item.AlbumArtists = final.AlbumArtists.ToArray();
                changed = true;
            }

            if (final.Genres.Count > 0 && !SameSet(item.Genres, final.Genres))
            {
                item.Genres = final.Genres.ToArray();
                changed = true;
            }

            if (final.Composers.Count > 0
                && !SameSet(item.Composers.Select(c => c.Name), final.Composers))
            {
                item.Composers = final.Composers.Select(c => new LinkedItemInfo { Name = c }).ToArray();
                changed = true;
            }

            changed |= SetProviderId(item, "MusicBrainzAlbum", final.MusicBrainzAlbumId);
            changed |= SetProviderId(item, "MusicBrainzTrack", final.MusicBrainzTrackId);
            changed |= SetProviderId(item, "MusicBrainzArtist", final.MusicBrainzArtistId);
            changed |= SetProviderId(item, "MusicBrainzAlbumArtist", final.MusicBrainzAlbumArtistId);
            changed |= SetProviderId(item, "MusicBrainzReleaseGroup", final.MusicBrainzReleaseGroupId);

            if (changed)
            {
                try
                {
                    // 同步持久化字段到库(不触发元数据刷新;专辑/艺术家组织由库扫描完成)
                    item.UpdateToRepository(ItemUpdateType.MetadataEdit);
                    _logger.Info($"[MusicStrmExtract] 已写回条目: Id={item.Id} Name='{item.Name}' Album='{item.Album}' Artists=[{string.Join(", ", item.Artists)}] AlbumArtists=[{string.Join(", ", item.AlbumArtists)}] Year={item.ProductionYear} 曲号={item.IndexNumber} ProviderIds=[{string.Join(", ", item.ProviderIds.Select(kv => kv.Key + "=" + kv.Value))}]");
                }
                catch (Exception ex)
                {
                    _logger.Error($"[MusicStrmExtract] UpdateToRepository 失败,尝试 LibraryManager.UpdateItems: Id={item.Id} {ex.Message}");
                    try
                    {
                        _libraryManager.UpdateItems(
                            new List<BaseItem> { item },
                            item.GetParent(),
                            ItemUpdateType.MetadataEdit,
                            null,
                            CancellationToken.None);
                        _logger.Info($"[MusicStrmExtract] LibraryManager.UpdateItems 写回成功: Id={item.Id}");
                    }
                    catch (Exception ex2)
                    {
                        _logger.Error($"[MusicStrmExtract] 写回彻底失败: Id={item.Id} {ex2.Message}");
                        return false;
                    }
                }
            }

            if (writeCover && !string.IsNullOrWhiteSpace(coverUrl))
            {
                await SaveCoverAsync(strmPath, coverUrl, cancellationToken).ConfigureAwait(false);
            }

            return changed;
        }

        private static bool SetProviderId(Audio item, string key, string? value)
        {
            value = value?.Trim();
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            if (item.ProviderIds.TryGetValue(key, out var existing) && string.Equals(existing, value, StringComparison.Ordinal))
            {
                return false;
            }

            item.ProviderIds[key] = value;
            return true;
        }

        private static bool SameSet(IEnumerable<string> current, IEnumerable<string> wanted)
        {
            var a = current.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
            var b = wanted.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
            return a.Count == b.Count && a.SequenceEqual(b, StringComparer.Ordinal);
        }

        private async Task SaveCoverAsync(string strmPath, string coverUrl, CancellationToken cancellationToken)
        {
            try
            {
                var dir = Path.GetDirectoryName(strmPath);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    return;
                }

                var dest = Path.Combine(dir, CoverFileName);
                if (File.Exists(dest))
                {
                    // 已有本地封面则保留,避免覆盖用户已有图片
                    _logger.Info($"[MusicStrmExtract] 封面已存在,跳过下载: {dest}");
                    return;
                }

                var bytes = await _http.GetByteArrayAsync(coverUrl, cancellationToken).ConfigureAwait(false);
                if (bytes.Length < 1024)
                {
                    _logger.Warn($"[MusicStrmExtract] 封面数据过小({bytes.Length} B),忽略: {coverUrl}");
                    return;
                }

                await File.WriteAllBytesAsync(dest, bytes, cancellationToken).ConfigureAwait(false);
                _logger.Info($"[MusicStrmExtract] 封面已写入: {dest} ({bytes.Length} B) <- {coverUrl}");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                _logger.Error($"[MusicStrmExtract] 封面下载失败: {coverUrl} -> {ex.Message}");
            }
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }
}
