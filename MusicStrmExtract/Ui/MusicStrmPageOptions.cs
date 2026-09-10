using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Common;
using Emby.Web.GenericEdit.Elements;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace MusicStrmExtract.Ui;

/// <summary>完整 Plugin UI 的页面数据。按钮与结果提示只存在于 UI 层,不写入持久化配置。</summary>
public sealed class MusicStrmPageOptions : EditableOptionsBase
{
    public const string RepairCommand = "RepairLegacyAlbumRelations";

    public MusicStrmPageOptions()
    {
        MusicBrainzCaption = new CaptionItem("MusicBrainz");
        MaintenanceCaption = new CaptionItem("维护");
        SectionSpacer = new SpacerItem(SpacerSize.Medium)
        {
            CanHideInCompactView = true
        };

        RepairButton = new ButtonItem("修复旧库专辑关系")
        {
            CommandId = RepairCommand,
            ConfirmationPrompt = "将清理无引用的陈旧专辑，并刷新相关 .strm。是否继续？",
            StandardIcon = StandardIcons.Edit
        };

        ResultLabel = new LabelItem("尚未运行。");
    }

    public override string EditorTitle => "Music Strm Extract 设置";

    public override string EditorDescription => "为 .strm 音乐补全 MusicBrainz 元数据。";

    public CaptionItem MusicBrainzCaption { get; set; }

    [DisplayName("服务地址")]
    [Description("留空使用官方服务，也可填写镜像地址。")]
    [SuppressMessage(
        "Design",
        "CA1056:URI properties should not be strings",
        Justification = "Emby's editable configuration model serializes this setting as a string.")]
    public string MusicBrainzBaseUrl { get; set; } = string.Empty;

    public SpacerItem SectionSpacer { get; set; }

    public CaptionItem MaintenanceCaption { get; set; }

    public ButtonItem RepairButton { get; set; }

    public LabelItem ResultLabel { get; set; }

    internal static MusicStrmPageOptions From(PluginConfiguration config)
    {
        return new MusicStrmPageOptions
        {
            MusicBrainzBaseUrl = config.MusicBrainzBaseUrl ?? string.Empty
        };
    }

    internal void ApplyTo(PluginConfiguration config)
    {
        config.MusicBrainzBaseUrl = MusicBrainzBaseUrl;
    }
}
