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
    /// "按艺人 + 专辑文件夹名定位 release-group(专辑概念),再从该组选择对应 release(可购买发行版本)":
    /// 先把专辑文件夹名(如 "叶惠美 (2003)")净化成核心名("叶惠美"),
    /// 用 release:"专辑" AND artist:"艺人" 查询候选 release 以反查 release-group(每个 release 只属于一个 RG),
    /// 再按本地碟组(碟号+轨号)校验组内各 release 的 media 布局,
    /// 返回命中 release 的完整轨道映射(每轨 recording MBID/标题)——单曲按碟号+轨号取数。
    /// 目录名原样透传给 MusicBrainz,不参与本地文本比较;候选顺序稳定(Official 优先 → Album 主类型 → 完整日期优先 → score)。
    /// </summary>
    public sealed class AlbumSearch
    {
        /// <summary>搜索结果一次取回的候选数量;布局不匹配时继续尝试下一个。</summary>
        private const int SearchCandidateLimit = 10;

        private readonly IMusicBrainzApi _api;

        public AlbumSearch(IMusicBrainzApi api)
        {
            _api = api;
        }

        /// <summary>去除专辑名中的年份/附加括号等,得到核心名:"叶惠美 (2003)"→"叶惠美","七里香-2004"→"七里香"。</summary>
        public static string? CleanAlbumName(string? raw, string? artistName = null)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            var trimmed = raw.Trim();
            // 同名专辑目录(如 "The 1975" 的艺人自名专辑)不应把标题年份当发行年份剥掉。
            if (!string.IsNullOrWhiteSpace(artistName)
                && string.Equals(trimmed, artistName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            var s = trimmed;
            s = Regex.Replace(s, @"[\s_\-\.]*[\(\[（【]?\s*(18|19|20)\d{2}\s*[\)\]）】]?\s*$", string.Empty);
            s = Regex.Replace(s, @"[\s\-\._]+$", string.Empty);
            return string.IsNullOrWhiteSpace(s) ? trimmed : s.Trim();
        }

        /// <summary>目录名被清洗掉年份后缀时，解析该发行年份；同名自名专辑标题年份不算发行年份。</summary>
        internal static int? ParseFolderYear(string? albumFolderName, string? artistName)
        {
            var trimmed = albumFolderName?.Trim();
            var clean = CleanAlbumName(trimmed, artistName);
            if (string.IsNullOrWhiteSpace(trimmed)
                || string.IsNullOrWhiteSpace(clean)
                || string.Equals(trimmed, clean, StringComparison.Ordinal))
            {
                return null;
            }

            var match = Regex.Match(trimmed, @"\b(1[89]\d{2}|20\d{2})\s*[\)\]）】]?\s*$");
            return match.Success ? int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : (int?)null;
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
            var result = AlbumSearchResult.Empty;
            var clean = CleanAlbumName(albumFolderName, artistName);
            if (string.IsNullOrWhiteSpace(clean)
                || localDiscs is null
                || localDiscs.Count == 0
                || !localDiscs.Any(d => d.TrackNumbers.Count > 0))
            {
                return result;
            }

            var scored = (await _api.SearchReleasesAsync(clean, artistName, SearchCandidateLimit, ct).ConfigureAwait(false))
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

            // 尝试用 release-group 下 browse 补齐后的 release 候选分层评分选出最优实体版本;没有 exact 时保留已看到的布局候选,
            // 继续走搜索回退路径,避免当前 RG 无精确版本时错过其它 RG 的精确命中。
            var rgResult = await TryResolveFromReleaseGroupAsync(
                ordered[0],
                localDiscs,
                ParseFolderYear(albumFolderName, artistName),
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
                .ThenBy(s => s.Release.Id ?? string.Empty, StringComparer.Ordinal)
                .ToList();
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
            {
                return null;
            }

            try
            {
                var rg = await _api.GetReleaseGroupAsync(topRgMbid, ct).ConfigureAwait(false);
                if (rg.Releases.Count <= 1)
                {
                    return null;
                }

                // 从 RG lookup 候选推断偏好国家(比搜索样本更准),并传给评分
                var rgPreferredCountry = ReleaseGroupScorer.InferPreferredCountry(rg.Releases, localDiscs);
                var ranked = ReleaseGroupScorer.ScoreAll(rg.Releases, localYear, rgPreferredCountry);

                foreach (var rankedRelease in ranked)
                {
                    var release = rankedRelease.Release;
                    if (string.IsNullOrWhiteSpace(release.Id))
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

                    if (ReleaseLayoutMatcher.HasExactTrackCount(localDiscs, mapping, parsed.Medias))
                    {
                        return BuildAlbumResult(parsed.Release, parsed.Medias, rg);
                    }

                    SetFallback(state, rg, parsed.Release, parsed.Medias);
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

                if (ReleaseLayoutMatcher.HasExactTrackCount(localDiscs, mapping, parsed.Medias))
                {
                    return BuildAlbumResult(parsed.Release, parsed.Medias, null);
                }

                SetFallback(state, null, parsed.Release, parsed.Medias);
            }

            return state.FirstFallback ?? AlbumSearchResult.Empty;
        }

        private static AlbumSearchResult BuildAlbumResult(
            ReleaseSummary release,
            IReadOnlyList<ReleaseMedia> medias,
            ParsedReleaseGroup? releaseGroup)
        {
            var artistCredits = release.ArtistCredits.Count > 0
                ? release.ArtistCredits
                : releaseGroup?.ArtistCredits ?? Array.Empty<ArtistCredit>();
            return new AlbumSearchResult(
                true,
                release.Title ?? releaseGroup?.Title,
                JsonUtil.ParseYear(release.Date),
                release.Id,
                release.ReleaseGroupMbid ?? releaseGroup?.Id,
                artistCredits
                    .Select(c => c.Name)
                    .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)),
                artistCredits
                    .Select(c => c.Id)
                    .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id)),
                medias.ToArray());
        }

        private static void SetFallback(
            SearchState state,
            ParsedReleaseGroup? releaseGroup,
            ReleaseSummary release,
            IReadOnlyList<ReleaseMedia> medias)
        {
            if (state.FirstFallback is not null || string.IsNullOrWhiteSpace(release.Id))
            {
                return;
            }

            state.FirstFallback = BuildAlbumResult(release, medias, releaseGroup);
            state.FirstFallbackMbid = release.Id;
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
    }
}
