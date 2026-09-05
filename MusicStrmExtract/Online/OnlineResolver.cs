using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using MusicStrmExtract.Metadata;
using static MusicStrmExtract.Online.JsonUtil;

namespace MusicStrmExtract.Online
{
    /// <summary>
    /// 在线元数据解析编排:
    ///   1. 内嵌标签带可信 MBID → MusicBrainz recording 精确取回(标题归一一致才判 Exact);
    ///   2. 无 MBID 或 MBID 与标题不符 → 标题+艺术家文本搜索 MusicBrainz(唯一高置信=Unique, 多候选=Ambiguous);
    ///   3. MusicBrainz 不可达/无结果 → 不产生在线命中,保留内嵌字段(无 iTunes 兜底,需自备 MB 连通)。
    /// </summary>
    public sealed class OnlineResolver : IDisposable
    {
        private readonly IMusicBrainzApi _musicBrainz;
        private bool _musicBrainzDown;

        public OnlineResolver(IMusicBrainzApi musicBrainz)
        {
            _musicBrainz = musicBrainz;
        }

        public OnlineResolver(string? musicBrainzBaseUrl = null)
            : this(new MusicBrainzApi(musicBrainzBaseUrl))
        {
        }

        public async Task<OnlineMetadata> ResolveAsync(TrackMetadata embedded, CancellationToken ct)
        {
            // 路径 1: MBID 精确(若 MusicBrainz 本会话已熔断则跳过)
            if (!_musicBrainzDown && !string.IsNullOrWhiteSpace(embedded.MusicBrainzTrackId))
            {
                try
                {
                    var result = await ResolveByTrackMbidAsync(embedded, embedded.MusicBrainzTrackId, ct).ConfigureAwait(false);
                    if (result.Kind == OnlineMatchKind.ExactByMbid)
                    {
                        return result;
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
                {
                    // 404 = 该 MBID/查询在 MusicBrainz 无对应条目(脏 ID 属业务性无结果),不熔断,继续文本搜索;
                    // 网络不可达/超时才熔断本会话
                    if (!(ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound }))
                    {
                        _musicBrainzDown = true;
                    }
                }
            }

            // 路径 2: 文本搜索 MusicBrainz
            if (!_musicBrainzDown)
            {
                try
                {
                    var textResult = await ResolveByTextSearchAsync(embedded, ct).ConfigureAwait(false);
                    if (textResult.Kind != OnlineMatchKind.None)
                    {
                        return textResult;
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
                {
                    // 搜索 404(无此查询结果)不熔断;网络/超时才熔断
                    if (!(ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound }))
                    {
                        _musicBrainzDown = true;
                    }
                }
            }

            // 路径 3: 无 iTunes 兜底 —— MusicBrainz 不可达/无结果时不产生在线命中
            return new OnlineMetadata
            {
                Kind = OnlineMatchKind.None,
                MusicBrainzUnavailable = _musicBrainzDown,
                Note = _musicBrainzDown
                    ? "MusicBrainz 本会话不可达(可能已熔断),无在线补全"
                    : "MusicBrainz 无结果,无在线补全(用户约定不使用 iTunes)"
            };
        }

        private async Task<OnlineMetadata> ResolveByTrackMbidAsync(TrackMetadata embedded, string trackMbid, CancellationToken ct)
        {
            var root = await _musicBrainz.GetRecordingAsync(trackMbid, ct).ConfigureAwait(false);

            var recordingTitle = GetString(root, "title");
            if (string.IsNullOrEmpty(recordingTitle))
            {
                return new OnlineMetadata { Kind = OnlineMatchKind.None };
            }

            // MBID 必须与内嵌标题一致才可信(样本库曾出现整张专辑复用同一 track MBID 的脏数据)
            if (!string.IsNullOrWhiteSpace(embedded.Title)
                && !string.Equals(MergePolicy.NormalizeTitle(recordingTitle), MergePolicy.NormalizeTitle(embedded.Title), StringComparison.Ordinal))
            {
                return new OnlineMetadata
                {
                    Kind = OnlineMatchKind.None,
                    Note = $"track MBID {trackMbid} 对应 '{recordingTitle}' 与内嵌标题 '{embedded.Title}' 不符,弃用 MBID 转文本匹配"
                };
            }

            var online = new OnlineMetadata
            {
                Kind = OnlineMatchKind.ExactByMbid,
                Source = "MusicBrainz"
            };
            online.Fields.MusicBrainzTrackId = trackMbid;

            online.Fields.Title = recordingTitle;
            ApplyArtistCredit(online, root);
            var release = PickRelease(root, embedded);
            if (release is not null)
            {
                var releaseMbid = GetString(release.Value, "id");
                online.Fields.Album = GetString(release.Value, "title");
                online.Fields.Year = ParseYear(GetString(release.Value, "date"));
                online.Fields.MusicBrainzAlbumId = releaseMbid;

                if (release.Value.TryGetProperty("release-group", out var rg))
                {
                    online.Fields.MusicBrainzReleaseGroupId = GetString(rg, "id");
                    if (online.Fields.Year is null)
                    {
                        online.Fields.Year = ParseYear(GetString(rg, "first-release-date"));
                    }
                }
            }

            online.Note = $"MBID 精确命中: {recordingTitle}";
            return online;
        }

        private async Task<OnlineMetadata> ResolveByTextSearchAsync(TrackMetadata embedded, CancellationToken ct)
        {
            var title = embedded.Title;
            var artist = embedded.Artists.FirstOrDefault() ?? embedded.AlbumArtists.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(title))
            {
                return new OnlineMetadata { Kind = OnlineMatchKind.None, Note = "内嵌无标题,无法文本搜索" };
            }

            var root = await _musicBrainz.SearchRecordingsAsync(title, 8, ct).ConfigureAwait(false);
            if (!root.TryGetProperty("recordings", out var recordings) || recordings.GetArrayLength() == 0)
            {
                return new OnlineMetadata { Kind = OnlineMatchKind.None, Note = "MusicBrainz 无结果" };
            }

            var wanted = MergePolicy.NormalizeTitle(title);
            var candidates = recordings.EnumerateArray().ToList();
            var scored = candidates
                .Select(r => new
                {
                    Item = r,
                    Score = GetInt(r, "score"),
                    TitleMatch = TitleSimilar(wanted, MergePolicy.NormalizeTitle(GetString(r, "title") ?? string.Empty)),
                    ArtistMatch = ArtistMatches(artist, GetArtistCreditNames(r))
                })
                .Where(x => x.Score > 0 && x.TitleMatch && x.ArtistMatch)
                .ToList();

            if (scored.Count == 0)
            {
                return new OnlineMetadata
                {
                    Kind = OnlineMatchKind.None,
                    Note = $"文本搜索无标题一致(且艺术家宽松匹配)候选(count={candidates.Count})"
                };
            }

            var best = scored[0];
            var unique = scored.Count == 1 && best.Score >= 90;

            var online = new OnlineMetadata
            {
                Kind = unique ? OnlineMatchKind.UniqueTextMatch : OnlineMatchKind.AmbiguousTextMatch,
                Source = "MusicBrainz",
                Note = unique
                    ? $"文本搜索唯一高置信(score={best.Score}, count={scored.Count})"
                    : $"文本搜索多候选(score={best.Score}, count={scored.Count}),模糊"
            };

            // 即使 Ambiguous 也带出 best 候选字段(在线优先策略下会被合并层采用覆盖内嵌)
            online.Fields.Title = GetString(best.Item, "title");
            online.Fields.MusicBrainzTrackId = GetString(best.Item, "id");
            ApplyArtistCredit(online, best.Item);
            var release = PickRelease(best.Item, embedded);
            if (release is not null)
            {
                var releaseMbid = GetString(release.Value, "id");
                online.Fields.Album = GetString(release.Value, "title");
                online.Fields.Year = ParseYear(GetString(release.Value, "date"));
                online.Fields.MusicBrainzAlbumId = releaseMbid;
                if (release.Value.TryGetProperty("release-group", out var rg))
                {
                    online.Fields.MusicBrainzReleaseGroupId = GetString(rg, "id");
                }
            }

            return online;
        }

        /// <summary>标题宽松相似:归一后全等,或较短者(≥3 个有效字符)为较长者子串
        /// (覆盖 "七里香" vs "七里香 (Qi-Li-Xiang)" 等带注释/附注形态;过短(1-2 字)不启用包含,防误配)。</summary>
        private static bool TitleSimilar(string wanted, string candidate)
        {
            if (string.Equals(wanted, candidate, StringComparison.Ordinal))
            {
                return true;
            }

            var shorter = wanted.Length <= candidate.Length ? wanted : candidate;
            var longer = wanted.Length <= candidate.Length ? candidate : wanted;
            return shorter.Length >= 3 && longer.Contains(shorter, StringComparison.Ordinal);
        }

        /// <summary>候选录音的 artist-credit 名称列表(优先 artist.name,缺省取 credit 项 name)。</summary>
        private static IEnumerable<string> GetArtistCreditNames(JsonElement recording)
        {
            if (!recording.TryGetProperty("artist-credit", out var credit) || credit.ValueKind != JsonValueKind.Array)
            {
                return Enumerable.Empty<string>();
            }

            return credit.EnumerateArray()
                .Select(item =>
                {
                    if (item.TryGetProperty("artist", out var artistEl) && artistEl.TryGetProperty("name", out var nm))
                    {
                        return nm.GetString();
                    }

                    return GetString(item, "name");
                })
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .ToList();
        }

        /// <summary>内嵌艺术家与候选艺术家名的宽松匹配:去括号内容/空白/大小写后互为子串即可
        /// (覆盖 "Jay Chou (周杰倫)" vs "Jay Chou"、"周杰倫" 等常见形态差异)。内嵌无艺术家则不限制。</summary>
        private static bool ArtistMatches(string? embeddedArtist, IEnumerable<string> candidateNames)
        {
            if (string.IsNullOrWhiteSpace(embeddedArtist))
            {
                return true;
            }

            var wanted = Compact(embeddedArtist);
            if (wanted.Length == 0)
            {
                return true;
            }

            foreach (var name in candidateNames)
            {
                var cand = Compact(name);
                if (cand.Length > 0
                    && (wanted.Contains(cand, StringComparison.Ordinal)
                        || cand.Contains(wanted, StringComparison.Ordinal)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>艺术家名归一:仅小写+去空白,**保留括号内容**(如 "Jay Chou (周杰倫)" → "jaychou(周杰倫)",
        /// 否则会丢掉中文名而无法与 MB 的 "周杰倫" 匹配)。标题比较才需要去括号(见 MergePolicy.NormalizeTitle)。</summary>
        private static string Compact(string value)
        {
            return new string(value.ToLowerInvariant().Where(c => !char.IsWhiteSpace(c)).ToArray());
        }

        private static void ApplyArtistCredit(OnlineMetadata online, JsonElement recording)
        {
            if (!recording.TryGetProperty("artist-credit", out var credit) || credit.GetArrayLength() == 0)
            {
                return;
            }

            foreach (var item in credit.EnumerateArray())
            {
                if (item.TryGetProperty("artist", out var artistEl))
                {
                    var name = GetString(artistEl, "name");
                    if (!string.IsNullOrEmpty(name))
                    {
                        online.Fields.Artists.Add(name);
                        if (online.Fields.MusicBrainzArtistId is null)
                        {
                            online.Fields.MusicBrainzArtistId = GetString(artistEl, "id");
                        }
                    }
                }

                if (item.TryGetProperty("name", out var joinEl))
                {
                    // 处理 joinphrase 的艺术家列表第一项已覆盖
                }
            }

            // artist-credit 整体即专辑艺术家
            online.Fields.AlbumArtists.AddRange(online.Fields.Artists);
            online.Fields.MusicBrainzAlbumArtistId = online.Fields.MusicBrainzArtistId;
        }

        private static JsonElement? PickRelease(JsonElement recording, TrackMetadata embedded)
        {
            if (!recording.TryGetProperty("releases", out var releases) || releases.GetArrayLength() == 0)
            {
                return null;
            }

            var list = releases.EnumerateArray().ToList();

            // 1) 专辑名归一相等
            if (!string.IsNullOrWhiteSpace(embedded.Album))
            {
                var wantedAlbum = MergePolicy.NormalizeTitle(embedded.Album);
                var byAlbum = list.FirstOrDefault(r => string.Equals(
                    MergePolicy.NormalizeTitle(GetString(r, "title") ?? string.Empty), wantedAlbum, StringComparison.Ordinal));
                if (byAlbum.ValueKind != JsonValueKind.Undefined)
                {
                    return byAlbum;
                }
            }

            // 2) 年份匹配
            if (embedded.Year is not null)
            {
                var byYear = list.FirstOrDefault(r => ParseYear(GetString(r, "date")) == embedded.Year);
                if (byYear.ValueKind != JsonValueKind.Undefined)
                {
                    return byYear;
                }
            }

            return list.Count > 0 ? list[0] : (JsonElement?)null;
        }

        public void Dispose()
        {
            _musicBrainz.Dispose();
        }
    }
}
