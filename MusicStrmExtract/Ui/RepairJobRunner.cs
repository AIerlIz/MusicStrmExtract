using System.Diagnostics.CodeAnalysis;

namespace MusicStrmExtract.Ui;

/// <summary>负责旧库修复后台任务的生命周期、防重入、取消和进度回写。</summary>
internal sealed class RepairJobRunner : IDisposable
{
    private readonly Func<IProgress<string>?, CancellationToken, string> _run;
    private readonly Action<string> _report;
    private CancellationTokenSource? _cts;
    private Task? _completion;
    private int _running;

    public RepairJobRunner(
        Func<IProgress<string>?, CancellationToken, string> run,
        Action<string> report)
    {
        _run = run ?? throw new ArgumentNullException(nameof(run));
        _report = report ?? throw new ArgumentNullException(nameof(report));
    }

    internal Task? Completion => _completion;

    public bool TryStart()
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            return false;

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _report("修复已开始，正在读取媒体库...");
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
    private void RunCore(CancellationToken ct)
    {
        try
        {
            var progress = new SynchronousProgress<string>(message =>
            {
                if (!ct.IsCancellationRequested)
                    _report(message);
            });

            var result = _run(progress, ct);
            if (!ct.IsCancellationRequested)
                _report(result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
                _report("修复失败: " + ex.Message);
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
