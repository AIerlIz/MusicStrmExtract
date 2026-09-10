using MediaBrowser.Model.Logging;
using MusicStrmExtract.Caching;
using MusicStrmExtract.Online;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;

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
    private readonly ResolutionDiagnosticsStore _diagnostics;
    private readonly object _inflightGate = new();
    private readonly Dictionary<string, SharedSearch> _inflight =
        new(StringComparer.Ordinal);

    public AlbumTrackMapLocator(ILogger logger, TtlCache<AlbumSearchResult> cache)
        : this(
            logger,
            cache,
            baseUrl => new MusicBrainzApi(baseUrl),
            new ResolutionDiagnosticsStore())
    {
    }

    internal AlbumTrackMapLocator(
        ILogger logger,
        TtlCache<AlbumSearchResult> cache,
        Func<string?, IMusicBrainzApi> apiFactory)
        : this(logger, cache, apiFactory, new ResolutionDiagnosticsStore())
    {
    }

    internal AlbumTrackMapLocator(
        ILogger logger,
        TtlCache<AlbumSearchResult> cache,
        Func<string?, IMusicBrainzApi> apiFactory,
        ResolutionDiagnosticsStore diagnostics)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _apiFactory = apiFactory ?? throw new ArgumentNullException(nameof(apiFactory));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
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

        SharedSearch search;
        lock (_inflightGate)
        {
            if (!_inflight.TryGetValue(cacheKey, out search!))
            {
                SharedSearch? created = null;
                created = new SharedSearch(
                    token => SearchCoreAsync(cacheKey, new AlbumResolutionRequest(
                        albumFolder,
                        artistFolder,
                        localDiscs,
                        musicBrainzBaseUrl), token),
                    () => RemoveInflight(cacheKey, created!));
                search = created;
                _inflight.Add(cacheKey, search);
                search.AddWaiter();
                search.Start();
            }
            else
            {
                search.AddWaiter();
            }
        }

        try
        {
            return await search.Task.WaitAsync(ct).ConfigureAwait(false);
        }
        finally
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
        try
        {
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
            _diagnostics.Record(
                request,
                result.Found ? AlbumResolutionOutcome.Found : AlbumResolutionOutcome.NotFound,
                result);
            _logger.Info($"[MusicStrmExtract] [LocalProvider] 专辑定位: '{request.AlbumFolder}' -> " +
                (result.Found
                    ? $"'{result.Title}' releaseMBID={result.ReleaseMbid} 碟数={result.Medias.Count} 轨数={result.Medias.Sum(m => m.Tracks.Count)}"
                    : "无命中/碟轨覆盖未通过"));
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _diagnostics.Record(
                request,
                AlbumResolutionOutcome.Unavailable,
                reason: ex.Message);
            throw;
        }
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
