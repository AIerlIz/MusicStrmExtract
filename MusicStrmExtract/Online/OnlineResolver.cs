using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using MusicStrmExtract.Metadata;

namespace MusicStrmExtract.Online
{
    /// <summary>
    /// 在线元数据解析编排:
    ///   1. 内嵌标签带可信 MBID → MusicBrainz recording 精确取回(标题归一一致才判 Exact);
    ///   2. 无 MBID 或 MBID 与标题不符 → 标题+艺术家文本搜索 MusicBrainz(唯一高置信=Unique, 多候选=Ambiguous);
    ///   3. MusicBrainz 不可达/无结果 → iTunes Search 兜底(仅补专辑/年份/封面, 不覆盖标题)。
    /// </summary>
    public sealed class OnlineResolver : IDisposable
    {
        private readonly MusicBrainzApi _musicBrainz;
        private readonly ITunesApi _iTunes;
        private bool _musicBrainzDown;

        public OnlineResolver(string? musicBrainzBaseUrl = null)
        {
            _musicBrainz = new MusicBrainzApi(musicBrainzBaseUrl);
            _iTunes = new ITunesApi();
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
                    _musicBrainzDown = true;
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
                    _musicBrainzDown = true;
                }
            }

            // 路径 3: iTunes 兜底
            return await ResolveByITunesAsync(embedded, ct).ConfigureAwait(false);
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
                Source = "MusicBrainz",
                RecordingMbid = trackMbid
            };

            online.Fields.Title = recordingTitle;
            ApplyArtistCredit(online, root);
            var release = PickRelease(root, embedded);
            if (release is not null)
            {
                online.ReleaseMbid = GetString(release.Value, "id");
                online.Fields.Album = GetString(release.Value, "title");
                online.Fields.Year = ParseYear(GetString(release.Value, "date"));
                online.Fields.MusicBrainzAlbumId = online.ReleaseMbid;
                online.CoverArtUrl = string.IsNullOrEmpty(online.ReleaseMbid)
                    ? null
                    : $"https://coverartarchive.org/release/{online.ReleaseMbid}/front-500";

                if (release.Value.TryGetProperty("release-group", out var rg))
                {
                    online.ReleaseGroupMbid = GetString(rg, "id");
                    online.Fields.MusicBrainzReleaseGroupId = online.ReleaseGroupMbid;
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

            var root = await _musicBrainz.SearchRecordingsAsync(title, artist, embedded.Album, 8, ct).ConfigureAwait(false);
            if (!root.TryGetProperty("recordings", out var recordings) || recordings.GetArrayLength() == 0)
            {
                return new OnlineMetadata { Kind = OnlineMatchKind.None, Note = "MusicBrainz 无结果" };
            }

            var wanted = MergePolicy.NormalizeTitle(title);
            var candidates = recordings.EnumerateArray().ToList();
            var scored = candidates
                .Select(r => new { Item = r, Score = GetInt(r, "score"), TitleMatch = string.Equals(MergePolicy.NormalizeTitle(GetString(r, "title") ?? string.Empty), wanted, StringComparison.Ordinal) })
                .Where(x => x.Score > 0 && x.TitleMatch)
                .ToList();

            if (scored.Count == 0)
            {
                return new OnlineMetadata
                {
                    Kind = OnlineMatchKind.None,
                    Note = $"文本搜索无标题一致候选(count={candidates.Count})"
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

            // 即使 Ambiguous 也带出候选信息(供日志),但合并层不会采用
            online.Fields.Title = GetString(best.Item, "title");
            online.RecordingMbid = GetString(best.Item, "id");
            ApplyArtistCredit(online, best.Item);
            var release = PickRelease(best.Item, embedded);
            if (release is not null)
            {
                online.ReleaseMbid = GetString(release.Value, "id");
                online.Fields.Album = GetString(release.Value, "title");
                online.Fields.Year = ParseYear(GetString(release.Value, "date"));
                online.Fields.MusicBrainzAlbumId = online.ReleaseMbid;
                online.CoverArtUrl = string.IsNullOrEmpty(online.ReleaseMbid)
                    ? null
                    : $"https://coverartarchive.org/release/{online.ReleaseMbid}/front-500";
                if (release.Value.TryGetProperty("release-group", out var rg))
                {
                    online.ReleaseGroupMbid = GetString(rg, "id");
                    online.Fields.MusicBrainzReleaseGroupId = online.ReleaseGroupMbid;
                }
            }

            return online;
        }

        private async Task<OnlineMetadata> ResolveByITunesAsync(TrackMetadata embedded, CancellationToken ct)
        {
            var title = embedded.Title;
            var artist = embedded.Artists.FirstOrDefault() ?? embedded.AlbumArtists.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(title))
            {
                return new OnlineMetadata { Kind = OnlineMatchKind.None, Note = "iTunes 兜底:无标题" };
            }

            try
            {
                var root = await _iTunes.SearchSongAsync(artist ?? string.Empty, title, 8, ct).ConfigureAwait(false);
                if (!root.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                {
                    return new OnlineMetadata { Kind = OnlineMatchKind.None, Note = "iTunes 兜底:无结果" };
                }

                var top = results.EnumerateArray().First();
                var collectionName = GetString(top, "collectionName");
                if (string.IsNullOrEmpty(collectionName))
                {
                    return new OnlineMetadata { Kind = OnlineMatchKind.None, Note = "iTunes 兜底:结果缺专辑名" };
                }

                var online = new OnlineMetadata
                {
                    Kind = OnlineMatchKind.ITunesFallback,
                    Source = "iTunes",
                    Note = $"iTunes 兜底命中 track='{GetString(top, "trackName")}' album='{collectionName}'"
                };
                online.Fields.Album = collectionName;
                online.Fields.Year = ParseYear(GetString(top, "releaseDate"));
                online.CoverArtUrl = ITunesApi.UpgradeArtworkUrl(GetString(top, "artworkUrl100"));
                return online;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                return new OnlineMetadata { Kind = OnlineMatchKind.None, Note = $"iTunes 兜底失败: {ex.Message}" };
            }
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
                        if (online.ArtistMbid is null)
                        {
                            online.ArtistMbid = GetString(artistEl, "id");
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
            online.AlbumArtistMbid = online.ArtistMbid;
            online.Fields.MusicBrainzArtistId = online.ArtistMbid;
            online.Fields.MusicBrainzAlbumArtistId = online.ArtistMbid;
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

        private static string? GetString(JsonElement element, string property)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }

            return null;
        }

        private static int GetInt(JsonElement element, string property)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value))
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
                {
                    return number;
                }

                if (value.ValueKind == JsonValueKind.String
                    && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }
            }

            return 0;
        }

        private static int? ParseYear(string? date)
        {
            if (string.IsNullOrWhiteSpace(date))
            {
                return null;
            }

            var m = System.Text.RegularExpressions.Regex.Match(date, @"\b(1[89]\d{2}|20\d{2})\b");
            return m.Success ? int.Parse(m.Value, CultureInfo.InvariantCulture) : null;
        }

        public void Dispose()
        {
            _musicBrainz.Dispose();
            _iTunes.Dispose();
        }
    }
}
