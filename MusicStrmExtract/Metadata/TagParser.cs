using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace MusicStrmExtract.Metadata
{
    /// <summary>
    /// 把 ffprobe 输出的标签字典(format.tags / stream.tags)解析为 <see cref="TrackMetadata"/>。
    /// 键大小写不敏感、忽略 _/-/ 空格差异,并兼容 VorbisComment / ID3v2 / MP4 的常见键名。
    /// </summary>
    public static class TagParser
    {
        /// <summary>把标签键归一化: 大写并移除 _、-、空格(如 "MusicBrainz Track Id" → "MUSICBRAINZTRACKID")。</summary>
        public static string NormalizeKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            var chars = key.Where(c => c != '_' && c != '-' && c != ' ' && c != '.').ToArray();
            return new string(chars).ToUpperInvariant();
        }

        public static TrackMetadata Parse(IReadOnlyDictionary<string, string> tags)
        {
            var result = new TrackMetadata();
            if (tags is null || tags.Count == 0)
            {
                return result;
            }

            var norm = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in tags)
            {
                var key = NormalizeKey(kv.Key);
                var value = (kv.Value ?? string.Empty).Trim();
                if (key.Length > 0 && value.Length > 0 && !norm.ContainsKey(key))
                {
                    norm[key] = value;
                }
            }

            result.Title = First(norm, "TITLE", "SONGTITLE", "NAME");
            result.Album = First(norm, "ALBUM", "ALBUMNAME");
            result.Artists.AddRange(SplitArtists(First(norm, "ARTIST", "ARTISTS", "PERFORMER", "CREATOR")));
            result.AlbumArtists.AddRange(SplitArtists(First(norm, "ALBUMARTIST", "ALBUMARTISTS", "ALBUM ARTIST")));
            result.Genres.AddRange(SplitList(First(norm, "GENRE", "GENRES", "STYLE")));
            result.Composers.AddRange(SplitArtists(First(norm, "COMPOSER", "COMPOSERS", "WRITER")));

            var yearText = First(norm, "YEAR", "DATE", "ORIGINALDATE", "RECORDINGDATE", "RELEASEDATE");
            if (!string.IsNullOrEmpty(yearText))
            {
                var m = Regex.Match(yearText, @"\b(1[89]\d{2}|20\d{2})\b");
                if (m.Success)
                {
                    result.Year = int.Parse(m.Value, CultureInfo.InvariantCulture);
                }
            }

            result.IndexNumber = ParseLeadingNumber(First(norm, "TRACK", "TRACKNUMBER", "TRACKNO"));
            result.ParentIndexNumber = ParseLeadingNumber(First(norm, "DISC", "DISCNUMBER", "DISCNO"));

            // MusicBrainz ID(模糊匹配,注意 ALBUMARTIST 先于 ARTIST 匹配)
            result.MusicBrainzTrackId = FirstContains(norm, "MUSICBRAINZTRACKID");
            result.MusicBrainzAlbumId = FirstContains(norm, "MUSICBRAINZALBUMID");
            result.MusicBrainzAlbumArtistId = FirstContains(norm, "MUSICBRAINZALBUMARTISTID");
            result.MusicBrainzArtistId = FirstContains(norm, "MUSICBRAINZARTISTID");
            result.MusicBrainzReleaseGroupId = FirstContains(norm, "MUSICBRAINZRELEASEGROUPID");

            return result;
        }

        private static string? First(Dictionary<string, string> norm, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (norm.TryGetValue(key, out var value))
                {
                    return value;
                }
            }

            return null;
        }

        private static string? FirstContains(Dictionary<string, string> norm, string fragment)
        {
            foreach (var kv in norm)
            {
                if (kv.Key.Contains(fragment, StringComparison.Ordinal))
                {
                    return kv.Value;
                }
            }

            return null;
        }

        private static IEnumerable<string> SplitArtists(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Enumerable.Empty<string>();
            }

            // 常见分隔: "A / B"、"A; B"、"A,B"、feat./ft. 不作拆分
            return SplitList(value);
        }

        private static IEnumerable<string> SplitList(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Enumerable.Empty<string>();
            }

            return value
                .Split(new[] { " / ", " /", "/ ", ";", ", " }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim().Trim('\u200b'))
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static int? ParseLeadingNumber(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var m = Regex.Match(value.Trim(), @"^\d+");
            if (m.Success && int.TryParse(m.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                return number;
            }

            return null;
        }
    }
}
