using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Logging;
using MusicStrmExtract.Caching;
using MusicStrmExtract.Online;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MusicStrmExtract.Providers
{
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
        private readonly AlbumTrackMapLocator _albumLocator;
        private readonly IMusicStrmConfigurationSource _configurationSource;

        /// <summary>专辑定位结果缓存(键=专辑文件夹|艺人文件夹|碟布局|服务地址;TTL 30 分钟,容量 500)。
        /// 过期项按插入序惰性清理,超容量只淘汰最旧条目。</summary>
        private static readonly TtlCache<AlbumSearchResult> AlbumCache =
            new TtlCache<AlbumSearchResult>(TimeSpan.FromMinutes(30), CacheMaxEntries);

        private const int CacheMaxEntries = 500;

        public MusicStrmLocalProvider(ILogManager logManager)
            : this(logManager, MusicStrmConfigurationSource.Default)
        {
        }

        internal MusicStrmLocalProvider(
            ILogManager logManager,
            IMusicStrmConfigurationSource configurationSource)
        {
            _logger = logManager.GetLogger("MusicStrmExtract");
            _configurationSource = configurationSource;
            _albumLocator = new AlbumTrackMapLocator(_logger, AlbumCache);
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
            if (!string.IsNullOrWhiteSpace(albumFolder)
                && !string.IsNullOrWhiteSpace(albumDir)
                && await TryResolveByAlbumTrackAsync(
                    info, albumFolder, artistFolder, albumDir, discNumber, config, result, cancellationToken).ConfigureAwait(false))
                return result;

            return result;
        }

        // ==================== 主路径:专辑轨道定位 ====================

        private async Task<bool> TryResolveByAlbumTrackAsync(
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
                return false; // 本文件无轨号,无法按轨取数

            var scan = AlbumDirectoryScanner.Scan(albumDir, message => _logger.Warn(message));
            if (scan.Discs.Count == 0)
                return false;

            AlbumSearchResult album;
            try
            {
                album = await _albumLocator.GetOrSearchAsync(
                    AlbumTrackMapLocator.BuildCacheKey(albumFolder, artistFolder, scan.Discs, config),
                    albumFolder,
                    artistFolder,
                    scan.Discs,
                    config,
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException && ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                // MB 不可达/超时:不写缓存、不产生结果(条目保持现状)
                _logger.Warn($"[MusicStrmExtract] [LocalProvider] 专辑定位 MB 不可达: '{albumFolder}' -> {ex.Message}");
                return false;
            }

            if (!album.Found)
                return false;

            var resolution = AudioTrackMetadataFactory.TryBuild(
                album,
                scan,
                info.Path,
                folderDisc,
                fileDisc,
                rawTrackNumber,
                isCommentary);
            if (resolution is null)
                return false;

            result.Item = resolution.Item;
            result.HasMetadata = true;
            ct.ThrowIfCancellationRequested();

            _logger.Info($"[MusicStrmExtract] [LocalProvider] 专辑轨道定位: '{albumFolder}' " +
                $"碟 {resolution.Media.Position} 轨 {resolution.Track.Number} '{resolution.Track.Title}' " +
                $"recordingMBID={resolution.Track.RecordingMbid}");
            return true;
        }
    }
}
