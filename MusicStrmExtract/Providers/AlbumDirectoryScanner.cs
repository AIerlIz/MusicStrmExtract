using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using MusicStrmExtract.Online;

namespace MusicStrmExtract.Providers
{
    /// <summary>一条尚未做评论轨归一化的本地文件轨号。</summary>
    internal sealed record TrackReference(int Number, bool IsCommentary);

    internal sealed class AlbumDirectoryScan
    {
        public AlbumDirectoryScan(
            IReadOnlyList<LocalDisc> discs,
            IReadOnlyDictionary<int, List<TrackReference>> rawTracks)
        {
            Discs = discs;
            RawTracks = rawTracks;
        }

        public IReadOnlyList<LocalDisc> Discs { get; }

        /// <summary>碟键与原始文件轨号；无碟号的组统一使用键 0。</summary>
        public IReadOnlyDictionary<int, List<TrackReference>> RawTracks { get; }
    }

    /// <summary>
    /// 扫描专辑目录上的 .strm(含 Disc N 子目录),构建本地碟组。
    /// 评论轨与正式轨先按原始轨号收集,再做评论轨归一化,避免 1..26 交错轨号破坏 release 覆盖校验。
    /// </summary>
    internal static class AlbumDirectoryScanner
    {
        public static AlbumDirectoryScan Scan(string albumDir, Action<string>? warning = null)
        {
            // 解析结果中碟号只会是 null 或正整数,用 0 作为无碟号的字典键。
            var rawGroups = new Dictionary<int, List<TrackReference>>();
            var seen = new HashSet<(int Disc, int Track, bool Commentary)>();

            void AddTrack(int? disc, int number, bool isCommentary)
            {
                var key = disc ?? 0;
                if (number <= 0 || !seen.Add((key, number, isCommentary)))
                {
                    return;
                }

                if (!rawGroups.TryGetValue(key, out var list))
                {
                    list = new List<TrackReference>();
                    rawGroups.Add(key, list);
                }

                list.Add(new TrackReference(number, isCommentary));
            }

            try
            {
                foreach (var f in Directory.EnumerateFiles(albumDir))
                {
                    if (!StrmFileParser.IsStrmPath(f))
                    {
                        continue;
                    }

                    var (disc, number, isCommentary) = StrmFileParser.ParseFileName(f);
                    AddTrack(disc, number, isCommentary);
                }

                foreach (var sub in Directory.EnumerateDirectories(albumDir))
                {
                    var disc = StrmFileParser.ParseDiscFolderName(Path.GetFileName(sub));
                    if (disc is null)
                    {
                        continue;
                    }

                    foreach (var f in Directory.EnumerateFiles(sub))
                    {
                        if (!StrmFileParser.IsStrmPath(f))
                        {
                            continue;
                        }

                        var (_, number, isCommentary) = StrmFileParser.ParseFileName(f);
                        AddTrack(disc, number, isCommentary);
                    }
                }
            }
            catch (Exception ex)
            {
                // 目录读取失败时返回已收集到的碟组;部分已收集的数据仍可用于定位
                warning?.Invoke(
                    $"[MusicStrmExtract] [LocalProvider] 扫描专辑目录失败: Path={albumDir} -> {ex.Message}");
            }

            var result = new List<LocalDisc>();
            foreach (var kv in rawGroups)
            {
                var raw = kv.Value;
                var commentaryNumbers = raw.Where(r => r.IsCommentary).Select(r => r.Number).ToArray();
                var regularNumbers = raw.Where(r => !r.IsCommentary).Select(r => r.Number).ToArray();
                var group = new LocalDisc { DiscNumber = kv.Key == 0 ? null : kv.Key };
                group.TrackNumbers.AddRange(raw
                    .Select(r => StrmFileParser.MapCommentaryTrackNumber(
                        r.Number,
                        r.IsCommentary,
                        commentaryNumbers,
                        regularNumbers))
                    .Where(n => n > 0)
                    .Distinct());
                result.Add(group);
            }

            result.Sort((a, b) =>
            {
                var an = a.DiscNumber ?? int.MaxValue;
                var bn = b.DiscNumber ?? int.MaxValue;
                return an.CompareTo(bn);
            });
            foreach (var g in result)
            {
                g.TrackNumbers.Sort();
            }

            return new AlbumDirectoryScan(result, rawGroups);
        }
    }
}
