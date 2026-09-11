using System.Text.Json;
using static MusicStrmExtract.Online.JsonUtil;

namespace MusicStrmExtract.Online;

/// <summary>把 MusicBrainz release JSON 解析成强类型候选,JSON 不再泄漏到选版/评分逻辑。</summary>
internal static class ReleaseJsonReader
{
    /// <summary>JSON 不是对象时的空 release 候选,避免在多处重复列出全 null 参数。</summary>
    private static readonly ReleaseSummary EmptyRelease = new(
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
        [],
        []);

    public static List<ScoredRelease> ParseSearchReleases(JsonElement root)
    {
        var result = new List<ScoredRelease>();
        foreach (var release in EnumerateReleaseObjects(root))
            result.Add(new ScoredRelease(ParseRelease(release), GetInt(release, "score")));

        return result;
    }

    public static (int TotalCount, List<ReleaseSummary> Releases) ParseBrowseReleases(JsonElement root)
    {
        return (GetInt(root, "count"), ParseReleaseSummaries(root));
    }

    public static ParsedReleaseGroup ParseReleaseGroup(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new ParsedReleaseGroup(
                null,
                null,
                null,
                null,
                [],
                []);
        }

        return new ParsedReleaseGroup(
            GetString(root, "id"),
            GetString(root, "title"),
            GetString(root, "primary-type"),
            GetString(root, "disambiguation"),
            GetArtistCredits(root, includeNameOnlyCredits: true),
            ParseReleaseSummaries(root));
    }

    public static ReleaseSummary ParseRelease(JsonElement release)
    {
        if (release.ValueKind != JsonValueKind.Object)
            return EmptyRelease;

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
            return media;

        foreach (var m in mediaArr.EnumerateArray())
        {
            media.Add(new ReleaseMediaInfo(
                GetInt(m, "position"),
                GetString(m, "format"),
                GetInt(m, "track-count")));
        }

        return media;
    }

    private static List<ReleaseSummary> ParseReleaseSummaries(JsonElement root)
    {
        var result = new List<ReleaseSummary>();
        foreach (var release in EnumerateReleaseObjects(root))
            result.Add(ParseRelease(release));

        return result;
    }

    private static IEnumerable<JsonElement> EnumerateReleaseObjects(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("releases", out var releases)
            || releases.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var release in releases.EnumerateArray())
        {
            if (release.ValueKind == JsonValueKind.Object)
                yield return release;
        }
    }

    private static string? GetPrimaryType(JsonElement release)
    {
        return GetReleaseGroupProperty(release, "primary-type");
    }

    private static string? GetReleaseGroupMbid(JsonElement release)
    {
        return GetReleaseGroupProperty(release, "id");
    }

    /// <summary>读取 release 内嵌 release-group 子对象的指定字段(search 响应形态)。</summary>
    private static string? GetReleaseGroupProperty(JsonElement release, string property)
    {
        return release.TryGetProperty("release-group", out var rg)
            ? GetString(rg, property)
            : null;
    }
}
