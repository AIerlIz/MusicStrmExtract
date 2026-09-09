using System.Collections.Generic;
using System.Linq;

namespace MusicStrmExtract.Providers
{
    /// <summary>
    /// 评论轨/正式轨原始轨号到 MusicBrainz 官方轨号的布局映射。
    /// </summary>
    internal static class CommentaryTrackMapper
    {
        public static int Map(
            int rawNumber,
            bool isCommentary,
            IReadOnlyCollection<int> commentaryNumbers,
            IReadOnlyCollection<int> regularNumbers)
        {
            if (rawNumber <= 0)
                return 0;

            var comm = DistinctPositiveSorted(commentaryNumbers);
            var reg = DistinctPositiveSorted(regularNumbers);
            return MapNormalized(rawNumber, isCommentary, comm, reg);
        }

        /// <summary>按已去重、升序的轨号集合执行评论轨映射，供批量扫描复用。</summary>
        public static int MapNormalized(
            int rawNumber,
            bool isCommentary,
            int[] commentaryNumbers,
            int[] regularNumbers)
        {
            if (rawNumber <= 0)
                return 0;

            var comm = commentaryNumbers;
            var reg = regularNumbers;
            if (comm.Length == 0 || reg.Length == 0)
                return rawNumber;

            // 只处理可判定的完整布局:等长奇偶交错、等长前后排列。
            // comm={1,3,5} reg={1..8} 这类部分交错缺少完整映射依据，保持原始轨号。
            // 完整奇偶交错:01/03/05 评论 + 02/04/06 正式 → 都映射到 1/2/3
            var commIsOdd = comm.All(n => n % 2 == 1) && reg.All(n => n % 2 == 0);
            var regIsOdd = reg.All(n => n % 2 == 1) && comm.All(n => n % 2 == 0);
            if ((commIsOdd || regIsOdd) && IsInterleavedPair(comm, reg))
            {
                var oddSet = commIsOdd ? comm : reg;
                var evenSet = commIsOdd ? reg : comm;
                if (oddSet.Contains(rawNumber))
                    return (rawNumber + 1) / 2;

                if (evenSet.Contains(rawNumber))
                    return rawNumber / 2;

                return rawNumber;
            }

            // 前后排列只在等长时有效;部分交错不在可判定布局内
            if (comm.Length == reg.Length)
            {
                // 评论轨接在正式轨之后:正式 1..N,评论 N+1..2N
                if (IsSequentialFrom(reg, 1) && IsSequentialFrom(comm, reg.Length + 1))
                    return isCommentary ? rawNumber - reg.Length : rawNumber;

                // 评论轨排在正式轨之前:评论 1..N,正式 N+1..2N
                if (IsSequentialFrom(comm, 1) && IsSequentialFrom(reg, comm.Length + 1))
                    return isCommentary ? rawNumber : rawNumber - comm.Length;
            }

            // 评论轨是正式轨的子集(共享相同轨号、或未命中上分支):按原始轨号返回
            if (comm.All(reg.Contains))
                return rawNumber;

            return rawNumber;
        }

        private static int[] DistinctPositiveSorted(IEnumerable<int> numbers)
        {
            return numbers.Where(n => n > 0).Distinct().OrderBy(n => n).ToArray();
        }

        private static bool IsInterleavedPair(
            int[] oddNumbers,
            int[] evenNumbers)
        {
            var count = oddNumbers.Length;
            if (count == 0 || oddNumbers.Length != evenNumbers.Length)
                return false;

            var canonical = oddNumbers.Select(n => (n + 1) / 2)
                .Concat(evenNumbers.Select(n => n / 2))
                .Where(n => n > 0)
                .ToArray();
            return canonical.Distinct().Count() == count && canonical.Max() == count;
        }

        private static bool IsSequentialFrom(int[] numbers, int start)
        {
            if (numbers.Length == 0)
                return false;

            for (var i = 0; i < numbers.Length; i++)
            {
                if (numbers[i] != start + i)
                    return false;
            }

            return true;
        }
    }
}
