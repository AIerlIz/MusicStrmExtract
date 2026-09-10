using System.Diagnostics.CodeAnalysis;

namespace MusicStrmExtract.Ui;

/// <summary>负责配置页后台任务的生命周期、防重入、取消和进度回写。</summary>
internal sealed class BackgroundJobRunner : IDisposable
{
    private readonly Func<IProgress<string>?, CancellationToken, Task<string>> _run;
    private readonly Action<string> _report;
    private readonly string _failurePrefix;
    private CancellationTokenSource? _cts;
    private Task? _completion;
    private int _running;

    public BackgroundJobRunner(
        Func<IProgress<string>?, CancellationToken, Task<string>> run,
        Action<string> report,
        string failurePrefix)
    {
        _run = run ?? throw new ArgumentNullException(nameof(run));
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _failurePrefix = failurePrefix ?? throw new ArgumentNullException(nameof(failurePrefix));
    }

    internal Task? Completion => _completion;

    public bool TryStart(string startMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startMessage);

        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            return false;

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _report(startMessage);
        _completion = Task.Run(() => RunCore(ct), CancellationToken.None);
        return true;
    }

    public async Task CancelAsync()
    {
        var cts = _cts;
        _cts = null;
        if (cts is not null)
        {
            await cts.CancelAsync().ConfigureAwait(false);
            cts.Dispose();
        }
    }

    public void Dispose()
    {
        _ = CancelAsync();
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A background UI command must surface unexpected failures without crashing the server process.")]
    private async Task RunCore(CancellationToken ct)
    {
        try
        {
            var progress = new SynchronousProgress<string>(message =>
            {
                if (!ct.IsCancellationRequested)
                    _report(message);
            });

            var result = await _run(progress, ct).ConfigureAwait(false);
            if (!ct.IsCancellationRequested)
                _report(result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
                _report($"{_failurePrefix}: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        private readonly Action<T> _handler = handler ?? throw new ArgumentNullException(nameof(handler));

        public void Report(T value)
        {
            _handler(value);
        }
    }
}
