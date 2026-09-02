using System;

namespace MusicStrmExtract.Metadata
{
    /// <summary>在线匹配的可信度分档。</summary>
    public enum OnlineMatchKind
    {
        /// <summary>无在线结果,仅内嵌字段。</summary>
        None,

        /// <summary>内嵌标签带 MBID 且精确取回(高可信)。</summary>
        ExactByMbid,

        /// <summary>文本搜索唯一高置信命中。</summary>
        UniqueTextMatch,

        /// <summary>文本搜索存在多个候选或置信不足,不覆盖内嵌字段。</summary>
        AmbiguousTextMatch,

        /// <summary>MusicBrainz 不可用/无结果,由 iTunes 兜底(仅补专辑侧与封面)。</summary>
        ITunesFallback
    }

    /// <summary>
    /// 在线来源解析出的候选元数据。
    /// </summary>
    public sealed class OnlineMetadata
    {
        /// <summary>在线侧可用的文本/数值字段。</summary>
        public TrackMetadata Fields { get; } = new TrackMetadata();

        public OnlineMatchKind Kind { get; set; } = OnlineMatchKind.None;

        /// <summary>来源名称(MusicBrainz / iTunes / 内嵌)。</summary>
        public string Source { get; set; } = string.Empty;

        public string? RecordingMbid { get; set; }

        public string? ReleaseMbid { get; set; }

        public string? ReleaseGroupMbid { get; set; }

        public string? ArtistMbid { get; set; }

        public string? AlbumArtistMbid { get; set; }

        /// <summary>封面图 URL(如有)。</summary>
        public string? CoverArtUrl { get; set; }

        /// <summary>命中说明(供日志/人工排查)。</summary>
        public string? Note { get; set; }
    }
}
