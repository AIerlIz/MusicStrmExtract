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
            var browseUrl = MusicBrainzUrlBuilder.BrowseReleases(
                _baseUrl,
                rgMbid,
                offset,
                BrowsePageSize);
            var (totalCount, page) = ReleaseJsonReader.ParseBrowseReleases(
                await _requestExecutor.GetJsonRootAsync(browseUrl, ct).ConfigureAwait(false));

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

    public void Dispose()
    {
        _requestExecutor.Dispose();
    }
}
