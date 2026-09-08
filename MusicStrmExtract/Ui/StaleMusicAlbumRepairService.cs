using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using MediaBrowser.Common;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;

namespace MusicStrmExtract.Ui
{
    /// <summary>
    /// 保守修复:只清理无 MusicBrainzAlbum、无文件路径、且没有任何 Audio 通过 AlbumId 引用的 MusicAlbum,
    /// 随后把 MusicBrainzAlbum 已存在但 AlbumId 缺失，或 AlbumId 指向陈旧专辑的 .strm 加入刷新队列。
    /// </summary>
    internal sealed class StaleMusicAlbumRepairService
    {
        private const string ScanRunningMessage =
            "媒体库扫描正在运行，请等待扫描结束后再执行修复。";

        private readonly ILogger _logger;
        private readonly Func<bool> _isScanRunning;
        private readonly Func<IReadOnlyList<Audio>> _getAudios;
        private readonly Func<IReadOnlyList<MusicAlbum>> _getMusicAlbums;
        private readonly Action<MusicAlbum> _deleteAlbum;
        private readonly Action<long, MetadataRefreshOptions> _queueRefresh;
        private readonly IFileSystem? _fileSystem;
        private int _isRunning;

        public StaleMusicAlbumRepairService(
            ILogManager logManager,
            ILibraryManager libraryManager,
            IProviderManager providerManager,
            IFileSystem fileSystem)
        {
            _logger = logManager.GetLogger("MusicStrmExtract");
            _isScanRunning = () => libraryManager.IsScanRunning;
            _getAudios = () => libraryManager
                .GetItemList(CreateItemQuery(nameof(Audio)))
                .OfType<Audio>()
                .ToList();
            _getMusicAlbums = () => libraryManager
                .GetItemList(CreateItemQuery(nameof(MusicAlbum)))
                .OfType<MusicAlbum>()
                .ToList();
            _deleteAlbum = album => libraryManager.DeleteItem(album, new DeleteOptions
            {
                DeleteFileLocation = false,
                DeleteFromExternalProvider = false
            });
            _queueRefresh = (id, options) =>
                providerManager.QueueRefresh(id, options, RefreshPriority.High);
            _fileSystem = fileSystem;
        }

        internal StaleMusicAlbumRepairService(
            ILogger logger,
            Func<bool> isScanRunning,
            Func<IReadOnlyList<Audio>> getAudios,
            Func<IReadOnlyList<MusicAlbum>> getMusicAlbums,
            Action<MusicAlbum> deleteAlbum,
            Action<long, MetadataRefreshOptions> queueRefresh,
            IFileSystem? fileSystem = null)
        {
            _logger = logger;
            _isScanRunning = isScanRunning;
            _getAudios = getAudios;
            _getMusicAlbums = getMusicAlbums;
            _deleteAlbum = deleteAlbum;
            _queueRefresh = queueRefresh;
            _fileSystem = fileSystem;
        }

        public string Run(IProgress<string>? progress = null, CancellationToken ct = default)
        {
            if (Interlocked.Exchange(ref _isRunning, 1) != 0)
            {
                return "修复正在运行，请等待当前任务结束后再执行。";
            }

            try
            {
                return RunCore(progress, ct);
            }
            finally
            {
                Interlocked.Exchange(ref _isRunning, 0);
            }
        }

