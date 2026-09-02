using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace MusicStrmExtract.Probing
{
    /// <summary>
    /// ffprobe -print_format json -show_format -show_streams 输出的轻量解析结果。
    /// </summary>
    public sealed class ProbeResult
    {
        private static readonly System.Text.RegularExpressions.Regex ControlCharRegex =
            new System.Text.RegularExpressions.Regex(@"[\x00-\x1F]", System.Text.RegularExpressions.RegexOptions.Compiled);
        /// <summary>format.tags(原始键值,保留大小写)。</summary>
        public Dictionary<string, string> Tags { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>是否存在内嵌封面(attached_pic)流。</summary>
        public bool HasEmbeddedCover { get; set; }

        /// <summary>容器(format_name,如 flac/mp3/mp4)。</summary>
        public string? Container { get; set; }

        /// <summary>时长(秒)。</summary>
        public double? DurationSeconds { get; set; }

        /// <summary>媒体总字节(服务器报告)。</summary>
        public long? SizeBytes { get; set; }

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

            if (root.TryGetProperty("format", out var format))
            {
                if (format.TryGetProperty("format_name", out var formatName))
                {
                    result.Container = formatName.GetString();
                }

                if (format.TryGetProperty("duration", out var duration)
                    && double.TryParse(duration.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                {
                    result.DurationSeconds = seconds;
                }

                if (format.TryGetProperty("size", out var size)
                    && long.TryParse(size.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes))
                {
                    result.SizeBytes = bytes;
                }

                if (format.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in tags.EnumerateObject())
                    {
                        result.Tags[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                            ? prop.Value.GetString() ?? string.Empty
                            : prop.Value.GetRawText();
                    }
                }
            }

            if (root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array)
            {
                foreach (var stream in streams.EnumerateArray())
                {
                    if (stream.TryGetProperty("disposition", out var disposition)
                        && disposition.TryGetProperty("attached_pic", out var attachedPic)
                        && attachedPic.GetInt32() == 1)
                    {
                        result.HasEmbeddedCover = true;
                    }
                    else if (stream.TryGetProperty("codec_type", out var codecType)
                             && string.Equals(codecType.GetString(), "video", StringComparison.OrdinalIgnoreCase)
                             && stream.TryGetProperty("codec_name", out var codecName)
                             && string.Equals(codecName.GetString(), "mjpeg", StringComparison.OrdinalIgnoreCase))
                    {
                        // 部分容器把封面标为 video/mjpeg 而非 attached_pic;ffprobe 对 flac/mp3 内嵌封面一般置 attached_pic
                        result.HasEmbeddedCover = true;
                    }
                }
            }

            return result;
        }
    }
}
