using MusicStrmExtract.Online;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class AlbumSearchResultFactoryTests
    {
        [Fact]
        public void Create_UsesReleaseGroupFallbackForMissingReleaseFields()
        {
            var release = new ReleaseSummary(
                "release-1",
                null,
                "2020-01-02",
                "Official",
                null,
                null,
                null,
                null,
                "Album",
                null,
                [],
                []);
            var group = new ParsedReleaseGroup(
                "rg-1",
                "Group Album",
                "Album",
                null,
                [new ArtistCredit("Group Artist", "artist-1")],
                []);
            var media = new ReleaseMedia(1, "CD", [new AlbumTrack(1, "Song", "rec-1", null, [])]);

            var result = AlbumSearchResultFactory.Create(release, [media], group);

            Assert.True(result.Found);
            Assert.Equal("Group Album", result.Title);
            Assert.Equal("rg-1", result.ReleaseGroupMbid);
            Assert.Equal("Group Artist", result.ArtistName);
            Assert.Equal("artist-1", result.AlbumArtistMbid);
            Assert.Equal(2020, result.Year);
        }

        [Fact]
        public void Create_PrefersReleaseArtistCredits()
        {
            var release = new ReleaseSummary(
                "release-1",
                "Release Album",
                "2020",
                "Official",
                null,
                null,
                null,
                null,
                "Album",
                "rg-1",
                [new ArtistCredit("Release Artist", "artist-2")],
                []);
            var group = new ParsedReleaseGroup(
                "rg-1",
                "Group Album",
                "Album",
                null,
                [new ArtistCredit("Group Artist", "artist-1")],
                []);

            var result = AlbumSearchResultFactory.Create(release, [], group);

            Assert.Equal("Release Album", result.Title);
            Assert.Equal("Release Artist", result.ArtistName);
            Assert.Equal("artist-2", result.AlbumArtistMbid);
            Assert.Equal(2020, result.Year);
        }
    }
}
