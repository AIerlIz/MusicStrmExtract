using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MusicStrmExtract.Metadata
{
    /// <summary>
    /// 合并策略:在线优先(OnlineFirst)。
    ///   * 任何 MusicBrainz 在线命中(MBID 精确 / 文本唯一高置信 / 模糊多候选 best)→ 在线字段覆盖内嵌重叠字段;
    ///   * 在线缺失的字段回填内嵌(MusicBrainz ID 除外:宁缺毋滥,防内嵌脏 ID 污染);
    ///   * 在线无结果(含 MB 不可达)→ 内嵌兜底(无 iTunes 兜底,需自备 MB 连通)。
    /// 说明:自建/用户填写的内嵌标签可能有误,信任在线数据源(用户约定)。
    /// </summary>
    public static class MergePolicy
    {
        /// <summary>标题比较用的宽松归一: 小写、去括号内容、去空白与常见标点。</summary>
        public static string NormalizeTitle(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var withoutParen = Regex.Replace(value, @"\s*[\(\[（【][^\)\]）】]*[\)\]）】]", " ");
            var lowered = withoutParen.ToLowerInvariant();
            var chars = lowered.Where(c => char.IsLetterOrDigit(c)).ToArray();
            return new string(chars);
        }

        /// <summary>
        /// 合并内嵌与在线结果,产出最终字段。返回是否发生"在线覆盖"以及说明。
        /// 策略:在线优先(OnlineFirst)—— 任何 MusicBrainz 在线命中(含模糊多候选的 best 候选)
        /// 都以在线字段覆盖内嵌重叠字段;仅在线缺失的字段回填内嵌。在线无结果时保留内嵌。
        /// </summary>
        public static (TrackMetadata Final, bool AppliedOnline, OnlineMatchKind Kind, string Note) Merge(
            TrackMetadata embedded,
            OnlineMetadata? online)
        {
            if (online is null || online.Kind == OnlineMatchKind.None)
            {
                return (embedded, false, OnlineMatchKind.None, "无在线结果,使用内嵌字段");
            }

            var final = CopyFrom(online.Fields);
            // 在线优先:有在线值即覆盖内嵌;在线缺失的字段用内嵌兜底
            ApplyFallback(final, embedded);

            var note = online.Kind switch
            {
                OnlineMatchKind.ExactByMbid =>
                    $"{online.Source}: {online.Note ?? "MBID 精确命中"} -> 在线字段覆盖内嵌",
                OnlineMatchKind.UniqueTextMatch =>
                    $"{online.Source}: {online.Note ?? "文本唯一高置信"} -> 在线字段覆盖内嵌",
                OnlineMatchKind.AmbiguousTextMatch =>
                    $"{online.Source}: {online.Note ?? "多候选模糊"} -> 采信 best 候选覆盖内嵌(在线优先)",
                _ => $"{online.Source}: 未知匹配档位,按在线优先覆盖"
            };

            return (final, true, online.Kind, note);
        }

        private static TrackMetadata CopyFrom(TrackMetadata source)
        {
            var result = new TrackMetadata
            {
                Title = source.Title,
                Album = source.Album,
                Year = source.Year,
                IndexNumber = source.IndexNumber,
                ParentIndexNumber = source.ParentIndexNumber,
                MusicBrainzTrackId = source.MusicBrainzTrackId,
                MusicBrainzAlbumId = source.MusicBrainzAlbumId,
                MusicBrainzArtistId = source.MusicBrainzArtistId,
                MusicBrainzAlbumArtistId = source.MusicBrainzAlbumArtistId,
                MusicBrainzReleaseGroupId = source.MusicBrainzReleaseGroupId
            };
            result.Artists.AddRange(source.Artists);
            result.AlbumArtists.AddRange(source.AlbumArtists);
            result.Genres.AddRange(source.Genres);
            result.Composers.AddRange(source.Composers);
            return result;
        }

        private static void ApplyFallback(TrackMetadata target, TrackMetadata fallback)
        {
            if (string.IsNullOrWhiteSpace(target.Title) && !string.IsNullOrWhiteSpace(fallback.Title))
            {
                target.Title = fallback.Title;
            }

            if (string.IsNullOrWhiteSpace(target.Album) && !string.IsNullOrWhiteSpace(fallback.Album))
            {
                target.Album = fallback.Album;
            }

            if (target.Year is null && fallback.Year is not null)
            {
                target.Year = fallback.Year;
            }

            if (target.IndexNumber is null && fallback.IndexNumber is not null)
            {
                target.IndexNumber = fallback.IndexNumber;
            }

            if (target.ParentIndexNumber is null && fallback.ParentIndexNumber is not null)
            {
                target.ParentIndexNumber = fallback.ParentIndexNumber;
            }

            if (target.Artists.Count == 0)
            {
                target.Artists.AddRange(fallback.Artists);
            }

            if (target.AlbumArtists.Count == 0)
            {
                target.AlbumArtists.AddRange(fallback.AlbumArtists);
            }

            if (target.Genres.Count == 0)
            {
                target.Genres.AddRange(fallback.Genres);
            }

            // 不回填内嵌 MusicBrainz ID:在线命中时以在线 ID 为准,在线缺失就缺着(宁缺毋滥)。
            // 实测样本库内嵌 track/releasegroup MBID 整专辑复用同一脏值,回填会把脏 ID 写进 ProviderIds。
        }
    }
}
