using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using MusicStrmExtract.Metadata;

namespace MusicStrmExtract.Online
{
    /// <summary>专辑名搜索的结果(MB 官方专辑名 + 专辑级数据)。</summary>
    public sealed class AlbumSearchResult
    {
        /// <summary>是否找到可信命中(标题相似且艺术家匹配)。</summary>
        public bool Found { get; set; }

        /// <summary>MB 官方专辑名(release.title)。</summary>
        public string? Title { get; set; }

        /// <summary>年份(release date 前四位)。</summary>
        public int? Year { get; set; }

        public string? ReleaseMbid { get; set; }

        public string? ReleaseGroupMbid { get; set; }

        /// <summary>专辑艺人(MB artist-credit 首个名字,用于 AlbumArtists 对齐)。</summary>
        public string? ArtistName { get; set; }
    }

    /// <summary>
    /// "根据专辑名称搜索":把专辑文件夹名(如 "叶惠美 (2003)")净化成核心名("叶惠美"),
    /// 用 MusicBrainz release 搜索取回官方专辑名/年份/MBID/封面。
    /// 候选过滤:score>0 + 标题相似 + 艺术家宽松匹配;排序稳定(Album 主类型优先 → 日期最早 → 官方名),
    /// 避免同专辑多版本导致每次选不同 release。
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

        public async Task<AlbumSearchResult> SearchAsync(string albumFolderName, string? artistName, CancellationToken ct)
        {
            var result = new AlbumSearchResult();
            var clean = CleanAlbumName(albumFolderName);
            if (string.IsNullOrWhiteSpace(clean))
            {
                return result;
            }

            var root = await _api.SearchReleasesAsync(clean, 10, ct).ConfigureAwait(false);
            if (!root.TryGetProperty("releases", out var releases) || releases.GetArrayLength() == 0)
            {
                return result;
            }

            var wanted = MergePolicy.NormalizeTitle(clean);
            var candidates = releases.EnumerateArray().ToList();

            var scored = candidates
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
                .Where(x => x.Score > 0
                            && !string.IsNullOrWhiteSpace(x.Title)
                            && TitleSimilar(wanted, MergePolicy.NormalizeTitle(x.Title!))
                            && ArtistMatches(artistName, x.Artists))
                .ToList();

            if (scored.Count == 0)
            {
                return result;
            }

            // 稳定排序(用户约定:优先 Official 官方发行,拒绝 Pseudo-Release 拼凑名):
            // status(Official 0 < Pseudo-Release 3) -> 主类型 Album -> 日期最早(空日期排最后)
            // -> score 降序 -> 官方名(字典序)
            var best = scored
                .OrderBy(x => StatusRank(x.Status))
                .ThenBy(x => string.Equals(x.PrimaryType, "Album", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(x => string.IsNullOrWhiteSpace(x.Date) ? "9999" : x.Date!)
                .ThenByDescending(x => x.Score)
                .ThenBy(x => x.Title, StringComparer.Ordinal)
                .First();

            result.Found = true;
            result.Title = best.Title;
            result.Year = ParseYear(best.Date);
            result.ReleaseMbid = GetString(best.Item, "id");
            var artist = best.Artists.FirstOrDefault();
            result.ArtistName = string.IsNullOrWhiteSpace(artist) ? null : artist;

            if (best.Item.TryGetProperty("release-group", out var rg))
            {
                result.ReleaseGroupMbid = GetString(rg, "id");
            }

            return result;
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

        private static bool TitleSimilar(string wanted, string candidate)
        {
            wanted = HanSimplifier.Simplify(wanted);
            candidate = HanSimplifier.Simplify(candidate);

            if (string.Equals(wanted, candidate, StringComparison.Ordinal))
            {
                return true;
            }

            var shorter = wanted.Length <= candidate.Length ? wanted : candidate;
            var longer = wanted.Length <= candidate.Length ? candidate : wanted;
            return shorter.Length >= 3 && longer.Contains(shorter, StringComparison.Ordinal);
        }

        private static string Compact(string value)
        {
            return new string(HanSimplifier.Simplify(value).ToLowerInvariant().Where(c => !char.IsWhiteSpace(c)).ToArray());
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