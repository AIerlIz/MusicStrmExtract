using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        private const int MinimumRequestIntervalMs = 1100;

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

        public async Task<ParsedRelease> GetReleaseAsync(string releaseMbid, CancellationToken ct)
        {
            var url = $"{_baseUrl}/ws/2/release/{Uri.EscapeDataString(releaseMbid)}?inc=recordings+artist-credits+release-groups&fmt=json";
            return ReleaseTracklistParser.ParseRelease(
                await GetJsonRootAsync(url, ct).ConfigureAwait(false));
        }

        public async Task<IReadOnlyList<ScoredRelease>> SearchReleasesAsync(
            string album,
            string? artist,
            int limit,
            CancellationToken ct)
        {
            var sb = new System.Text.StringBuilder("release:");
            sb.Append('"').Append(album.Replace("\"", string.Empty)).Append('"');
            if (!string.IsNullOrWhiteSpace(artist))
            {
                sb.Append(" AND artist:\"").Append(artist.Trim().Replace("\"", string.Empty)).Append('"');
            }
            var query = Uri.EscapeDataString(sb.ToString());
            var url = $"{_baseUrl}/ws/2/release?query={query}&fmt=json&limit={limit}";
            return ReleaseJsonReader.ParseSearchReleases(
                await GetJsonRootAsync(url, ct).ConfigureAwait(false));
        }

        /// <summary>按 release-group MBID 获取该组全部 release(最多 50 条),用于多版本加权选版。
        /// inc=releases+media 使每个 release 附带 media(格式/轨数),供评分使用。</summary>
        public async Task<IReadOnlyList<ReleaseSummary>> GetReleaseGroupReleasesAsync(
            string rgMbid,
            CancellationToken ct)
        {
            var url = $"{_baseUrl}/ws/2/release-group/{Uri.EscapeDataString(rgMbid)}?inc=releases+media&fmt=json";
            return ReleaseJsonReader.ParseReleaseGroup(
                await GetJsonRootAsync(url, ct).ConfigureAwait(false));
        }

        private async Task<JsonElement> GetJsonRootAsync(string url, CancellationToken ct)
        {
            if (_responseCache.TryGetValue(url, out var cached))
            {
                using var cachedDoc = JsonDocument.Parse(cached);
                return cachedDoc.RootElement.Clone();
            }

            // 锁要覆盖整个请求而不是只覆盖排队:否则两个请求虽然间隔 1.1 秒启动,
            // 前一个仍可能未结束就并发打向 MusicBrainz,触发 503/429。
            await Gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var interval = TimeSpan.FromMilliseconds(MinimumRequestIntervalMs);
                var elapsed = DateTime.UtcNow - _lastRequestUtc;
                if (elapsed < interval)
                {
                    await Task.Delay(interval - elapsed, ct).ConfigureAwait(false);
                }

                _lastRequestUtc = DateTime.UtcNow;
                using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(
                        $"MusicBrainz HTTP {(int)response.StatusCode}: {Truncate(body, 200)}",
                        null,
                        response.StatusCode);
                }

                using var doc = JsonDocument.Parse(body);
                _responseCache.TryAdd(url, body);
                return doc.RootElement.Clone();
            }
            finally
            {
                Gate.Release();
            }
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max) return value ?? string.Empty;
            return value.Substring(0, max) + "...";
        }

        public void Dispose() { _http.Dispose(); }
    }
}
