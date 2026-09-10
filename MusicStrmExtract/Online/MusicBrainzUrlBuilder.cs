using System.Text;

namespace MusicStrmExtract.Online;

/// <summary>MusicBrainz WS/2 请求 URL 的单一构造入口。</summary>
internal static class MusicBrainzUrlBuilder
{
    public static string GetRelease(string baseUrl, string releaseMbid)
    {
        return $"{baseUrl}/ws/2/release/{Uri.EscapeDataString(releaseMbid)}" +
            "?inc=recordings+artist-credits+release-groups&fmt=json";
    }

    public static string SearchReleases(string baseUrl, string album, string? artist, int limit)
    {
        var queryBuilder = new StringBuilder("release:");
        queryBuilder
            .Append('"')
            .Append(album.Replace("\"", string.Empty, StringComparison.Ordinal))
            .Append('"');

        if (!string.IsNullOrWhiteSpace(artist))
        {
            queryBuilder
                .Append(" AND artist:\"")
                .Append(artist.Trim().Replace("\"", string.Empty, StringComparison.Ordinal))
                .Append('"');
        }

        var query = Uri.EscapeDataString(queryBuilder.ToString());
        return $"{baseUrl}/ws/2/release?query={query}&fmt=json&limit={limit}";
    }

    public static string GetReleaseGroup(string baseUrl, string rgMbid)
    {
        return $"{baseUrl}/ws/2/release-group/{Uri.EscapeDataString(rgMbid)}" +
            "?inc=releases+media+artist-credits&fmt=json";
    }

    public static string BrowseReleases(
        string baseUrl,
        string rgMbid,
        int offset,
        int limit)
    {
        return $"{baseUrl}/ws/2/release?release-group={Uri.EscapeDataString(rgMbid)}" +
            $"&inc=media+artist-credits&fmt=json&limit={limit}&offset={offset}";
    }
}
