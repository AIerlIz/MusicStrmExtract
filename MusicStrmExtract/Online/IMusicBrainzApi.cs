using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MusicStrmExtract.Online
{
    /// <summary>MusicBrainz WS/2 客户端的能力抽象;便于选版逻辑脱离网络单测。</summary>
    public interface IMusicBrainzApi : IDisposable
    {
        Task<JsonElement> GetRecordingAsync(string recordingMbid, CancellationToken ct);

        Task<JsonElement> SearchRecordingsAsync(string title, int limit, CancellationToken ct);

        Task<JsonElement> GetReleaseAsync(string releaseMbid, CancellationToken ct);

        Task<JsonElement> SearchReleasesAsync(string album, string? artist, int limit, CancellationToken ct);

        Task<JsonElement> GetReleaseGroupReleasesAsync(string rgMbid, CancellationToken ct);
    }
}
