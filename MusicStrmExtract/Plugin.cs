using MediaBrowser.Common;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Plugins.UI;
using MusicStrmExtract.Ui;
using System.Xml;
using System.Xml.Linq;

namespace MusicStrmExtract;

/// <summary>
/// Emby 服务端插件:为音乐库中的 .strm 音频条目补全元数据。
/// 按歌手/专辑目录结构与文件名轨号锁定 MusicBrainz 专辑并补全元数据。
/// </summary>
public class Plugin : BasePluginSimpleUI<PluginConfiguration>, IHasThumbImage, IHasUIPages
{
    public const string PluginGuid = "6a2f9c4e-8d3b-4f6a-9c1e-2b7d4a5f0e21";

    private readonly IApplicationPaths _applicationPaths;
    private readonly MusicStrmPageController _uiPageController;

    public Plugin(IApplicationHost applicationHost)
        : base(applicationHost ?? throw new ArgumentNullException(nameof(applicationHost)))
    {
        Instance = this;
        _applicationPaths = applicationHost.Resolve<IApplicationPaths>();
        MigrateLegacyXmlConfiguration();
        _uiPageController = new MusicStrmPageController(
            applicationHost,
            Name,
            GetOptions,
            SaveOptions);
    }

    /// <summary>以完整 Plugin UI 替换基类 Simple UI 页面,使设置页能承载运行按钮。</summary>
    public IReadOnlyCollection<IPluginUIPageController> UIPageControllers
        => [_uiPageController];

    public static Plugin? Instance { get; private set; }

    internal static PluginConfiguration GetConfiguration()
    {
        return Instance?.Configuration ?? new PluginConfiguration();
    }

    /// <summary>当前插件配置。基类 Simple UI 会将其保存为 JSON,业务代码仍通过该属性读取。</summary>
    public PluginConfiguration Configuration => GetOptions();

    public override Guid Id => new(PluginGuid);

    public override string Name => "Music Strm Extract";

    public override string Description => "按歌手/专辑目录与轨号从 MusicBrainz 补全 .strm 音乐元数据,使 strm 音乐可正常刮削组织";

    public ImageFormat ThumbImageFormat => ImageFormat.Png;

    public Stream GetThumbImage()
    {
        var stream = typeof(Plugin).Assembly.GetManifestResourceStream("MusicStrmExtract.icon.png");
        if (stream is null)
        {
            throw new InvalidOperationException("Missing embedded icon resource.");
        }

        return stream;
    }

    /// <summary>升级到 Simple UI 后首次启动时,把旧版 XML 配置迁移到新 JSON 配置,避免已有设置丢失。</summary>
    private void MigrateLegacyXmlConfiguration()
    {
        var configurationsPath = _applicationPaths.PluginConfigurationsPath;
        var jsonPath = Path.Combine(configurationsPath, $"{Name}.json");
        var legacyXmlPath = Path.Combine(configurationsPath, "MusicStrmExtract.xml");

        MigrateLegacyXmlConfiguration(jsonPath, legacyXmlPath, SaveOptions);
    }

    /// <summary>
    /// 迁移逻辑主体(可单测):jsonPath 已存在或旧 XML 不存在时直接返回;
    /// 否则解析旧 XML 并交由 <paramref name="save"/> 落盘。解析/读取失败一律吞掉,
    /// 不阻塞插件启动(旧 XML 保留原处可由用户手动迁移)。
    /// </summary>
    internal static void MigrateLegacyXmlConfiguration(
        string jsonPath,
        string legacyXmlPath,
        Action<PluginConfiguration> save)
    {
        if (File.Exists(jsonPath) || !File.Exists(legacyXmlPath))
        {
            return;
        }

        try
        {
            var root = XDocument.Load(legacyXmlPath).Root;
            if (root is null)
            {
                return;
            }

            var migrated = new PluginConfiguration
            {
                MusicBrainzBaseUrl = (string?)root.Element("MusicBrainzBaseUrl") ?? string.Empty
            };

            save(migrated);
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            // 旧 XML 保留在原处,可由用户手动迁移,不阻塞插件启动。
            // UnauthorizedAccessException 与 IOException 无继承关系:配置目录不可读时若不捕获会逃逸,
            // 导致整个插件加载失败,与 M3 的加固理由一致(迁移失败不应让插件起不来)。
        }
    }
}
