using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Logging;

using MusicStrmExtract.Metadata;
using MusicStrmExtract.Probing;

namespace MusicStrmExtract.Providers
{
    /// <summary>
    /// Audio 元数据自定义 Provider(实验 v2):
    /// Emby 在扫描/刷新 Audio 条目时调用;对 .strm 条目探测目标(HTTP 直链)内嵌标签并填充条目字段,
    /// 返回 MetadataEdit 交由 Emby 刷新引擎统一持久化(不自行 UpdateToRepository,避免与引擎冲突)。
    /// </summary>
    public sealed class MusicStrmAudioProvider : ICustomMetadataProvider<Audio>
    {
        private readonly ILogManager _logManager;
        private readonly ILogger _logger;

        public MusicStrmAudioProvider(ILogManager logManager)
        {
            _logManager = logManager;
            _logger = logManager.GetLogger("MusicStrmExtract");
        }

        public string Name => "Music Strm Extract";

        public async Task<ItemUpdateType> FetchAsync(
            MetadataResult<Audio> result,
            MetadataRefreshOptions options,
            LibraryOptions libraryOptions,
            CancellationToken cancellationToken)
        {
            var item = result?.BaseItem as Audio;
            if (item is null || string.IsNullOrEmpty(item.Path)
                || !item.Path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase))
            {
                return ItemUpdateType.None;
            }

            _logger.Info($"[MusicStrmExtract] [Provider] 进入 Fetch: Id={item.Id} Name='{item.Name}' 已带元数据={( !string.IsNullOrWhiteSpace(item.Album) || item.AlbumArtists.Length > 0)}");

            // 已有完整元数据(专辑/艺术家)且并非强制刷新时跳过,避免每次刷新都远程探测
            var force = options?.MetadataRefreshMode == MetadataRefreshMode.FullRefresh
                        || options?.MetadataRefreshMode == MetadataRefreshMode.ValidationOnly;
            if (!force && (!string.IsNullOrWhiteSpace(item.Album) || item.AlbumArtists.Length > 0))
            {
                return ItemUpdateType.None;
            }

            try
            {
                var url = ReadStrmUrl(item.Path);
                if (string.IsNullOrEmpty(url))
                {
                    return ItemUpdateType.None;
                }

                var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
                var ffprobePath = FfprobeRunner.Locate(config.FfprobePath);
                if (string.IsNullOrEmpty(ffprobePath))
                {
                    return ItemUpdateType.None;
                }

                var runner = new FfprobeRunner(ffprobePath, config.ProbeTimeoutSeconds, config.ExtraHeaders);
                var probe = await runner.ProbeAsync(url, cancellationToken).ConfigureAwait(false);
                if (probe is null || !probe.HasTags)
                {
                    _logger.Warn($"[MusicStrmExtract] [Provider] 探测无标签: {item.Path}");
                    return ItemUpdateType.None;
                }

                var md = TagParser.Parse(probe.Tags);
                md.HasEmbeddedCover = probe.HasEmbeddedCover;
                if (md.IsEmpty)
                {
                    return ItemUpdateType.None;
                }

                ApplyFields(item, md);
                _logger.Info($"[MusicStrmExtract] [Provider] 条目探测完成: Id={item.Id} Name='{item.Name}' Album='{item.Album}' Artists=[{string.Join(",", item.Artists)}] ProviderIds=[{string.Join(",", item.ProviderIds.Select(kv => kv.Key + "=" + kv.Value))}]");
                return ItemUpdateType.MetadataEdit;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error($"[MusicStrmExtract] [Provider] 处理失败: {item.Path} -> {ex.Message}");
                return ItemUpdateType.None;
            }
        }

        private static void ApplyFields(Audio item, TrackMetadata md)
        {
            if (!string.IsNullOrWhiteSpace(md.Title))
            {
                item.Name = md.Title.Trim();
            }

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
                item.Composers = md.Composers.Select(c => new LinkedItemInfo { Name = c }).ToArray();
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
