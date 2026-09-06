using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MusicStrmExtract.Online
{
    public sealed class MusicBrainzApi : IMusicBrainzApi
    {
        private const string DefaultBaseUrl = "https://musicbrainz.org";

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
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(PluginConstants.UserAgent);
        }

        public async Task<JsonElement> GetReleaseAsync(string releaseMbid, CancellationToken ct)
        {
            var url = $"{_baseUrl}/ws/2/release/{Uri.EscapeDataString(releaseMbid)}?inc=recordings+artist-credits+release-groups&fmt=json";
            return await GetJsonRootAsync(url, ct).ConfigureAwait(false);
        }

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

        /// <summary>按 release-group MBID 获取该组全部 release(最多 50 条),用于多版本加权选版。
        /// inc=releases+media 使每个 release 附带 media(格式/轨数),供评分使用。</summary>
        public async Task<JsonElement> GetReleaseGroupReleasesAsync(string rgMbid, CancellationToken ct)
        {
            var url = $"{_baseUrl}/ws/2/release-group/{Uri.EscapeDataString(rgMbid)}?inc=releases+media&fmt=json";
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
                    await Task.Delay(TimeSpan.FromMilliseconds(1100) - elapsed, ct).ConfigureAwait(false);
                _lastRequestUtc = DateTime.UtcNow;
            }
            finally { Gate.Release(); }
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max) return value ?? string.Empty;
            return value.Substring(0, max) + "...";
        }

        public void Dispose() { _http.Dispose(); }
    }
}
