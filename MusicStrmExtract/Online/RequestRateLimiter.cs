using System.Threading;

namespace MusicStrmExtract.Online;

/// <summary>请求门在释放前必须保持占用,保证"间隔等待 + 完整 HTTP 请求"不会被并发打穿。</summary>
internal interface IRequestGate
{
    Task<IDisposable> AcquireAsync(CancellationToken ct);
}

internal sealed class RequestRateLimiter : IRequestGate, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _minimumInterval;
    private readonly Func<DateTime> _clock;
    private DateTime _lastRequestUtc;

    public RequestRateLimiter(TimeSpan minimumInterval, Func<DateTime> clock)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumInterval, TimeSpan.Zero);
        _minimumInterval = minimumInterval;
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    public async Task<IDisposable> AcquireAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = _clock();
            var elapsed = now - _lastRequestUtc;
            if (elapsed < _minimumInterval)
                await Task.Delay(_minimumInterval - elapsed, ct).ConfigureAwait(false);

            _lastRequestUtc = _clock();
            return new Lease(_gate);
        }
        catch
        {
            _gate.Release();
            throw;
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
    }

    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                gate.Release();
        }
    }
}

/// <summary>全插件共享的限流器:多 MusicBrainzApi 实例共用一个静态门,与历史实现一致。</summary>
internal sealed class StaticMusicBrainzRateGate : IRequestGate
{
    private const int MinimumRequestIntervalMs = 1100;

    private static readonly RequestRateLimiter s_limiter = new(
        TimeSpan.FromMilliseconds(MinimumRequestIntervalMs),
        () => DateTime.UtcNow);

    public static readonly StaticMusicBrainzRateGate Instance = new();

    public Task<IDisposable> AcquireAsync(CancellationToken ct)
    {
        return s_limiter.AcquireAsync(ct);
    }
}
