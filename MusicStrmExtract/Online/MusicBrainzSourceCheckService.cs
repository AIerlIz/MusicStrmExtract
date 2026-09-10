using System.Diagnostics;

namespace MusicStrmExtract.Online;

/// <summary>检查当前 MusicBrainz 来源是否可用，不修改库数据。</summary>
internal sealed class MusicBrainzSourceCheckService
{
    private readonly Func<string?, IMusicBrainzApi> _apiFactory;

    public MusicBrainzSourceCheckService(Func<string?, IMusicBrainzApi> apiFactory)
    {
        _apiFactory = apiFactory ?? throw new ArgumentNullException(nameof(apiFactory));
    }

    public async Task<string> CheckAsync(string? baseUrl, CancellationToken ct)
    {
        using var api = _apiFactory(string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl);
        var stopwatch = Stopwatch.StartNew();
        var releases = await api
            .SearchReleasesAsync("1989", "Taylor Swift", 1, ct)
            .ConfigureAwait(false);
        stopwatch.Stop();

        return releases.Count > 0
            ? $"连接成功，返回 {releases.Count} 条候选，耗时 {stopwatch.ElapsedMilliseconds} ms。"
            : $"连接成功，但未返回候选，耗时 {stopwatch.ElapsedMilliseconds} ms。";
    }
}
