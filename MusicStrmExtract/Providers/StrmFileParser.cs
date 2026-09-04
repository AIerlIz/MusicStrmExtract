using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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

        private static readonly Regex CommentaryRegex = new Regex(
            @"\(?\s*(?:commentary|评论音轨|評論音轨|评论轨|評論轨|评论|評論|解说|解說)\s*\)?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// 解析 strm 文件名,返回 (碟号, 原始轨号, 是否评论轨)。
        /// 无碟号时 DiscNumber=null,无轨号时 TrackNumber=0。
        /// </summary>
        public static (int? DiscNumber, int TrackNumber, bool IsCommentary) ParseFileName(string filePath)
        {
            var name = Path.GetFileName(filePath);
            if (name.EndsWith(".strm", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - ".strm".Length);
            }

            var isCommentary = CommentaryRegex.IsMatch(name);
            var discTrack = KeywordDiscTrackRegex.Match(name);
            if (discTrack.Success && TryNumber(discTrack.Groups[2], out var track) && track > 0)
            {
                return (GetDisc(discTrack.Groups[1]), track, isCommentary);
            }

            discTrack = PlainDiscTrackRegex.Match(name);
            if (discTrack.Success && TryNumber(discTrack.Groups[2], out track) && track > 0)
            {
                return (GetDisc(discTrack.Groups[1]), track, isCommentary);
            }

            var trackOnly = TrackNumberRegex.Match(name);
            if (trackOnly.Success && TryNumber(trackOnly.Groups[1], out track) && track > 0)
            {
                return (null, track, isCommentary);
            }

            return (null, 0, isCommentary);
        }

        /// <summary>
        /// 把评论轨/正式轨的原始轨号映射为 MusicBrainz 官方轨号。
        /// 支持同轨号并存、"评论轨在前/在后"及奇偶交错("01/03...评论 + 02/04...正式")布局;
        /// 无法识别时原样返回。
        /// </summary>
        public static int MapCommentaryTrackNumber(
            int rawNumber,
            bool isCommentary,
            IReadOnlyCollection<int> commentaryNumbers,
            IReadOnlyCollection<int> regularNumbers)
        {
            if (rawNumber <= 0)
            {
                return 0;
            }

            var comm = commentaryNumbers.Where(n => n > 0).Distinct().OrderBy(n => n).ToArray();
            var reg = regularNumbers.Where(n => n > 0).Distinct().OrderBy(n => n).ToArray();
            if (comm.Length == 0 || reg.Length == 0)
            {
                return rawNumber;
            }

            // 优先检查各类结构布局(奇偶交错/前后排列),避免 comm 是 reg 子集时提前返回导致漏判;
            // 例如 comm={1,3,5} reg={1..8} 是典型的"部分交错"形态,应映射到 1/2/3 而非原始轨号。
            // 奇偶交错:01/03/05 评论 + 02/04/06 正式 → 都映射到 1/2/3
            var commIsOdd = comm.All(n => n % 2 == 1) && reg.All(n => n % 2 == 0);
            var regIsOdd = reg.All(n => n % 2 == 1) && comm.All(n => n % 2 == 0);
            if ((commIsOdd || regIsOdd) && IsInterleavedPair(comm, reg))
            {
                var oddSet = commIsOdd ? comm : reg;
                var evenSet = commIsOdd ? reg : comm;
                if (oddSet.Contains(rawNumber))
                {
                    return (rawNumber + 1) / 2;
                }

                if (evenSet.Contains(rawNumber))
                {
                    return rawNumber / 2;
                }

                return rawNumber;
            }

            // 前后排列只在等长时有效;部分交错已在上分支处理,此处只需处理等长场景
            if (comm.Length == reg.Length)
            {
                // 评论轨接在正式轨之后:正式 1..N,评论 N+1..2N
                if (IsSequentialFrom(reg, 1) && IsSequentialFrom(comm, reg.Length + 1))
                {
                    return isCommentary ? rawNumber - reg.Length : rawNumber;
                }

                // 评论轨排在正式轨之前:评论 1..N,正式 N+1..2N
                if (IsSequentialFrom(comm, 1) && IsSequentialFrom(reg, comm.Length + 1))
                {
                    return isCommentary ? rawNumber : rawNumber - comm.Length;
                }
            }

            // 评论轨是正式轨的子集(共享相同轨号、或未命中上分支):按原始轨号返回
            if (comm.All(reg.Contains))
            {
                return rawNumber;
            }

            return rawNumber;
        }

        private static bool IsInterleavedPair(
            IReadOnlyCollection<int> oddNumbers,
            IReadOnlyCollection<int> evenNumbers)
        {
            var count = oddNumbers.Count;
            if (count == 0 || oddNumbers.Count != evenNumbers.Count)
            {
                return false;
            }

            var canonical = oddNumbers.Select(n => (n + 1) / 2)
                .Concat(evenNumbers.Select(n => n / 2))
                .Where(n => n > 0)
                .ToArray();
            return canonical.Distinct().Count() == count && canonical.Max() == count;
        }

        private static bool IsSequentialFrom(IReadOnlyList<int> numbers, int start)
        {
            if (numbers.Count == 0)
            {
                return false;
            }

            for (var i = 0; i < numbers.Count; i++)
            {
                if (numbers[i] != start + i)
                {
                    return false;
                }
            }

            return true;
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
