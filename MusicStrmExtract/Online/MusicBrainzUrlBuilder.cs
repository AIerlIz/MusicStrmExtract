using System.Text;
using System.Text.RegularExpressions;

namespace MusicStrmExtract.Online;

/// <summary>MusicBrainz WS/2 请求 URL 的单一构造入口。</summary>
internal static class MusicBrainzUrlBuilder
{
    /// <summary>
    /// Lucene 全量元字符转义表,等价于 Lucene <c>QueryParser.escape</c> 语义。
    /// <c>Uri.EscapeDataString</c> 只做 URL 百分号编码,不理解 Lucene 查询语义:
    /// 未经本表转义的 <c>:</c> <c>(</c> <c>)</c> <c>+</c> <c>-</c> <c>*</c> <c>?</c> 等会被
    /// 查询解析器当作语法,导致语义偏移甚至绕过 <c>AND artist:</c> 子句。
    /// </summary>
    private static readonly Regex s_luceneEscape = new(
        @"([+\-&|!(){}\[\]^""~*?:\\/])",
        RegexOptions.Compiled);

    /// <summary>对 Lucene 查询文本里的全部元字符加反斜杠转义,使其被当作字面量。</summary>
    private static string EscapeLucene(string value)
    {
        return s_luceneEscape.Replace(value, @"\$1");
    }

    public static string GetRelease(string baseUrl, string releaseMbid)
    {
        return $"{baseUrl}/ws/2/release/{Uri.EscapeDataString(releaseMbid)}" +
            "?inc=recordings+artist-credits+release-groups&fmt=json";
    }

    public static string SearchReleases(string baseUrl, string album, string? artist, int limit)
    {
        var queryBuilder = new StringBuilder("release:");
        // 引号内文本整体转义:采用全量元字符转义后,双引号只需转义(不再删除),
        // 否则会出现"先删后转义"的混乱语义,且删除本身无法阻止其它元字符改写查询。
        queryBuilder
            .Append('"')
            .Append(EscapeLucene(album))
            .Append('"');

        if (!string.IsNullOrWhiteSpace(artist))
        {
            queryBuilder
                .Append(" AND artist:\"")
                .Append(EscapeLucene(artist.Trim()))
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
