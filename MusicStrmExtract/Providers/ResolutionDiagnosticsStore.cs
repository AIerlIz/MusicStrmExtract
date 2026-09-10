using MusicStrmExtract.Online;

namespace MusicStrmExtract.Providers;

internal enum AlbumResolutionOutcome
{
    Found,
    NotFound,
    Unavailable
}

internal sealed record AlbumResolutionDiagnostic(
    string AlbumFolder,
    string? ArtistFolder,
    DateTimeOffset RecordedAt,
    AlbumResolutionOutcome Outcome,
    string? ReleaseMbid,
    string? Title,
    int MediaCount,
    int TrackCount,
    string? Reason);

/// <summary>记录插件自身的专辑定位结果，供配置页展示，不保存到 Emby。</summary>
internal sealed class ResolutionDiagnosticsStore
{
    private const int DefaultMaxEntries = 100;

    private readonly object _gate = new();
    private readonly int _maxEntries;
    private readonly Dictionary<string, AlbumResolutionDiagnostic> _entries =
        new(StringComparer.Ordinal);
    private readonly List<string> _order = [];
    private long _foundCount;
    private long _notFoundCount;
    private long _unavailableCount;

    public ResolutionDiagnosticsStore(int maxEntries = DefaultMaxEntries)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntries);
        _maxEntries = maxEntries;
    }

    public void Record(
        AlbumResolutionRequest request,
        AlbumResolutionOutcome outcome,
        AlbumSearchResult? result = null,
        string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            switch (outcome)
            {
                case AlbumResolutionOutcome.Found:
                    _foundCount++;
                    break;
                case AlbumResolutionOutcome.NotFound:
                    _notFoundCount++;
                    break;
                case AlbumResolutionOutcome.Unavailable:
                    _unavailableCount++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown resolution outcome.");
            }

            var key = $"{request.AlbumFolder}|{request.ArtistFolder}";
            _ = _order.Remove(key);
            _order.Add(key);
            _entries[key] = new AlbumResolutionDiagnostic(
                request.AlbumFolder,
                request.ArtistFolder,
                DateTimeOffset.UtcNow,
                outcome,
                result?.ReleaseMbid,
                result?.Title,
                result?.Medias.Count ?? 0,
                result?.Medias.Sum(media => media.Tracks.Count) ?? 0,
                reason);

            while (_order.Count > _maxEntries)
            {
                var oldest = _order[0];
                _order.RemoveAt(0);
                _ = _entries.Remove(oldest);
            }
        }
    }

    public string GetSummary(int recentCount = 5)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(recentCount);

        lock (_gate)
        {
            if (_order.Count == 0)
                return "暂无定位记录。";

            var recent = _order
                .Skip(Math.Max(0, _order.Count - recentCount))
                .Select(key => Format(_entries[key]));

            return $"成功 {_foundCount}，未命中 {_notFoundCount}，来源异常 {_unavailableCount}。" +
                $" 最近：{string.Join("；", recent)}";
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _order.Clear();
            _foundCount = 0;
            _notFoundCount = 0;
            _unavailableCount = 0;
        }
    }

    private static string Format(AlbumResolutionDiagnostic diagnostic)
    {
        return diagnostic.Outcome switch
        {
            AlbumResolutionOutcome.Found =>
                $"'{diagnostic.AlbumFolder}' → '{diagnostic.Title}' release={diagnostic.ReleaseMbid} {diagnostic.MediaCount} 碟 {diagnostic.TrackCount} 轨",
            AlbumResolutionOutcome.NotFound =>
                $"'{diagnostic.AlbumFolder}' → 未命中",
            AlbumResolutionOutcome.Unavailable =>
                $"'{diagnostic.AlbumFolder}' → 来源异常" +
                (string.IsNullOrWhiteSpace(diagnostic.Reason) ? string.Empty : $"（{Truncate(diagnostic.Reason, 60)}）"),
            _ => diagnostic.AlbumFolder
        };
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}
