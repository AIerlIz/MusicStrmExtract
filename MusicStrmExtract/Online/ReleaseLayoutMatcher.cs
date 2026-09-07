using System.Collections.Generic;
using System.Linq;

namespace MusicStrmExtract.Online
{
    /// <summary>把本地碟组映射到 release media，并判断布局是否完全命中。</summary>
    internal static class ReleaseLayoutMatcher
    {
        /// <summary>
        /// 把本地碟组映射到 release 的 media:
        ///   带碟号的组按 media.Position 一一对应并校验轨号覆盖,失败即整张未命中;
        ///   无碟号的组在剩余 media 中取 Position 最小且覆盖轨号者(单碟保持原行为)。
        /// 任一碟组无法映射时返回 null,避免产生半对半错的专辑。
        /// </summary>
        public static Dictionary<LocalDisc, ReleaseMedia>? MapLocalDiscsToMedias(
            IReadOnlyList<LocalDisc> localDiscs,
            IReadOnlyList<ReleaseMedia> medias)
        {
            if (localDiscs is null || localDiscs.Count == 0 || medias is null || medias.Count == 0)
            {
                return null;
            }

            var explicitGroups = localDiscs
                .Where(d => d.DiscNumber is > 0)
                .OrderBy(d => d.DiscNumber!.Value)
                .ToList();
            var implicitGroups = localDiscs
                .Where(d => d.DiscNumber is not > 0)
                .ToList();

            var usedPositions = new HashSet<int>();
            var map = new Dictionary<LocalDisc, ReleaseMedia>();

            foreach (var group in explicitGroups)
            {
                var media = medias.FirstOrDefault(m => m.Position == group.DiscNumber!.Value);
                if (media is null || !usedPositions.Add(media.Position) || !Covers(media, group.TrackNumbers))
                {
                    return null;
                }

                map.Add(group, media);
            }

            var remaining = medias
                .Where(m => !usedPositions.Contains(m.Position))
                .OrderBy(m => m.Position)
                .ToList();
            foreach (var group in implicitGroups.OrderByDescending(g => g.TrackNumbers.Count))
            {
                var media = remaining.FirstOrDefault(m => Covers(m, group.TrackNumbers));
                if (media is null)
                {
                    return null;
                }

                remaining.Remove(media);
                map.Add(group, media);
            }

            return map;
        }

        /// <summary>本地碟组与 release media 的轨数是否逐碟完全一致(用于优先标准版/普通版)。</summary>
        public static bool HasExactTrackCount(
            IReadOnlyList<LocalDisc> localDiscs,
            IReadOnlyDictionary<LocalDisc, ReleaseMedia> mapping)
        {
            foreach (var pair in mapping)
            {
                if (pair.Key.TrackNumbers.Count != pair.Value.Tracks.Count)
                {
                    return false;
                }
            }

            return localDiscs.Count > 0;
        }

        private static bool Covers(ReleaseMedia media, IReadOnlyCollection<int> trackNumbers)
        {
            var mediaNumbers = media.Tracks
                .Select(t => t.Number)
                .Where(n => n > 0)
                .ToHashSet();
            return trackNumbers.Where(n => n > 0).All(mediaNumbers.Contains);
        }
    }
}
