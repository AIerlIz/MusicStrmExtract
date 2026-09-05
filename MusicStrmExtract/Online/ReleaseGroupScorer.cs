using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using static MusicStrmExtract.Online.JsonUtil;

namespace MusicStrmExtract.Online
{
    /// <summary>
    /// 对同 release-group 下的多个 release 进行加权评分，选出最合适的版本。
    /// </summary>
    public static class ReleaseGroupScorer
    {
        private const int OfficialStatusWeight = 40;
        private const int PseudoReleaseWeight = -10;
        private const int BootlegOrWithdrawnWeight = -40;
        private const int BarcodePresentWeight = 30;
        private const int BarcodeFrequencyPerOccurrence = 5;
        private const int BarcodeFrequencyMax = 50;
        private const int CompleteDateWeight = 10;
        private const int CdFormatWeight = 8;
        private const int JewelCaseWeight = 5;
        private const int DisambiguationEmptyWeight = 5;
        private const int PreferredCountryWeight = 500;

        /// <summary>
        /// 对同 RG 下所有 release 评分并排序，返回 (release JSON, score) 列表。
        /// 主排序：总分降序；次级：本地年份与 release date 年份就近（差值绝对值小者优先）。
        /// </summary>
        /// <param name="allReleases">release-group 响应中的 releases JSON 数组。</param>
        /// <param name="localYear">本地目录名解析出的年份（如 "七里香 (2004)" → 2004）；null 表示无年份，跳过就近排序。</param>
        /// <param name="preferredCountry">自动推断出的偏好国家（ISO 3166-1 alpha-2）；null 表示不启用国家加权。</param>
        public static List<(JsonElement Item, int Score)> ScoreAll(JsonElement allReleases, int? localYear = null, string? preferredCountry = null)
        {
            var result = new List<(JsonElement Item, int Score)>();
            var barcodeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var r in allReleases.EnumerateArray())
            {
                var bc = GetString(r, "barcode");
                if (!string.IsNullOrWhiteSpace(bc))
                {
                    if (!barcodeCounts.TryGetValue(bc, out var c)) barcodeCounts[bc] = 0;
                    barcodeCounts[bc] = c + 1;
                }
            }

            foreach (var r in allReleases.EnumerateArray())
            {
                var score = ScoreRelease(r, barcodeCounts, preferredCountry);
                result.Add((r, score));
            }

