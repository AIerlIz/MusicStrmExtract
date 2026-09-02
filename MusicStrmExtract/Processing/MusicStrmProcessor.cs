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

using MusicStrmExtract.Metadata;
using MusicStrmExtract.Online;
using MusicStrmExtract.Probing;
using MusicStrmExtract.Writing;

namespace MusicStrmExtract.Processing
{
    /// <summary>
    /// 核心处理管线,供计划任务与"库扫描后自动任务"(ILibraryPostScanTask)共用。
    /// 仅处理 CollectionType==music 的库:
    ///   1) 对 .strm Audio:读取目标 URL → ffprobe 探测内嵌标签 → MusicBrainz/iTunes 在线补全(分档合并)→ 写回条目;
    ///   2) 发生写回变更时排队库扫描(促成 MusicAlbum/MusicArtist 生成);
    ///   3) 库状态稳定(本轮 0 变更,即上次扫描已完成)时,补写 MusicAlbum 条目的 MBID/年份。
    /// 幂等;静态闸门保证与计划任务不并发。
    /// </summary>
    public sealed class MusicStrmProcessor
    {
        private const string TagPrefix = "[MusicStrmExtract]";

        private static readonly SemaphoreSlim RunGate = new SemaphoreSlim(1, 1);
        private static DateTime _lastQueuedLibraryScanUtc = DateTime.MinValue;

        /// <summary>组织扫描限流:两次排队至少间隔 180s,防止"始终无法归组的条目"导致无限循环扫描。</summary>
        private static bool TryQueueLibraryScan()
        {
            var now = DateTime.UtcNow;
            if (now - _lastQueuedLibraryScanUtc < TimeSpan.FromSeconds(180))
            {
                return false;
            }

            _lastQueuedLibraryScanUtc = now;
            return true;
        }

        private readonly ILibraryManager _libraryManager;
        private readonly ILogManager _logManager;
        private readonly ILogger _logger;

        public MusicStrmProcessor(ILibraryManager libraryManager, ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _logManager = logManager;
            _logger = logManager.GetLogger("MusicStrmExtract");
        }

        public async Task RunAsync(
            PluginConfiguration config,
            CancellationToken cancellationToken,
            IProgress<double>? progress = null)
        {
            progress ??= new Progress<double>();

            if (!await RunGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                _logger.Info($"{TagPrefix} 已有一次处理在运行,本次跳过(防并发)。");
                progress.Report(100);
                return;
            }

            try
            {
                await RunCoreAsync(config, cancellationToken, progress).ConfigureAwait(false);
            }
            finally
            {
                RunGate.Release();
            }
        }

        private async Task RunCoreAsync(PluginConfiguration config, CancellationToken cancellationToken, IProgress<double> progress)
        {
            // 1) 音乐库根路径
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

            // 3) strm Audio 定位(仅音乐库)
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
                    _logger.Info($"{TagPrefix} 处理被取消,已处理 {processed}/{total}。");
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

            // 4) 触发组织扫描:有写回变更,或存在"已填专辑名却尚未归组(MusicAlbum 关联)"的 strm Audio
            //    (Provider 在入库刷新中先行填充字段时,写回变更可能为 0,需据此补一次扫描促成音乐索引)
            var needOrganize = strmAudios.Count(a =>
                !string.IsNullOrWhiteSpace(a.Album) && a.AlbumId == 0);

            if (writtenCount > 0 || needOrganize > 0)
            {
                if (TryQueueLibraryScan())
                {
                    _logger.Info($"{TagPrefix} 写回变更={writtenCount}, 待组织条目={needOrganize} -> 排队触发库扫描以促成专辑/艺术家组织(专辑补写将在下次稳定运行进行)。");
                    _libraryManager.QueueLibraryScan();
                }
                else
                {
                    _logger.Info($"{TagPrefix} 写回变更={writtenCount}, 待组织条目={needOrganize} -> 距上次组织扫描不足 180s,跳过本次排队(等待上次扫描结果,避免循环扫描)。");
                }
            }
            else
            {
                // 库已稳定(上次扫描已完成):补写 MusicAlbum 条目的 MBID/年份(幂等)
                var albumUpdated = 0;
                if (config.WriteBack)
                {
                    albumUpdated = await new AlbumUpdater(_libraryManager, _logManager)
                        .RunAsync(cancellationToken).ConfigureAwait(false);
                }

                _logger.Info($"{TagPrefix} 汇总: 总数={total}, 探测成功={okCount}, 写回变更=0, 待组织条目=0, 专辑补写={albumUpdated}, 封面已就绪={coverWrittenCount}, 失败={failCount}");
                progress.Report(100);
                return;
            }

            _logger.Info($"{TagPrefix} 汇总: 总数={total}, 探测成功={okCount}, 写回变更={writtenCount}, 待组织条目={needOrganize}, 封面落盘={coverWrittenCount}, 失败={failCount}");
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
