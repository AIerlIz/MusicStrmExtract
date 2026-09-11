using MediaBrowser.Controller.Entities.Audio;
using MusicStrmExtract.Online;

namespace MusicStrmExtract.Providers;

/// <summary>Album 定位命中后，单条 strm 的 Audio 条目构建结果。</summary>
internal sealed record AudioTrackResolution(
    Audio Item,
    ReleaseMedia Media,
    AlbumTrack Track,
    bool IsCommentary);

/// <summary>
/// 把 AlbumSearchResult 与本地目录扫描结果转换为 Audio 条目。
/// 只做纯计算与对象构建，不访问网络、缓存或 Emby 仓库。
/// </summary>
internal static class AudioTrackMetadataFactory
{
    public static AudioTrackResolution? TryBuild(
        AlbumSearchResult album,
        AlbumDirectoryScan scan,
        string strmPath,
        int? folderDisc,
        int? fileDisc,
        int rawTrackNumber,
        bool isCommentary)
    {
        if (album is null || !album.Found || scan is null || rawTrackNumber <= 0)
            return null;

        var layout = ReleaseLayoutMatcher.TryMatch(scan.Discs, album.Medias);
        if (layout is null)
            return null;

        var group = scan.Discs.FirstOrDefault(d => d.DiscNumber == (folderDisc ?? fileDisc));
        if (group is null || !layout.Mapping.TryGetValue(group, out var media))
            return null;

        var selfNumber = ResolveSelfTrackNumber(scan, group, rawTrackNumber, isCommentary);
        if (selfNumber <= 0)
            return null;

        var track = media.Tracks.FirstOrDefault(t => t.Number == selfNumber);
        if (track is null)
            return null;

        return new AudioTrackResolution(
            BuildAudio(album, MediaContext.From(scan, group, media, track, isCommentary), strmPath),
            media,
            track,
            isCommentary);
    }

    /// <summary>把本地原始轨号(含评论轨)映射为 MusicBrainz 官方轨号;无原始记录时沿用传入轨号。</summary>
    private static int ResolveSelfTrackNumber(
        AlbumDirectoryScan scan,
        LocalDisc group,
        int rawTrackNumber,
        bool isCommentary)
    {
        if (!scan.RawTracks.TryGetValue(group.DiscNumber ?? 0, out var rawRefs))
            return rawTrackNumber;

        return StrmFileParser.MapCommentaryTrackNumber(
            rawTrackNumber,
            isCommentary,
            SelectNumbers(rawRefs, isCommentary: true),
            SelectNumbers(rawRefs, isCommentary: false));
    }

    private static int[] SelectNumbers(IEnumerable<TrackReference> rawRefs, bool isCommentary)
    {
        return rawRefs.Where(r => r.IsCommentary == isCommentary).Select(r => r.Number).ToArray();
    }

    private static Audio BuildAudio(AlbumSearchResult album, MediaContext context, string strmPath)
    {
        var albumArtists = AlbumArtistNames(album);
        var item = new Audio
        {
            Name = BuildDisplayName(context.Track.Title, strmPath, context.IsCommentary),
            Album = album.Title,
            ProductionYear = album.Year,
            IndexNumber = context.Track.Number,
            ParentIndexNumber = ResolveParentIndexNumber(context),
            Artists = ResolveTrackArtists(context.Track, albumArtists),
            AlbumArtists = albumArtists
        };

        ApplyProviderIds(item, album, context);
        return item;
    }

    private static string[] AlbumArtistNames(AlbumSearchResult album)
    {
        return !string.IsNullOrWhiteSpace(album.ArtistName)
            ? [album.ArtistName!]
            : [];
    }

    /// <summary>曲目有独立艺人时用曲目艺人,否则回退到专辑艺人。</summary>
    private static string[] ResolveTrackArtists(AlbumTrack track, string[] albumArtists)
    {
        return track.Artists.Count > 0 ? track.Artists.ToArray() : albumArtists;
    }

    /// <summary>多碟专辑(显式碟号或本地存在多个碟组)保留碟号,单碟专辑不写 ParentIndexNumber。</summary>
    private static int? ResolveParentIndexNumber(MediaContext context)
    {
        var isMultiDisc = context.Group.DiscNumber is not null || context.AllDiscs.Count > 1;
        return isMultiDisc ? context.Media.Position : null;
    }

    private static string BuildDisplayName(string? trackTitle, string strmPath, bool isCommentary)
    {
        var displayName = (trackTitle ?? Path.GetFileNameWithoutExtension(strmPath)).Trim();
        return isCommentary ? displayName + " (Commentary)" : displayName;
    }

    private static void ApplyProviderIds(Audio item, AlbumSearchResult album, MediaContext context)
    {
        SetProviderId(item, PluginConstants.MusicBrainzTrack, context.Track.RecordingMbid);
        SetProviderId(item, PluginConstants.MusicBrainzAlbum, album.ReleaseMbid);
        // 曲目已有独立艺人信息时不能回退到专辑艺人 MBID，避免不同艺人被错误合并。
        var artistMbid = context.Track.Artists.Count == 0 ? album.AlbumArtistMbid : context.Track.ArtistMbid;
        SetProviderId(item, PluginConstants.MusicBrainzArtist, artistMbid);
        SetProviderId(item, PluginConstants.MusicBrainzAlbumArtist, album.AlbumArtistMbid);
        SetProviderId(item, PluginConstants.MusicBrainzReleaseGroup, album.ReleaseGroupMbid);
    }

    private static void SetProviderId(Audio item, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        item.ProviderIds[key] = value.Trim();
    }

    /// <summary>构建单条 Audio 所需的碟组/碟/madia/曲目上下文,避免长参数列表。</summary>
    private readonly record struct MediaContext(
        IReadOnlyList<LocalDisc> AllDiscs,
        LocalDisc Group,
        ReleaseMedia Media,
        AlbumTrack Track,
        bool IsCommentary)
    {
        public static MediaContext From(
            AlbumDirectoryScan scan,
            LocalDisc group,
            ReleaseMedia media,
            AlbumTrack track,
            bool isCommentary)
        {
            return new MediaContext(scan.Discs, group, media, track, isCommentary);
        }
    }
}
