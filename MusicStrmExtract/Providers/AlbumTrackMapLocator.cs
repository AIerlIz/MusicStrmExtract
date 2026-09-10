using MediaBrowser.Model.Logging;
using MusicStrmExtract.Caching;
using MusicStrmExtract.Online;
using System.Collections.Concurrent;
using System.Globalization;

namespace MusicStrmExtract.Providers;

/// <summary>
/// 专辑定位结果的网络读取与缓存入口。
/// 一次专辑定位包含整张专辑的轨道映射;同专辑后续 strm 条目零请求直接命中缓存。
/// </summary>
internal sealed class AlbumTrackMapLocator : IAlbumResolutionService
{
    private readonly ILogger _logger;
    private readonly TtlCache<AlbumSearchResult> _cache;
    private readonly Func<string?, IMusicBrainzApi> _apiFactory;
    private readonly ConcurrentDictionary<string, Task<AlbumSearchResult>> _inflight =
        new(StringComparer.Ordinal);

    public AlbumTrackMapLocator(ILogger logger, TtlCache<AlbumSearchResult> cache)
        : this(
            logger,
            cache,
            baseUrl => new MusicBrainzApi(baseUrl))
    {
    }

    internal AlbumTrackMapLocator(
        ILogger logger,
        TtlCache<AlbumSearchResult> cache,
        Func<string?, IMusicBrainzApi> apiFactory)
    {
        _logger = logger;
        _cache = cache;
        _apiFactory = apiFactory;
    }

    public Task<AlbumSearchResult> ResolveAsync(
        AlbumResolutionRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ResolveCoreAsync(
            BuildCacheKey(
                request.AlbumFolder,
                request.ArtistFolder,
                request.LocalDiscs,
                request.MusicBrainzBaseUrl),
            request.AlbumFolder,
            request.ArtistFolder,
            request.LocalDiscs,
            request.MusicBrainzBaseUrl,
            ct);
    }

    private async Task<AlbumSearchResult> ResolveCoreAsync(
        string cacheKey,
        string albumFolder,
        string? artistFolder,
        IReadOnlyList<LocalDisc> localDiscs,
        string? musicBrainzBaseUrl,
        CancellationToken ct)
    {
        if (_cache.TryGet(cacheKey, out var cached))
            return cached;

        var task = _inflight.GetOrAdd(cacheKey, _ => SearchCoreAsync(
            cacheKey,
            albumFolder,
            artistFolder,
            localDiscs,
            musicBrainzBaseUrl,
            CancellationToken.None));
        _ = task.ContinueWith(
            _ => _inflight.TryRemove(
                new KeyValuePair<string, Task<AlbumSearchResult>>(cacheKey, task)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        try
        {
            return await task.WaitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            if (task.IsCompleted)
            {
                _ = _inflight.TryRemove(
                    new KeyValuePair<string, Task<AlbumSearchResult>>(cacheKey, task));
            }
        }
    }

    private async Task<AlbumSearchResult> SearchCoreAsync(
        string cacheKey,
        string albumFolder,
        string? artistFolder,
        IReadOnlyList<LocalDisc> localDiscs,
        string? musicBrainzBaseUrl,
        CancellationToken ct)
    {
        using var api = _apiFactory(
            string.IsNullOrWhiteSpace(musicBrainzBaseUrl) ? null : musicBrainzBaseUrl);
        var search = new AlbumSearch(api);
        var result = await search.SearchForTrackMapAsync(albumFolder, artistFolder, localDiscs, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        _cache.Set(cacheKey, result);
        _logger.Info($"[MusicStrmExtract] [LocalProvider] 专辑定位: '{albumFolder}' -> " +
            (result.Found
                ? $"'{result.Title}' releaseMBID={result.ReleaseMbid} 碟数={result.Medias.Count} 轨数={result.Medias.Sum(m => m.Tracks.Count)}"
                : "无命中/碟轨覆盖未通过"));
        return result;
    }

    internal static string BuildCacheKey(
        string albumFolder,
        string? artistFolder,
        IReadOnlyList<LocalDisc> localDiscs,
        string? musicBrainzBaseUrl)
    {
        // 对碟组按 DiscNumber 和 TrackNumbers 排序,保证目录枚举非确定性下缓存 Key 稳定
        var layout = string.Join("|", localDiscs
            .OrderBy(d => d.DiscNumber ?? int.MaxValue)
            .Select(d =>
                (d.DiscNumber?.ToString(CultureInfo.InvariantCulture) ?? "_")
                + ":"
                + string.Join("-", d.TrackNumbers.OrderBy(n => n))));
        var musicBrainzSource = string.IsNullOrWhiteSpace(musicBrainzBaseUrl)
            ? "official"
            : musicBrainzBaseUrl.Trim().TrimEnd('/');

        // 服务地址也进 key:切换镜像后不应继续命中旧镜像缓存的专辑定位结果。
        return $"{albumFolder}|{artistFolder}|{layout}|{musicBrainzSource}";
    }
}
