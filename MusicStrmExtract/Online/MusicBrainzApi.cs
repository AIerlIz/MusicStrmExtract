using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MusicStrmExtract.Online
{
    /// <summary>
    /// MusicBrainz Web Service 客户端。遵守 1 req/s 限速与规范 User-Agent。
    /// 端点默认官方 https://musicbrainz.org;可传入镜像(如 https://musicbrainz.emby.tv)。
    /// 按 URL 缓存响应正文(会话级),避免脏标签导致同一 MBID 重复请求。
    /// </summary>
    public sealed class MusicBrainzApi : IDisposable
    {
        private const string DefaultBaseUrl = "https://musicbrainz.org";
        private const string UserAgent = "MusicStrmExtract/1.0.0.0 (Emby plugin; contact: local)";

        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        private static DateTime _lastRequestUtc = DateTime.MinValue;

        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly ConcurrentDictionary<string, string> _responseCache = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        public MusicBrainzApi(string? baseUrl = null, int timeoutSeconds = 25)
        {
            _baseUrl = (baseUrl ?? DefaultBaseUrl).TrimEnd('/');
            _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
            _http.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        }

        /// <summary>GET recording by MBID, inc=releases+artist-credits。失败抛异常。</summary>
        public async Task<JsonElement> GetRecordingAsync(string recordingMbid, CancellationToken ct)
        {
            var url = $"{_baseUrl}/ws/2/recording/{Uri.EscapeDataString(recordingMbid)}?inc=releases+artist-credits&fmt=json";
            return await GetJsonRootAsync(url, ct).ConfigureAwait(false);
        }

        /// <summary>按标题搜索录音, limit 条。艺术家一致性由调用方在候选过滤层做宽松匹配。</summary>
        public async Task<JsonElement> SearchRecordingsAsync(string title, int limit, CancellationToken ct)
        {
            var sb = new System.Text.StringBuilder("recording:");
            sb.Append('"').Append(title.Replace("\"", string.Empty)).Append('"');

            var query = Uri.EscapeDataString(sb.ToString());
            var url = $"{_baseUrl}/ws/2/recording?query={query}&fmt=json&limit={limit}";
            return await GetJsonRootAsync(url, ct).ConfigureAwait(false);
        }

        /// <summary>GET release by MBID, inc=recordings+artist-credits+release-groups(取轨道映射用)。失败抛异常。</summary>
        public async Task<JsonElement> GetReleaseAsync(string releaseMbid, CancellationToken ct)
        {
            var url = $"{_baseUrl}/ws/2/release/{Uri.EscapeDataString(releaseMbid)}?inc=recordings+artist-credits+release-groups&fmt=json";
            return await GetJsonRootAsync(url, ct).ConfigureAwait(false);
        }

        /// <summary>按专辑名 + 艺人名搜索 release, limit 条(供"艺人 + 专辑文件夹名锁定 release"使用)。
        /// 本地目录名原样透传,不做字形转换。</summary>
        public async Task<JsonElement> SearchReleasesAsync(string album, string? artist, int limit, CancellationToken ct)
        {
            var sb = new System.Text.StringBuilder("release:");
            sb.Append('"').Append(album.Replace("\"", string.Empty)).Append('"');
            if (!string.IsNullOrWhiteSpace(artist))
            {
                sb.Append(" AND artist:\"").Append(artist.Trim().Replace("\"", string.Empty)).Append('"');
            }

            var query = Uri.EscapeDataString(sb.ToString());
            var url = $"{_baseUrl}/ws/2/release?query={query}&fmt=json&limit={limit}";
            return await GetJsonRootAsync(url, ct).ConfigureAwait(false);
        }

        private async Task<JsonElement> GetJsonRootAsync(string url, CancellationToken ct)
        {
            if (_responseCache.TryGetValue(url, out var cached))
            {
                using var cachedDoc = JsonDocument.Parse(cached);
                return cachedDoc.RootElement.Clone();
            }

            await ThrottleAsync(ct).ConfigureAwait(false);
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // 带上 StatusCode:调用方需区分"该 MBID/查询无结果(404)"与"网络/服务不可达"
                throw new HttpRequestException(
                    $"MusicBrainz HTTP {(int)response.StatusCode}: {Truncate(body, 200)}",
                    null,
                    response.StatusCode);
            }

            _responseCache.TryAdd(url, body);

            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.Clone();
        }

        private static async Task ThrottleAsync(CancellationToken ct)
        {
            await Gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var elapsed = DateTime.UtcNow - _lastRequestUtc;
                if (elapsed < TimeSpan.FromMilliseconds(1100))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(1100) - elapsed, ct).ConfigureAwait(false);
                }

                _lastRequestUtc = DateTime.UtcNow;
            }
            finally
            {
                Gate.Release();
            }
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max)
            {
                return value ?? string.Empty;
            }

            return value.Substring(0, max) + "...";
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }
}
