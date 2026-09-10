using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace MusicStrmExtract.Online;

/// <summary>
/// MusicBrainz JSON 请求的基础设施层:响应缓存、共享限流门、HTTP 执行与成功响应缓存。
/// </summary>
internal sealed class MusicBrainzRequestExecutor : IDisposable
{
    private const int DefaultTimeoutSeconds = 25;

    private readonly IHttpTransport _transport;
    private readonly IRequestGate _gate;
    private readonly bool _ownsTransport;
    private readonly ConcurrentDictionary<string, string> _responseCache = new(StringComparer.Ordinal);

    public MusicBrainzRequestExecutor(
        IHttpTransport transport,
        IRequestGate gate,
        bool ownsTransport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _ownsTransport = ownsTransport;
    }

    public async Task<JsonElement> GetJsonRootAsync(string url, CancellationToken ct)
    {
        if (_responseCache.TryGetValue(url, out var cached))
        {
            using var cachedDoc = JsonDocument.Parse(cached);
            return cachedDoc.RootElement.Clone();
        }

        // 门在"间隔等待 + 完整 HTTP 请求"期间保持占用:否则两个请求虽然间隔 1.1 秒启动,
        // 前一个仍可能未结束就并发打向 MusicBrainz,触发 503/429。
        using (await _gate.AcquireAsync(ct).ConfigureAwait(false))
        {
            var response = await _transport.GetAsync(url, ct).ConfigureAwait(false);
            if (response.StatusCode < 200 || response.StatusCode >= 300)
            {
                throw new HttpRequestException(
                    $"MusicBrainz HTTP {response.StatusCode}: {Truncate(response.Body, 200)}",
                    null,
                    (HttpStatusCode)response.StatusCode);
            }

            using var doc = JsonDocument.Parse(response.Body);
            _ = _responseCache.TryAdd(url, response.Body);
            return doc.RootElement.Clone();
        }
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "HttpClientTransport owns and disposes the HttpClient and its handler.")]
    public static HttpClientTransport CreateDefaultTransport(int timeoutSeconds)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            CheckCertificateRevocationList = true
        };
        var http = new HttpClient(handler, disposeHandler: true);
        http.Timeout = TimeSpan.FromSeconds(timeoutSeconds <= 0 ? DefaultTimeoutSeconds : timeoutSeconds);
        http.DefaultRequestHeaders.UserAgent.ParseAdd(PluginConstants.UserAgent);
        return new HttpClientTransport(http);
    }

    public void Dispose()
    {
        if (_ownsTransport)
            _transport.Dispose();
    }

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
            return value ?? string.Empty;

        return string.Concat(value.AsSpan(0, max), "...");
    }
}
