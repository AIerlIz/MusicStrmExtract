using static MusicStrmExtract.Online.JsonUtil;

namespace MusicStrmExtract.Online;

/// <summary>
/// MusicBrainz 搜索候选的稳定排序策略。
/// 排序维度必须保持:状态 → Album 主类型 → 完整日期 → 日期 → 国家偏好 → score → 标题 → release id。
/// </summary>
internal static class SearchCandidateOrderingPolicy
{
    public static List<ScoredRelease> Order(
        IReadOnlyList<ScoredRelease> scored,
        string? preferredCountry)
    {
        return scored
            .OrderBy(s => ReleaseStatusPolicy.SearchPriority(s.Release.Status))
            .ThenBy(s => string.Equals(s.Release.PrimaryType, "Album", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(s => IsIncompleteDate(s.Release.Date) ? 1 : 0)
            .ThenBy(s => string.IsNullOrWhiteSpace(s.Release.Date) ? "9999" : s.Release.Date!)
            .ThenBy(s => string.Equals(s.Release.Country, preferredCountry, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
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
