using System.Linq;
using System.Text.Json;

using MusicStrmExtract.Online;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class ReleaseJsonReaderTests
    {
        [Fact]
        public void ParseSearchReleases_ReadsCandidateFieldsAndNestedGroup()
        {
            var root = JsonDocument.Parse(
                "{\"releases\":[{" +
                "\"id\":\"release-1\",\"score\":92,\"title\":\"1989\",\"date\":\"2014-10-27\"," +
                "\"status\":\"Official\",\"country\":\"US\",\"packaging\":\"Jewel Case\"," +
                "\"artist-credit\":[{\"artist\":{\"id\":\"artist-1\",\"name\":\"Taylor Swift\"}}]," +
                "\"release-group\":{\"id\":\"rg-1\",\"primary-type\":\"Album\"}}]}").RootElement;

            var parsed = ReleaseJsonReader.ParseSearchReleases(root);

            var scored = Assert.Single(parsed);
            Assert.Equal(92, scored.Score);
            Assert.Equal("release-1", scored.Release.Id);
            Assert.Equal("1989", scored.Release.Title);
            Assert.Equal("2014-10-27", scored.Release.Date);
            Assert.Equal("Official", scored.Release.Status);
            Assert.Equal("US", scored.Release.Country);
            Assert.Equal("Album", scored.Release.PrimaryType);
            Assert.Equal("rg-1", scored.Release.ReleaseGroupMbid);
            Assert.Equal("Taylor Swift", scored.Release.ArtistCredits.Single().Name);
            Assert.Equal("artist-1", scored.Release.ArtistCredits.Single().Id);
        }

        [Fact]
        public void ParseReleaseGroup_ReadsMediaLayoutAndScoringFields()
        {
            var root = JsonDocument.Parse(
                "{\"releases\":[{" +
                "\"id\":\"us\",\"title\":\"1989\",\"date\":\"2014-10-27\",\"status\":\"Official\"," +
                "\"country\":\"US\",\"barcode\":\"123\",\"packaging\":\"Jewel Case\"," +
                "\"disambiguation\":null,\"media\":[{\"position\":1,\"format\":\"CD\",\"track-count\":13}]}," +
                "{\"id\":\"jp\",\"title\":\"1989\",\"date\":\"2014-10-27\",\"status\":\"Official\"," +
                "\"country\":\"JP\",\"barcode\":\"456\",\"packaging\":null," +
                "\"disambiguation\":\"MOINS CHER\",\"media\":[{\"position\":1,\"format\":\"CD\",\"track-count\":13}]}" +
                "]}").RootElement;

            var releases = ReleaseJsonReader.ParseReleaseGroup(root);

            Assert.Equal(2, releases.Count);
            Assert.Equal("Jewel Case", releases[0].Packaging);
            Assert.Null(releases[0].Disambiguation);
            Assert.Equal("MOINS CHER", releases[1].Disambiguation);
            var media = Assert.Single(releases[0].Media);
            Assert.Equal(1, media.Position);
            Assert.Equal("CD", media.Format);
            Assert.Equal(13, media.TrackCount);
        }

        [Fact]
        public void ParseRelease_UsesNameOnlyArtistCreditAsFallback()
        {
            var release = JsonDocument.Parse(
                "{\"id\":\"release-1\",\"title\":\"Compilation\"," +
                "\"artist-credit\":[{\"name\":\"Various Artists\"}]}").RootElement;

            var parsed = ReleaseJsonReader.ParseRelease(release);

            Assert.Equal("Various Artists", parsed.ArtistCredits.Single().Name);
            Assert.Null(parsed.ArtistCredits.Single().Id);
        }
    }
}
