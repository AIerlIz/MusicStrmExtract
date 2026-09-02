using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MusicStrmExtract.Online
{
    /// <summary>
    /// Apple iTunes Search API 客户端(公开、无需密钥),作为 MusicBrainz 之后的兜底数据源。
    /// 注意:返回的 trackName 为罗马/本地化拼写,不应用于覆盖中文标题——仅用于专辑侧补全与封面。
    /// </summary>
    public sealed class ITunesApi : IDisposable
    {
        private const string SearchUrl = "https://itunes.apple.com/search";

        private readonly HttpClient _http;

        public ITunesApi(int timeoutSeconds = 20)
        {
            _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
            _http.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        }

        /// <summary>搜索单曲, limit 条。返回 results 数组元素(JsonElement)。</summary>
        public async Task<JsonElement> SearchSongAsync(string artist, string title, int limit, CancellationToken ct)
        {
            var term = Uri.EscapeDataString($"{artist} {title}");
            var url = $"{SearchUrl}?media=music&entity=song&limit={limit}&term={term}";

            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"iTunes HTTP {(int)response.StatusCode}");
            }

            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.Clone();
        }

        /// <summary>把 artworkUrl100 放大为 600x600。</summary>
        public static string? UpgradeArtworkUrl(string? artworkUrl)
        {
            if (string.IsNullOrWhiteSpace(artworkUrl))
            {
                return null;
            }

            return artworkUrl.Contains("100x100bb", StringComparison.Ordinal)
                ? artworkUrl.Replace("100x100bb", "600x600bb")
                : artworkUrl;
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }
}
