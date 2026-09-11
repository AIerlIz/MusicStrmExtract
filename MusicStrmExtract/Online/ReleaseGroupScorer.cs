using static MusicStrmExtract.Online.JsonUtil;

namespace MusicStrmExtract.Online;

/// <summary>
/// 对同 release-group 下的多个 release 进行分层排序。
/// 排序维度依次是:状态层、年份贴近层、偏好国家层、日期、同层质量分。
/// </summary>
/// <remarks>
/// 前三个维度通过 <see cref="BuildRank"/> 编码进单个 <c>long</c> 实现严格分层:
/// 高层权重与低层权重相差至少两个数量级,低层任意取值都无法翻越高层的最小步进。
/// 后两个维度(<see cref="CompareInRankTier"/>)只在 Rank 完全相等时作为残余同分者的兜底,
/// 因此永远不会影响任何一层的既定顺序。
/// </remarks>
public static class ReleaseGroupScorer
{
    private const int BarcodePresentWeight = 30;
    private const int BarcodeFrequencyPerOccurrence = 5;
    private const int BarcodeFrequencyMax = 25;
    private const int CompleteDateWeight = 10;
    private const int CdFormatWeight = 8;
    private const int JewelCaseWeight = 5;

    private const long StatusRankBase = 1_000_000_000_000L;
    private const long YearGapRankBase = 100_000L;
    private const long CountryRankBase = 1_000L;
    private const int MissingYearDistance = 9999;

    /// <summary>
    /// 分层权重必须严格满足的量级契约:低层权重的任何取值都不得跨越高层权重的最小步进,
    /// 即国家层(最低的加权层)必须严格小于年份层的最小步进,否则国家偏好会翻越年份贴近层。
    /// 年份层内部取值的上限由 <see cref="MissingYearDistance"/> 自行保证,与跨层隔离无关。
    /// 契约由测试 <c>ReleaseGroupScorerTests.RankLayerBases_SatisfyIsolationContract</c> 锁定。
    /// </summary>
    private const bool RankLayersAreIsolated = CountryRankBase < YearGapRankBase;

    /// <summary>
    /// 对同 RG 下所有 release 分层排序。
    /// </summary>
    /// <param name="releases">同 release-group 的全部候选。</param>
    /// <param name="localYear">本地目录名解析出的年份（如 "七里香 (2004)" → 2004）；null 表示无年份，跳过就近排序。</param>
    /// <param name="preferredCountry">自动推断出的偏好国家（ISO 3166-1 alpha-2）；
    /// 只给官方状态且地区匹配的候选加国家层，null 表示不启用国家加权。</param>
    public static IReadOnlyList<RankedRelease> ScoreAll(
        IReadOnlyList<ReleaseSummary> releases,
        int? localYear = null,
        string? preferredCountry = null)
    {
        var result = new List<RankedRelease>();
        if (releases is null || releases.Count == 0)
            return result;

        var barcodeCounts = CountBarcodes(releases);
        foreach (var release in releases)
        {
            result.Add(new RankedRelease(
                release,
                ScoreRelease(release, barcodeCounts),
                BuildRank(release, localYear, preferredCountry)));
        }

        result.Sort((a, b) => CompareRanked(a, b, DetermineTieBreakOrder(localYear)));

        return result;
    }

    /// <summary>
    /// 同一 Rank 层内的残余同分者比较顺序。
    /// 本地年份已知时年份层已按 <c>yearGap</c> 分过档,层内再按完整日期取首发原版;
    /// 本地年份未知时年份层整体缺席,只能让质量分主导、日期退化为稳定排序键。
    /// </summary>
    private enum TieBreakOrder
    {
        /// <summary>无本地年份:质量分优先,日期兜底。</summary>
        ScoreThenDate,

        /// <summary>有本地年份:日期优先(同 gap 内首发原版胜出),质量分兜底。</summary>
        DateThenScore
    }

    /// <summary>本地年份是否存在,决定同层比较先看日期还是先看质量分。</summary>
    private static TieBreakOrder DetermineTieBreakOrder(int? localYear)
    {
        return localYear.HasValue ? TieBreakOrder.DateThenScore : TieBreakOrder.ScoreThenDate;
    }

    private static int CompareRanked(
        RankedRelease a,
        RankedRelease b,
        TieBreakOrder order)
    {
        var cmp = a.Rank.CompareTo(b.Rank);
        if (cmp != 0)
            return cmp;

        cmp = CompareInRankTier(a, b, order);
        return cmp != 0 ? cmp : CompareById(a, b);
    }

