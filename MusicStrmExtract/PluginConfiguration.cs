using System.ComponentModel;

using Emby.Web.GenericEdit;

namespace MusicStrmExtract
{
    public class PluginConfiguration : EditableOptionsBase
    {
        public override string EditorTitle => "Music Strm Extract 设置";

        public override string EditorDescription =>
            "按歌手与专辑目录结构从 MusicBrainz 锁定专辑，并按轨号补全 .strm 元数据。";

        [DisplayName("MusicBrainz 服务地址")]
        [Description("留空使用官方 https://musicbrainz.org；官方不稳定时可填写镜像，例如 https://musicbrainz.emby.tv。")]
        public string MusicBrainzBaseUrl { get; set; } = string.Empty;
    }
}
