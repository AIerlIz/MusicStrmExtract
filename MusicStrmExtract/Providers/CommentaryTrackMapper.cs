namespace MusicStrmExtract.Providers;

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

        if (commentaryNumbers.Length == 0 || regularNumbers.Length == 0)
            return rawNumber;

        // 只处理可判定的完整布局:等长奇偶交错、等长前后排列。
        // comm={1,3,5} reg={1..8} 这类部分交错缺少完整映射依据，保持原始轨号。
        var interleaved = TryMapInterleaved(rawNumber, commentaryNumbers, regularNumbers);
        if (interleaved is not null)
            return interleaved.Value;

        var contiguous = TryMapContiguous(rawNumber, isCommentary, commentaryNumbers, regularNumbers);
        if (contiguous is not null)
            return contiguous.Value;

        // 未命中任何可判定布局:共享相同轨号、部分交错等都按原始轨号返回
        return rawNumber;
    }

    /// <summary>
    /// 完整奇偶交错:01/03/05 评论 + 02/04/06 正式 → 都映射到 1/2/3。
    /// 不是合法交错形态时返回 null,交由其它布局判定。
    /// </summary>
    private static int? TryMapInterleaved(
        int rawNumber,
        int[] commentaryNumbers,
        int[] regularNumbers)
    {
        var commentaryIsOdd = commentaryNumbers.All(IsOdd);
        var regularIsOdd = regularNumbers.All(IsOdd);

        var oddSet = commentaryIsOdd && regularNumbers.All(IsEven)
            ? commentaryNumbers
            : regularIsOdd && commentaryNumbers.All(IsEven)
                ? regularNumbers
                : null;
        if (oddSet is null)
            return null;

        var evenSet = ReferenceEquals(oddSet, commentaryNumbers) ? regularNumbers : commentaryNumbers;
        if (!IsInterleavedPair(oddSet, evenSet))
            return null;

        if (oddSet.Contains(rawNumber))
            return (rawNumber + 1) / 2;

        if (evenSet.Contains(rawNumber))
            return rawNumber / 2;

        return rawNumber;
    }

    /// <summary>
    /// 前后排列(只在等长时有效):正式 1..N + 评论 N+1..2N,或评论 1..N + 正式 N+1..2N。
    /// 不是可判定排列时返回 null。
    /// </summary>
    private static int? TryMapContiguous(
        int rawNumber,
        bool isCommentary,
        int[] commentaryNumbers,
        int[] regularNumbers)
    {
        if (commentaryNumbers.Length != regularNumbers.Length)
            return null;

        // 评论轨接在正式轨之后:正式 1..N,评论 N+1..2N
        if (IsSequentialFrom(regularNumbers, 1) && IsSequentialFrom(commentaryNumbers, regularNumbers.Length + 1))
            return isCommentary ? rawNumber - regularNumbers.Length : rawNumber;

        // 评论轨排在正式轨之前:评论 1..N,正式 N+1..2N
        if (IsSequentialFrom(commentaryNumbers, 1) && IsSequentialFrom(regularNumbers, commentaryNumbers.Length + 1))
            return isCommentary ? rawNumber : rawNumber - commentaryNumbers.Length;

        return null;
    }

    private static bool IsOdd(int number)
    {
        return number % 2 == 1;
    }

    private static bool IsEven(int number)
    {
        return number % 2 == 0;
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
