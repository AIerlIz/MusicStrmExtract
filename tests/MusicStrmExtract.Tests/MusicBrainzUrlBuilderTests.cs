using MusicStrmExtract.Online;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class MusicBrainzUrlBuilderTests
    {
        [Fact]
        public void SearchReleases_RemovesQuotesAndEncodesQuery()
        {
            var url = MusicBrainzUrlBuilder.SearchReleases(
                "https://mb.example",
                "1989",
                "Jay \"Chou\"",
                10);

            Assert.Contains("/ws/2/release?query=", url);
            Assert.DoesNotContain("Chou%22%22", url);
            Assert.Contains("limit=10", url);
        }

        [Fact]
        public void ReleaseGroupEndpoints_PreserveExpectedParameters()
        {
            var lookup = MusicBrainzUrlBuilder.GetReleaseGroup(
                "https://mb.example",
                "rg-1");
            var browse = MusicBrainzUrlBuilder.BrowseReleases(
                "https://mb.example",
                "rg-1",
                offset: 25,
                limit: 100);

            Assert.Contains("/ws/2/release-group/rg-1?inc=releases+media+artist-credits", lookup);
            Assert.Contains("release?release-group=rg-1", browse);
            Assert.Contains("limit=100", browse);
            Assert.Contains("offset=25", browse);
        }
    }
}
