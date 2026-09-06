using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static MusicStrmExtract.Online.JsonUtil;

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

        /// <summary>release 全部 media(碟)的轨道映射;本地碟组按 mapping 逐碟定位。</summary>
        public List<ReleaseMedia> Medias { get; } = new List<ReleaseMedia>();
    }

    /// <summary>
    /// "按艺人 + 专辑文件夹名锁定 MusicBrainz release":把专辑文件夹名(如 "叶惠美 (2003)")净化成核心名("叶惠美"),
    /// 用 release:"专辑" AND artist:"艺人" 查询候选 release,再按本地碟组(碟号+轨号)校验每个候选的 media 布局,
    /// 返回首个布局匹配的 release 的完整轨道映射(每轨 recording MBID/标题)——单曲按碟号+轨号取数。
    /// 目录名原样透传给 MusicBrainz,不参与本地文本比较;候选顺序稳定(Official 优先 → Album 主类型 → 完整日期优先 → score)。
    /// </summary>
    public sealed class AlbumSearch
    {
        /// <summary>逐个尝试的候选 release 上限;布局不匹配时继续尝试下一个,避免多碟选到错误的单碟版本。</summary>
        public const int MaxCandidateReleases = 5;

        private readonly IMusicBrainzApi _api;
        private readonly ICoverArtClient _coverArtClient;

        public AlbumSearch(IMusicBrainzApi api, ICoverArtClient coverArtClient)
        {
            _api = api;
            _coverArtClient = coverArtClient;
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
        /// 专辑轨道映射搜索:搜索候选 release + 取各自 tracklist + 本地碟组指纹校验选碟。
        /// 未找到 release、或候选 release 的 media 布局均不匹配本地碟组时返回 Found=false
        /// (调用方按未命中处理)。
        /// </summary>
        public async Task<AlbumSearchResult> SearchForTrackMapAsync(
            string albumFolderName,
            string? artistName,
            IReadOnlyList<LocalDisc> localDiscs,
            CancellationToken ct)
        {
            var result = new AlbumSearchResult();
            var clean = CleanAlbumName(albumFolderName);
            if (string.IsNullOrWhiteSpace(clean)
                || localDiscs is null
                || localDiscs.Count == 0
                || !localDiscs.Any(d => d.TrackNumbers.Count > 0))
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
                    PrimaryType = GetPrimaryType(r)
                })
                .Where(x => x.Score > 0 && !string.IsNullOrWhiteSpace(x.Title))
                .ToList();

            if (scored.Count == 0)
            {
                return result;
            }

            // 无感国家偏好:从搜索候选推断多数国家,用于 top-10 排序时的 tie-break
            var preferredCountry = ReleaseGroupScorer.InferPreferredCountry(scored.Select(x => x.Item), localDiscs);

            // 本地目录文本已作为查询条件交给 MB,不再做字形过滤;稳定排序只按 MB 元数据:
            // status(Official 0 < Promotional/Unknown 1 < Bootleg/Withdrawn 2 < Pseudo-Release 3) -> 主类型 Album -> 完整日期优先 -> 日期最早
            // (空日期排最后)-> score 降序 -> 官方名(字典序)
            var ordered = scored
                .OrderBy(x => StatusRank(x.Status))
                .ThenBy(x => string.Equals(x.PrimaryType, "Album", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(x => IsIncompleteDate(x.Date) ? 1 : 0)
                .ThenBy(x => string.IsNullOrWhiteSpace(x.Date) ? "9999" : x.Date!)
                .ThenBy(x => string.Equals(GetString(x.Item, "country"), preferredCountry, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenByDescending(x => x.Score)
                .ThenBy(x => x.Title, StringComparer.Ordinal)
                .Take(MaxCandidateReleases)
                .ToList();

            // 尝试用 release-group 全量加权评分选出最优版本:
            // 从 top-1 候选取 release-group-id,拉取该 RG 下全部 release,按 barcode/status/格式等维度打分。
            // 若 RG 查询失败、仅有一个 release、或全部不满足布局校验,回退到原有 top-10 排序逻辑。
            AlbumSearchResult? firstFallback = null;
            string? firstFallbackMbid = null;
            var topRgInfo = ordered[0].Item.TryGetProperty("release-group", out var rgEl) ? rgEl : default;
            var topRgMbid = GetString(topRgInfo, "id");
            if (!string.IsNullOrWhiteSpace(topRgMbid))
            {
                try
                {
                    var rgRoot = await _api.GetReleaseGroupReleasesAsync(topRgMbid, ct).ConfigureAwait(false);
                    if (rgRoot.TryGetProperty("releases", out var rgReleases) && rgReleases.GetArrayLength() > 1)
                    {
                        var localYear = ParseYear(albumFolderName);
                        // 从 RG 全量候选推断偏好国家(比搜索样本更准),并传给评分
                        var rgPreferredCountry = ReleaseGroupScorer.InferPreferredCountry(rgReleases.EnumerateArray(), localDiscs);
                        var ranked = ReleaseGroupScorer.ScoreAll(rgReleases, localYear, rgPreferredCountry);

                        // 只收集"顶级分数档"且精确轨数命中的候选,再用封面数(CAA)打破残余并列;
                        // 顶级分数档之外的命中不可能靠封面数胜出,无需继续收集(有界 CAA 请求)。
                        var exactCandidates = new List<(JsonElement Release, JsonElement ReleaseRoot, List<ReleaseMedia> Medias, int Score)>();
                        foreach (var (release, score) in ranked)
                        {
                            var releaseMbid = GetString(release, "id");
                            if (string.IsNullOrWhiteSpace(releaseMbid)) continue;

                            // 已找到首个 exact 后,只有真正同档的候选才值得继续拉详情做 CAA 决胜。
                            if (exactCandidates.Count > 0
                                && !EquivalentInRanking(release, exactCandidates[0].Release, score, exactCandidates[0].Score, localYear))
                            {
                                break;
                            }

                            var releaseRoot = await _api.GetReleaseAsync(releaseMbid, ct).ConfigureAwait(false);
                            var medias = ParseReleaseMedias(releaseRoot);
                            if (medias.Count == 0) continue;
                            var mapping = MapLocalDiscsToMedias(localDiscs, medias);
                            if (mapping is null) continue;
                            if (HasExactTrackCount(localDiscs, mapping))
                            {
                                exactCandidates.Add((release, releaseRoot, medias, score));
                            }
                            else
                            {
                                firstFallback ??= BuildAlbumResult(releaseRoot, medias);
                                firstFallbackMbid ??= releaseMbid;
                            }
                        }

                        if (exactCandidates.Count > 0)
                        {
                            return await PickExactByCoverAsync(exactCandidates, ct).ConfigureAwait(false);
                        }

                        // RG 路径没有 exact 时不直接返回 firstFallback:
                        // 搜索中更低顺位的其它 RG 可能才是轨数完全一致的版本,继续走下方 top-10 循环。
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
                {
                    // RG 查询失败,降级到下方原有 top-10 循环
                }
            }

            // 逐个候选取 tracklist,用本地碟组布局校验 media 映射;
            // 轨数完全一致(每碟本地轨数 == release media 轨数)优先,避免普通版被豪华版/加歌版抢先选中。
            // 注意:release 详情取回的网络/解析异常向上抛,由调用方区分"MB 不可达(不缓存)"与"确认未命中";
            // 吞成 Found=false 会让暂时性故障被 30 分钟缓存锁死。
            foreach (var best in ordered)
            {
                var releaseMbid = GetString(best.Item, "id");
                if (string.IsNullOrWhiteSpace(releaseMbid))
                {
                    continue;
                }

                if (string.Equals(firstFallbackMbid, releaseMbid, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var releaseRoot = await _api.GetReleaseAsync(releaseMbid, ct).ConfigureAwait(false);
                var medias = ParseReleaseMedias(releaseRoot);
                if (medias.Count == 0)
                {
                    continue;
                }

                var mapping = MapLocalDiscsToMedias(localDiscs, medias);
                if (mapping is null)
                {
                    continue;
                }

                if (HasExactTrackCount(localDiscs, mapping))
                {
                    return BuildAlbumResult(best.Item, medias);
                }

                firstFallback ??= BuildAlbumResult(best.Item, medias);
            }

            return firstFallback ?? result;
        }

        private static AlbumSearchResult BuildAlbumResult(JsonElement releaseItem, List<ReleaseMedia> medias)
        {
            var result = new AlbumSearchResult
            {
                Found = true,
                Title = GetString(releaseItem, "title"),
                Year = ParseYear(GetString(releaseItem, "date")),
                ReleaseMbid = GetString(releaseItem, "id"),
                ArtistName = GetArtistCreditNames(releaseItem).FirstOrDefault(),
                AlbumArtistMbid = GetArtistCreditIds(releaseItem).FirstOrDefault()
            };
            if (releaseItem.TryGetProperty("release-group", out var rg))
            {
                result.ReleaseGroupMbid = GetString(rg, "id");
            }

            foreach (var media in medias)
            {
                result.Medias.Add(media);
            }

            return result;
        }

        /// <summary>在顶级分数档的精确命中候选中,用 Cover Art Archive 封面数打破残余并列;单候选直接返回。</summary>
        private async Task<AlbumSearchResult> PickExactByCoverAsync(
            List<(JsonElement Release, JsonElement ReleaseRoot, List<ReleaseMedia> Medias, int Score)> candidates,
            CancellationToken ct)
        {
            if (candidates.Count == 1)
            {
                return BuildAlbumResult(candidates[0].ReleaseRoot, candidates[0].Medias);
            }

            AlbumSearchResult? best = null;
            var bestCover = -1;
            foreach (var (_, releaseRoot, medias, _) in candidates)
            {
                var mbid = GetString(releaseRoot, "id");
                var cover = string.IsNullOrWhiteSpace(mbid) ? 0 : await _coverArtClient.GetCoverArtCountAsync(mbid, ct).ConfigureAwait(false);
                var built = BuildAlbumResult(releaseRoot, medias);
                if (best is null || cover > bestCover)
                {
                    best = built;
                    bestCover = cover;
                }
            }

            return best!;
        }

        /// <summary>两个 release 是否在 ScoreAll 的排序键下真正并列(同分 + 同年份就近 + 同日期);
        /// 无本地年份时 ScoreAll 只按分排序,同分即同级、可交给 CAA 决胜。</summary>
        private static bool EquivalentInRanking(JsonElement candidate, JsonElement baseline, int candidateScore, int baselineScore, int? localYear)
        {
            if (candidateScore != baselineScore)
            {
                return false;
            }

            if (localYear is null)
            {
                return true;
            }

            var candidateYear = ParseYear(GetString(candidate, "date"));
            var baselineYear = ParseYear(GetString(baseline, "date"));
            if (!candidateYear.HasValue && !baselineYear.HasValue)
            {
                // 双方都缺年份时 ScoreAll 的排序键仍并列,应继续用 CAA 决胜。
                return true;
            }

            if (!candidateYear.HasValue || !baselineYear.HasValue)
            {
                return false; // 仅一方缺年份:ScoreAll 会把"有年份"排前,缺失项不视为并列
            }

            if (Math.Abs(candidateYear.Value - localYear.Value) != Math.Abs(baselineYear.Value - localYear.Value))
            {
                return false;
            }

            return string.Equals(
                GetString(candidate, "date") ?? "9999",
                GetString(baseline, "date") ?? "9999",
                StringComparison.Ordinal);
        }

        /// <summary>本地碟组与 release media 的轨数是否逐碟完全一致(用于优先标准版/普通版)。</summary>
        public static bool HasExactTrackCount(
            IReadOnlyList<LocalDisc> localDiscs,
            IReadOnlyDictionary<LocalDisc, ReleaseMedia> mapping)
        {
            foreach (var pair in mapping)
            {
                if (pair.Key.TrackNumbers.Count != pair.Value.Tracks.Count)
                {
                    return false;
                }
            }

            return localDiscs.Count > 0;
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

        /// <summary>单碟兼容入口:本地为一组无碟号轨号时,选 Position 最小且覆盖全部轨号的 media。</summary>
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

            var group = new LocalDisc();
            group.TrackNumbers.AddRange(localTrackNumbers);
            var map = MapLocalDiscsToMedias(new[] { group }, medias);
            return map?.Values.FirstOrDefault();
        }

        /// <summary>
        /// 把本地碟组映射到 release 的 media:
        ///   带碟号的组按 media.Position 一一对应并校验轨号覆盖,失败即整张未命中;
        ///   无碟号的组在剩余 media 中取 Position 最小且覆盖轨号者(单碟保持原行为)。
        /// 任一碟组无法映射时返回 null,避免产生半对半错的专辑。
        /// </summary>
        public static Dictionary<LocalDisc, ReleaseMedia>? MapLocalDiscsToMedias(
            IReadOnlyList<LocalDisc> localDiscs,
            IReadOnlyList<ReleaseMedia> medias)
        {
            if (localDiscs is null || localDiscs.Count == 0 || medias is null || medias.Count == 0)
            {
                return null;
            }

            var explicitGroups = localDiscs
                .Where(d => d.DiscNumber is > 0)
                .OrderBy(d => d.DiscNumber!.Value)
                .ToList();
            var implicitGroups = localDiscs
                .Where(d => d.DiscNumber is not > 0)
                .ToList();

            var usedPositions = new HashSet<int>();
            var map = new Dictionary<LocalDisc, ReleaseMedia>();

            foreach (var group in explicitGroups)
            {
                var media = medias.FirstOrDefault(m => m.Position == group.DiscNumber!.Value);
                if (media is null || !usedPositions.Add(media.Position) || !Covers(media, group.TrackNumbers))
                {
                    return null;
                }

                map.Add(group, media);
            }

            var remaining = medias
                .Where(m => !usedPositions.Contains(m.Position))
                .OrderBy(m => m.Position)
                .ToList();
            foreach (var group in implicitGroups.OrderByDescending(g => g.TrackNumbers.Count))
            {
                var media = remaining.FirstOrDefault(m => Covers(m, group.TrackNumbers));
                if (media is null)
                {
                    return null;
                }

                remaining.Remove(media);
                map.Add(group, media);
            }

            return map;
        }

        private static bool Covers(ReleaseMedia media, IReadOnlyCollection<int> trackNumbers)
        {
            var mediaNumbers = media.Tracks
                .Select(t => t.Number)
                .Where(n => n > 0)
                .ToHashSet();
            return trackNumbers.Where(n => n > 0).All(mediaNumbers.Contains);
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

        internal static int StatusRank(string? status)
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

            if (string.Equals(status, "Bootleg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "Withdrawn", StringComparison.OrdinalIgnoreCase))
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

    }
}
