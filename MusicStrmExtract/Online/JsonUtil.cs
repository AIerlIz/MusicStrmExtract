using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MusicStrmExtract.Online
{
    /// <summary>读取 MusicBrainz JSON (System.Text.Json) 的通用只读辅助,避免在多处重复实现。</summary>
    internal static class JsonUtil
    {
        public static string? GetString(JsonElement element, string property)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }

            return null;
        }

        public static int GetInt(JsonElement element, string property)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value))
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
                {
                    return number;
                }

                if (value.ValueKind == JsonValueKind.String
                    && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }
            }

            return 0;
        }

        public static int? ParseYear(string? date)
        {
            if (string.IsNullOrWhiteSpace(date))
            {
                return null;
            }

            var m = Regex.Match(date, @"\b(1[89]\d{2}|20\d{2})\b");
            return m.Success ? int.Parse(m.Value, CultureInfo.InvariantCulture) : null;
        }

        /// <summary>解析以四位年份开头的日期；仅接受标准日期字段形态。</summary>
        public static int? ParseLeadingYear(string? date)
        {
            if (string.IsNullOrWhiteSpace(date))
            {
                return null;
            }

            var m = Regex.Match(date, @"^\d{4}");
            return m.Success ? int.Parse(m.Value, CultureInfo.InvariantCulture) : null;
        }

        /// <summary>完整日期 (YYYY-MM-DD)。</summary>
        public static bool IsCompleteDate(string? date)
        {
            return DateOnly.TryParseExact(
                date,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _);
        }

        /// <summary>
        /// 读取 artist-credit 数组。includeNameOnlyCredits=true 时兼容 MB 搜索响应中只有
        /// item.name、没有 artist 子对象的形态。
        /// </summary>
        public static List<ArtistCredit> GetArtistCredits(JsonElement owner, bool includeNameOnlyCredits)
        {
            var credits = new List<ArtistCredit>();
            if (owner.ValueKind != JsonValueKind.Object
                || !owner.TryGetProperty("artist-credit", out var credit)
                || credit.ValueKind != JsonValueKind.Array)
            {
                return credits;
            }

            foreach (var item in credit.EnumerateArray())
            {
                if (item.TryGetProperty("artist", out var artistEl))
                {
                    credits.Add(new ArtistCredit(GetString(artistEl, "name"), GetString(artistEl, "id")));
                    continue;
                }

                if (includeNameOnlyCredits)
                {
                    var name = GetString(item, "name");
                    if (name is not null)
                    {
                        credits.Add(new ArtistCredit(name, null));
                    }
                }
            }

            return credits;
        }
    }
}