            if (localYear.HasValue)
            {
                result.Sort((a, b) =>
                {
                    var cmp = b.Score.CompareTo(a.Score);
                    if (cmp != 0) return cmp;
                    var ya = GetYear(a.Item);
                    var yb = GetYear(b.Item);
                    if (ya.HasValue && yb.HasValue)
                    {
                        var da = Math.Abs(ya.Value - localYear.Value);
                        var db = Math.Abs(yb.Value - localYear.Value);
                        cmp = da.CompareTo(db);
                        if (cmp != 0) return cmp;
                        // 年份差值相同，日期更早者优先（首发原版胜出）
                        return string.Compare(
                            GetString(a.Item, "date") ?? "9999",
                            GetString(b.Item, "date") ?? "9999",
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
        public static string? InferPreferredCountry(IEnumerable<JsonElement> releases, IReadOnlyList<LocalDisc> localDiscs)
        {
            if (releases is null || localDiscs is null || localDiscs.Count == 0)
            {
                return null;
            }

            var list = releases.Where(r => r.ValueKind == JsonValueKind.Object).ToList();
            if (list.Count == 0)
            {
                return null;
            }

            var barcodeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in list)
            {
                var bc = GetString(r, "barcode");
                if (!string.IsNullOrWhiteSpace(bc))
                {
                    if (!barcodeCounts.TryGetValue(bc, out var c)) barcodeCounts[bc] = 0;
                    barcodeCounts[bc] = c + 1;
                }
            }

            // 只统计"官方实体版"且碟布局与本地一致的候选,避免被 Withdrawn/数字版带偏
            var compatible = list
                .Where(r => string.Equals(GetString(r, "status"), "Official", StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(GetString(r, "barcode"))
                            && LayoutMatchesLocal(r, localDiscs))
                .ToList();
            if (compatible.Count == 0)
            {
                return null;
            }

            return compatible
                .GroupBy(r => GetString(r, "country") ?? string.Empty)
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

        /// <summary>release 的 media 布局是否与本地碟组完全一致(逐碟 track-count 相等)。</summary>
        private static bool LayoutMatchesLocal(JsonElement release, IReadOnlyList<LocalDisc> localDiscs)
        {
            if (!release.TryGetProperty("media", out var mediaArr) || mediaArr.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var medias = mediaArr.EnumerateArray().ToList();
            if (medias.Count != localDiscs.Count)
            {
                return false;
            }

            // 按 media.position 与本地碟(DiscNumber/轨数)排序后逐碟比对
            var sortedMedia = medias.OrderBy(m => GetInt(m, "position")).ToList();
            var sortedLocal = localDiscs.OrderBy(d => d.DiscNumber ?? int.MaxValue).ToList();
            for (var i = 0; i < sortedLocal.Count; i++)
            {
                var tc = GetInt(sortedMedia[i], "track-count");
                if (tc <= 0 || tc != sortedLocal[i].TrackNumbers.Count)
                {
                    return false;
                }
            }

            return true;
        }

        private static int ScoreRelease(JsonElement release, Dictionary<string, int> barcodeCounts, string? preferredCountry = null)
        {
            int score = 0;

            // Status: Official +40, Pseudo-Release -10, Bootleg/Withdrawn -40
            var status = GetString(release, "status");
            switch (status)
            {
                case "Official": score += OfficialStatusWeight; break;
                case "Pseudo-Release": score += PseudoReleaseWeight; break;
                case "Bootleg":
                case "Withdrawn":
                    score += BootlegOrWithdrawnWeight; break;
            }

            // Barcode 存在 +30
            var barcode = GetString(release, "barcode");
            if (!string.IsNullOrWhiteSpace(barcode))
            {
                score += BarcodePresentWeight;
                // 频次加分 +5/次，上限 +50
                if (barcodeCounts.TryGetValue(barcode, out var count))
                {
                    score += Math.Min(count * BarcodeFrequencyPerOccurrence, BarcodeFrequencyMax);
                }
            }

            // 完整日期 YYYY-MM-DD +10
            var date = GetString(release, "date");
            if (!string.IsNullOrWhiteSpace(date) && IsCompleteDate(date))
            {
                score += CompleteDateWeight;
            }

            // Format = CD +8（MB release-group 响应中字段名为 media，非 medium-list）
            if (IsCdFormat(release))
            {
                score += CdFormatWeight;
            }

            // Packaging = Jewel Case +5
            var packaging = GetString(release, "packaging");
            if (string.Equals(packaging, "Jewel Case", StringComparison.OrdinalIgnoreCase))
            {
                score += JewelCaseWeight;
            }

            // Disambiguation 为空 +5
            var disambig = GetString(release, "disambiguation");
            if (string.IsNullOrWhiteSpace(disambig))
            {
                score += DisambiguationEmptyWeight;
            }

            // 国家偏好:命中偏好国家,加一个大权重顶到并列前列(是否真正选中仍受精确轨数硬校验约束)
            if (!string.IsNullOrWhiteSpace(preferredCountry))
            {
                var country = GetString(release, "country");
                if (string.Equals(country, preferredCountry, StringComparison.OrdinalIgnoreCase))
                {
                    score += PreferredCountryWeight;
                }
            }

            return score;
        }

        private static bool IsCdFormat(JsonElement release)
        {
            if (!release.TryGetProperty("media", out var ml) || ml.ValueKind != JsonValueKind.Array) return false;
            foreach (var m in ml.EnumerateArray())
            {
                var fmt = GetString(m, "format");
                if (string.Equals(fmt, "CD", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool IsCompleteDate(string date)
        {
            if (date.Length != 10) return false;
            var m = Regex.Match(date, @"^\d{4}-\d{2}-\d{2}$");
            return m.Success;
        }

        private static int? GetYear(JsonElement release)
        {
            var date = GetString(release, "date");
            if (string.IsNullOrWhiteSpace(date)) return null;
            var m = Regex.Match(date, @"^\d{4}");
            return m.Success ? int.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture) : null;
        }

    }
}
