using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;

namespace MusicStrmExtract.Ui;

/// <summary>执行 RepairPlan,并在删除和刷新循环中持续防御媒体库扫描竞态。</summary>
internal sealed class RepairExecutor
{
    private readonly ILogger _logger;
    private readonly Func<bool> _isScanRunning;
    private readonly Action<MusicAlbum> _deleteAlbum;
    private readonly Action<long, MetadataRefreshOptions> _queueRefresh;
    private readonly IFileSystem? _fileSystem;

    public RepairExecutor(
        ILogger logger,
        Func<bool> isScanRunning,
        Action<MusicAlbum> deleteAlbum,
        Action<long, MetadataRefreshOptions> queueRefresh,
        IFileSystem? fileSystem)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _isScanRunning = isScanRunning ?? throw new ArgumentNullException(nameof(isScanRunning));
        _deleteAlbum = deleteAlbum ?? throw new ArgumentNullException(nameof(deleteAlbum));
        _queueRefresh = queueRefresh ?? throw new ArgumentNullException(nameof(queueRefresh));
        _fileSystem = fileSystem;
    }

    public string Execute(
        RepairPlan plan,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);

        ct.ThrowIfCancellationRequested();
        var aborted = AbortIfScanRunning("中止删除", "已删除 0 个陈旧 MusicAlbum");
        if (aborted is not null)
            return aborted;

        progress?.Report($"正在删除 {plan.AlbumsToDelete.Count} 个陈旧 MusicAlbum...");
        var deleted = 0;
        foreach (var album in plan.AlbumsToDelete)
        {
            ct.ThrowIfCancellationRequested();
            aborted = AbortIfScanRunning(
                "中止删除",
                $"已删除 {deleted}/{plan.AlbumsToDelete.Count} 个陈旧 MusicAlbum");
            if (aborted is not null)
                return aborted;

            _deleteAlbum(album);
            deleted++;
        }

        ct.ThrowIfCancellationRequested();
        aborted = AbortIfScanRunning("跳过刷新", $"已删除 {deleted} 个陈旧 MusicAlbum");
        if (aborted is not null)
            return aborted;

        var queued = 0;
        if (plan.StrmToRefresh.Count > 0)
        {
            progress?.Report($"正在排队刷新 {plan.StrmToRefresh.Count} 个 .strm...");
            var refreshOptions = new MetadataRefreshOptions(_fileSystem)
            {
                MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                ImageRefreshMode = MetadataRefreshMode.FullRefresh,
                ReplaceAllMetadata = false
            };

            foreach (var audio in plan.StrmToRefresh)
            {
                aborted = AbortIfScanRunning(
                    "中止刷新",
                    $"已删除 {deleted} 个陈旧 MusicAlbum，已排队 {queued} 个 .strm");
                if (aborted is not null)
                    return aborted;

                _queueRefresh(audio.InternalId, refreshOptions);
                queued++;
                ct.ThrowIfCancellationRequested();
            }
        }

        _logger.Info(
            $"[MusicStrmExtract] [LegacyRepair] 删除陈旧 MusicAlbum={plan.AlbumsToDelete.Count}, 排队刷新 .strm={plan.StrmToRefresh.Count}");
        progress?.Report("修复完成。");
        return $"已删除 {plan.AlbumsToDelete.Count} 个陈旧 MusicAlbum，已排队刷新 {plan.StrmToRefresh.Count} 个 .strm。";
    }

    private string? AbortIfScanRunning(string phase, string summary)
    {
        if (!_isScanRunning())
            return null;

        _logger.Info(
            $"[MusicStrmExtract] [LegacyRepair] 媒体库扫描已开始，{phase}：{summary}");
        return $"媒体库扫描已开始，修复已中止（{summary}）。";
    }
}
