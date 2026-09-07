using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MusicStrmExtract.Online
{
    public sealed class MusicBrainzApi : IMusicBrainzApi
    {
        private const string DefaultBaseUrl = "https://musicbrainz.org";

        private readonly string _baseUrl;
        private readonly IHttpTransport _transport;
        private readonly IRequestGate _gate;
        private readonly ConcurrentDictionary<string, string> _responseCache = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        public MusicBrainzApi(string? baseUrl = null, int timeoutSeconds = 25)
            : this(baseUrl, CreateDefaultTransport(timeoutSeconds), StaticMusicBrainzRateGate.Instance)
        {
        }

        internal MusicBrainzApi(string? baseUrl, IHttpTransport transport, IRequestGate gate)
        {
            _baseUrl = (baseUrl ?? DefaultBaseUrl).TrimEnd('/');
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
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

            // 门在"间隔等待 + 完整 HTTP 请求"期间保持占用:否则两个请求虽然间隔 1.1 秒启动,
            // 前一个仍可能未结束就并发打向 MusicBrainz,触发 503/429。
            using var _ = await _gate.AcquireAsync(ct).ConfigureAwait(false);
            var response = await _transport.GetAsync(url, ct).ConfigureAwait(false);
            if (response.StatusCode < 200 || response.StatusCode >= 300)
            {
                throw new HttpRequestException(
                    $"MusicBrainz HTTP {response.StatusCode}: {Truncate(response.Body, 200)}",
                    null,
                    (System.Net.HttpStatusCode)response.StatusCode);
            }

            using var doc = JsonDocument.Parse(response.Body);
            _responseCache.TryAdd(url, response.Body);
            return doc.RootElement.Clone();
        }

        private static IHttpTransport CreateDefaultTransport(int timeoutSeconds)
        {
            var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
            http.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            http.DefaultRequestHeaders.UserAgent.ParseAdd(PluginConstants.UserAgent);
            return new HttpClientTransport(http);
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
            _transport.Dispose();
        }
    }
}
