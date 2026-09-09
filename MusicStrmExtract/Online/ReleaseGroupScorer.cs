using System;
using System.Collections.Generic;
using System.Linq;
using static MusicStrmExtract.Online.JsonUtil;

namespace MusicStrmExtract.Online
{
    /// <summary>
    /// 对同 release-group 下的多个 release 进行分层排序。
    /// 排序维度依次是:状态层、年份贴近层、偏好国家层、日期、同层质量分。
    /// </summary>
    public static class ReleaseGroupScorer
    {
        private const int BarcodePresentWeight = 30;
        private const int BarcodeFrequencyPerOccurrence = 5;
        private const int BarcodeFrequencyMax = 25;
        private const int CompleteDateWeight = 10;
        private const int CdFormatWeight = 8;
        private const int JewelCaseWeight = 5;

        private const long StatusRankBase = 1_000_000_000_000L;
        private const long YearGapRankBase = 100_000L;
        private const long CountryRankBase = 1_000L;
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

            result.Sort((a, b) => CompareRanked(a, b, localYear.HasValue));

            return result;
        }

        private static int CompareRanked(
            RankedRelease a,
            RankedRelease b,
            bool dateBeforeScore)
        {
            var cmp = a.Rank.CompareTo(b.Rank);
            if (cmp != 0)
            {
                return cmp;
            }

            var scoreCmp = b.Score.CompareTo(a.Score);
            var dateCmp = string.Compare(
                a.Release.Date ?? "9999",
                b.Release.Date ?? "9999",
                StringComparison.Ordinal);

            // 有本地年份时,同年份贴近层内先比日期(首发原版胜出),再比质量分;
            // 无本地年份时保持同层质量分优先、日期只作稳定排序。
            cmp = dateBeforeScore
                ? (dateCmp != 0 ? dateCmp : scoreCmp)
                : (scoreCmp != 0 ? scoreCmp : dateCmp);
            return cmp != 0
                ? cmp
                : string.Compare(
                    a.Release.Id ?? string.Empty,
                    b.Release.Id ?? string.Empty,
                    StringComparison.Ordinal);
        }

        /// <summary>
        /// 自动推断一个无感的国家偏好:在"官方实体版 + 有 barcode + 有效地区 + 音频碟布局与本地一致"的候选里,
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

            // 只统计官方实体版与有效地区候选:Worldwide/数字发行不应把国家偏好带到实体原版之前
            var compatible = releases
                .Where(r => ReleaseStatusPolicy.IsOfficial(r.Status)
                            && !string.IsNullOrWhiteSpace(r.Barcode)
                            && IsMarketCountry(r.Country)
                            && HasPhysicalMedia(r)
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
                    MaxBase = g.Max(r => ScoreRelease(r, barcodeCounts)),
                    EarliestDate = g.Min(r => string.IsNullOrWhiteSpace(r.Date) ? "9999" : r.Date!)
                })
                .OrderByDescending(g => g.Count)
                .ThenByDescending(g => g.MaxBase)
                .ThenBy(g => g.EarliestDate, StringComparer.Ordinal)
                .ThenBy(g => g.Country, StringComparer.Ordinal)
                .Select(g => g.Country)
                .FirstOrDefault();
        }

        private static long BuildRank(
            ReleaseSummary release,
            int? localYear,
            string? preferredCountry)
        {
            var rank = ReleaseStatusPolicy.SearchPriority(release.Status) * StatusRankBase;

            if (localYear.HasValue)
            {
                var releaseYear = ParseLeadingYear(release.Date);
                var gap = releaseYear.HasValue
                    ? Math.Min(Math.Abs(releaseYear.Value - localYear.Value), MissingYearDistance)
                    : MissingYearDistance;
                rank += gap * YearGapRankBase;
            }

            if (!string.IsNullOrWhiteSpace(preferredCountry)
                && ReleaseStatusPolicy.IsOfficial(release.Status)
                && !string.Equals(release.Country, preferredCountry, StringComparison.OrdinalIgnoreCase))
            {
                // 同状态、同年份贴近层内的国家偏好;不跨越年份贴近层。
                rank += CountryRankBase;
            }

            return rank;
        }

        /// <summary>release 的 media 布局是否与本地碟组完全一致(逐碟 track-count 相等)。</summary>
        private static bool LayoutMatchesLocal(ReleaseSummary release, IReadOnlyList<LocalDisc> localDiscs)
        {
            var audioMedia = release.Media
                .Where(m => !ReleaseLayoutMatcher.IsVideoMedia(m.Format))
                .ToList();
            if (audioMedia.Count != localDiscs.Count)
            {
                return false;
            }

            // 按 media.position 与本地碟(DiscNumber/轨数)排序后逐碟比对
            var sortedMedia = audioMedia.OrderBy(m => m.Position).ToList();
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

        private static bool HasPhysicalMedia(ReleaseSummary release)
        {
            return release.Media.Any(m =>
                !string.Equals(m.Format, "Digital Media", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsMarketCountry(string? country)
        {
            return !string.IsNullOrWhiteSpace(country)
                && country.Length == 2
                && country[0] != 'X';
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


            return score;
        }

        private static bool IsCdFormat(ReleaseSummary release)
        {
            return release.Media.Any(m =>
                string.Equals(m.Format, "CD", StringComparison.OrdinalIgnoreCase));
        }
    }
}