        private string RunCore(IProgress<string>? progress, CancellationToken ct)
        {
            progress?.Report("正在检查媒体库状态...");
            if (_isScanRunning())
            {
                return ScanRunningMessage;
            }

            var audios = _getAudios();
            ct.ThrowIfCancellationRequested();
            var referencedAlbumIds = audios
                .Where(a => a.AlbumId != 0)
                .Select(a => a.AlbumId)
                .ToHashSet();

            var albums = _getMusicAlbums();
            ct.ThrowIfCancellationRequested();
            var staleAlbums = albums
                .Where(a => !HasMusicBrainzAlbum(a)
                    && string.IsNullOrWhiteSpace(a.Path)
                    && !referencedAlbumIds.Contains(a.InternalId))
                .ToList();

            ct.ThrowIfCancellationRequested();
            if (_isScanRunning())
            {
                _logger.Info(
                    "[MusicStrmExtract] [Repair] 媒体库扫描已开始，中止删除：已删除 0 个陈旧 MusicAlbum");
                return "媒体库扫描已开始，修复已中止（已删除 0 个陈旧 MusicAlbum）。";
            }

            progress?.Report($"正在删除 {staleAlbums.Count} 个陈旧 MusicAlbum...");
            var deleted = 0;
            foreach (var album in staleAlbums)
            {
                ct.ThrowIfCancellationRequested();
                if (_isScanRunning())
                {
                    _logger.Info(
                        $"[MusicStrmExtract] [Repair] 媒体库扫描已开始，中止删除：已删除 {deleted}/{staleAlbums.Count} 个陈旧 MusicAlbum");
                    return $"媒体库扫描已开始，修复已中止（已删除 {deleted} 个陈旧 MusicAlbum）。";
                }

                _deleteAlbum(album);
                deleted++;
            }

            var staleReferencedAlbumIds = albums
                .Where(a => !HasMusicBrainzAlbum(a) && referencedAlbumIds.Contains(a.InternalId))
                .Select(a => a.InternalId)
                .ToHashSet();

            ct.ThrowIfCancellationRequested();
            if (_isScanRunning())
            {
                _logger.Info(
                    $"[MusicStrmExtract] [Repair] 媒体库扫描已开始，跳过刷新：已删除 {deleted} 个陈旧 MusicAlbum");
                return $"媒体库扫描已开始，修复已中止（已删除 {deleted} 个陈旧 MusicAlbum）。";
            }

            var toRefresh = audios
                .Where(a => a.Path?.EndsWith(".strm", StringComparison.OrdinalIgnoreCase) == true
                    && (staleReferencedAlbumIds.Contains(a.AlbumId)
                        || (a.AlbumId == 0 && HasMusicBrainzAlbum(a))))
                .ToList();

            if (toRefresh.Count > 0)
            {
                progress?.Report($"正在排队刷新 {toRefresh.Count} 个 .strm...");
                var refreshOptions = new MetadataRefreshOptions(_fileSystem)
                {
                    MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                    ImageRefreshMode = MetadataRefreshMode.FullRefresh,
                    ReplaceAllMetadata = false
                };

                var queued = 0;
                foreach (var audio in toRefresh)
                {
                    if (_isScanRunning())
                    {
                        _logger.Info(
                            $"[MusicStrmExtract] [Repair] 媒体库扫描已开始，中止刷新：已删除 {deleted} 个陈旧 MusicAlbum，已排队 {queued} 个 .strm");
                        return $"媒体库扫描已开始，修复已中止（已删除 {deleted} 个陈旧 MusicAlbum，已排队 {queued} 个 .strm）。";
                    }

                    _queueRefresh(audio.InternalId, refreshOptions);
                    queued++;
                    ct.ThrowIfCancellationRequested();
                }
            }

            _logger.Info(
                $"[MusicStrmExtract] [Repair] 删除陈旧 MusicAlbum={staleAlbums.Count}, 排队刷新 .strm={toRefresh.Count}");
            progress?.Report("修复完成。");
            return $"已删除 {staleAlbums.Count} 个陈旧 MusicAlbum，已排队刷新 {toRefresh.Count} 个 .strm。";
        }

        private static InternalItemsQuery CreateItemQuery(string itemType)
        {
            return new InternalItemsQuery
            {
                Recursive = true,
                IncludeItemTypes = new[] { itemType }
            };
        }

        private static bool HasMusicBrainzAlbum(BaseItem item)
        {
            return item.ProviderIds != null
                && item.ProviderIds.TryGetValue(PluginConstants.MusicBrainzAlbum, out var id)
                && !string.IsNullOrWhiteSpace(id);
        }
    }
}
