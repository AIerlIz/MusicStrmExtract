using System.ComponentModel;

using Emby.Web.GenericEdit;

namespace MusicStrmExtract
{
    /// <summary>
    /// 插件配置。Emby 根据该类型自动生成插件设置页。
    /// </summary>
    public class PluginConfiguration : EditableOptionsBase
    {
        public override string EditorTitle => "Music Strm Extract 设置";

        public override string EditorDescription =>
            "按歌手与专辑目录结构从 MusicBrainz 锁定专辑，并按轨号补全 .strm 元数据。";

        /// <summary>MusicBrainz 端点;留空=官方 https://musicbrainz.org(华语网络下官方源间歇 503,建议填镜像/socks 前置,如 https://musicbrainz.emby.tv)。</summary>
        [DisplayName("MusicBrainz 服务地址")]
        [Description("留空使用官方 https://musicbrainz.org；官方不稳定时可填写镜像，例如 https://musicbrainz.emby.tv。")]
        public string MusicBrainzBaseUrl { get; set; } = string.Empty;
    }
}
