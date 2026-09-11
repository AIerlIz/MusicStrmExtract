using System.Net.Http;
using System.Text.Json;

namespace MusicStrmExtract.Online;

/// <summary>
/// "按艺人 + 专辑文件夹名定位 release-group(专辑概念),再从该组选择对应 release(可购买发行版本)":
/// 先把专辑文件夹名(如 "叶惠美 (2003)")净化成核心名("叶惠美"),
/// 用 release:"专辑" AND artist:"艺人" 查询候选 release 以反查 release-group(每个 release 只属于一个 RG),
/// 再按本地碟组(碟号+轨号)校验组内各 release 的 media 布局,
/// 返回命中 release 的完整轨道映射(每轨 recording MBID/标题)——单曲按碟号+轨号取数。
/// 目录名原样透传给 MusicBrainz,不参与本地文本比较;候选顺序稳定(Official 优先 → Album 主类型 → 完整日期优先 → score)。
/// 国家偏好只在组内选版阶段(ReleaseGroupScorer)推断与生效,搜索阶段不参与。
/// </summary>
public sealed class AlbumSearch
{
    /// <summary>搜索结果一次取回的候选数量;布局不匹配时继续尝试下一个。</summary>
    private const int SearchCandidateLimit = 10;

    private readonly IMusicBrainzApi _api;
    private readonly ReleaseCandidateEvaluator _candidateEvaluator;

    public AlbumSearch(IMusicBrainzApi api)
    {
        _api = api;
        _candidateEvaluator = new ReleaseCandidateEvaluator(api);
    }

    /// <summary>去除专辑名中的年份/附加括号等,得到核心名:"叶惠美 (2003)"→"叶惠美","七里香-2004"→"七里香"。</summary>
    public static string? CleanAlbumName(string? raw, string? artistName = null)
    {
        return AlbumFolderNameParser.Clean(raw, artistName);
    }

    /// <summary>目录名被清洗掉年份后缀时，解析该发行年份；同名自名专辑标题年份不算发行年份。</summary>
    internal static int? ParseFolderYear(string? albumFolderName, string? artistName)
    {
        return AlbumFolderNameParser.ParseYear(albumFolderName, artistName);
    }

