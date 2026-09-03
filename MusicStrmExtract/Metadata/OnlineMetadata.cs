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

        /// <summary>文本搜索存在多个候选或置信不足;在线优先策略下采信 best 候选覆盖内嵌(日志标注模糊)。</summary>
        AmbiguousTextMatch
    }

    /// <summary>
    /// 在线来源解析出的候选元数据。
    /// </summary>
    public sealed class OnlineMetadata
    {
        /// <summary>在线侧可用的文本/数值字段。</summary>
        public TrackMetadata Fields { get; } = new TrackMetadata();

        public OnlineMatchKind Kind { get; set; } = OnlineMatchKind.None;

        /// <summary>来源名称(MusicBrainz / 内嵌)。</summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>命中说明(供日志/人工排查)。</summary>
        public string? Note { get; set; }

        /// <summary>本次解析因 MusicBrainz 网络不可达/熔断而未完成(与"确认无结果"不同);
        /// 客户端应据此决定是否保留内嵌 MBID(网络抖动不应丢失 ID)。</summary>
        public bool MusicBrainzUnavailable { get; set; }
    }
}
