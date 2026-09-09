using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;

namespace MusicStrmExtract.Online;

public sealed class MusicBrainzApi : IMusicBrainzApi
{
    private const string DefaultBaseUrl = "https://musicbrainz.org";
    private const int DefaultTimeoutSeconds = 25;
    private const int LinkedReleaseLookupLimit = 25;
    private const int BrowsePageSize = 100;

    private static readonly HttpClientTransport s_sharedTransport =
        CreateDefaultTransport(DefaultTimeoutSeconds);

    private readonly string _baseUrl;
    private readonly IHttpTransport _transport;
    private readonly IRequestGate _gate;
    private readonly bool _ownsTransport;
    private readonly ConcurrentDictionary<string, string> _responseCache = new(StringComparer.Ordinal);

    public MusicBrainzApi(string? baseUrl = null, int timeoutSeconds = 25)
        : this(
            baseUrl,
            timeoutSeconds == DefaultTimeoutSeconds
                ? s_sharedTransport
                : CreateDefaultTransport(timeoutSeconds),
            StaticMusicBrainzRateGate.Instance,
            disposeTransport: timeoutSeconds != DefaultTimeoutSeconds)
    {
    }

    internal MusicBrainzApi(string? baseUrl, IHttpTransport transport, IRequestGate gate)
        : this(baseUrl, transport, gate, disposeTransport: true)
    {
    }

    private MusicBrainzApi(
        string? baseUrl,
        IHttpTransport transport,
        IRequestGate gate,
        bool disposeTransport)
    {
        _baseUrl = (baseUrl ?? DefaultBaseUrl).TrimEnd('/');
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _ownsTransport = disposeTransport;
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
            sb.Append(" AND artist:\"").Append(artist.Trim().Replace("\"", string.Empty)).Append('"');

        var query = Uri.EscapeDataString(sb.ToString());
        var url = $"{_baseUrl}/ws/2/release?query={query}&fmt=json&limit={limit}";
        return ReleaseJsonReader.ParseSearchReleases(
            await GetJsonRootAsync(url, ct).ConfigureAwait(false));
    }

    /// <summary>按 release-group MBID 获取专辑概念及该组 release。
    /// lookup 对 linked releases 最多返回 25 条且按 GID 排序，超过 25 条时用 browse 继续分页补齐。
    /// inc=releases+media+artist-credits 同时带回 release 布局、组级与 release 级艺人信息。</summary>
    public async Task<ParsedReleaseGroup> GetReleaseGroupAsync(
        string rgMbid,
        CancellationToken ct)
    {
        var url = $"{_baseUrl}/ws/2/release-group/{Uri.EscapeDataString(rgMbid)}?inc=releases+media+artist-credits&fmt=json";
        var group = ReleaseJsonReader.ParseReleaseGroup(
            await GetJsonRootAsync(url, ct).ConfigureAwait(false));
        if (group.Releases.Count < LinkedReleaseLookupLimit)
            return group;

        return await LoadRemainingReleaseGroupReleasesAsync(group, rgMbid, ct).ConfigureAwait(false);
    }

    private async Task<ParsedReleaseGroup> LoadRemainingReleaseGroupReleasesAsync(
        ParsedReleaseGroup group,
        string rgMbid,
        CancellationToken ct)
    {
        var releases = group.Releases.ToList();
        var seen = new HashSet<string>(
            releases
                .Select(r => r.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!),
            StringComparer.OrdinalIgnoreCase);
        var offset = LinkedReleaseLookupLimit;

        while (true)
        {
            var browseUrl = $"{_baseUrl}/ws/2/release?release-group={Uri.EscapeDataString(rgMbid)}" +
                $"&inc=media+artist-credits&fmt=json&limit={BrowsePageSize}&offset={offset}";
            var (totalCount, page) = ReleaseJsonReader.ParseBrowseReleases(
                await GetJsonRootAsync(browseUrl, ct).ConfigureAwait(false));

            foreach (var release in page)
            {
                if (!string.IsNullOrWhiteSpace(release.Id) && seen.Add(release.Id))
                    releases.Add(release);
            }

            if (page.Count == 0 || releases.Count >= totalCount)
                break;

            offset += page.Count;
        }

        return new ParsedReleaseGroup(
            group.Id,
            group.Title,
            group.PrimaryType,
            group.Disambiguation,
            group.ArtistCredits,
            [.. releases]);
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
        using (await _gate.AcquireAsync(ct).ConfigureAwait(false))
        {
            var response = await _transport.GetAsync(url, ct).ConfigureAwait(false);
            if (response.StatusCode < 200 || response.StatusCode >= 300)
            {
                throw new HttpRequestException(
                    $"MusicBrainz HTTP {response.StatusCode}: {Truncate(response.Body, 200)}",
                    null,
                    (System.Net.HttpStatusCode)response.StatusCode);
            }

            using var doc = JsonDocument.Parse(response.Body);
            _ = _responseCache.TryAdd(url, response.Body);
            return doc.RootElement.Clone();
        }
    }

    private static HttpClientTransport CreateDefaultTransport(int timeoutSeconds)
    {
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
        http.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        http.DefaultRequestHeaders.UserAgent.ParseAdd(PluginConstants.UserAgent);
        return new HttpClientTransport(http);
    }

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
            return value ?? string.Empty;

        return string.Concat(value.AsSpan(0, max), "...");
    }

    public void Dispose()
    {
        if (_ownsTransport)
            _transport.Dispose();
    }
}
