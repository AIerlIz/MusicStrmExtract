using System;
using System.Collections.Generic;
using System.Text.Json;

namespace MusicStrmExtract.Probing
{
    /// <summary>
    /// ffprobe -print_format json -show_format 输出中的标签解析结果。
    /// </summary>
    public sealed class ProbeResult
    {
        private static readonly System.Text.RegularExpressions.Regex ControlCharRegex =
            new System.Text.RegularExpressions.Regex(@"[\x00-\x1F]", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>format.tags(原始键值,保留大小写)。</summary>
        public Dictionary<string, string> Tags { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        public bool HasTags => Tags.Count > 0;

        /// <summary>解析 ffprobe JSON。格式不符时抛出 <see cref="JsonException"/>。</summary>
        public static ProbeResult FromJson(string json)
        {
            var result = new ProbeResult();
            if (string.IsNullOrWhiteSpace(json))
            {
                return result;
            }

            // 防御: 某些 ffprobe/标签会把裸控制字符(如 \r)直接写进 JSON 字符串, System.Text.Json 会拒绝。
            // 仅替换 ASCII 控制字符; 转义序列(\\n/\\r 等两个字符)不受影响。
            json = ControlCharRegex.Replace(json, " ");

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("format", out var format)
                && format.TryGetProperty("tags", out var tags)
                && tags.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in tags.EnumerateObject())
                {
                    result.Tags[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString() ?? string.Empty
                        : prop.Value.GetRawText();
                }
            }

            return result;
        }
    }
}
