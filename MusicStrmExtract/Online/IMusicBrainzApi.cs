namespace MusicStrmExtract.Online;

/// <summary>MusicBrainz WS/2 客户端的能力抽象;便于选版逻辑脱离网络单测。</summary>
public interface IMusicBrainzApi : IDisposable
{
    Task<ParsedRelease> GetReleaseAsync(string releaseMbid, CancellationToken ct);

    Task<IReadOnlyList<ScoredRelease>> SearchReleasesAsync(string album, string? artist, int limit, CancellationToken ct);

    Task<ParsedReleaseGroup> GetReleaseGroupAsync(string rgMbid, CancellationToken ct);
}
