using MediaBrowser.Model.Logging;
using MusicStrmExtract.Caching;
using MusicStrmExtract.Online;
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
    private readonly object _inflightGate = new();
    private readonly Dictionary<string, SharedSearch> _inflight =
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
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _apiFactory = apiFactory ?? throw new ArgumentNullException(nameof(apiFactory));
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

        var search = AcquireSharedSearch(cacheKey, albumFolder, artistFolder, localDiscs, musicBrainzBaseUrl);
        try
        {
            return await search.Task.WaitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            ReleaseSharedSearch(cacheKey, search);
        }
    }

    /// <summary>取得（或创建）该 cacheKey 的共享搜索，并登记一个等待者。</summary>
    private SharedSearch AcquireSharedSearch(
        string cacheKey,
        string albumFolder,
        string? artistFolder,
        IReadOnlyList<LocalDisc> localDiscs,
        string? musicBrainzBaseUrl)
    {
        lock (_inflightGate)
        {
            if (_inflight.TryGetValue(cacheKey, out var existing))
            {
                existing.AddWaiter();
                return existing;
            }

            SharedSearch? created = null;
            created = new SharedSearch(
                token => SearchCoreAsync(cacheKey, new AlbumResolutionRequest(
                    albumFolder,
                    artistFolder,
                    localDiscs,
                    musicBrainzBaseUrl), token),
                () => RemoveInflight(cacheKey, created!));

            _inflight.Add(cacheKey, created);
            created.AddWaiter();
            created.Start();
            return created;
        }
    }

    /// <summary>
    /// 撤销等待者；若这是最后一个等待者且共享搜索仍在进行，则移除登记并取消它，
    /// 避免无人等待的请求继续占用 MusicBrainz 限流额度。
    /// </summary>
    private void ReleaseSharedSearch(string cacheKey, SharedSearch search)
    {
        var cancelLastWaiter = false;
        lock (_inflightGate)
        {
            search.RemoveWaiter();
            if (search.CanCancel
                && _inflight.TryGetValue(cacheKey, out var currentSearch)
                && ReferenceEquals(currentSearch, search))
            {
                _ = _inflight.Remove(cacheKey);
                cancelLastWaiter = true;
            }
        }

        if (cancelLastWaiter)
            search.Cancel();
    }

    private void RemoveInflight(string cacheKey, SharedSearch search)
    {
        lock (_inflightGate)
        {
            if (_inflight.TryGetValue(cacheKey, out var currentSearch)
                && ReferenceEquals(currentSearch, search))
            {
                _ = _inflight.Remove(cacheKey);
            }
        }
    }

    private async Task<AlbumSearchResult> SearchCoreAsync(
        string cacheKey,
        AlbumResolutionRequest request,
        CancellationToken ct)
    {
        using var api = _apiFactory(
            string.IsNullOrWhiteSpace(request.MusicBrainzBaseUrl) ? null : request.MusicBrainzBaseUrl);
        var search = new AlbumSearch(api);
        var result = await search
            .SearchForTrackMapAsync(
                request.AlbumFolder,
                request.ArtistFolder,
                request.LocalDiscs,
                ct)
            .ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        _cache.Set(cacheKey, result);
        _logger.Info(FormatResolveLog(request, result));
        return result;
    }

    /// <summary>记录一次专辑定位结果;命中时附标题、MBID 与碟/轨统计。</summary>
    private static string FormatResolveLog(AlbumResolutionRequest request, AlbumSearchResult result)
    {
        var head = $"[Resolve] album=\"{request.AlbumFolder}\" artist=\"{request.ArtistFolder ?? string.Empty}\" " +
            $"result={(result.Found ? "found" : "not_found")}";
        if (!result.Found)
            return head;

        return head +
            $" title=\"{result.Title}\" releaseId={result.ReleaseMbid} " +
            $"releaseGroupId={result.ReleaseGroupMbid} discs={result.Medias.Count} " +
            $"tracks={result.Medias.Sum(m => m.Tracks.Count)}";
    }

    internal static string BuildCacheKey(
        string albumFolder,
        string? artistFolder,
        IReadOnlyList<LocalDisc> localDiscs,
        string? musicBrainzBaseUrl)
    {
        // 服务地址也进 key:切换镜像后不应继续命中旧镜像缓存的专辑定位结果。
        return $"{albumFolder}|{artistFolder}|{BuildDiscLayoutKey(localDiscs)}|{NormalizeBaseUrl(musicBrainzBaseUrl)}";
    }

    /// <summary>
    /// 把本地碟组编码成缓存键片段。对碟组按 DiscNumber 与轨号排序,
    /// 保证目录枚举顺序非确定时同一专辑仍得到相同 key。
    /// </summary>
    private static string BuildDiscLayoutKey(IReadOnlyList<LocalDisc> localDiscs)
    {
        return string.Join("|", localDiscs
            .OrderBy(d => d.DiscNumber ?? int.MaxValue)
            .Select(d =>
                (d.DiscNumber?.ToString(CultureInfo.InvariantCulture) ?? "_")
                + ":"
                + string.Join("-", d.TrackNumbers.OrderBy(n => n))));
    }

    /// <summary>空地址代表官方服务,统一归一为 "official";非空地址去掉尾部斜杠。</summary>
    private static string NormalizeBaseUrl(string? musicBrainzBaseUrl)
    {
        return string.IsNullOrWhiteSpace(musicBrainzBaseUrl)
            ? "official"
            : musicBrainzBaseUrl.Trim().TrimEnd('/');
    }

    private sealed class SharedSearch : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly Func<CancellationToken, Task<AlbumSearchResult>> _search;
        private readonly Action _onCompleted;
        private Task<AlbumSearchResult>? _task;
        private int _waiters;

        public SharedSearch(
            Func<CancellationToken, Task<AlbumSearchResult>> search,
            Action onCompleted)
        {
            _search = search ?? throw new ArgumentNullException(nameof(search));
            _onCompleted = onCompleted ?? throw new ArgumentNullException(nameof(onCompleted));
        }

        public Task<AlbumSearchResult> Task =>
            _task ?? throw new InvalidOperationException("Shared search has not been started.");

        public bool CanCancel => _waiters == 0 && _task is { IsCompleted: false };

        public void Start()
        {
            if (_task is not null)
                throw new InvalidOperationException("Shared search has already been started.");

            _task = RunAsync();
        }

        public void AddWaiter()
        {
            _waiters++;
        }

        public void RemoveWaiter()
        {
            if (_waiters <= 0)
                throw new InvalidOperationException("No shared search waiter to remove.");

            _waiters--;
        }

        public void Cancel()
        {
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The shared work completed between the last-waiter check and cancellation.
            }
        }

        public void Dispose()
        {
            _cts.Dispose();
        }

        private async Task<AlbumSearchResult> RunAsync()
        {
            try
            {
                return await _search(_cts.Token).ConfigureAwait(false);
            }
            finally
            {
                _onCompleted();
                Dispose();
            }
        }
    }
}