    /// <summary>
    /// 专辑轨道映射搜索:通过候选 release 定位其 release-group,再从组内 release 按本地指纹选实体版本;
    /// top-1 RG 无精确命中时保留搜索回退(其它 RG 的精确版本可能是 top-1 命错专辑的纠错)。
    /// 未找到 release、或候选 release 的 media 布局均不匹配本地碟组时返回 Found=false
    /// (调用方按未命中处理)。
    /// </summary>
    public async Task<AlbumSearchResult> SearchForTrackMapAsync(
        string albumFolderName,
        string? artistName,
        IReadOnlyList<LocalDisc> localDiscs,
        CancellationToken ct)
    {
        var clean = CleanAlbumName(albumFolderName, artistName);
        if (string.IsNullOrWhiteSpace(clean)
            || localDiscs is null
            || localDiscs.Count == 0
            || !localDiscs.Any(d => d.TrackNumbers.Count > 0))
            return AlbumSearchResult.Empty;

        var scored = (await _api.SearchReleasesAsync(clean, artistName, SearchCandidateLimit, ct).ConfigureAwait(false))
            .Where(s => s.Score > 0 && !string.IsNullOrWhiteSpace(s.Release.Title))
            .ToList();
        if (scored.Count == 0)
            return AlbumSearchResult.Empty;

        // 搜索阶段只按"候选是否像目标专辑"排序选出 RG 锚点;
        // 国家偏好从属于组内选版,统一在 TryResolveFromReleaseGroupAsync 里推断并使用。
        var ordered = SearchCandidateOrderingPolicy.Order(scored);
        var state = new SearchState();

        // 优先用 release-group 下 browse 补齐后的 release 候选分层评分选出最优实体版本;
        // 没有 exact 时保留已看到的布局候选,继续走搜索回退路径,避免当前 RG 无精确版本时错过其它 RG 的精确命中。
        var rgResult = await TryResolveFromReleaseGroupAsync(
            ordered[0],
            localDiscs,
            ParseFolderYear(albumFolderName, artistName),
            state,
            ct).ConfigureAwait(false);

        return rgResult
            ?? await TryResolveFromOrderedCandidatesAsync(ordered, localDiscs, state, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// RG 加权路径:从 top-1 候选取 release-group-id,对 browse 补齐后的 release 候选评分;
    /// 按分层结果返回第一个轨数完全一致的实体版本。失败或没有 exact 时返回 null 走搜索回退。
    /// </summary>
    private async Task<AlbumSearchResult?> TryResolveFromReleaseGroupAsync(
        ScoredRelease topCandidate,
        IReadOnlyList<LocalDisc> localDiscs,
        int? localYear,
        SearchState state,
        CancellationToken ct)
    {
        var topRgMbid = topCandidate.Release.ReleaseGroupMbid;
        if (string.IsNullOrWhiteSpace(topRgMbid))
            return null;

        try
        {
            var rg = await _api.GetReleaseGroupAsync(topRgMbid, ct).ConfigureAwait(false);
            if (rg.Releases.Count <= 1)
                return null;

            // 从 RG lookup 候选推断偏好国家(比搜索样本更准),并传给评分
            var rgPreferredCountry = ReleaseGroupScorer.InferPreferredCountry(rg.Releases, localDiscs);
            var ranked = ReleaseGroupScorer.ScoreAll(rg.Releases, localYear, rgPreferredCountry);

            return await EvaluateCandidatesAsync(
                ranked.Select(r => r.Release).ToList(),
                localDiscs,
                rg,
                state,
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // RG 查询/评分失败,降级到搜索候选回退
            return null;
        }
    }

    /// <summary>
    /// 逐个搜索候选取 tracklist,用本地碟组布局校验 media 映射;
    /// 轨数完全一致(每碟本地轨数 == release media 轨数)优先,避免普通版被豪华版/加歌版抢先选中。
    /// 注意:release 详情取回的网络/解析异常向上抛,由调用方区分"MB 不可达(不缓存)"与"确认未命中";
    /// 吞成 Found=false 会让暂时性故障被 30 分钟缓存锁死。
    /// </summary>
    private async Task<AlbumSearchResult> TryResolveFromOrderedCandidatesAsync(
        IReadOnlyList<ScoredRelease> ordered,
        IReadOnlyList<LocalDisc> localDiscs,
        SearchState state,
        CancellationToken ct)
    {
        var result = await EvaluateCandidatesAsync(
            ordered.Select(s => s.Release).ToList(),
            localDiscs,
            releaseGroup: null,
            state,
            ct).ConfigureAwait(false);

        return result ?? state.FirstFallback ?? AlbumSearchResult.Empty;
    }

    /// <summary>
    /// 按顺序评估候选 release:命中 exact 立即返回(无论是否来自 RG 评分路径);
    /// 非 exact 只记录首个布局候选作回退。全部未命中时返回 null,由调用方决定后续路径。
    /// </summary>
    private async Task<AlbumSearchResult?> EvaluateCandidatesAsync(
        IReadOnlyList<ReleaseSummary> candidates,
        IReadOnlyList<LocalDisc> localDiscs,
        ParsedReleaseGroup? releaseGroup,
        SearchState state,
        CancellationToken ct)
    {
        foreach (var candidate in candidates)
        {
            var match = await _candidateEvaluator.TryMatchAsync(
                candidate,
                localDiscs,
                state.EvaluatedReleaseIds,
                ct).ConfigureAwait(false);
            if (match is null)
                continue;

            if (match.IsExact)
                return AlbumSearchResultFactory.Create(match.Release, match.Medias, releaseGroup);

            state.RecordFallback(match, releaseGroup);
        }

        return null;
    }

    /// <summary>一次搜索的中间状态:首个布局候选回退 + 已取过详情的 release(避免重复请求)。</summary>
    private sealed class SearchState
    {
        private AlbumSearchResult? _firstFallback;

        public AlbumSearchResult? FirstFallback => _firstFallback;

        public HashSet<string> EvaluatedReleaseIds { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>记录首个非 exact 的布局候选;已记录过或 release 无 id 时忽略。</summary>
        public void RecordFallback(ReleaseMatch match, ParsedReleaseGroup? releaseGroup)
        {
            if (_firstFallback is not null || string.IsNullOrWhiteSpace(match.Release.Id))
                return;

            _firstFallback = AlbumSearchResultFactory.Create(match.Release, match.Medias, releaseGroup);
        }
    }
}
