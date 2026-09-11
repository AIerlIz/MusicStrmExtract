using System.Globalization;
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

        // 删除阶段:逐条删除并在每一步前后检查取消与扫描竞态,扫描一开始立即中止。
        ct.ThrowIfCancellationRequested();
        var aborted = AbortIfScanRunning("中止删除", FormatDeleteSummary(0));
        if (aborted is not null)
            return aborted;

        progress?.Report($"正在删除 {plan.AlbumsToDelete.Count} 个陈旧 MusicAlbum...");
        var deleted = 0;
        foreach (var album in plan.AlbumsToDelete)
        {
            ct.ThrowIfCancellationRequested();
            aborted = AbortIfScanRunning("中止删除", FormatDeleteSummary(deleted));
            if (aborted is not null)
                return aborted;

            _deleteAlbum(album);
            deleted++;
        }

        // 删除完成后进入刷新阶段;此时的中止摘要需带上已删除数量。
        ct.ThrowIfCancellationRequested();
        aborted = AbortIfScanRunning("跳过刷新", FormatDeleteSummary(deleted));
        if (aborted is not null)
            return aborted;

        var queued = 0;
        if (plan.StrmToRefresh.Count > 0)
        {
            progress?.Report($"正在排队刷新 {plan.StrmToRefresh.Count} 个 .strm...");
            var refreshOptions = CreateRefreshOptions();

            foreach (var audio in plan.StrmToRefresh)
            {
                aborted = AbortIfScanRunning("中止刷新", FormatRefreshSummary(deleted, queued));
                if (aborted is not null)
                    return aborted;

                _queueRefresh(audio.InternalId, refreshOptions);
                queued++;
                ct.ThrowIfCancellationRequested();
            }
        }

        _logger.Info(
            $"[LegacyRepair] result=completed deletedAlbums={plan.AlbumsToDelete.Count} " +
            $"queuedStrm={plan.StrmToRefresh.Count}");
        progress?.Report("修复完成。");
        return $"已删除 {plan.AlbumsToDelete.Count} 个陈旧 MusicAlbum，已排队刷新 {plan.StrmToRefresh.Count} 个 .strm。";
    }

    /// <summary>删除阶段的中止摘要:仅汇报已删除数量。</summary>
    private static string FormatDeleteSummary(int deletedAlbums)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"已删除 {deletedAlbums} 个陈旧 MusicAlbum");
    }

    /// <summary>刷新阶段的中止摘要:同时汇报已删除与已排队数量。</summary>
    private static string FormatRefreshSummary(int deletedAlbums, int queuedStrm)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"已删除 {deletedAlbums} 个陈旧 MusicAlbum，已排队 {queuedStrm} 个 .strm");
    }

    /// <summary>本次修复的刷新选项:强制全量元数据/图片刷新,但保留已有字段。</summary>
    private MetadataRefreshOptions CreateRefreshOptions()
    {
        return new MetadataRefreshOptions(_fileSystem)
        {
            MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
            ImageRefreshMode = MetadataRefreshMode.FullRefresh,
            ReplaceAllMetadata = false
        };
    }

    private string? AbortIfScanRunning(string phase, string summary)
    {
        if (!_isScanRunning())
            return null;

        _logger.Info(
            $"[LegacyRepair] result=aborted phase=\"{phase}\" summary=\"{summary}\" " +
            "reason=\"library_scan_started\"");
        return $"媒体库扫描已开始，修复已中止（{summary}）。";
    }
}
