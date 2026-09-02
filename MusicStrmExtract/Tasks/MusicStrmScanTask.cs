using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;

using MusicStrmExtract.Metadata;
using MusicStrmExtract.Online;
using MusicStrmExtract.Probing;
using MusicStrmExtract.Writing;

namespace MusicStrmExtract.Tasks
{
    /// <summary>
    /// 扫描任务:仅处理 CollectionType==music 的库。
    /// 对 .strm 支撑的 Audio 条目:读取目标 URL → ffprobe 探测内嵌标签 → MusicBrainz/iTunes 在线补全(分档合并)
    /// → 写回 Emby 条目字段与 ProviderIds → 下载封面 → 触发库扫描促成专辑/艺术家组织。
    /// </summary>
    public class MusicStrmScanTask : IScheduledTask
    {
        private const string TagPrefix = "[MusicStrmExtract]";

        private readonly ILibraryManager _libraryManager;
        private readonly ILogManager _logManager;
        private readonly ILogger _logger;

        public MusicStrmScanTask(ILibraryManager libraryManager, ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _logManager = logManager;
            _logger = logManager.GetLogger("MusicStrmExtract");
        }

        public string Name => "Music Strm 元数据提取";

        public string Key => "MusicStrmExtractScan";

        public string Description => "遍历音乐类型库,探测 .strm 目标(HTTP 直链)内嵌标签,并补全 MusicBrainz/iTunes 在线元数据与封面。";

        public string Category => "Music";

