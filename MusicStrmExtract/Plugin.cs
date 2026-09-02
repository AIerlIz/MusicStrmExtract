using System;

using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace MusicStrmExtract
{
    /// <summary>
    /// Emby 服务端插件:为音乐库中的 .strm 音频条目补全元数据。
    /// 探测 strm 指向的 HTTP 直链目标文件的内嵌标签,并可选地获取 MusicBrainz / iTunes 在线元数据。
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>
    {
        public const string PluginGuid = "6a2f9c4e-8d3b-4f6a-9c1e-2b7d4a5f0e21";

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public override Guid Id => new Guid(PluginGuid);

        public override string Name => "Music Strm Extract";

        public override string Description => "探测音乐库 .strm 条目的目标(HTTP 直链)内嵌标签,并补全 MusicBrainz/iTunes 在线元数据,使 strm 音乐可正常刮削组织";

        public static Plugin? Instance { get; private set; }
    }
}
