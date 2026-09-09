using MediaBrowser.Controller.Entities.Audio;
using MusicStrmExtract.Online;
using System;
using System.IO;
using System.Linq;

namespace MusicStrmExtract.Providers
{
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
                BuildAudio(album, scan, strmPath, group, media, track, isCommentary),
                media,
                track,
                isCommentary);
        }

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
                rawRefs.Where(r => r.IsCommentary).Select(r => r.Number).ToArray(),
                rawRefs.Where(r => !r.IsCommentary).Select(r => r.Number).ToArray());
        }

        private static Audio BuildAudio(
            AlbumSearchResult album,
            AlbumDirectoryScan scan,
            string strmPath,
            LocalDisc group,
            ReleaseMedia media,
            AlbumTrack track,
            bool isCommentary)
        {
            var albumArtists = !string.IsNullOrWhiteSpace(album.ArtistName)
                ? new[] { album.ArtistName! }
                : Array.Empty<string>();
            var trackArtists = track.Artists.Count > 0 ? track.Artists.ToArray() : albumArtists;

            var displayName = (track.Title ?? Path.GetFileNameWithoutExtension(strmPath)).Trim();
            if (isCommentary)
                displayName += " (Commentary)";

            var item = new Audio
            {
                Name = displayName,
                Album = album.Title,
                ProductionYear = album.Year,
                IndexNumber = track.Number,
                ParentIndexNumber = group.DiscNumber is not null || scan.Discs.Count > 1
                    ? media.Position
                    : null,
                Artists = trackArtists,
                AlbumArtists = albumArtists
            };

            SetProviderId(item, PluginConstants.MusicBrainzTrack, track.RecordingMbid);
            SetProviderId(item, PluginConstants.MusicBrainzAlbum, album.ReleaseMbid);
            // 曲目已有独立艺人信息时不能回退到专辑艺人 MBID，避免不同艺人被错误合并。
            var artistMbid = track.Artists.Count == 0 ? album.AlbumArtistMbid : track.ArtistMbid;
            SetProviderId(item, PluginConstants.MusicBrainzArtist, artistMbid);
            SetProviderId(item, PluginConstants.MusicBrainzAlbumArtist, album.AlbumArtistMbid);
            SetProviderId(item, PluginConstants.MusicBrainzReleaseGroup, album.ReleaseGroupMbid);
            return item;
        }

        private static void SetProviderId(Audio item, string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            item.ProviderIds[key] = value.Trim();
        }
    }
}
