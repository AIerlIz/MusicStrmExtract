using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;

using MusicStrmExtract.Processing;

namespace MusicStrmExtract.Tasks
{
    /// <summary>
    /// 计划任务入口(手动运行;或由用户在 Emby 计划任务页自行添加定时触发器)。
    /// 常规自动处理已由"库扫描后自动任务"(ILibraryPostScanTask)承担,无需依赖本任务定时。
    /// </summary>
    public class MusicStrmScanTask : IScheduledTask
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogManager _logManager;

        public MusicStrmScanTask(ILibraryManager libraryManager, ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _logManager = logManager;
        }

        public string Name => "Music Strm 元数据提取";

        public string Key => "MusicStrmExtractScan";

        public string Description => "遍历音乐类型库,探测 .strm 目标(HTTP 直链)内嵌标签,并补全 MusicBrainz/iTunes 在线元数据与封面。平时由媒体库扫描自动触发,本任务可手动运行。";

        public string Category => "Music";

        public bool IsHidden => false;

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            // 默认不注册自动触发器(避免与库扫描自动触发重复);如需定时可在 Emby 计划任务页添加。
            return Array.Empty<TaskTriggerInfo>();
        }

        public Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
            return new MusicStrmProcessor(_libraryManager, _logManager)
                .RunAsync(config, cancellationToken, progress);
        }
    }
}
