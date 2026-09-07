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
    /// 随后把 MusicBrainzAlbum 已存在但 AlbumId 缺失或指向陈旧专辑的 .strm 加入刷新队列。
    /// </summary>
    internal sealed class StaleMusicAlbumRepairService
    {
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IProviderManager _providerManager;
        private readonly IFileSystem _fileSystem;
        private int _isRunning;

        public StaleMusicAlbumRepairService(
            ILogManager logManager,
            ILibraryManager libraryManager,
            IProviderManager providerManager,
            IFileSystem fileSystem)
        {
            _logger = logManager.GetLogger("MusicStrmExtract");
            _libraryManager = libraryManager;
            _providerManager = providerManager;
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
            if (_libraryManager.IsScanRunning)
            {
                return "媒体库扫描正在运行，请等待扫描结束后再执行修复。";
            }

            var audios = GetAudios();
            ct.ThrowIfCancellationRequested();
            var referencedAlbumIds = audios
                .Where(a => a.AlbumId != 0)
                .Select(a => a.AlbumId)
                .ToHashSet();

            var albums = GetMusicAlbums();
            ct.ThrowIfCancellationRequested();
            var staleAlbums = albums
                .Where(a => !HasMusicBrainzAlbum(a)
                    && string.IsNullOrWhiteSpace(a.Path)
                    && !referencedAlbumIds.Contains(a.InternalId))
                .ToList();

            ct.ThrowIfCancellationRequested();
            progress?.Report($"正在删除 {staleAlbums.Count} 个陈旧 MusicAlbum...");
            foreach (var album in staleAlbums)
            {
                _libraryManager.DeleteItem(album, new DeleteOptions
                {
                    DeleteFileLocation = false,
                    DeleteFromExternalProvider = false
                });
            }

            var staleReferencedAlbumIds = albums
                .Where(a => !HasMusicBrainzAlbum(a) && referencedAlbumIds.Contains(a.InternalId))
                .Select(a => a.InternalId)
                .ToHashSet();

            ct.ThrowIfCancellationRequested();
            var toRefresh = audios
                .Where(a => a.Path?.EndsWith(".strm", StringComparison.OrdinalIgnoreCase) == true
                    && HasMusicBrainzAlbum(a)
                    && (a.AlbumId == 0 || staleReferencedAlbumIds.Contains(a.AlbumId)))
                .ToList();

            progress?.Report($"正在排队刷新 {toRefresh.Count} 个 .strm...");
            var refreshOptions = new MetadataRefreshOptions(_fileSystem)
            {
                MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                ImageRefreshMode = MetadataRefreshMode.FullRefresh,
                ReplaceAllMetadata = false
            };

            foreach (var audio in toRefresh)
            {
                _providerManager.QueueRefresh(audio.InternalId, refreshOptions, RefreshPriority.High);
                ct.ThrowIfCancellationRequested();
            }

            _logger.Info(
                $"[MusicStrmExtract] [Repair] 删除陈旧 MusicAlbum={staleAlbums.Count}, 排队刷新 .strm={toRefresh.Count}");
            progress?.Report("修复完成。");
            return $"已删除 {staleAlbums.Count} 个陈旧 MusicAlbum，已排队刷新 {toRefresh.Count} 个 .strm。";
        }

        private List<MusicAlbum> GetMusicAlbums()
        {
            return _libraryManager.GetItemList(new InternalItemsQuery
            {
                Recursive = true,
                IncludeItemTypes = new[] { nameof(MusicAlbum) }
            }).OfType<MusicAlbum>().ToList();
        }

        private List<Audio> GetAudios()
        {
            return _libraryManager.GetItemList(new InternalItemsQuery
            {
                Recursive = true,
                IncludeItemTypes = new[] { nameof(Audio) }
            }).OfType<Audio>().ToList();
        }

        private static bool HasMusicBrainzAlbum(BaseItem item)
        {
            return item.ProviderIds != null
                && item.ProviderIds.TryGetValue(PluginConstants.MusicBrainzAlbum, out var id)
                && !string.IsNullOrWhiteSpace(id);
        }
    }
}
