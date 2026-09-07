using System;
using System.Collections.Generic;
using System.Text.Json;
using static MusicStrmExtract.Online.JsonUtil;

namespace MusicStrmExtract.Online
{
    /// <summary>把 MusicBrainz release JSON 解析成强类型候选,JSON 不再泄漏到选版/评分逻辑。</summary>
    internal static class ReleaseJsonReader
    {
        public static List<ScoredRelease> ParseSearchReleases(JsonElement root)
        {
            var result = new List<ScoredRelease>();
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("releases", out var releases)
                || releases.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var release in releases.EnumerateArray())
            {
                if (release.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                result.Add(new ScoredRelease(ParseRelease(release), GetInt(release, "score")));
            }

            return result;
        }

        public static List<ReleaseSummary> ParseReleaseGroup(JsonElement root)
        {
            var result = new List<ReleaseSummary>();
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("releases", out var releases)
                || releases.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var release in releases.EnumerateArray())
            {
                if (release.ValueKind == JsonValueKind.Object)
                {
                    result.Add(ParseRelease(release));
                }
            }

            return result;
        }

        public static ReleaseSummary ParseRelease(JsonElement release)
        {
            if (release.ValueKind != JsonValueKind.Object)
            {
                return new ReleaseSummary(
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    Array.Empty<ArtistCredit>(),
                    Array.Empty<ReleaseMediaInfo>());
            }

            return new ReleaseSummary(
                GetString(release, "id"),
                GetString(release, "title"),
                GetString(release, "date"),
                GetString(release, "status"),
                GetString(release, "country"),
                GetString(release, "barcode"),
                GetString(release, "packaging"),
                GetString(release, "disambiguation"),
                GetPrimaryType(release),
                GetReleaseGroupMbid(release),
                GetArtistCredits(release, includeNameOnlyCredits: true),
                GetMedia(release));
        }

        private static List<ReleaseMediaInfo> GetMedia(JsonElement release)
        {
            var media = new List<ReleaseMediaInfo>();
            if (!release.TryGetProperty("media", out var mediaArr) || mediaArr.ValueKind != JsonValueKind.Array)
            {
                return media;
            }

            foreach (var m in mediaArr.EnumerateArray())
            {
                media.Add(new ReleaseMediaInfo(
                    GetInt(m, "position"),
                    GetString(m, "format"),
                    GetInt(m, "track-count")));
            }

            return media;
        }

        private static string? GetPrimaryType(JsonElement release)
        {
            if (release.TryGetProperty("release-group", out var rg))
            {
                return GetString(rg, "primary-type");
            }

            return null;
        }

        private static string? GetReleaseGroupMbid(JsonElement release)
        {
            if (release.TryGetProperty("release-group", out var rg))
            {
                return GetString(rg, "id");
            }

            return null;
        }
    }
}
