using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Providers;

using MusicStrmExtract.Online;

namespace MusicStrmExtract.Providers
{
    /// <summary>
    /// 封面下载 Provider:只服务 .strm 条目。普通音频自带标签,交给 Emby 其它 Provider;
    /// .strm 元数据由 MusicStrmLocalProvider 在本地路径补全,本类不返回 MetadataResult。
    /// 封面经 GetImages 交给引擎下载挂图(不手写 cover.jpg)。
    /// </summary>
    public sealed class MusicStrmRemoteProvider : IRemoteMetadataProvider<Audio, SongInfo>, IDisposable
    {
        private HttpClient? _http;
        private readonly IMusicStrmConfigurationSource _configurationSource;

        public MusicStrmRemoteProvider(ILogManager logManager)
            : this(MusicStrmConfigurationSource.Default)
        {
        }

        internal MusicStrmRemoteProvider(IMusicStrmConfigurationSource configurationSource)
        {
            _configurationSource = configurationSource;
            _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(PluginConstants.UserAgent);
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

        public Task<MetadataResult<Audio>> GetMetadata(SongInfo info, CancellationToken cancellationToken)
        {
            // 普通音频自带标签;.strm 元数据由本地 Provider 负责,这里不参与在线元数据合并。
            return Task.FromResult(new MetadataResult<Audio>());
        }

        public Task<IEnumerable<RemoteImageInfo>> GetImages(SongInfo info, CancellationToken cancellationToken)
        {
            // 常规路径:条目刷新后已带 MusicBrainzAlbum ProviderId,直接指向 Cover Art Archive。
            // 无 Album ID 时无封面可给(不做同步在线解析,避免阻塞引擎图片流程)。
            var images = new List<RemoteImageInfo>();
            if (info is null || !StrmFileParser.IsStrmPath(info.Path))
            {
                return Task.FromResult((IEnumerable<RemoteImageInfo>)Array.Empty<RemoteImageInfo>());
            }

            var albumId = info.GetProviderId(PluginConstants.MusicBrainzAlbum);
            if (!string.IsNullOrWhiteSpace(albumId))
            {
                var config = _configurationSource.Current;
                var coverArt = new CoverArtClient(config.CoverArtBaseUrl);
                images.Add(new RemoteImageInfo
                {
                    ProviderName = Name,
                    Url = coverArt.BuildFrontImageUrl(albumId),
                    Type = ImageType.Primary
                });
            }

            return Task.FromResult((IEnumerable<RemoteImageInfo>)images);
        }
    }
}
