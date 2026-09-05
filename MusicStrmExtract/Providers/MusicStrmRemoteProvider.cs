using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Providers;

using MusicStrmExtract.Metadata;
using MusicStrmExtract.Online;

namespace MusicStrmExtract.Providers
{
    /// <summary>
    /// 标准在线元数据下载器(IRemoteMetadataProvider):Emby 刷新引擎在本地元数据之后调用,
    /// 基于条目当前信息(lookupInfo)做 MusicBrainz 在线补全并返回 MetadataResult,
    /// 由引擎统一合并持久化与专辑/艺术家组织(不再自行写库)。
    /// 封面经 GetImages 交给引擎下载挂图(不手写 cover.jpg)。
    /// </summary>
    public sealed class MusicStrmRemoteProvider : IRemoteMetadataProvider<Audio, SongInfo>, IDisposable
    {
        private readonly ILogger _logger;
        private HttpClient? _http;

        public MusicStrmRemoteProvider(ILogManager logManager)
        {
            _logger = logManager.GetLogger("MusicStrmExtract");
            _logger.Info("[MusicStrmExtract] [RemoteProvider] 构造函数被调用(已注册)");
            _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("MusicStrmExtract/1.0.0.0 (Emby plugin; contact: local)");
        }

        public void Dispose()
        {
            _http?.Dispose();
            _http = null;
        }

        public string Name => "Music Strm Extract";

        /// <summary>搜索资源(本插件在线源固定,无需搜索候选;空实现以满足 IRemoteSearchProvider)。</summary>
        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SongInfo searchInfo, CancellationToken cancellationToken)
        {
            return Task.FromResult((IEnumerable<RemoteSearchResult>)Array.Empty<RemoteSearchResult>());
        }

        /// <summary>下载引擎请求的图片字节(封面 RemoteImageInfo 的 Url 由此加载)。</summary>
        public async Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            if (_http is null)
            {
                return new HttpResponseInfo { StatusCode = System.Net.HttpStatusCode.ServiceUnavailable };
            }

            using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            return new HttpResponseInfo
            {
                StatusCode = response.StatusCode,
                ContentType = response.Content.Headers.ContentType?.ToString(),
                Content = new MemoryStream(bytes),
                ContentLength = bytes.Length
            };
        }

        public async Task<MetadataResult<Audio>> GetMetadata(SongInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Audio>();

            if (info is null)
            {
                return result;
            }

            // strm 条目的元数据只由目录定位 Provider 负责:库里可能残留旧/脏 MBID,
            // 在线 Provider 若基于它们精确取回会覆盖本地正确结果;封面仍由 GetImages 提供。
            if (info.Path?.EndsWith(".strm", StringComparison.OrdinalIgnoreCase) == true)
            {
                return result;
            }

            var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();

            var embedded = FromLookupInfo(info);
            if (string.IsNullOrWhiteSpace(embedded.Title))
            {
                return result;
            }

            using var resolver = new OnlineResolver(
                string.IsNullOrWhiteSpace(config.MusicBrainzBaseUrl) ? null : config.MusicBrainzBaseUrl);
            var online = await resolver.ResolveAsync(embedded, cancellationToken).ConfigureAwait(false);
            var (final, _, kind, note) = MergePolicy.Merge(embedded, online);

            _logger.Info($"[MusicStrmExtract] [RemoteProvider] {info.Name} | kind={kind} | source={online.Source} | 标题='{final.Title}' | 专辑='{final.Album}' | {note}");

            if (online.Kind == OnlineMatchKind.None || final.IsEmpty)
            {
                // 在线无命中:引擎将保留现有/本地结果
                return result;
            }

            var item = new Audio
            {
                Name = final.Title?.Trim(),
                Album = final.Album,
                ProductionYear = final.Year,
                IndexNumber = final.IndexNumber,
                ParentIndexNumber = final.ParentIndexNumber
            };
            foreach (var artist in final.Artists)
            {
                item.Artists = item.Artists.Append(artist).ToArray();
            }

            foreach (var albumArtist in final.AlbumArtists)
            {
                item.AlbumArtists = item.AlbumArtists.Append(albumArtist).ToArray();
            }

            item.Genres = final.Genres.ToArray();
            item.Composers = final.Composers.Select(c => new LinkedItemInfo { Name = c }).ToArray();

            SetProviderId(item, "MusicBrainzAlbum", final.MusicBrainzAlbumId);
            SetProviderId(item, "MusicBrainzTrack", final.MusicBrainzTrackId);
            SetProviderId(item, "MusicBrainzArtist", final.MusicBrainzArtistId);
            SetProviderId(item, "MusicBrainzAlbumArtist", final.MusicBrainzAlbumArtistId);
            SetProviderId(item, "MusicBrainzReleaseGroup", final.MusicBrainzReleaseGroupId);

            result.Item = item;
            result.HasMetadata = true;
            return result;
        }

        public Task<IEnumerable<RemoteImageInfo>> GetImages(SongInfo info, CancellationToken cancellationToken)
        {
            // 常规路径:条目刷新后已带 MusicBrainzAlbum ProviderId,直接指向 Cover Art Archive。
            // 无 Album ID 时无封面可给(不做同步在线解析,避免阻塞引擎图片流程)。
            var images = new List<RemoteImageInfo>();
            var albumId = info.GetProviderId("MusicBrainzAlbum");
            if (!string.IsNullOrWhiteSpace(albumId))
            {
                images.Add(new RemoteImageInfo
                {
                    ProviderName = Name,
                    Url = $"https://coverartarchive.org/release/{albumId}/front-500",
                    Type = ImageType.Primary
                });
            }

            return Task.FromResult((IEnumerable<RemoteImageInfo>)images);
        }

        private static TrackMetadata FromLookupInfo(SongInfo info)
        {
            var md = new TrackMetadata
            {
                Title = info.Name,
                Album = info.Album,
                MusicBrainzTrackId = info.GetProviderId("MusicBrainzTrack"),
                MusicBrainzAlbumId = info.GetProviderId("MusicBrainzAlbum"),
                MusicBrainzArtistId = info.GetProviderId("MusicBrainzArtist"),
                MusicBrainzAlbumArtistId = info.GetProviderId("MusicBrainzAlbumArtist"),
                MusicBrainzReleaseGroupId = info.GetProviderId("MusicBrainzReleaseGroup")
            };
            if (info.Artists != null)
            {
                md.Artists.AddRange(info.Artists);
            }

            if (info.AlbumArtists != null)
            {
                md.AlbumArtists.AddRange(info.AlbumArtists);
            }

            return md;
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
