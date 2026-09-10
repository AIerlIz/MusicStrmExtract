using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using System.Threading;

namespace MusicStrmExtract.Ui;

/// <summary>
/// 保守修复:只清理无 MusicBrainzAlbum、无文件路径、且没有任何 Audio 通过 AlbumId 引用的 MusicAlbum,
/// 随后把 MusicBrainzAlbum 已存在但 AlbumId 缺失，或 AlbumId 指向陈旧专辑的 .strm 加入刷新队列。
/// </summary>
internal sealed class LegacyAlbumRepairService
{
    private const string ScanRunningMessage =
        "媒体库扫描正在运行，请等待扫描结束后再执行修复。";

    private readonly Func<bool> _isScanRunning;
    private readonly Func<IReadOnlyList<Audio>> _getAudios;
    private readonly Func<IReadOnlyList<MusicAlbum>> _getMusicAlbums;
    private readonly RepairExecutor _repairExecutor;
    private int _isRunning;

    public LegacyAlbumRepairService(
        ILogManager logManager,
        ILibraryManager libraryManager,
        IProviderManager providerManager,
        IFileSystem fileSystem)
    {
        _isScanRunning = () => libraryManager.IsScanRunning;
        _getAudios = () => libraryManager
            .GetItemList(CreateItemQuery(nameof(Audio)))
            .OfType<Audio>()
            .ToList();
        _getMusicAlbums = () => libraryManager
            .GetItemList(CreateItemQuery(nameof(MusicAlbum)))
            .OfType<MusicAlbum>()
            .ToList();
        _repairExecutor = new RepairExecutor(
            logManager.GetLogger("MusicStrmExtract"),
            _isScanRunning,
            album => libraryManager.DeleteItem(album, new DeleteOptions
            {
                DeleteFileLocation = false,
                DeleteFromExternalProvider = false
            }),
            (id, options) => providerManager.QueueRefresh(id, options, RefreshPriority.High),
            fileSystem);
    }

    internal LegacyAlbumRepairService(
        ILogger logger,
        Func<bool> isScanRunning,
        Func<IReadOnlyList<Audio>> getAudios,
        Func<IReadOnlyList<MusicAlbum>> getMusicAlbums,
        Action<MusicAlbum> deleteAlbum,
        Action<long, MetadataRefreshOptions> queueRefresh,
        IFileSystem? fileSystem = null)
    {
        _isScanRunning = isScanRunning;
        _getAudios = getAudios;
        _getMusicAlbums = getMusicAlbums;
        _repairExecutor = new RepairExecutor(
            logger,
            isScanRunning,
            deleteAlbum,
            queueRefresh,
            fileSystem);
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
        var albums = _getMusicAlbums();
        ct.ThrowIfCancellationRequested();
        var plan = RepairPlanBuilder.Build(audios, albums);
        return _repairExecutor.Execute(plan, progress, ct);
    }

    private static InternalItemsQuery CreateItemQuery(string itemType)
    {
        return new InternalItemsQuery
        {
            Recursive = true,
            IncludeItemTypes = [itemType]
        };
    }

}
