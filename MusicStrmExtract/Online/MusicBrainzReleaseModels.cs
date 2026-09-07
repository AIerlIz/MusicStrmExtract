using System.Collections.Generic;

namespace MusicStrmExtract.Online
{
    /// <summary>MusicBrainz artist-credit 中的一位艺人。</summary>
    public sealed record ArtistCredit(string? Name, string? Id);

    /// <summary>release 响应中 media 的布局摘要(用于本地碟组比对与评分)。</summary>
    public sealed record ReleaseMediaInfo(int Position, string? Format, int TrackCount);

    /// <summary>
    /// release 候选的强类型视图。
    /// 搜索响应与 release-group 响应共用该结构,便于评分、国家推断与排序不再直接操作 JSON。
    /// </summary>
    public sealed record ReleaseSummary(
        string? Id,
        string? Title,
        string? Date,
        string? Status,
        string? Country,
        string? Barcode,
        string? Packaging,
        string? Disambiguation,
        string? PrimaryType,
        string? ReleaseGroupMbid,
        IReadOnlyList<ArtistCredit> ArtistCredits,
        IReadOnlyList<ReleaseMediaInfo> Media);

    /// <summary>带 MusicBrainz 搜索分的候选。</summary>
    public sealed record ScoredRelease(ReleaseSummary Release, int Score);

    /// <summary>带 RG 加权评分的候选。</summary>
    public sealed record RankedRelease(ReleaseSummary Release, int Score);

    /// <summary>release 详情解析结果:强类型元数据 + 可映射的 media 轨道。</summary>
    public sealed record ParsedRelease(ReleaseSummary Release, IReadOnlyList<ReleaseMedia> Medias);

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
}
