using System;
using System.Collections.Generic;
using System.Linq;
using static MusicStrmExtract.Online.JsonUtil;

namespace MusicStrmExtract.Online
{
    /// <summary>
    /// 对同 release-group 下的多个 release 进行加权评分，选出最合适的版本。
    /// </summary>
    public static class ReleaseGroupScorer
    {
        private const int BarcodePresentWeight = 30;
        private const int BarcodeFrequencyPerOccurrence = 5;
        private const int BarcodeFrequencyMax = 50;
        private const int CompleteDateWeight = 10;
        private const int CdFormatWeight = 8;
        private const int JewelCaseWeight = 5;
        private const int DisambiguationEmptyWeight = 5;
        private const int PreferredCountryWeight = 500;

        /// <summary>
        /// 对同 RG 下所有 release 评分并排序。
        /// 主排序：总分降序；次级：本地年份与 release date 年份就近（差值绝对值小者优先）。
        /// </summary>
        /// <param name="releases">同 release-group 的全部候选。</param>
        /// <param name="localYear">本地目录名解析出的年份（如 "七里香 (2004)" → 2004）；null 表示无年份，跳过就近排序。</param>
        /// <param name="preferredCountry">自动推断出的偏好国家（ISO 3166-1 alpha-2）；
        /// 只在最高基础分档内参与决胜，null 表示不启用国家加权。</param>
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
                result.Add(new RankedRelease(release, ScoreRelease(release, barcodeCounts)));
            }

            // 偏好国家只在最高基础分档内生效:低于最高档的 Bootleg/Pseudo-Release
            // 即使来自偏好国家也不能靠大权重反超更可信的官方版本。
            if (!string.IsNullOrWhiteSpace(preferredCountry) && result.Count > 0)
            {
                var maxBase = result.Max(x => x.Score);
                for (var i = 0; i < result.Count; i++)
                {
                    if (result[i].Score != maxBase)
                    {
                        continue;
                    }

                    if (string.Equals(result[i].Release.Country, preferredCountry, StringComparison.OrdinalIgnoreCase))
                    {
                        result[i] = result[i] with { Score = result[i].Score + PreferredCountryWeight };
                    }
                }
            }

            if (localYear.HasValue)
            {
                result.Sort((a, b) =>
                {
                    var cmp = b.Score.CompareTo(a.Score);
                    if (cmp != 0)
                    {
                        return cmp;
                    }

                    var ya = GetYear(a.Release);
                    var yb = GetYear(b.Release);
                    if (ya.HasValue && yb.HasValue)
                    {
                        var da = Math.Abs(ya.Value - localYear.Value);
                        var db = Math.Abs(yb.Value - localYear.Value);
                        cmp = da.CompareTo(db);
                        if (cmp != 0)
                        {
                            return cmp;
                        }

                        // 年份差值相同，日期更早者优先（首发原版胜出）
                        return string.Compare(
                            a.Release.Date ?? "9999",
                            b.Release.Date ?? "9999",
                            StringComparison.Ordinal);
                    }

                    // 有年份的排在无年份前面
                    return ya.HasValue ? -1 : yb.HasValue ? 1 : 0;
                });
            }
            else
            {
                result.Sort((a, b) => b.Score.CompareTo(a.Score));
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
        /// 两个 release 是否在 ScoreAll 的排序键下真正并列(同分 + 同年份就近 + 同日期);
        /// 无本地年份时 ScoreAll 只按分排序,同分即同级、可交给 CAA 决胜。
        /// </summary>
        internal static bool AreInSameRankingTier(
            ReleaseSummary first,
            ReleaseSummary second,
            int firstScore,
            int secondScore,
            int? localYear)
        {
            if (firstScore != secondScore)
            {
                return false;
            }

            if (localYear is null)
            {
                return true;
            }

            var firstYear = GetYear(first);
            var secondYear = GetYear(second);
            if (!firstYear.HasValue && !secondYear.HasValue)
            {
                // 双方都缺年份时 ScoreAll 的排序键仍并列,应继续用 CAA 决胜。
                return true;
            }

            if (!firstYear.HasValue || !secondYear.HasValue)
            {
                return false; // 仅一方缺年份:ScoreAll 会把"有年份"排前,缺失项不视为并列
            }

            if (Math.Abs(firstYear.Value - localYear.Value) != Math.Abs(secondYear.Value - localYear.Value))
            {
                return false;
            }

            return string.Equals(
                first.Date ?? "9999",
                second.Date ?? "9999",
                StringComparison.Ordinal);
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

        private static int? GetYear(ReleaseSummary release)
        {
            return ParseLeadingYear(release.Date);
        }
    }
}
