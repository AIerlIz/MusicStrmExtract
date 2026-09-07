using System;
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
    public sealed record AlbumTrack(
        int Number,
        string? Title,
        string? RecordingMbid,
        string? ArtistMbid,
        IReadOnlyList<string> Artists);

    /// <summary>release 响应(inc=recordings)中解析出的一张 media(碟)。</summary>
    public sealed record ReleaseMedia(
        int Position,
        IReadOnlyList<AlbumTrack> Tracks);

    /// <summary>轨道映射搜索的结果(本地指纹校验通过后创建,创建后不可变)。</summary>
    public sealed record AlbumSearchResult(
        bool Found,
        string? Title,
        int? Year,
        string? ReleaseMbid,
        string? ReleaseGroupMbid,
        string? ArtistName,
        string? AlbumArtistMbid,
        IReadOnlyList<ReleaseMedia> Medias)
    {
        public static readonly AlbumSearchResult Empty = new AlbumSearchResult(
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            Array.Empty<ReleaseMedia>());
    }
}
