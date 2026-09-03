using System;
using System.Collections.Generic;
using System.Linq;

namespace MusicStrmExtract.Metadata
{
    /// <summary>
    /// 归一化的音轨元数据(来自内嵌标签或在线来源,合并前统一形态)。
    /// </summary>
    public sealed class TrackMetadata
    {
        public string? Title { get; set; }

        public List<string> Artists { get; } = new List<string>();

        public List<string> AlbumArtists { get; } = new List<string>();

        public string? Album { get; set; }

        public List<string> Genres { get; } = new List<string>();

        public List<string> Composers { get; } = new List<string>();

        public int? Year { get; set; }

        public int? IndexNumber { get; set; }

        public int? ParentIndexNumber { get; set; }

        public string? MusicBrainzTrackId { get; set; }

        public string? MusicBrainzAlbumId { get; set; }

        public string? MusicBrainzArtistId { get; set; }

        public string? MusicBrainzAlbumArtistId { get; set; }

        public string? MusicBrainzReleaseGroupId { get; set; }

        /// <summary>文本类字段是否有任何内容(判断是否"可识别")。</summary>
        public bool HasAnyText =>
            !string.IsNullOrWhiteSpace(Title)
            || !string.IsNullOrWhiteSpace(Album)
            || Artists.Count > 0
            || AlbumArtists.Count > 0;

        /// <summary>是否有任一 MusicBrainz ID。</summary>
        public bool HasAnyMbid =>
            !string.IsNullOrWhiteSpace(MusicBrainzTrackId)
            || !string.IsNullOrWhiteSpace(MusicBrainzAlbumId)
            || !string.IsNullOrWhiteSpace(MusicBrainzArtistId)
            || !string.IsNullOrWhiteSpace(MusicBrainzAlbumArtistId)
            || !string.IsNullOrWhiteSpace(MusicBrainzReleaseGroupId);

        public bool IsEmpty =>
            !HasAnyText && !HasAnyMbid && Year is null && IndexNumber is null;
    }
}
