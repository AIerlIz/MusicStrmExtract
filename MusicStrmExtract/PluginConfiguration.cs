using MediaBrowser.Model.Plugins;

namespace MusicStrmExtract
{
    /// <summary>
    /// 插件配置(配置页保存到 config/plugins/MusicStrmExtract.xml)。
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>ffprobe 可执行文件完整路径;留空则自动在 Emby system 目录及 PATH 中查找。</summary>
        public string FfprobePath { get; set; } = string.Empty;

        /// <summary>探测远程目标时附加的 HTTP 头,每行一个 "Header: value"(用于防盗链 UA/Referer 等)。</summary>
        public string ExtraHeaders { get; set; } = string.Empty;

        /// <summary>单次远程探测超时(秒)。</summary>
        public int ProbeTimeoutSeconds { get; set; } = 30;

        /// <summary>是否启用在线元数据补全(MusicBrainz → iTunes 兜底)。</summary>
        public bool EnableOnlineMetadata { get; set; } = true;

        /// <summary>文本搜索仅匹配"唯一命中"时才覆盖内嵌字段;模糊命中保留内嵌并记日志。</summary>
        public bool RequireExactOnlineMatch { get; set; } = true;

        /// <summary>是否把内嵌/在线封面写到 strm 所在目录(Emby 音乐库封面惯例)。</summary>
        public bool WriteAlbumCover { get; set; } = true;

        /// <summary>处理范围:true=仅 CollectionType==music 的库。</summary>
        public bool MusicLibrariesOnly { get; set; } = true;

        /// <summary>是否把解析/在线合并后的元数据写回 Emby 条目(含 ProviderIds)。</summary>
        public bool WriteBack { get; set; } = true;

        /// <summary>调试:单次任务最多处理的条目数,0=不限制。</summary>
        public int MaxItemsToProcess { get; set; } = 0;

        /// <summary>调试:输出探测到的原始标签到日志。</summary>
        public bool LogProbeDetails { get; set; } = false;
    }
}
