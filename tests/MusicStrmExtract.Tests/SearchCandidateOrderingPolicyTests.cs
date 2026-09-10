using System.Collections.Generic;

using MusicStrmExtract.Online;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class SearchCandidateOrderingPolicyTests
    {
        [Fact]
        public void Order_PutsOfficialAlbumWithCompleteDateFirst()
        {
            var officialAlbum = CreateScored("official", "Official", "Album", "2020-01-02", "US", 10);
            var officialSingle = CreateScored("single", "Official", "Single", "2020-01-01", "US", 100);
            var bootlegAlbum = CreateScored("bootleg", "Bootleg", "Album", "2020-01-01", "US", 100);

            var ordered = SearchCandidateOrderingPolicy.Order(
                [officialSingle, bootlegAlbum, officialAlbum],
                preferredCountry: null);

            Assert.Equal("official", ordered[0].Release.Id);
        }

        [Fact]
        public void Order_PrefersCompleteThenEarlierDate()
        {
            var incomplete = CreateScored("incomplete", "Official", "Album", "2020", "US", 100);
            var later = CreateScored("later", "Official", "Album", "2020-02-01", "US", 100);
            var earlier = CreateScored("earlier", "Official", "Album", "2020-01-01", "US", 100);

            var ordered = SearchCandidateOrderingPolicy.Order(
                [incomplete, later, earlier],
                preferredCountry: null);

            Assert.Equal(new[] { "earlier", "later", "incomplete" }, ordered.ConvertAll(r => r.Release.Id));
        }

        [Fact]
        public void Order_UsesCountryScoreTitleAndIdAsStableTieBreakers()
        {
            var otherCountry = CreateScored("a", "Official", "Album", "2020-01-01", "GB", 100, "Alpha");
            var lowerScore = CreateScored("b", "Official", "Album", "2020-01-01", "US", 90, "Alpha");
            var titleLater = CreateScored("c", "Official", "Album", "2020-01-01", "US", 100, "Beta");
            var titleEarlier = CreateScored("d", "Official", "Album", "2020-01-01", "US", 100, "Alpha");

            var ordered = SearchCandidateOrderingPolicy.Order(
                [otherCountry, lowerScore, titleLater, titleEarlier],
                preferredCountry: "US");

            Assert.Equal(new[] { "d", "c", "b", "a" }, ordered.ConvertAll(r => r.Release.Id));
        }

        private static ScoredRelease CreateScored(
            string id,
            string status,
            string primaryType,
            string date,
            string country,
            int score,
            string? title = null)
        {
            return new ScoredRelease(
                new ReleaseSummary(
                    id,
                    title ?? id,
                    date,
                    status,
                    country,
                    null,
                    null,
                    null,
                    primaryType,
                    null,
                    [],
                    []),
                score);
        }
    }
}
