using MusicStrmExtract.Online;

namespace MusicStrmExtract.Providers;

/// <summary>一条尚未做评论轨归一化的本地文件轨号。</summary>
internal sealed record TrackReference(int Number, bool IsCommentary);

internal sealed class AlbumDirectoryScan(
    IReadOnlyList<LocalDisc> discs,
    IReadOnlyDictionary<int, List<TrackReference>> rawTracks)
{
    public IReadOnlyList<LocalDisc> Discs { get; } = discs;

    /// <summary>碟键与原始文件轨号；无碟号的组统一使用键 0。</summary>
    public IReadOnlyDictionary<int, List<TrackReference>> RawTracks { get; } = rawTracks;
}

/// <summary>
/// 扫描专辑目录上的 .strm(含 Disc N 子目录),构建本地碟组。
/// 评论轨与正式轨先按原始轨号收集,再做评论轨归一化,避免 1..26 交错轨号破坏 release 覆盖校验。
/// </summary>
internal static class AlbumDirectoryScanner
{
    /// <summary>无碟号的音轨在字典中统一使用该键。</summary>
    private const int NoDiscKey = 0;

    public static AlbumDirectoryScan Scan(string albumDir, Action<string>? warning = null)
    {
        var rawGroups = CollectRawTrackGroups(albumDir, warning);

        var discs = rawGroups
            .Select(CreateNormalizedDisc)
            .OrderBy(d => d.DiscNumber ?? int.MaxValue)
            .ToList();

        return new AlbumDirectoryScan(discs, rawGroups);
    }

    /// <summary>枚举专辑根目录与 Disc N 子目录，按碟号收集去重后的原始轨号。</summary>
    private static Dictionary<int, List<TrackReference>> CollectRawTrackGroups(
        string albumDir,
        Action<string>? warning)
    {
        var rawGroups = new Dictionary<int, List<TrackReference>>();
        var seen = new HashSet<(int Disc, int Track, bool Commentary)>();

        void AddTrack(int? disc, int number, bool isCommentary)
        {
            var key = disc ?? NoDiscKey;
            if (number <= 0 || !seen.Add((key, number, isCommentary)))
                return;

            if (!rawGroups.TryGetValue(key, out var list))
            {
                list = [];
                rawGroups.Add(key, list);
            }

            list.Add(new TrackReference(number, isCommentary));
        }

        try
        {
            AddDirectoryTracks(albumDir, disc: null, AddTrack);

            foreach (var sub in Directory.EnumerateDirectories(albumDir))
            {
                var disc = StrmFileParser.ParseDiscFolderName(Path.GetFileName(sub));
                if (disc is not null)
                    AddDirectoryTracks(sub, disc, AddTrack);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 目录读取失败时返回已收集到的碟组;部分已收集的数据仍可用于定位。
            // UnauthorizedAccessException 与 IOException 无继承关系,若不显式捕获会逃逸到
            // Emby 调用栈,可能导致整个媒体库扫描中断,故与 IOException 同等降级处理。
            warning?.Invoke(
                $"[Scan] albumDir=\"{albumDir}\" result=partial error=\"{ex.Message}\"");
        }

        return rawGroups;
    }

    private static void AddDirectoryTracks(string directory, int? disc, Action<int?, int, bool> addTrack)
    {
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            if (!StrmFileParser.IsStrmPath(file))
                continue;

            var (_, number, isCommentary) = StrmFileParser.ParseFileName(file);
            addTrack(disc, number, isCommentary);
        }
    }

    /// <summary>把一个碟组的原始轨号归一化为官方轨号集合（升序、去重）。</summary>
    private static LocalDisc CreateNormalizedDisc(KeyValuePair<int, List<TrackReference>> rawGroup)
    {
        var raw = rawGroup.Value;
        var commentaryNumbers = SelectSortedNumbers(raw, isCommentary: true);
        var regularNumbers = SelectSortedNumbers(raw, isCommentary: false);

        var disc = new LocalDisc { DiscNumber = rawGroup.Key == NoDiscKey ? null : rawGroup.Key };
        disc.TrackNumbers.AddRange(raw
            .Select(r => StrmFileParser.MapCommentaryTrackNumberNormalized(
                r.Number,
                r.IsCommentary,
                commentaryNumbers,
                regularNumbers))
            .Where(n => n > 0)
            .Distinct());
        disc.TrackNumbers.Sort();
        return disc;
    }

    private static int[] SelectSortedNumbers(List<TrackReference> raw, bool isCommentary)
    {
        return raw
            .Where(r => r.IsCommentary == isCommentary)
            .Select(r => r.Number)
            .Where(n => n > 0)
            .Distinct()
            .OrderBy(n => n)
            .ToArray();
    }
}
