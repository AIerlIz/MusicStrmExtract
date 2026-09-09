using System.Globalization;
using System.Text.RegularExpressions;

namespace MusicStrmExtract.Providers;

/// <summary>
/// 解析 .strm 路径、文件名与碟目录名中的碟号/轨号。
/// 单碟常见形态 "01 - 标题";碟+轨形态 "1-01", "01.01", "CD1-01", "Disc 1 - 01";
/// 碟目录形态 "Disc 1", "CD2"。
/// </summary>
public static class StrmFileParser
{
    private static readonly Regex s_plainDiscTrackRegex = new(
        @"^(\d{1,2})[-.](\d{1,3})(?=[\s_\-.]|$)",
        RegexOptions.Compiled);

    private static readonly Regex s_keywordDiscTrackRegex = new(
        @"^(?:cd|disc|disk|dvd)[\s_\-.]*(\d{1,2})[\s_\-.]*(\d{1,3})(?=[\s_\-.]|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex s_trackNumberRegex = new(
        @"^(\d{1,3})\s*[-_]\s*",
        RegexOptions.Compiled);

    private static readonly Regex s_discFolderRegex = new(
        @"^(?:disc|disk|cd|dvd)[\s_\-.]*(\d{1,2})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex s_commentaryRegex = new(
        @"\(?\s*(?:commentary|评论音轨|評論音轨|评论轨|評論轨|评论|評論|解说|解說)\s*\)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>路径是否指向 .strm 文件(大小写不敏感)。</summary>
    public static bool IsStrmPath(string? path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>按 strm 路径解析"艺人\专辑\碟"文件夹结构。
    /// 返回 (专辑文件夹名, 艺人文件夹名, 专辑实际目录, 碟号);strm 直接在专辑目录时碟号为 null,
    /// 位于 "Album/Disc N/" 时解析出碟号并上移一级专辑目录。</summary>
    public static (string? AlbumFolder, string? ArtistFolder, string? AlbumDir, int? DiscNumber) GetFolderStructure(string strmPath)
    {
        var fileDir = Path.GetDirectoryName(strmPath);
        if (string.IsNullOrWhiteSpace(fileDir))
            return (null, null, null, null);

        var discNumber = ParseDiscFolderName(Path.GetFileName(fileDir));
        if (discNumber is not null && !string.IsNullOrWhiteSpace(Path.GetDirectoryName(fileDir)))
        {
            var albumDir = Path.GetDirectoryName(fileDir)!;
            var artistDir = Path.GetDirectoryName(albumDir);
            return (
                Path.GetFileName(albumDir),
                string.IsNullOrWhiteSpace(artistDir) ? null : Path.GetFileName(artistDir),
                albumDir,
                discNumber);
        }

        var artistDir2 = Path.GetDirectoryName(fileDir);
        return (
            Path.GetFileName(fileDir),
            string.IsNullOrWhiteSpace(artistDir2) ? null : Path.GetFileName(artistDir2),
            fileDir,
            null);
    }

    /// <summary>
    /// 解析 strm 文件名,返回 (碟号, 原始轨号, 是否评论轨)。
    /// 无碟号时 DiscNumber=null,无轨号时 TrackNumber=0。
    /// </summary>
    public static (int? DiscNumber, int TrackNumber, bool IsCommentary) ParseFileName(string filePath)
    {
        var name = Path.GetFileName(filePath);
        if (name.EndsWith(".strm", StringComparison.OrdinalIgnoreCase))
            name = name[..^".strm".Length];

        var isCommentary = s_commentaryRegex.IsMatch(name);
        var discTrack = s_keywordDiscTrackRegex.Match(name);
        if (discTrack.Success && TryNumber(discTrack.Groups[2], out var track) && track > 0)
            return (GetDisc(discTrack.Groups[1]), track, isCommentary);

        discTrack = s_plainDiscTrackRegex.Match(name);
        if (discTrack.Success && TryNumber(discTrack.Groups[2], out track) && track > 0)
            return (GetDisc(discTrack.Groups[1]), track, isCommentary);

        var trackOnly = s_trackNumberRegex.Match(name);
        if (trackOnly.Success && TryNumber(trackOnly.Groups[1], out track) && track > 0)
            return (null, track, isCommentary);

        return (null, 0, isCommentary);
    }

    /// <summary>
    /// 把评论轨/正式轨的原始轨号映射为 MusicBrainz 官方轨号。
    /// 支持同轨号并存、"评论轨在前/在后"及奇偶交错("01/03...评论 + 02/04...正式")布局;
    /// 无法识别时原样返回。
    /// </summary>
    public static int MapCommentaryTrackNumber(
        int rawNumber,
        bool isCommentary,
        IReadOnlyCollection<int> commentaryNumbers,
        IReadOnlyCollection<int> regularNumbers)
    {
        return CommentaryTrackMapper.Map(
            rawNumber,
            isCommentary,
            commentaryNumbers,
            regularNumbers);
    }

    /// <summary>按已去重、升序的轨号集合执行评论轨映射，供批量扫描复用。</summary>
    internal static int MapCommentaryTrackNumberNormalized(
        int rawNumber,
        bool isCommentary,
        int[] commentaryNumbers,
        int[] regularNumbers)
    {
        return CommentaryTrackMapper.MapNormalized(
            rawNumber,
            isCommentary,
            commentaryNumbers,
            regularNumbers);
    }

    /// <summary>解析碟目录名,返回碟号;"Disc 1"/"CD2" → 1/2,其它目录名 → null。</summary>
    public static int? ParseDiscFolderName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var m = s_discFolderRegex.Match(name);
        return m.Success && TryNumber(m.Groups[1], out var disc) ? disc : null;
    }

    private static int? GetDisc(Group group)
    {
        return TryNumber(group, out var disc) && disc > 0 ? disc : null;
    }

    private static bool TryNumber(Group group, out int number)
    {
        return int.TryParse(group.Value, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }
}
