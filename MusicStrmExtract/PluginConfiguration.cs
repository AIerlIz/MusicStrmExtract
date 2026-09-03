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

        /// <summary>是否启用在线元数据补全(MusicBrainz)。</summary>
        public bool EnableOnlineMetadata { get; set; } = true;

        /// <summary>MusicBrainz 端点;留空=官方 https://musicbrainz.org(华语网络下官方源间歇 503,建议填镜像/socks 前置,如 https://musicbrainz.emby.tv)。</summary>
        public string MusicBrainzBaseUrl { get; set; } = string.Empty;
    }
}
