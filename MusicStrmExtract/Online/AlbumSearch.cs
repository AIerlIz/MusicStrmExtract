using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MusicStrmExtract.Online
{
    /// <summary>
    /// "按艺人 + 专辑文件夹名锁定 MusicBrainz release":把专辑文件夹名(如 "叶惠美 (2003)")净化成核心名("叶惠美"),
    /// 用 release:"专辑" AND artist:"艺人" 查询候选 release,再按本地碟组(碟号+轨号)校验每个候选的 media 布局,
    /// 返回首个布局匹配的 release 的完整轨道映射(每轨 recording MBID/标题)——单曲按碟号+轨号取数。
    /// 目录名原样透传给 MusicBrainz,不参与本地文本比较;候选顺序稳定(Official 优先 → Album 主类型 → 完整日期优先 → score)。
    /// </summary>
    public sealed class AlbumSearch
    {
        /// <summary>逐个尝试的候选 release 上限;布局不匹配时继续尝试下一个,避免多碟选到错误的单碟版本。</summary>
        public const int MaxCandidateReleases = 5;

        private readonly IMusicBrainzApi _api;
        private readonly ICoverArtClient _coverArtClient;

        public AlbumSearch(IMusicBrainzApi api, ICoverArtClient coverArtClient)
        {
            _api = api;
            _coverArtClient = coverArtClient;
        }

        /// <summary>去除专辑名中的年份/附加括号等,得到核心名:"叶惠美 (2003)"→"叶惠美","七里香-2004"→"七里香"。</summary>
        public static string? CleanAlbumName(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            var s = raw.Trim();
            s = Regex.Replace(s, @"[\s_\-\.]*[\(\[（【]?\s*(18|19|20)\d{2}\s*[\)\]）】]?\s*$", string.Empty);
            s = Regex.Replace(s, @"[\s\-\._]+$", string.Empty);
            return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        }

        /// <summary>
        /// 专辑轨道映射搜索:搜索候选 release + 取各自 tracklist + 本地碟组指纹校验选碟。
        /// 未找到 release、或候选 release 的 media 布局均不匹配本地碟组时返回 Found=false
        /// (调用方按未命中处理)。
        /// </summary>
        public async Task<AlbumSearchResult> SearchForTrackMapAsync(
            string albumFolderName,
            string? artistName,
            IReadOnlyList<LocalDisc> localDiscs,
            CancellationToken ct)
        {
            var result = AlbumSearchResult.Empty;
            var clean = CleanAlbumName(albumFolderName);
            if (string.IsNullOrWhiteSpace(clean)
                || localDiscs is null
                || localDiscs.Count == 0
                || !localDiscs.Any(d => d.TrackNumbers.Count > 0))
            {
                return result;
            }

            var scored = (await _api.SearchReleasesAsync(clean, artistName, 10, ct).ConfigureAwait(false))
                .Where(s => s.Score > 0 && !string.IsNullOrWhiteSpace(s.Release.Title))
                .ToList();
            if (scored.Count == 0)
            {
                return result;
            }

            // 无感国家偏好:从搜索候选推断多数国家,用于 top-10 排序时的 tie-break
            var preferredCountry = ReleaseGroupScorer.InferPreferredCountry(
                scored.Select(s => s.Release).ToList(),
                localDiscs);

            var ordered = OrderSearchCandidates(scored, preferredCountry);
            var state = new SearchState();

            // 尝试用 release-group 全量加权评分选出最优版本;没有 exact 时保留已看到的布局候选,
            // 继续走搜索回退路径,避免当前 RG 无精确版本时错过其它 RG 的精确命中。
            var rgResult = await TryResolveFromReleaseGroupAsync(
                ordered[0],
                localDiscs,
                JsonUtil.ParseYear(albumFolderName),
                state,
                ct).ConfigureAwait(false);
            if (rgResult is not null)
            {
                return rgResult;
            }

            return await TryResolveFromOrderedCandidatesAsync(ordered, localDiscs, state, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 稳定排序:本地目录文本已作为查询条件交给 MB,不再做字形过滤;
        /// status(Official 0 < Promotional/Unknown 1 < Bootleg/Withdrawn 2 < Pseudo-Release 3) -> 主类型 Album -> 完整日期优先 -> 日期最早
        /// (空日期排最后)-> score 降序 -> 官方名(字典序)
        /// </summary>
        private static List<ScoredRelease> OrderSearchCandidates(
            List<ScoredRelease> scored,
            string? preferredCountry)
        {
            return scored
                .OrderBy(s => ReleaseStatusPolicy.SearchPriority(s.Release.Status))
                .ThenBy(s => string.Equals(s.Release.PrimaryType, "Album", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(s => IsIncompleteDate(s.Release.Date) ? 1 : 0)
                .ThenBy(s => string.IsNullOrWhiteSpace(s.Release.Date) ? "9999" : s.Release.Date!)
                .ThenBy(s => string.Equals(s.Release.Country, preferredCountry, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenByDescending(s => s.Score)
                .ThenBy(s => s.Release.Title!, StringComparer.Ordinal)
                .Take(MaxCandidateReleases)
                .ToList();
        }

        /// <summary>
        /// RG 加权路径:从 top-1 候选取 release-group-id,拉取该 RG 下全部 release 评分;
        /// 只收集顶级分数档的精确命中并交给 CAA 决胜。失败或没有 exact 时返回 null 走搜索回退。
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
            {
                return null;
            }

            try
            {
                var rgReleases = await _api.GetReleaseGroupReleasesAsync(topRgMbid, ct).ConfigureAwait(false);
                if (rgReleases.Count <= 1)
                {
                    return null;
                }

                // 从 RG 全量候选推断偏好国家(比搜索样本更准),并传给评分
                var rgPreferredCountry = ReleaseGroupScorer.InferPreferredCountry(rgReleases, localDiscs);
                var ranked = ReleaseGroupScorer.ScoreAll(rgReleases, localYear, rgPreferredCountry);

                // 顶级分数档之外的命中不可能靠封面数胜出,无需继续收集(有界 CAA 请求)。
                var exactCandidates = new List<ExactCandidate>();
                foreach (var rankedRelease in ranked)
                {
                    var release = rankedRelease.Release;
                    if (string.IsNullOrWhiteSpace(release.Id))
                    {
                        continue;
                    }

                    // 已找到首个 exact 后,只有真正同档的候选才值得继续拉详情做 CAA 决胜。
                    if (exactCandidates.Count > 0
                        && !ReleaseGroupScorer.AreInSameRankingTier(
                            release,
                            exactCandidates[0].Release,
                            rankedRelease.Score,
                            exactCandidates[0].Score,
                            localYear))
                    {
                        break;
                    }

                    var parsed = await _api.GetReleaseAsync(release.Id, ct).ConfigureAwait(false);
                    if (parsed.Medias.Count == 0)
                    {
                        continue;
                    }

                    var mapping = ReleaseLayoutMatcher.MapLocalDiscsToMedias(localDiscs, parsed.Medias);
                    if (mapping is null)
                    {
                        continue;
                    }

                    if (ReleaseLayoutMatcher.HasExactTrackCount(localDiscs, mapping))
                    {
                        exactCandidates.Add(new ExactCandidate(release, parsed, rankedRelease.Score));
                    }
                    else
                    {
                        SetFallback(state, parsed.Release, parsed.Medias);
                    }
                }

                if (exactCandidates.Count > 0)
                {
                    return await PickExactByCoverAsync(exactCandidates, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                // RG 查询/评分失败,降级到搜索候选回退
            }

            return null;
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
            foreach (var scoredRelease in ordered)
            {
                var release = scoredRelease.Release;
                if (string.IsNullOrWhiteSpace(release.Id))
                {
                    continue;
                }

                if (string.Equals(state.FirstFallbackMbid, release.Id, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var parsed = await _api.GetReleaseAsync(release.Id, ct).ConfigureAwait(false);
                if (parsed.Medias.Count == 0)
                {
                    continue;
                }

                var mapping = ReleaseLayoutMatcher.MapLocalDiscsToMedias(localDiscs, parsed.Medias);
                if (mapping is null)
                {
                    continue;
                }

                if (ReleaseLayoutMatcher.HasExactTrackCount(localDiscs, mapping))
                {
                    return BuildAlbumResult(parsed.Release, parsed.Medias);
                }

                SetFallback(state, parsed.Release, parsed.Medias);
            }

            return state.FirstFallback ?? AlbumSearchResult.Empty;
        }

        private static AlbumSearchResult BuildAlbumResult(
            ReleaseSummary release,
            IReadOnlyList<ReleaseMedia> medias)
        {
            return new AlbumSearchResult(
                true,
                release.Title,
                JsonUtil.ParseYear(release.Date),
                release.Id,
                release.ReleaseGroupMbid,
                release.ArtistCredits
                    .Select(c => c.Name)
                    .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)),
                release.ArtistCredits
                    .Select(c => c.Id)
                    .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id)),
                medias.ToArray());
        }

        private static void SetFallback(
            SearchState state,
            ReleaseSummary release,
            IReadOnlyList<ReleaseMedia> medias)
        {
            if (state.FirstFallback is not null || string.IsNullOrWhiteSpace(release.Id))
            {
                return;
            }

            state.FirstFallback = BuildAlbumResult(release, medias);
            state.FirstFallbackMbid = release.Id;
        }

        /// <summary>在顶级分数档的精确命中候选中,用 Cover Art Archive 封面数打破残余并列;单候选直接返回。</summary>
        private async Task<AlbumSearchResult> PickExactByCoverAsync(
            List<ExactCandidate> candidates,
            CancellationToken ct)
        {
            if (candidates.Count == 1)
            {
                return BuildAlbumResult(candidates[0].Parsed.Release, candidates[0].Parsed.Medias);
            }

            AlbumSearchResult? best = null;
            var bestCover = -1;
            foreach (var candidate in candidates)
            {
                var mbid = candidate.Parsed.Release.Id;
                var cover = string.IsNullOrWhiteSpace(mbid)
                    ? 0
                    : await _coverArtClient.GetCoverArtCountAsync(mbid, ct).ConfigureAwait(false);
                var built = BuildAlbumResult(candidate.Parsed.Release, candidate.Parsed.Medias);
                if (best is null || cover > bestCover)
                {
                    best = built;
                    bestCover = cover;
                }
            }

            return best!;
        }

        private static bool IsIncompleteDate(string? date)
        {
            return !JsonUtil.IsCompleteDate(date);
        }

        private sealed class SearchState
        {
            public AlbumSearchResult? FirstFallback { get; set; }

            public string? FirstFallbackMbid { get; set; }
        }

        private sealed record ExactCandidate(ReleaseSummary Release, ParsedRelease Parsed, int Score);
    }
}
