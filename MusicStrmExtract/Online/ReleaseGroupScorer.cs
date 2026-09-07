using System;
using System.Collections.Generic;
using System.Linq;
using static MusicStrmExtract.Online.JsonUtil;

namespace MusicStrmExtract.Online
{
    /// <summary>
    /// 对同 release-group 下的多个 release 进行分层排序。
    /// 排序维度依次是:状态层、偏好国家层、年份贴近层、日期、同层质量分。
    /// </summary>
    public static class ReleaseGroupScorer
    {
        private const int BarcodePresentWeight = 30;
        private const int BarcodeFrequencyPerOccurrence = 5;
        private const int BarcodeFrequencyMax = 25;
        private const int CompleteDateWeight = 10;
        private const int CdFormatWeight = 8;
        private const int JewelCaseWeight = 5;
        private const int DisambiguationEmptyWeight = 10;

        private const long StatusRankBase = 1_000_000_000_000L;
        private const long PreferredCountryRankBase = 100_000_000_000L;
        private const int MissingYearDistance = 9999;

        /// <summary>
        /// 对同 RG 下所有 release 分层排序。
        /// </summary>
        /// <param name="releases">同 release-group 的全部候选。</param>
        /// <param name="localYear">本地目录名解析出的年份（如 "七里香 (2004)" → 2004）；null 表示无年份，跳过就近排序。</param>
        /// <param name="preferredCountry">自动推断出的偏好国家（ISO 3166-1 alpha-2）；
        /// 只给官方状态且地区匹配的候选加国家层，null 表示不启用国家加权。</param>
        public static List<RankedRelease> ScoreAll(
            IReadOnlyList<ReleaseSummary> releases,
            int? localYear = null,
            string? preferredCountry = null)
        {
            var result = new List<RankedRelease>();
            if (releases is null || releases.Count == 0)
            {
                return result;
            }

            var barcodeCounts = CountBarcodes(releases);
            foreach (var release in releases)
            {
                result.Add(new RankedRelease(
                    release,
                    ScoreRelease(release, barcodeCounts),
                    BuildRank(release, localYear, preferredCountry)));
            }

            if (localYear.HasValue)
            {
                result.Sort((a, b) =>
                {
                    var cmp = a.Rank.CompareTo(b.Rank);
                    if (cmp != 0)
                    {
                        return cmp;
                    }

                    // 年份贴近相同(如 2004 vs 2006,本地 2005)时,日期更早者优先(首发原版胜出)
                    cmp = string.Compare(
                        a.Release.Date ?? "9999",
                        b.Release.Date ?? "9999",
                        StringComparison.Ordinal);
                    return cmp != 0 ? cmp : b.Score.CompareTo(a.Score);
                });
            }
            else
            {
                result.Sort((a, b) =>
                {
                    var cmp = a.Rank.CompareTo(b.Rank);
                    return cmp != 0 ? cmp : b.Score.CompareTo(a.Score);
                });
            }

            return result;
        }

        /// <summary>
        /// 自动推断一个无感的国家偏好:在"官方 + 有 barcode + 碟布局与本地一致"的候选里,
        /// 取出现次数最多的国家(mode);次数打平时,取"该国基础分最高"的国家;再打平按国名稳定。
        /// 没有任何可匹配候选时返回 null(不启用加权,保持原行为)。
        /// </summary>
        public static string? InferPreferredCountry(
            IReadOnlyList<ReleaseSummary> releases,
            IReadOnlyList<LocalDisc> localDiscs)
        {
            if (releases is null || releases.Count == 0 || localDiscs is null || localDiscs.Count == 0)
            {
                return null;
            }

            var barcodeCounts = CountBarcodes(releases);

            // 只统计"官方实体版"且碟布局与本地一致的候选,避免被 Withdrawn/数字版带偏
            var compatible = releases
                .Where(r => ReleaseStatusPolicy.IsOfficial(r.Status)
                            && !string.IsNullOrWhiteSpace(r.Barcode)
                            && LayoutMatchesLocal(r, localDiscs))
                .ToList();
            if (compatible.Count == 0)
            {
                return null;
            }

            return compatible
                .GroupBy(r => r.Country ?? string.Empty)
                .Where(g => !string.IsNullOrWhiteSpace(g.Key))
                .Select(g => new
                {
                    Country = g.Key,
                    Count = g.Count(),
                    MaxBase = g.Max(r => ScoreRelease(r, barcodeCounts))
                })
                .OrderByDescending(g => g.Count)
                .ThenByDescending(g => g.MaxBase)
                .ThenBy(g => g.Country, StringComparer.Ordinal)
                .Select(g => g.Country)
                .FirstOrDefault();
        }

