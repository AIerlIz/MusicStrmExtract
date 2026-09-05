using System;
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
    }
}
