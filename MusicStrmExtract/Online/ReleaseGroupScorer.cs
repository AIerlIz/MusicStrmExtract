using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MusicStrmExtract.Online
{
    /// <summary>
    /// 对同 release-group 下的多个 release 进行加权评分，选出最合适的版本。
    /// </summary>
    public static class ReleaseGroupScorer
    {
        /// <summary>
        /// 对同 RG 下所有 release 评分并排序，返回 (release JSON, score) 列表。
        /// 主排序：总分降序；次级：本地年份与 release date 年份就近（差值绝对值小者优先）。
        /// </summary>
        /// <param name="allReleases">release-group 响应中的 releases JSON 数组。</param>
        /// <param name="localYear">本地目录名解析出的年份（如 "七里香 (2004)" → 2004）；null 表示无年份，跳过就近排序。</param>
        public static List<(JsonElement Item, int Score)> ScoreAll(JsonElement allReleases, int? localYear = null)
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
                var score = ScoreRelease(r, barcodeCounts);
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

        private static int ScoreRelease(JsonElement release, Dictionary<string, int> barcodeCounts)
        {
            int score = 0;

            // Status: Official +40, Pseudo-Release -10, Bootleg/Withdrawn -40
            var status = GetString(release, "status");
            switch (status)
            {
                case "Official": score += 40; break;
                case "Pseudo-Release": score -= 10; break;
                case "Bootleg":
                case "Withdrawn":
                    score -= 40; break;
            }

            // Barcode 存在 +30
            var barcode = GetString(release, "barcode");
            if (!string.IsNullOrWhiteSpace(barcode))
            {
                score += 30;
                // 频次加分 +5/次，上限 +50
                if (barcodeCounts.TryGetValue(barcode, out var count))
                {
                    score += Math.Min(count * 5, 50);
                }
            }

            // 完整日期 YYYY-MM-DD +10
            var date = GetString(release, "date");
            if (!string.IsNullOrWhiteSpace(date) && IsCompleteDate(date))
            {
                score += 10;
            }

            // Format = CD +8（MB release-group 响应中字段名为 media，非 medium-list）
            if (IsCdFormat(release))
            {
                score += 8;
            }

            // Packaging = Jewel Case +5
            var packaging = GetString(release, "packaging");
            if (string.Equals(packaging, "Jewel Case", StringComparison.OrdinalIgnoreCase))
            {
                score += 5;
            }

            // Disambiguation 为空 +5
            var disambig = GetString(release, "disambiguation");
            if (string.IsNullOrWhiteSpace(disambig))
            {
                score += 5;
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

        private static string? GetString(JsonElement element, string property)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
            return null;
        }
    }
}
