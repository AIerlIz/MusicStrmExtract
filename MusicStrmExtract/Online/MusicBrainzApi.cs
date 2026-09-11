namespace MusicStrmExtract.Online;

public sealed class MusicBrainzApi : IMusicBrainzApi
{
    private const string DefaultBaseUrl = "https://musicbrainz.org";
    private const int DefaultTimeoutSeconds = 25;
    private const int LinkedReleaseLookupLimit = 25;
    private const int BrowsePageSize = 100;

    private static readonly HttpClientTransport s_sharedTransport =
        MusicBrainzRequestExecutor.CreateDefaultTransport(DefaultTimeoutSeconds);

    private readonly string _baseUrl;
    private readonly MusicBrainzRequestExecutor _requestExecutor;

    public MusicBrainzApi(string? baseUrl = null, int timeoutSeconds = 25)
        : this(
            baseUrl,
            timeoutSeconds == DefaultTimeoutSeconds
                ? s_sharedTransport
                : MusicBrainzRequestExecutor.CreateDefaultTransport(timeoutSeconds),
            StaticMusicBrainzRateGate.Instance,
            disposeTransport: timeoutSeconds != DefaultTimeoutSeconds)
    {
    }

    public MusicBrainzApi(Uri baseUrl, int timeoutSeconds = 25)
        : this(
            (baseUrl ?? throw new ArgumentNullException(nameof(baseUrl))).ToString(),
            timeoutSeconds)
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
        _requestExecutor = new MusicBrainzRequestExecutor(transport, gate, disposeTransport);
    }

    public async Task<ParsedRelease> GetReleaseAsync(string releaseMbid, CancellationToken ct)
    {
        return ReleaseTracklistParser.ParseRelease(
            await _requestExecutor
                .GetJsonRootAsync(MusicBrainzUrlBuilder.GetRelease(_baseUrl, releaseMbid), ct)
                .ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<ScoredRelease>> SearchReleasesAsync(
        string album,
        string? artist,
        int limit,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(album);

        return ReleaseJsonReader.ParseSearchReleases(
            await _requestExecutor
                .GetJsonRootAsync(MusicBrainzUrlBuilder.SearchReleases(_baseUrl, album, artist, limit), ct)
                .ConfigureAwait(false));
    }

    /// <summary>按 release-group MBID 获取专辑概念及该组 release。
    /// lookup 对 linked releases 最多返回 25 条且按 GID 排序，超过 25 条时用 browse 继续分页补齐。
    /// inc=releases+media+artist-credits 同时带回 release 布局、组级与 release 级艺人信息。</summary>
    public async Task<ParsedReleaseGroup> GetReleaseGroupAsync(
        string rgMbid,
        CancellationToken ct)
    {
        var group = ReleaseJsonReader.ParseReleaseGroup(
            await _requestExecutor
                .GetJsonRootAsync(MusicBrainzUrlBuilder.GetReleaseGroup(_baseUrl, rgMbid), ct)
                .ConfigureAwait(false));
        if (group.Releases.Count < LinkedReleaseLookupLimit)
            return group;

        return await LoadRemainingReleaseGroupReleasesAsync(group, rgMbid, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 分页硬上限。browse 每页 100 条,20 页即 2000 个 release,
    /// 远超任何真实 release-group 的规模;仅作为"服务端异常/去重失效"时的兜底护栏。
    /// </summary>
    private const int MaxBrowsePages = 20;
    private async Task<ParsedReleaseGroup> LoadRemainingReleaseGroupReleasesAsync(
        ParsedReleaseGroup group,
        string rgMbid,
        CancellationToken ct)
    {
        var releases = group.Releases.ToList();
        var seen = CreateSeenReleaseIds(releases);
        var offset = LinkedReleaseLookupLimit;
        var pages = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var page = await BrowseReleasePageAsync(rgMbid, offset, ct).ConfigureAwait(false);
            var added = AppendNewReleases(page.Releases, seen, releases);

            if (page.Releases.Count == 0)
                break; // 空页必然结束

            if (page.TotalCount is int total && releases.Count >= total)
                break; // 仅在服务端给出确切总数时作为终止依据;未知总数(count 缺失)不作为依据

            if (added == 0)
                break; // 本页未新增任何 release:服务端重复返回已见页,继续循环会死循环

            if (++pages >= MaxBrowsePages)
                break; // 硬上限兜底:至多 MaxBrowsePages(20)次 browse,与常量注释一致

            offset += page.Releases.Count;
        }

        return new ParsedReleaseGroup(
            group.Id,
            group.Title,
            group.PrimaryType,
            group.Disambiguation,
            group.ArtistCredits,
            [.. releases]);
    }

    /// <summary>已见过的 release id 集合(忽略大小写、跳过空 id),用于分页去重。</summary>
    private static HashSet<string> CreateSeenReleaseIds(IEnumerable<ReleaseSummary> releases)
    {
        return new HashSet<string>(
            releases
                .Select(r => r.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!),
            StringComparer.OrdinalIgnoreCase);
    }

    private async Task<(int? TotalCount, IReadOnlyList<ReleaseSummary> Releases)> BrowseReleasePageAsync(
        string rgMbid,
        int offset,
        CancellationToken ct)
    {
        var browseUrl = MusicBrainzUrlBuilder.BrowseReleases(_baseUrl, rgMbid, offset, BrowsePageSize);
        return ReleaseJsonReader.ParseBrowseReleases(
            await _requestExecutor.GetJsonRootAsync(browseUrl, ct).ConfigureAwait(false));
    }

    /// <summary>把本页中首次出现的 release 追加到结果集,并登记进去重集合;返回本页新增条数。</summary>
    private static int AppendNewReleases(
        IReadOnlyList<ReleaseSummary> page,
        HashSet<string> seen,
        List<ReleaseSummary> releases)
    {
        var added = 0;
        foreach (var release in page)
        {
            if (!string.IsNullOrWhiteSpace(release.Id) && seen.Add(release.Id))
            {
                releases.Add(release);
                added++;
            }
        }

        return added;
    }

    public void Dispose()
    {
        _requestExecutor.Dispose();
    }
}
