using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MusicStrmExtract.Online
{
    /// <summary>
    /// 从专辑文件夹名提取 MusicBrainz 搜索所需的干净标题与本地发行年份。
    /// </summary>
    internal static class AlbumFolderNameParser
    {
        private static readonly Regex YearSuffixRegex = new Regex(
            @"[\s_\-\.]*[\(\[（【]?\s*(18|19|20)\d{2}\s*[\)\]）】]?\s*$",
            RegexOptions.Compiled);

        private static readonly Regex TrailingSeparatorRegex = new Regex(
            @"[\s\-\._]+$",
            RegexOptions.Compiled);

        private static readonly Regex FolderYearRegex = new Regex(
            @"\b(1[89]\d{2}|20\d{2})\s*[\)\]）】]?\s*$",
            RegexOptions.Compiled);

        /// <summary>去除专辑名中的年份/附加括号等,得到核心名:"叶惠美 (2003)"→"叶惠美","七里香-2004"→"七里香"。</summary>
        public static string? Clean(string? raw, string? artistName = null)
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
            s = YearSuffixRegex.Replace(s, string.Empty);
            s = TrailingSeparatorRegex.Replace(s, string.Empty);
            return string.IsNullOrWhiteSpace(s) ? trimmed : s.Trim();
        }

        /// <summary>目录名被清洗掉年份后缀时，解析该发行年份；同名自名专辑标题年份不算发行年份。</summary>
        public static int? ParseYear(string? albumFolderName, string? artistName)
        {
            var trimmed = albumFolderName?.Trim();
            var clean = Clean(trimmed, artistName);
            if (string.IsNullOrWhiteSpace(trimmed)
                || string.IsNullOrWhiteSpace(clean)
                || string.Equals(trimmed, clean, StringComparison.Ordinal))
            {
                return null;
            }

            var match = FolderYearRegex.Match(trimmed);
            return match.Success
                ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
                : (int?)null;
        }
    }
}