    /// <summary>
    /// 同一 Rank 层内的比较。进入本方法的前置条件是 <c>a.Rank == b.Rank</c>,
    /// 即状态层、年份贴近层、国家层已全部相等,此处只决定残余同分者的顺序。
    /// </summary>
    private static int CompareInRankTier(RankedRelease a, RankedRelease b, TieBreakOrder order)
    {
        var scoreCmp = b.Score.CompareTo(a.Score);
        var dateCmp = CompareDates(a.Release.Date, b.Release.Date);

        // 两个分支共用同一短路模板,仅主次维度互换。
        return order == TieBreakOrder.DateThenScore
            ? (dateCmp != 0 ? dateCmp : scoreCmp)
            : (scoreCmp != 0 ? scoreCmp : dateCmp);
    }

    /// <summary>
    /// 比较发行日期。缺失/空白日期经 <see cref="JsonUtil.NormalizeDate"/> 归一到
    /// <see cref="JsonUtil.MissingDateSentinel"/>,与年份层"缺失即最远"的约定保持一致,一律排最后。
    /// </summary>
    private static int CompareDates(string? left, string? right)
    {
        return string.Compare(NormalizeDate(left), NormalizeDate(right), StringComparison.Ordinal);
    }

    private static int CompareById(RankedRelease a, RankedRelease b)
    {
        return string.Compare(
            a.Release.Id ?? string.Empty,
            b.Release.Id ?? string.Empty,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// 自动推断一个无感的国家偏好:在"官方实体版 + 有 barcode + 有效地区 + 音频碟布局与本地一致"的候选里,
    /// 取出现次数最多的国家(mode);次数打平时,取"该国基础分最高"的国家;再打平按国名稳定。
    /// 没有任何可匹配候选时返回 null(不启用加权,保持原行为)。
    /// </summary>
    /// <remarks>
    /// 推断结果只是 <see cref="BuildRank"/> 国家层的输入,返回 null 等价于不启用该层。
    /// 每一级排序键都必须完全确定,否则同一批候选可能在不同调用中得到不同国家,
    /// 导致同一专辑两次定位结果不一致(缓存 miss 后行为漂移)。
    /// </remarks>
    public static string? InferPreferredCountry(
        IReadOnlyList<ReleaseSummary> releases,
        IReadOnlyList<LocalDisc> localDiscs)
    {
        if (releases is null || releases.Count == 0 || localDiscs is null || localDiscs.Count == 0)
            return null;

        var barcodeCounts = CountBarcodes(releases);

        // 只统计官方实体版与有效地区候选:Worldwide/数字发行不应把国家偏好带到实体原版之前
        var compatible = releases
            .Where(r => IsCountryInferenceCandidate(r, localDiscs))
            .ToList();
        if (compatible.Count == 0)
            return null;

        // mode 优先,其次该国最高基础分,再按最早发行日与国家名稳定
        return compatible
            .GroupBy(r => r.Country ?? string.Empty)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .Select(g => new CountryMode(
                g.Key,
                g.Count(),
                g.Max(r => ScoreRelease(r, barcodeCounts)),
                g.Select(r => NormalizeDate(r.Date)).Min() ?? MissingDateSentinel))
            .OrderByDescending(m => m.Occurrences)
            .ThenByDescending(m => m.BestBaseScore)
            .ThenBy(m => m.EarliestDate, StringComparer.Ordinal)
            .ThenBy(m => m.Country, StringComparer.Ordinal)
            .Select(m => m.Country)
            .FirstOrDefault();
    }

    /// <summary>该 release 是否可用于国家偏好推断:官方状态 + 有条码 + 有效地区 + 实体介质 + 布局与本地一致。</summary>
    private static bool IsCountryInferenceCandidate(ReleaseSummary release, IReadOnlyList<LocalDisc> localDiscs)
    {
        return ReleaseStatusPolicy.IsOfficial(release.Status)
            && !string.IsNullOrWhiteSpace(release.Barcode)
            && IsMarketCountry(release.Country)
            && HasPhysicalMedia(release)
            && LayoutMatchesLocal(release, localDiscs);
    }

    /// <summary>按国家聚合后的 mode 统计,用于挑选偏好国家。</summary>
    private sealed record CountryMode(
        string Country,
        int Occurrences,
        int BestBaseScore,
        string EarliestDate);

    private static long BuildRank(
        ReleaseSummary release,
        int? localYear,
        string? preferredCountry)
    {
        // 分层编码:高层权重先占位,低层只在其层内步进,故必须先满足量级契约。
        var statusLayer = ReleaseStatusPolicy.SearchPriority(release.Status) * StatusRankBase;
        var yearLayer = localYear.HasValue
            ? ComputeYearGap(release.Date, localYear.Value) * YearGapRankBase
            : 0L;

        // 国家层是"惩罚式硬优先级"而非"可调软倾斜":只要契约成立,
        // 非偏好国的官方版就会被整体推到偏好国之后,偏好国内部再按日期/质量分决出。
        // 因此不要为了做成"轻微倾斜"而调小 CountryRankBase —— 只要它 < YearGapRankBase,
        // 效果就恒为硬墙;真正想软化需改成单独的比较维度,而不是改权重数值。
        var countryLayer = IsForeignOfficial(release, preferredCountry) && RankLayersAreIsolated
            ? CountryRankBase
            : 0L;

        return statusLayer + yearLayer + countryLayer;
    }

    /// <summary>release 年份与本地年份的距离;缺失或无法解析日期时按最大距离处理。</summary>
    private static int ComputeYearGap(string? releaseDate, int localYear)
    {
        var releaseYear = ParseLeadingYear(releaseDate);
        return releaseYear.HasValue
            ? Math.Min(Math.Abs(releaseYear.Value - localYear), MissingYearDistance)
            : MissingYearDistance;
    }

    /// <summary>
    /// 是否是"偏好国家之外的官方版本",即在国家层需要被推后的候选。
    /// </summary>
    /// <remarks>
    /// 这里刻意采用"惩罚非匹配"而非"奖励匹配"的写法:
    /// 若改为给偏好国加分,当候选里恰无偏好国时所有候选都得不到加分,国家层退化为恒等,
    /// 排序虽仍稳定但与"无国家偏好"路径行为分叉;
    /// 惩罚式则保证"无偏好国"与"不启用国家层"严格等价。非官方版本不参与国家加权,
    /// 避免把 Bootleg/Pseudo 因恰好在偏好国而抬高。
    /// </remarks>
    private static bool IsForeignOfficial(ReleaseSummary release, string? preferredCountry)
    {
        return !string.IsNullOrWhiteSpace(preferredCountry)
            && ReleaseStatusPolicy.IsOfficial(release.Status)
            && !string.Equals(release.Country, preferredCountry, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>release 的 media 布局是否与本地碟组完全一致(逐碟 track-count 相等)。</summary>
    private static bool LayoutMatchesLocal(ReleaseSummary release, IReadOnlyList<LocalDisc> localDiscs)
    {
        var audioMedia = release.Media
            .Where(m => !ReleaseLayoutMatcher.IsVideoMedia(m.Format))
            .ToList();
        if (audioMedia.Count != localDiscs.Count)
            return false;

        // 按 media.position 与本地碟(DiscNumber/轨数)排序后逐碟比对
        var sortedMedia = audioMedia.OrderBy(m => m.Position).ToList();
        var sortedLocal = localDiscs.OrderBy(d => d.DiscNumber ?? int.MaxValue).ToList();
        for (var i = 0; i < sortedLocal.Count; i++)
        {
            var trackCount = sortedMedia[i].TrackCount;
            if (trackCount <= 0 || trackCount != sortedLocal[i].TrackNumbers.Count)
                return false;
        }

        return true;
    }

    private static bool HasPhysicalMedia(ReleaseSummary release)
    {
        return release.Media.Any(m =>
            !string.Equals(m.Format, "Digital Media", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsMarketCountry(string? country)
    {
        return !string.IsNullOrWhiteSpace(country)
            && country.Length == 2
            && country[0] != 'X';
    }

    private static Dictionary<string, int> CountBarcodes(IEnumerable<ReleaseSummary> releases)
    {
        var barcodeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var release in releases)
        {
            if (string.IsNullOrWhiteSpace(release.Barcode))
                continue;

            barcodeCounts.TryGetValue(release.Barcode, out var count);
            barcodeCounts[release.Barcode] = count + 1;
        }

        return barcodeCounts;
    }

    private static int ScoreRelease(ReleaseSummary release, Dictionary<string, int> barcodeCounts)
    {
        int score = 0;
        score += ReleaseStatusPolicy.ScoreWeight(release.Status);

        if (!string.IsNullOrWhiteSpace(release.Barcode))
        {
            score += BarcodePresentWeight;
            if (barcodeCounts.TryGetValue(release.Barcode, out var count))
                score += Math.Min(count * BarcodeFrequencyPerOccurrence, BarcodeFrequencyMax);
        }

        if (!string.IsNullOrWhiteSpace(release.Date) && IsCompleteDate(release.Date))
            score += CompleteDateWeight;

        if (IsCdFormat(release))
            score += CdFormatWeight;

        if (string.Equals(release.Packaging, "Jewel Case", StringComparison.OrdinalIgnoreCase))
            score += JewelCaseWeight;

        return score;
    }

    private static bool IsCdFormat(ReleaseSummary release)
    {
        return release.Media.Any(m =>
            string.Equals(m.Format, "CD", StringComparison.OrdinalIgnoreCase));
    }
}
