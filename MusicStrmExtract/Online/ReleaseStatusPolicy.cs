using System;

namespace MusicStrmExtract.Online
{
    internal enum ReleaseStatusTier
    {
        Official,
        PromotionalOrUnknown,
        BootlegOrWithdrawn,
        PseudoRelease
    }

    /// <summary>
    /// MusicBrainz release status 的单一分类入口。
    /// 搜索排序与 RG 评分共用同一组状态归类，避免两处字符串判断漂移。
    /// </summary>
    internal static class ReleaseStatusPolicy
    {
        public static ReleaseStatusTier Classify(string? status)
        {
            if (string.Equals(status, "Official", StringComparison.OrdinalIgnoreCase))
            {
                return ReleaseStatusTier.Official;
            }

            if (string.Equals(status, "Pseudo-Release", StringComparison.OrdinalIgnoreCase))
            {
                return ReleaseStatusTier.PseudoRelease;
            }

            if (string.Equals(status, "Bootleg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "Withdrawn", StringComparison.OrdinalIgnoreCase))
            {
                return ReleaseStatusTier.BootlegOrWithdrawn;
            }

            return ReleaseStatusTier.PromotionalOrUnknown;
        }

        /// <summary>搜索候选稳定排序中的状态优先级；越小越靠前。</summary>
        public static int SearchPriority(string? status)
        {
            return Classify(status) switch
            {
                ReleaseStatusTier.Official => 0,
                ReleaseStatusTier.BootlegOrWithdrawn => 2,
                ReleaseStatusTier.PseudoRelease => 3,
                _ => 1
            };
        }

        /// <summary>RG 评分中的状态加权；Official 为正，Bootleg/Withdrawn 为负，Pseudo 轻微负。</summary>
        public static int ScoreWeight(string? status)
        {
            return Classify(status) switch
            {
                ReleaseStatusTier.Official => 40,
                ReleaseStatusTier.BootlegOrWithdrawn => -40,
                ReleaseStatusTier.PseudoRelease => -10,
                _ => 0
            };
        }

        public static bool IsOfficial(string? status)
        {
            return Classify(status) == ReleaseStatusTier.Official;
        }
    }
}
