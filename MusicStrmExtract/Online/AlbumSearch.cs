using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MusicStrmExtract.Online
{
    /// <summary>MB release 选定 media(碟)轨道映射中的一轨。</summary>
    public sealed class AlbumTrack
    {
        /// <summary>轨号(MB track number/position)。</summary>
        public int Number { get; set; }

        /// <summary>官方轨标题。</summary>
        public string? Title { get; set; }

        /// <summary>recording MBID(真实、无脏 ID)。</summary>
        public string? RecordingMbid { get; set; }

        /// <summary>该轨艺人(recording artist-credit;合辑场景与专辑艺人不同)。</summary>
        public List<string> Artists { get; } = new List<string>();

        /// <summary>该轨艺人 MBID(artist-credit 首个)。</summary>
        public string? ArtistMbid { get; set; }

    }

    /// <summary>release 响应(inc=recordings)中解析出的一张 media(碟)。</summary>
    public sealed class ReleaseMedia
    {
        /// <summary>碟序号(media.position,1 起)。</summary>
        public int Position { get; set; }

        /// <summary>该碟轨道(按 Number 升序)。</summary>
        public List<AlbumTrack> Tracks { get; } = new List<AlbumTrack>();
    }

    /// <summary>轨道映射搜索的结果(本地指纹校验通过后填充)。</summary>
    public sealed class AlbumSearchResult
    {
        /// <summary>是否找到可信命中(搜索有候选,且本地指纹校验通过)。</summary>
        public bool Found { get; set; }

        /// <summary>MB 官方专辑名(release.title)。</summary>
        public string? Title { get; set; }

        /// <summary>年份(release date 前四位)。</summary>
        public int? Year { get; set; }

        public string? ReleaseMbid { get; set; }

        public string? ReleaseGroupMbid { get; set; }

        /// <summary>专辑艺人(MB artist-credit 首个名字)。</summary>
        public string? ArtistName { get; set; }

        /// <summary>专辑艺人 MBID(artist-credit 首个 id)。</summary>
        public string? AlbumArtistMbid { get; set; }

        /// <summary>选定 media 的轨道映射(按轨号升序;本专辑整张一次取得)。</summary>
        public List<AlbumTrack> Tracks { get; } = new List<AlbumTrack>();
    }

    /// <summary>
    /// "按艺人 + 专辑文件夹名锁定 MusicBrainz release":把专辑文件夹名(如 "叶惠美 (2003)")净化成核心名("叶惠美"),
    /// 用 release:"专辑" AND artist:"艺人" 查询取回 release,再用本地轨号覆盖校验/选择 media,
    /// 返回该专辑的完整轨道映射(每轨 recording MBID/标题)——单曲按轨号直接取数,无需文件名标题匹配。
    /// 目录名原样透传给 MusicBrainz,不参与本地文本比较;版本选择稳定(Official 优先 → Album 主类型 → 完整日期优先)。
    /// </summary>
    public sealed class AlbumSearch
    {
        private readonly MusicBrainzApi _api;

        public AlbumSearch(MusicBrainzApi api)
        {
            _api = api;
        }

        /// <summary>去除专辑名中的年份/附加括号等,得到核心名:"叶惠美 (2003)"→"叶惠美","七里香-2004"→"七里香"。</summary>
        public static string? CleanAlbumName(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            var s = raw.Trim();
            s = Regex.Replace(s, @"[\s_\-\.]*[\(\[（【]?\s*(18|19|20)\d{2}\s*[\)\]）】]?\s*$", string.Empty);
            s = Regex.Replace(s, @"[\s\-\._]+$", string.Empty);
            return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        }

        /// <summary>
        /// 专辑轨道映射搜索:搜索 + 取 tracklist + 本地指纹校验选碟。
        /// 未找到 release、或所选 release 的 media 均未覆盖本地轨号时返回 Found=false
        /// (调用方降级到探测路径)。
        /// </summary>
        public async Task<AlbumSearchResult> SearchForTrackMapAsync(
            string albumFolderName,
            string? artistName,
            IReadOnlyList<int> localTrackNumbers,
            CancellationToken ct)
        {
            var result = new AlbumSearchResult();
            var clean = CleanAlbumName(albumFolderName);
            if (string.IsNullOrWhiteSpace(clean) || localTrackNumbers is null || localTrackNumbers.Count == 0)
            {
                return result;
            }

            var root = await _api.SearchReleasesAsync(clean, artistName, 10, ct).ConfigureAwait(false);
            if (!root.TryGetProperty("releases", out var releases) || releases.GetArrayLength() == 0)
            {
                return result;
            }

            var scored = releases.EnumerateArray().ToList()
                .Select(r => new
                {
                    Item = r,
                    Score = GetInt(r, "score"),
                    Title = GetString(r, "title"),
                    Date = GetString(r, "date"),
                    Status = GetString(r, "status"),
                    PrimaryType = GetPrimaryType(r),
                    Artists = GetArtistCreditNames(r)
                })
                .Where(x => x.Score > 0 && !string.IsNullOrWhiteSpace(x.Title))
                .ToList();

            if (scored.Count == 0)
            {
                return result;
            }

            // 本地目录文本已作为查询条件交给 MB,不再做字形过滤;稳定排序只按 MB 元数据:
            // status(Official 0 < Pseudo-Release 3) -> 主类型 Album -> 完整日期优先 -> 日期最早
            // (空日期排最后)-> score 降序 -> 官方名(字典序)
            var best = scored
                .OrderBy(x => StatusRank(x.Status))
                .ThenBy(x => string.Equals(x.PrimaryType, "Album", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(x => IsIncompleteDate(x.Date) ? 1 : 0)
                .ThenBy(x => string.IsNullOrWhiteSpace(x.Date) ? "9999" : x.Date!)
                .ThenByDescending(x => x.Score)
                .ThenBy(x => x.Title, StringComparer.Ordinal)
                .First();

            // 取 tracklist,用本地轨号覆盖校验并选择 media。
            // 注意:release 详情取回的网络/解析异常向上抛,由调用方区分"MB 不可达(不缓存)"与"确认未命中";
            // 吞成 Found=false 会让暂时性故障被 30 分钟缓存锁死。
            var releaseMbid = GetString(best.Item, "id");
            if (string.IsNullOrWhiteSpace(releaseMbid))
            {
                return result;
            }

            var releaseRoot = await _api.GetReleaseAsync(releaseMbid, ct).ConfigureAwait(false);

            var medias = ParseReleaseMedias(releaseRoot);
            var chosen = SelectBestMedia(localTrackNumbers, medias);
            if (chosen is null)
            {
                return result;
            }

            result.Found = true;
            result.Title = best.Title;
            result.Year = ParseYear(best.Date);
            result.ReleaseMbid = releaseMbid;
            result.ArtistName = best.Artists.FirstOrDefault();
            var artistIds = GetArtistCreditIds(best.Item);
            result.AlbumArtistMbid = artistIds.FirstOrDefault();
            if (best.Item.TryGetProperty("release-group", out var rg))
            {
                result.ReleaseGroupMbid = GetString(rg, "id");
            }

            foreach (var t in chosen.Tracks)
            {
                result.Tracks.Add(t);
            }

            return result;
        }

        /// <summary>
        /// 解析 release 响应(inc=recordings)中的 media 轨道列表(含每轨 recording 数据)。
        /// </summary>
        public static List<ReleaseMedia> ParseReleaseMedias(JsonElement releaseRoot)
        {
            var medias = new List<ReleaseMedia>();
            if (!releaseRoot.TryGetProperty("media", out var mediaArr) || mediaArr.ValueKind != JsonValueKind.Array)
            {
                return medias;
            }

            foreach (var m in mediaArr.EnumerateArray())
            {
                var media = new ReleaseMedia { Position = GetInt(m, "position") };
                if (m.TryGetProperty("tracks", out var tracks) && tracks.ValueKind == JsonValueKind.Array)
                {
                    foreach (var t in tracks.EnumerateArray())
                    {
                        var track = new AlbumTrack();
                        var numberText = GetString(t, "number");
                        track.Number = int.TryParse(numberText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                            ? n
                            : GetInt(t, "position");
                        track.Title = GetString(t, "title");
                        if (t.TryGetProperty("recording", out var rec))
                        {
                            track.RecordingMbid = GetString(rec, "id");
                            if (track.Title is null)
                            {
                                track.Title = GetString(rec, "title");
                            }

                            ApplyArtistCredit(track, rec);
                        }

                        if (track.Number > 0)
                        {
                            media.Tracks.Add(track);
                        }
                    }
                }

                media.Tracks.Sort((a, b) => a.Number.CompareTo(b.Number));
                if (media.Tracks.Count > 0)
                {
                    medias.Add(media);
                }
            }

            return medias;
        }

        /// <summary>
        /// 本地轨号覆盖校验候选 media:
        ///   判定:media 必须包含本地全部轨号(本地为普通版且 MB 为含 bonus 的版本也通过;
        ///   MV/附加碟因轨号缺失被淘汰)。本地轨号只来自文件名数字前缀,不读取文件名标题。
        /// 在通过的 media 中取 Position 最小者;无通过返回 null。
        /// </summary>
        public static ReleaseMedia? SelectBestMedia(IReadOnlyList<int> localTrackNumbers, IReadOnlyList<ReleaseMedia> medias)
        {
            if (localTrackNumbers is null || localTrackNumbers.Count == 0 || medias is null || medias.Count == 0)
            {
                return null;
            }

            var localNumbers = localTrackNumbers
                .Where(n => n > 0)
                .Distinct()
                .ToArray();
            if (localNumbers.Length == 0)
            {
                return null;
            }

            ReleaseMedia? best = null;
            foreach (var media in medias.OrderBy(m => m.Position))
            {
                var mediaNumbers = media.Tracks
                    .Select(t => t.Number)
                    .Where(n => n > 0)
                    .ToHashSet();
                if (localNumbers.All(mediaNumbers.Contains))
                {
                    best = media;
                    break;
                }
            }

            return best;
        }

        private static void ApplyArtistCredit(AlbumTrack track, JsonElement recording)
        {
            if (!recording.TryGetProperty("artist-credit", out var credit) || credit.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var item in credit.EnumerateArray())
            {
                if (!item.TryGetProperty("artist", out var artistEl))
                {
                    continue;
                }

                var name = GetString(artistEl, "name");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    track.Artists.Add(name);
                }

                if (track.ArtistMbid is null)
                {
                    track.ArtistMbid = GetString(artistEl, "id");
                }
            }
        }

        private static IEnumerable<string> GetArtistCreditIds(JsonElement release)
        {
            if (!release.TryGetProperty("artist-credit", out var credit) || credit.ValueKind != JsonValueKind.Array)
            {
                return Enumerable.Empty<string>();
            }

            return credit.EnumerateArray()
                .Select(item => item.TryGetProperty("artist", out var artistEl) ? GetString(artistEl, "id") : null)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .ToList();
        }

        private static int StatusRank(string? status)
        {
            if (string.Equals(status, "Official", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (string.Equals(status, "Pseudo-Release", StringComparison.OrdinalIgnoreCase))
            {
                return 3;
            }

            if (string.Equals(status, "Promotional", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            if (string.Equals(status, "Bootleg", StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            return 1;
        }

        private static bool IsIncompleteDate(string? date)
        {
            return string.IsNullOrWhiteSpace(date) || date.Length < 10;
        }

        private static string? GetPrimaryType(JsonElement release)
        {
            if (release.TryGetProperty("release-group", out var rg))
            {
                return GetString(rg, "primary-type");
            }

            return null;
        }

        private static IEnumerable<string> GetArtistCreditNames(JsonElement release)
        {
            if (!release.TryGetProperty("artist-credit", out var credit) || credit.ValueKind != JsonValueKind.Array)
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

            var m = Regex.Match(date, @"\b(1[89]\d{2}|20\d{2})\b");
            return m.Success ? int.Parse(m.Value, CultureInfo.InvariantCulture) : null;
        }
    }
}
