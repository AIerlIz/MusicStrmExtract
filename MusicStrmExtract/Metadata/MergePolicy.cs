using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MusicStrmExtract.Metadata
{
    /// <summary>
    /// 合并策略(按用户约定分档):
    ///   * 可信命中(MBID 精确 / 文本唯一高置信)→ 在线字段覆盖内嵌字段;
    ///   * 模糊命中(多候选不确定)→ 不覆盖,保留内嵌并记录;
    ///   * 在线无结果 → 内嵌兜底。
    /// iTunes 兜底仅补专辑侧字段与封面,绝不覆盖标题(其返回罗马音译标题,会损害中文曲名)。
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
        /// </summary>
        public static (TrackMetadata Final, bool AppliedOnline, OnlineMatchKind Kind, string Note) Merge(
            TrackMetadata embedded,
            OnlineMetadata? online)
        {
            if (online is null || online.Kind == OnlineMatchKind.None)
            {
                return (embedded, false, OnlineMatchKind.None, "无在线结果,使用内嵌字段");
            }

            if (online.Kind == OnlineMatchKind.ExactByMbid || online.Kind == OnlineMatchKind.UniqueTextMatch)
            {
                var final = CopyFrom(online.Fields);
                // 在线结果缺失的字段用内嵌兜底
                ApplyFallback(final, embedded);
                return (final, true, online.Kind,
                    $"{online.Source}: {online.Note ?? "高可信命中"} -> 在线字段覆盖内嵌");
            }

            if (online.Kind == OnlineMatchKind.AmbiguousTextMatch)
            {
                return (embedded, false, online.Kind,
                    $"{online.Source}: {online.Note ?? "多候选不确定"} -> 保留内嵌字段,待人工确认");
            }

            // ITunesFallback: 仅用在线结果补"缺失"字段,已有内嵌值不覆盖(标题永不来自 iTunes)
            if (online.Kind == OnlineMatchKind.ITunesFallback)
            {
                var final = CopyFrom(embedded);
                if (string.IsNullOrWhiteSpace(final.Album) && !string.IsNullOrWhiteSpace(online.Fields.Album))
                {
                    final.Album = online.Fields.Album;
                }

                if (final.Year is null && online.Fields.Year is not null)
                {
                    final.Year = online.Fields.Year;
                }

                return (final, false, online.Kind, $"{online.Source}: iTunes 兜底仅补缺失字段(专辑/年份),标题保持内嵌");
            }

            return (embedded, false, online.Kind, "未知匹配档位,保留内嵌字段");
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
                MusicBrainzReleaseGroupId = source.MusicBrainzReleaseGroupId,
                HasEmbeddedCover = source.HasEmbeddedCover
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

            if (string.IsNullOrWhiteSpace(target.MusicBrainzTrackId) && !string.IsNullOrWhiteSpace(fallback.MusicBrainzTrackId))
            {
                target.MusicBrainzTrackId = fallback.MusicBrainzTrackId;
            }

            if (string.IsNullOrWhiteSpace(target.MusicBrainzAlbumId) && !string.IsNullOrWhiteSpace(fallback.MusicBrainzAlbumId))
            {
                target.MusicBrainzAlbumId = fallback.MusicBrainzAlbumId;
            }

            if (string.IsNullOrWhiteSpace(target.MusicBrainzAlbumArtistId) && !string.IsNullOrWhiteSpace(fallback.MusicBrainzAlbumArtistId))
            {
                target.MusicBrainzAlbumArtistId = fallback.MusicBrainzAlbumArtistId;
            }

            if (string.IsNullOrWhiteSpace(target.MusicBrainzArtistId) && !string.IsNullOrWhiteSpace(fallback.MusicBrainzArtistId))
            {
                target.MusicBrainzArtistId = fallback.MusicBrainzArtistId;
            }

            if (string.IsNullOrWhiteSpace(target.MusicBrainzReleaseGroupId) && !string.IsNullOrWhiteSpace(fallback.MusicBrainzReleaseGroupId))
            {
                target.MusicBrainzReleaseGroupId = fallback.MusicBrainzReleaseGroupId;
            }
        }
    }
}
