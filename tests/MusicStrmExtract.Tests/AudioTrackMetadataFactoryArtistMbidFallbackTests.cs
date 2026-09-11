using System.Collections.Generic;

using MediaBrowser.Controller.Entities.Audio;

using MusicStrmExtract.Online;
using MusicStrmExtract.Providers;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class AudioTrackMetadataFactoryArtistMbidFallbackTests
    {
        [Fact]
        public void TryBuild_SetsMusicBrainzArtist_FromAlbumArtistWhenTrackHasNoArtists()
        {
            var scan = SingleDiscScan();
            var album = BuildAlbumWithNoTrackArtists();

            var resolution = AudioTrackMetadataFactory.TryBuild(
                album,
                scan,
                @"C:\music\Album\01 - Song.flac.strm",
                null,
                null,
                1,
                false);

            Assert.NotNull(resolution);
            Assert.Equal("album-artist-1", resolution.Item.ProviderIds[PluginConstants.MusicBrainzArtist]);
        }

        [Fact]
        public void TryBuild_SetsMusicBrainzArtist_EmptyWhenTrackHasArtistsButNoMbid()
        {
            var scan = SingleDiscScan();
            var album = BuildAlbumWithTrackArtistsNoMbid();

            var resolution = AudioTrackMetadataFactory.TryBuild(
                album,
                scan,
                @"C:\music\Album\01 - Song.flac.strm",
                null,
                null,
                1,
                false);

            Assert.NotNull(resolution);
            Assert.DoesNotContain(PluginConstants.MusicBrainzArtist, resolution.Item.ProviderIds.Keys);
        }

        [Fact]
        public void TryBuild_SetsMusicBrainzArtist_FromTrackWhenTrackHasArtistsAndMbid()
        {
            var scan = SingleDiscScan();
            var album = BuildAlbumWithTrackArtistsAndMbid();

            var resolution = AudioTrackMetadataFactory.TryBuild(
                album,
                scan,
                @"C:\music\Album\01 - Song.flac.strm",
                null,
                null,
                1,
                false);

            Assert.NotNull(resolution);
            Assert.Equal("track-artist-1", resolution.Item.ProviderIds[PluginConstants.MusicBrainzArtist]);
        }

        private static AlbumDirectoryScan SingleDiscScan()
        {
            var disc = new LocalDisc { DiscNumber = null };
            disc.TrackNumbers.Add(1);
            var raw = new Dictionary<int, List<TrackReference>>
            {
                [0] = new List<TrackReference> { new TrackReference(1, false) }
            };
            return new AlbumDirectoryScan(new[] { disc }, raw);
        }

        private static AlbumSearchResult BuildAlbumWithNoTrackArtists()
        {
            var track = new AlbumTrack(1, "Song", "rec-1", null, []);
            var media = new ReleaseMedia(1, "CD", [track]);
            return new AlbumSearchResult(
                true, "Album", 2004, "release-1", "rg-1",
                "Artist", "album-artist-1", [media]);
        }

        private static AlbumSearchResult BuildAlbumWithTrackArtistsNoMbid()
        {
            var track = new AlbumTrack(1, "Song", "rec-1", null, ["Featured Artist"]);
            var media = new ReleaseMedia(1, "CD", [track]);
            return new AlbumSearchResult(
                true, "Album", 2004, "release-1", "rg-1",
                "Artist", "album-artist-1", [media]);
        }

        private static AlbumSearchResult BuildAlbumWithTrackArtistsAndMbid()
        {
            var track = new AlbumTrack(1, "Song", "rec-1", "track-artist-1", ["Featured Artist"]);
            var media = new ReleaseMedia(1, "CD", [track]);
            return new AlbumSearchResult(
                true, "Album", 2004, "release-1", "rg-1",
                "Artist", "album-artist-1", [media]);
        }
    }
}
