using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MusicStrmExtract.Online
{
    /// <summary>Cover Art Archive 客户端:查询封面数并生成封面下载 URL。</summary>
    public sealed class CoverArtClient : ICoverArtClient
    {
        private const string DefaultBaseUrl = "https://coverartarchive.org/release/";

        private static readonly HttpClient Http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });

        private readonly string _baseUrl;

        static CoverArtClient()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd(PluginConstants.UserAgent);
            Http.Timeout = TimeSpan.FromSeconds(15);
        }

        public CoverArtClient(string? baseUrl = null)
        {
            _baseUrl = string.IsNullOrWhiteSpace(baseUrl)
                ? DefaultBaseUrl
                : baseUrl.TrimEnd('/') + "/";
        }

        public async Task<int> GetCoverArtCountAsync(string releaseMbid, CancellationToken ct)
        {
            try
            {
                var url = $"{_baseUrl}{Uri.EscapeDataString(releaseMbid)}";
                using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    return 0;
                }

                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(body);
                return ParseCoverArtCount(doc.RootElement);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>按配置的基础地址生成 release 正面封面 URL(供 Emby 图片下载器使用)。</summary>
        public string BuildFrontImageUrl(string releaseMbid)
        {
            return $"{_baseUrl}{Uri.EscapeDataString(releaseMbid)}/front-500";
        }

        /// <summary>从 Cover Art Archive release 响应的根节点解析封面分:有正面 +10000,再加图数。</summary>
        public static int ParseCoverArtCount(JsonElement root)
        {
            if (root.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array)
            {
                var count = 0;
                var front = false;
                foreach (var img in images.EnumerateArray())
                {
                    if (img.TryGetProperty("front", out var f) && f.ValueKind == JsonValueKind.True)
                    {
                        front = true;
                    }

                    count++;
                }

                return (front ? 10000 : 0) + count;
            }

            return 0;
        }
    }
}