        /// <summary>
        /// 两个候选是否处于同一排序层(同状态/国家/年份贴近)且质量分与日期一致;
        /// 只有这样的残余并列才允许交给 CAA 决胜。
        /// </summary>
        internal static bool AreInSameRankingTier(
            RankedRelease first,
            RankedRelease second,
            int? localYear)
        {
            if (first.Rank != second.Rank || first.Score != second.Score)
            {
                return false;
            }

            if (localYear is null)
            {
                return true;
            }

            return string.Equals(
                first.Release.Date ?? "9999",
                second.Release.Date ?? "9999",
                StringComparison.Ordinal);
        }

        private static long BuildRank(
            ReleaseSummary release,
            int? localYear,
            string? preferredCountry)
        {
            var rank = ReleaseStatusPolicy.SearchPriority(release.Status) * StatusRankBase;
            if (!string.IsNullOrWhiteSpace(preferredCountry)
                && ReleaseStatusPolicy.IsOfficial(release.Status)
                && !string.Equals(release.Country, preferredCountry, StringComparison.OrdinalIgnoreCase))
            {
                // 偏好国家的官方版先于其它国家的官方版,但不跨越状态层。
                rank += PreferredCountryRankBase;
            }

            if (localYear.HasValue)
            {
                var releaseYear = ParseLeadingYear(release.Date);
                rank += releaseYear.HasValue
                    ? Math.Min(Math.Abs(releaseYear.Value - localYear.Value), MissingYearDistance)
                    : MissingYearDistance;
            }

            return rank;
        }

        /// <summary>release 的 media 布局是否与本地碟组完全一致(逐碟 track-count 相等)。</summary>
        private static bool LayoutMatchesLocal(ReleaseSummary release, IReadOnlyList<LocalDisc> localDiscs)
        {
            if (release.Media.Count != localDiscs.Count)
            {
                return false;
            }

            // 按 media.position 与本地碟(DiscNumber/轨数)排序后逐碟比对
            var sortedMedia = release.Media.OrderBy(m => m.Position).ToList();
            var sortedLocal = localDiscs.OrderBy(d => d.DiscNumber ?? int.MaxValue).ToList();
            for (var i = 0; i < sortedLocal.Count; i++)
            {
                var trackCount = sortedMedia[i].TrackCount;
                if (trackCount <= 0 || trackCount != sortedLocal[i].TrackNumbers.Count)
                {
                    return false;
                }
            }

            return true;
        }

        private static Dictionary<string, int> CountBarcodes(IEnumerable<ReleaseSummary> releases)
        {
            var barcodeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var release in releases)
            {
                if (string.IsNullOrWhiteSpace(release.Barcode))
                {
                    continue;
                }

                barcodeCounts.TryGetValue(release.Barcode, out var count);
                barcodeCounts[release.Barcode] = count + 1;
            }

            return barcodeCounts;
        }

        private static int ScoreRelease(ReleaseSummary release, Dictionary<string, int> barcodeCounts)
        {
            int score = 0;
            score += ReleaseStatusPolicy.ScoreWeight(release.Status);

            if (!string.IsNullOrWhiteSpace(release.Barcode))
            {
                score += BarcodePresentWeight;
                if (barcodeCounts.TryGetValue(release.Barcode, out var count))
                {
                    score += Math.Min(count * BarcodeFrequencyPerOccurrence, BarcodeFrequencyMax);
                }
            }

            if (!string.IsNullOrWhiteSpace(release.Date) && IsCompleteDate(release.Date))
            {
                score += CompleteDateWeight;
            }

            if (IsCdFormat(release))
            {
                score += CdFormatWeight;
            }

            if (string.Equals(release.Packaging, "Jewel Case", StringComparison.OrdinalIgnoreCase))
            {
                score += JewelCaseWeight;
            }

            if (string.IsNullOrWhiteSpace(release.Disambiguation))
            {
                score += DisambiguationEmptyWeight;
            }

            return score;
        }

        private static bool IsCdFormat(ReleaseSummary release)
        {
            return release.Media.Any(m =>
                string.Equals(m.Format, "CD", StringComparison.OrdinalIgnoreCase));
        }
    }
}
