using static MusicStrmExtract.Online.JsonUtil;

namespace MusicStrmExtract.Online;

/// <summary>
/// MusicBrainz 搜索候选的稳定排序策略,只用于"选哪个候选作为 release-group 锚点"。
/// 排序维度必须保持:状态 → Album 主类型 → 完整日期 → 日期 → score → 标题 → release id。
/// </summary>
/// <remarks>
/// 这里刻意不做国家偏好:本阶段回答的是"哪张专辑是你要的",候选可能分属不同专辑;
/// 国家偏好的语义是"同一张专辑下选哪个发行版",只在 <see cref="ReleaseGroupScorer"/> 的
/// 组内评分阶段才有意义,不要在搜索阶段重新引入。
/// </remarks>
internal static class SearchCandidateOrderingPolicy
{
    public static List<ScoredRelease> Order(IReadOnlyList<ScoredRelease> scored)
    {
        return scored
            .OrderBy(s => ReleaseStatusPolicy.SearchPriority(s.Release.Status))
            .ThenBy(s => string.Equals(s.Release.PrimaryType, "Album", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(s => IsIncompleteDate(s.Release.Date) ? 1 : 0)
            .ThenBy(s => NormalizeDate(s.Release.Date), StringComparer.Ordinal)
            .ThenByDescending(s => s.Score)
            .ThenBy(s => s.Release.Title!, StringComparer.Ordinal)
            .ThenBy(s => s.Release.Id ?? string.Empty, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsIncompleteDate(string? date)
    {
        return !IsCompleteDate(date);
    }
}
