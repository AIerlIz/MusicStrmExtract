using System;
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
    /// </summary>
    public sealed class MusicBrainzApi : IDisposable
    {
        private const string DefaultBaseUrl = "https://musicbrainz.org";
        private const string UserAgent = "MusicStrmExtract/1.0.0.0 (Emby plugin; contact: local)";

        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        private static DateTime _lastRequestUtc = DateTime.MinValue;

        private readonly HttpClient _http;
        private readonly string _baseUrl;

        public MusicBrainzApi(string? baseUrl = null, int timeoutSeconds = 25)
        {
            _baseUrl = (baseUrl ?? DefaultBaseUrl).TrimEnd('/');
            _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
            _http.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        }

        /// <summary>GET recording by MBID, inc=releases+artist-credits+release-groups。失败抛异常。</summary>
        public async Task<JsonElement> GetRecordingAsync(string recordingMbid, CancellationToken ct)
        {
            var url = $"{_baseUrl}/ws/2/recording/{Uri.EscapeDataString(recordingMbid)}?inc=releases+artist-credits&fmt=json";
            return await GetJsonRootAsync(url, ct).ConfigureAwait(false);
        }

        /// <summary>按标题+艺术家(可选专辑)搜索录音, limit 条。</summary>
        public async Task<JsonElement> SearchRecordingsAsync(string title, string? artist, string? album, int limit, CancellationToken ct)
        {
            var sb = new System.Text.StringBuilder("recording:");
            sb.Append('"').Append(title.Replace("\"", string.Empty)).Append('"');
            if (!string.IsNullOrWhiteSpace(artist))
            {
                sb.Append(" AND artist:").Append('"').Append(artist.Replace("\"", string.Empty)).Append('"');
            }

            if (!string.IsNullOrWhiteSpace(album))
            {
                sb.Append(" AND release:").Append('"').Append(album.Replace("\"", string.Empty)).Append('"');
            }

            var query = Uri.EscapeDataString(sb.ToString());
            var url = $"{_baseUrl}/ws/2/recording?query={query}&fmt=json&limit={limit}";
            return await GetJsonRootAsync(url, ct).ConfigureAwait(false);
        }

        private async Task<JsonElement> GetJsonRootAsync(string url, CancellationToken ct)
        {
            await ThrottleAsync(ct).ConfigureAwait(false);
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"MusicBrainz HTTP {(int)response.StatusCode}: {Truncate(body, 200)}");
            }

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