        public bool IsHidden => false;

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = "IntervalTrigger",
                    IntervalTicks = TimeSpan.FromDays(7).Ticks
                }
            };
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();

            // 1) 收集所有音乐库的根路径
            var musicLocations = _libraryManager.GetVirtualFolders()
                .Where(v => string.Equals(v.CollectionType, "music", StringComparison.OrdinalIgnoreCase))
                .SelectMany(v => v.Locations ?? Array.Empty<string>())
                .ToList();

            if (musicLocations.Count == 0)
            {
                _logger.Info($"{TagPrefix} 未找到 CollectionType=music 的音乐库,任务结束。");
                progress.Report(100);
                return;
            }

            // 2) 定位 ffprobe
            var ffprobePath = FfprobeRunner.Locate(config.FfprobePath);
            if (string.IsNullOrEmpty(ffprobePath))
            {
                _logger.Error($"{TagPrefix} 未找到 ffprobe(配置路径无效且 Emby 运行目录/PATH 无 ffprobe)。请在插件配置中指定 FfprobePath。");
                progress.Report(100);
                return;
            }

            _logger.Info($"{TagPrefix} 使用 ffprobe: {ffprobePath} (超时 {config.ProbeTimeoutSeconds}s, 在线补全={config.EnableOnlineMetadata}, 写回={config.WriteBack}, 上限={config.MaxItemsToProcess})");

            // 3) 仅保留位于音乐库且以 .strm 结尾的 Audio 条目
            var strmAudios = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    Recursive = true,
                    IncludeItemTypes = new[] { "Audio" }
                })
                .OfType<Audio>()
                .Where(a => !string.IsNullOrEmpty(a.Path)
                            && a.Path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase)
                            && musicLocations.Any(root =>
                                a.Path.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var total = config.MaxItemsToProcess > 0
                ? Math.Min(config.MaxItemsToProcess, strmAudios.Count)
                : strmAudios.Count;

            _logger.Info($"{TagPrefix} 本次处理 strm Audio={total} 条(库内共 {strmAudios.Count}, 音乐库根路径数={musicLocations.Count})");

            var runner = new FfprobeRunner(ffprobePath, config.ProbeTimeoutSeconds, config.ExtraHeaders);

            var okCount = 0;
            var failCount = 0;
            var writtenCount = 0;
            var coverWrittenCount = 0;
            var processed = 0;

            foreach (var audio in strmAudios.Take(total))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.Info($"{TagPrefix} 任务被取消,已处理 {processed}/{total}。");
                    break;
                }

                processed++;
                progress.Report(100.0 * processed / total);

                try
                {
                    var url = ReadStrmUrl(audio.Path!);
                    if (string.IsNullOrEmpty(url))
                    {
                        _logger.Warn($"{TagPrefix} 无法读取 strm 内容: {audio.Path}");
                        failCount++;
                        continue;
                    }

                    var probe = await runner.ProbeAsync(url, cancellationToken).ConfigureAwait(false);
                    if (probe is null || !probe.HasTags)
                    {
                        _logger.Warn($"{TagPrefix} 探测无标签(可能非音频或网络不可达): {audio.Path} -> {Truncate(url, 120)}");
                        failCount++;
                        continue;
                    }

                    var md = TagParser.Parse(probe.Tags);
                    md.HasEmbeddedCover = probe.HasEmbeddedCover;
                    okCount++;

                    if (config.LogProbeDetails)
                    {
                        _logger.Info($"{TagPrefix} [探测OK {processed}/{total}] {audio.Name} | 容器={probe.Container} | 标题='{md.Title}' | 专辑='{md.Album}' | 艺术家=[{string.Join(" / ", md.Artists)}] | 专辑艺术家=[{string.Join(" / ", md.AlbumArtists)}] | 年份={md.Year} | 曲号={md.IndexNumber} | MBID_track={md.MusicBrainzTrackId} | 封面={md.HasEmbeddedCover}");
                    }

                    // 在线层(MusicBrainz → iTunes 兜底)
                    var online = new OnlineMetadata();
                    if (config.EnableOnlineMetadata)
                    {
                        using var resolver = new OnlineResolver();
                        online = await resolver.ResolveAsync(md, cancellationToken).ConfigureAwait(false);
                    }

                    var (final, _, kind, note) = MergePolicy.Merge(md, online);
                    if (online.Kind != OnlineMatchKind.None)
                    {
                        _logger.Info($"{TagPrefix} [在线 {processed}/{total}] {audio.Name} | kind={kind} | source={online.Source} | 标题='{final.Title}' | 专辑='{final.Album}' | MBID_album={final.MusicBrainzAlbumId} MBID_track={final.MusicBrainzTrackId} | 封面URL={online.CoverArtUrl ?? "(无)"} | {note}");
                    }
                    else
                    {
                        _logger.Info($"{TagPrefix} [在线 {processed}/{total}] {audio.Name} | kind=None | {online.Note ?? "无在线候选(使用内嵌标签)"}");
                    }

                    if (config.WriteBack && !final.IsEmpty)
                    {
                        using var writer = new ItemWriter(_libraryManager, _logManager);
                        var changed = await writer.ApplyToAudioAsync(
                            audio, final, audio.Path!, online.CoverArtUrl, config.WriteAlbumCover, cancellationToken).ConfigureAwait(false);

                        if (changed)
                        {
                            writtenCount++;
                        }

                        if (!string.IsNullOrWhiteSpace(online.CoverArtUrl)
                            && config.WriteAlbumCover
                            && CoverTargetExists(audio.Path))
                        {
                            coverWrittenCount++;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failCount++;
                    _logger.Error($"{TagPrefix} 处理失败: {audio.Path} -> {ex.Message}");
                }
            }

            if (writtenCount > 0)
            {
                _logger.Info($"{TagPrefix} 有 {writtenCount} 条发生变更,排队触发库扫描以促成专辑/艺术家组织。");
                _libraryManager.QueueLibraryScan();
            }

            _logger.Info($"{TagPrefix} 汇总: 总数={total}, 探测成功={okCount}, 写回变更={writtenCount}, 封面落盘={coverWrittenCount}, 失败={failCount}");
            progress.Report(100);
        }

        private static bool CoverTargetExists(string strmPath)
        {
            var dir = Path.GetDirectoryName(strmPath);
            return !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "cover.jpg"));
        }

        private static string? ReadStrmUrl(string strmPath)
        {
            foreach (var line in File.ReadLines(strmPath))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0)
                {
                    return trimmed;
                }
            }

            return null;
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max)
            {
                return value ?? string.Empty;
            }

            return value.Substring(0, max) + "...";
        }
    }
}
