using Emby.Web.GenericEdit.Elements;

namespace MusicStrmExtract.Ui;

/// <summary>完整 Plugin UI 的页面数据。按钮与结果提示只存在于 UI 层,不写入持久化配置。</summary>
public sealed class MusicStrmPageOptions : PluginConfiguration
{
    public const string RepairCommand = "RepairLegacyAlbumRelations";
    public const string ClearCacheCommand = "ClearPluginCache";
    public const string CheckSourceCommand = "CheckMusicBrainzSource";
    public const string RefreshDiagnosticsCommand = "RefreshResolutionDiagnostics";

    public MusicStrmPageOptions()
    {
        RepairButton = new ButtonItem("修复旧库专辑关系")
        {
            CommandId = RepairCommand,
            ConfirmationPrompt = "将删除无路径、无 MusicBrainzAlbum 且未被 Audio 引用的 MusicAlbum，并刷新缺失或陈旧专辑关联的 .strm。是否继续？"
        };

        ResultLabel = new LabelItem("尚未运行旧库专辑关系修复。");
        ClearCacheButton = new ButtonItem("清除插件缓存")
        {
            CommandId = ClearCacheCommand
        };
        CacheStatusLabel = new LabelItem("缓存状态尚未读取。");
        CheckSourceButton = new ButtonItem("测试 MusicBrainz 连接")
        {
            CommandId = CheckSourceCommand
        };
        SourceStatusLabel = new LabelItem("尚未检查 MusicBrainz 连接。");
        RefreshDiagnosticsButton = new ButtonItem("刷新定位诊断")
        {
            CommandId = RefreshDiagnosticsCommand
        };
        DiagnosticsLabel = new LabelItem("暂无定位记录。");
    }

    public override string EditorTitle => "Music Strm Extract 设置";

    public override string EditorDescription => "配置 MusicBrainz 服务地址，并修复旧版本残留的专辑关系。";

    public ButtonItem RepairButton { get; set; }

    public LabelItem ResultLabel { get; set; }

    public ButtonItem ClearCacheButton { get; set; }

    public LabelItem CacheStatusLabel { get; set; }

    public ButtonItem CheckSourceButton { get; set; }

    public LabelItem SourceStatusLabel { get; set; }

    public ButtonItem RefreshDiagnosticsButton { get; set; }

    public LabelItem DiagnosticsLabel { get; set; }

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
