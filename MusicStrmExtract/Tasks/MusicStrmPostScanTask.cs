using System;
using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;

using MusicStrmExtract.Processing;

namespace MusicStrmExtract.Tasks
{
    /// <summary>
    /// 库扫描后自动任务(ILibraryPostScanTask,由 Emby 在每次媒体库扫描完成后调用):
    /// 新 .strm 音乐入库 → Emby 自动扫描 → 本任务自动处理(探测/在线/写回/专辑补写),全程无需计划任务。
    /// 处理器幂等且带防并发闸门,重复触发无副作用。
    /// </summary>
    public class MusicStrmPostScanTask : ILibraryPostScanTask
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogManager _logManager;

        public MusicStrmPostScanTask(ILibraryManager libraryManager, ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _logManager = logManager;
        }

        public Task Run(IProgress<double> progress, CancellationToken cancellationToken)
        {
            var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
            return new MusicStrmProcessor(_libraryManager, _logManager)
                .RunAsync(config, cancellationToken, progress);
        }
    }
}
