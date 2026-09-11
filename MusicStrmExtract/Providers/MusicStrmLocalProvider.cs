using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Logging;
using MusicStrmExtract.Online;
using System.Net.Http;
using System.Text.Json;

namespace MusicStrmExtract.Providers;

/// <summary>
/// 标准本地元数据读取器(ILocalMetadataProvider):Emby 在扫描/刷新 Audio 条目时调用。
/// 只保留主路径·专辑轨道定位:文件名只解析数字轨号,扫描专辑文件夹得到本地轨号集合;
/// 按艺人 + 专辑文件夹名定位 MB release-group,再在该组内按本地轨号覆盖选择 release media,按轨号直接取 tracklist
/// 数据(recording MBID/标题/艺人),整张专辑一次定位并缓存;不做远程探测、不做文件名文本匹配。
/// 未命中的条目返回空结果,由 Emby 后续流程决定是否保持现状或做其它在线补全。
/// 命中时不直接写库:返回的 Audio 带 Album/AlbumArtists/MBID,由 Emby 合并保存并自动
/// 创建/关联 MusicAlbum、MusicArtist。
/// </summary>
public sealed class MusicStrmLocalProvider : ILocalMetadataProvider<Audio>
{
    private readonly ILogger _logger;
    private readonly IAlbumResolutionService _albumResolutionService;
    private readonly IAlbumDirectoryScanService _albumDirectoryScanService;
    private readonly IMusicStrmConfigurationSource _configurationSource;

    public MusicStrmLocalProvider(ILogManager logManager)
        : this(
            (logManager ?? throw new ArgumentNullException(nameof(logManager)))
                .GetLogger("MusicStrmExtract"),
            MusicStrmConfigurationSource.Default,
            albumResolutionService: null,
            albumDirectoryScanService: null)
    {
    }

    internal MusicStrmLocalProvider(
        ILogger logger,
        IMusicStrmConfigurationSource configurationSource,
        IAlbumResolutionService? albumResolutionService = null,
        IAlbumDirectoryScanService? albumDirectoryScanService = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configurationSource = configurationSource ?? throw new ArgumentNullException(nameof(configurationSource));
        _albumResolutionService = albumResolutionService
            ?? MusicStrmRuntime.CreateAlbumResolutionService(_logger);
        _albumDirectoryScanService = albumDirectoryScanService
            ?? MusicStrmRuntime.CreateAlbumDirectoryScanService();
    }

    public string Name => "Music Strm Extract";

    public async Task<MetadataResult<Audio>> GetMetadata(
        ItemInfo info,
        LibraryOptions libraryOptions,
        IDirectoryService directoryService,
        CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Audio>();
        if (info?.Path is null || !StrmFileParser.IsStrmPath(info.Path))
        {
            return result;
        }

        var config = _configurationSource.Current;

        // ===== 主路径:专辑轨道定位(艺人/专辑文件夹 → MB release tracklist;零远程探测)=====
        var (albumFolder, artistFolder, albumDir, discNumber) = StrmFileParser.GetFolderStructure(info.Path);
        if (!string.IsNullOrWhiteSpace(albumFolder) && !string.IsNullOrWhiteSpace(albumDir))
        {
            await TryResolveByAlbumTrackAsync(
                info, albumFolder, artistFolder, albumDir, discNumber, config, result, cancellationToken)
                .ConfigureAwait(false);
        }

        return result;
    }

    // ==================== 主路径:专辑轨道定位 ====================

    /// <summary>按专辑轨道定位结果填充 result；无法定位时保持空结果，由 Emby 后续流程决定。</summary>
    private async Task TryResolveByAlbumTrackAsync(
        ItemInfo info,
        string albumFolder,
        string? artistFolder,
        string albumDir,
        int? folderDisc,
        PluginConfiguration config,
        MetadataResult<Audio> result,
        CancellationToken ct)
    {
        var (fileDisc, rawTrackNumber, isCommentary) = StrmFileParser.ParseFileName(info.Path);
        if (rawTrackNumber <= 0)
            return; // 本文件无轨号,无法按轨取数

        var scan = _albumDirectoryScanService.GetOrScan(albumDir, message => _logger.Warn(message));
        if (scan.Discs.Count == 0)
            return;

        var album = await ResolveAlbumAsync(albumFolder, artistFolder, scan, config, ct).ConfigureAwait(false);
        if (album is null || !album.Found)
            return;

        var resolution = AudioTrackMetadataFactory.TryBuild(
            album,
            scan,
            info.Path,
            folderDisc,
            fileDisc,
            rawTrackNumber,
            isCommentary);
        if (resolution is null)
            return;

        result.Item = resolution.Item;
        result.HasMetadata = true;
        ct.ThrowIfCancellationRequested();

        _logger.Debug(
            $"[Track] album=\"{albumFolder}\" disc={resolution.Media.Position} " +
            $"track={resolution.Track.Number} title=\"{resolution.Track.Title}\" " +
            $"recordingId={resolution.Track.RecordingMbid}");
    }

    /// <summary>
    /// 调用专辑定位服务。MusicBrainz 不可达/超时返回 null(不写缓存、不产生结果);
    /// 用户取消则向上抛出,避免把取消吞成"未命中"并被缓存锁死。
    /// </summary>
    private async Task<AlbumSearchResult?> ResolveAlbumAsync(
        string albumFolder,
        string? artistFolder,
        AlbumDirectoryScan scan,
        PluginConfiguration config,
        CancellationToken ct)
    {
        try
        {
            return await _albumResolutionService.ResolveAsync(
                new AlbumResolutionRequest(
                    albumFolder,
                    artistFolder,
                    scan.Discs,
                    config.MusicBrainzBaseUrl),
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // MB 不可达/超时:不写缓存、不产生结果(条目保持现状)
            _logger.Warn(
                $"[Resolve] album=\"{albumFolder}\" artist=\"{artistFolder ?? string.Empty}\" " +
                $"result=unavailable error=\"{ex.Message}\"");
            return null;
        }
    }
}
