using System.Collections.Generic;

using MediaBrowser.Controller.Entities.Audio;

using MusicStrmExtract.Online;
using MusicStrmExtract.Providers;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class AudioTrackMetadataFactoryTests
    {
        [Fact]
        public void TryBuild_CreatesAudioFromTrackMap()
        {
            var scan = SingleDiscScan(regularOnly: true);
            var album = BuildAlbum("Album", 1, "Song", "rec-1");

            var resolution = AudioTrackMetadataFactory.TryBuild(
                album,
                scan,
                @"C:\music\Album\01 - Song.flac.strm",
                null,
                null,
                1,
                false);

            Assert.NotNull(resolution);
            var item = resolution!.Item;
            Assert.Equal("Song", item.Name);
            Assert.Equal("Album", item.Album);
            Assert.Equal(2004, item.ProductionYear);
            Assert.Equal(1, item.IndexNumber);
            Assert.Null(item.ParentIndexNumber);
            Assert.Equal("Artist", Assert.Single(item.Artists));
            Assert.Equal("rec-1", item.ProviderIds[PluginConstants.MusicBrainzTrack]);
            Assert.Equal("release-1", item.ProviderIds[PluginConstants.MusicBrainzAlbum]);
            Assert.Equal("artist-1", item.ProviderIds[PluginConstants.MusicBrainzArtist]);
        }

        [Fact]
        public void TryBuild_MapsInterleavedCommentaryTrackNumber()
        {
            var scan = SingleDiscScan(regularOnly: false);
            var album = BuildAlbum("Album", 1, "Song", "rec-1");

            var commentary = AudioTrackMetadataFactory.TryBuild(
                album,
                scan,
                @"C:\music\Album\01 - Song (Commentary).flac.strm",
                null,
                null,
                1,
                true);
            var regular = AudioTrackMetadataFactory.TryBuild(
                album,
                scan,
                @"C:\music\Album\02 - Song.flac.strm",
                null,
                null,
                2,
                false);

            Assert.NotNull(commentary);
            Assert.Equal("Song (Commentary)", commentary!.Item.Name);
            Assert.Equal("rec-1", commentary.Item.ProviderIds[PluginConstants.MusicBrainzTrack]);
            Assert.NotNull(regular);
            Assert.Equal("Song", regular!.Item.Name);
            Assert.Equal("rec-1", regular.Item.ProviderIds[PluginConstants.MusicBrainzTrack]);
        }

        [Fact]
        public void TryBuild_ReturnsNullWithoutTrackNumber()
        {
            var scan = SingleDiscScan(regularOnly: true);
            var album = BuildAlbum("Album", 1, "Song", "rec-1");

            var resolution = AudioTrackMetadataFactory.TryBuild(
                album,
                scan,
                @"C:\music\Album\01 - Song.flac.strm",
                null,
                null,
                0,
                false);

            Assert.Null(resolution);
        }

        [Fact]
        public void TryBuild_KeepsRawTrackNumberWhenDiscHasNoRawReferences()
        {
            var disc = new LocalDisc { DiscNumber = null };
            disc.TrackNumbers.Add(7);
            var scan = new AlbumDirectoryScan(
                new[] { disc },
                new Dictionary<int, List<TrackReference>>());
            var album = BuildAlbum("Album", 7, "Song", "rec-7");

            var resolution = AudioTrackMetadataFactory.TryBuild(
                album,
                scan,
                @"C:\music\Album\07 - Song.flac.strm",
                null,
                null,
                7,
                false);

            Assert.NotNull(resolution);
            Assert.Equal("rec-7", resolution!.Item.ProviderIds[PluginConstants.MusicBrainzTrack]);
        }

        private static AlbumDirectoryScan SingleDiscScan(bool regularOnly)
        {
            var disc = new LocalDisc { DiscNumber = null };
            disc.TrackNumbers.Add(1);
            var raw = new Dictionary<int, List<TrackReference>>
            {
                [0] = regularOnly
                    ? new List<TrackReference> { new TrackReference(1, false) }
                    : new List<TrackReference>
                    {
                        new TrackReference(1, true),
                        new TrackReference(2, false)
                    }
            };
            return new AlbumDirectoryScan(new[] { disc }, raw);
        }

        private static AlbumSearchResult BuildAlbum(string title, int trackNumber, string trackTitle, string recordingMbid)
        {
            var track = new AlbumTrack(
                trackNumber,
                trackTitle,
                recordingMbid,
                "artist-1",
                new[] { "Artist" });
            var media = new ReleaseMedia(1, "CD", new[] { track });
            return new AlbumSearchResult(
                true,
                title,
                2004,
                "release-1",
                "rg-1",
                "Artist",
                "album-artist-1",
                new[] { media });
        }
    }
}
