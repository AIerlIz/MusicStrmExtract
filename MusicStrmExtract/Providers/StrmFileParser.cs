using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace MusicStrmExtract.Providers
{
    /// <summary>
    /// 解析 .strm 文件名与碟目录名中的碟号/轨号。
    /// 单碟常见形态 "01 - 标题";碟+轨形态 "1-01", "01.01", "CD1-01", "Disc 1 - 01";
    /// 碟目录形态 "Disc 1", "CD2"。
    /// </summary>
    public static class StrmFileParser
    {
        private static readonly Regex PlainDiscTrackRegex = new Regex(
            @"^(\d{1,2})[-.](\d{1,3})(?=[\s_\-.]|$)",
            RegexOptions.Compiled);

        private static readonly Regex KeywordDiscTrackRegex = new Regex(
            @"^(?:cd|disc|disk|dvd)[\s_\-.]*(\d{1,2})[\s_\-.]*(\d{1,3})(?=[\s_\-.]|$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex TrackNumberRegex = new Regex(
            @"^(\d{1,3})\s*[-_]\s*",
            RegexOptions.Compiled);

        private static readonly Regex DiscFolderRegex = new Regex(
            @"^(?:disc|disk|cd|dvd)[\s_\-.]*(\d{1,2})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>解析 strm 文件名,返回 (碟号, 轨号);无碟号时 DiscNumber=null,无轨号时 TrackNumber=0。</summary>
        public static (int? DiscNumber, int TrackNumber) ParseFileName(string filePath)
        {
            var name = Path.GetFileName(filePath);
            if (name.EndsWith(".strm", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - ".strm".Length);
            }

            var discTrack = KeywordDiscTrackRegex.Match(name);
            if (discTrack.Success && TryNumber(discTrack.Groups[2], out var track) && track > 0)
            {
                return (GetDisc(discTrack.Groups[1]), track);
            }

            discTrack = PlainDiscTrackRegex.Match(name);
            if (discTrack.Success && TryNumber(discTrack.Groups[2], out track) && track > 0)
            {
                return (GetDisc(discTrack.Groups[1]), track);
            }

            var trackOnly = TrackNumberRegex.Match(name);
            if (trackOnly.Success && TryNumber(trackOnly.Groups[1], out track) && track > 0)
            {
                return (null, track);
            }

            return (null, 0);
        }

        /// <summary>解析碟目录名,返回碟号;"Disc 1"/"CD2" → 1/2,其它目录名 → null。</summary>
        public static int? ParseDiscFolderName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var m = DiscFolderRegex.Match(name);
            return m.Success && TryNumber(m.Groups[1], out var disc) ? disc : (int?)null;
        }

        private static int? GetDisc(Group group)
        {
            return TryNumber(group, out var disc) && disc > 0 ? disc : (int?)null;
        }

        private static bool TryNumber(Group group, out int number)
        {
            return int.TryParse(group.Value, NumberStyles.None, CultureInfo.InvariantCulture, out number);
        }
    }
}
