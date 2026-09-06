using System.ComponentModel;

using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Elements;

namespace MusicStrmExtract.Ui
{
    /// <summary>完整 Plugin UI 的页面数据。按钮与结果提示只存在于 UI 层,不写入持久化配置。</summary>
    public sealed class MusicStrmPageOptions : EditableOptionsBase
    {
        public const string RepairCommand = "RepairStaleAlbums";

        public MusicStrmPageOptions()
        {
            RepairButton = new ButtonItem("运行旧库修复")
            {
                CommandId = RepairCommand,
                ConfirmationPrompt = "将删除未被任何 Audio 引用且缺少 MusicBrainzAlbum 的 MusicAlbum，并刷新相关 .strm。是否继续？"
            };

            ResultLabel = new LabelItem("尚未运行修复。");
        }

        public override string EditorTitle => "Music Strm Extract 设置";

        public override string EditorDescription => "配置 MusicBrainz/Cover Art 地址,并运行旧库修复。";

        [DisplayName("MusicBrainz 服务地址")]
        [Description("留空使用官方 https://musicbrainz.org；官方不稳定时可填写镜像，例如 https://musicbrainz.emby.tv。")]
        public string MusicBrainzBaseUrl { get; set; } = string.Empty;

        [DisplayName("Cover Art Archive 服务地址")]
        [Description("留空使用官方 https://coverartarchive.org/release/；网络受限时可填写镜像地址（需以 / 或 /release 结尾）。")]
        public string CoverArtBaseUrl { get; set; } = string.Empty;

        public ButtonItem RepairButton { get; set; }

        public LabelItem ResultLabel { get; set; }
    }
}
