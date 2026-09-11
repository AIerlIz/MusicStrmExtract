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
                [officialSingle, bootlegAlbum, officialAlbum]);

            Assert.Equal("official", ordered[0].Release.Id);
        }

        [Fact]
        public void Order_PrefersCompleteThenEarlierDate()
        {
            var incomplete = CreateScored("incomplete", "Official", "Album", "2020", "US", 100);
            var later = CreateScored("later", "Official", "Album", "2020-02-01", "US", 100);
            var earlier = CreateScored("earlier", "Official", "Album", "2020-01-01", "US", 100);

            var ordered = SearchCandidateOrderingPolicy.Order(
                [incomplete, later, earlier]);

            Assert.Equal(new[] { "earlier", "later", "incomplete" }, ordered.ConvertAll(r => r.Release.Id));
        }

        [Fact]
        public void Order_UsesScoreTitleAndIdAsStableTieBreakers()
        {
            var lowerScore = CreateScored("b", "Official", "Album", "2020-01-01", "US", 90, "Alpha");
            var titleLater = CreateScored("c", "Official", "Album", "2020-01-01", "US", 100, "Beta");
            var titleEarlier = CreateScored("d", "Official", "Album", "2020-01-01", "US", 100, "Alpha");
            var sameTitleLowerId = CreateScored("a", "Official", "Album", "2020-01-01", "US", 100, "Alpha");

            var ordered = SearchCandidateOrderingPolicy.Order(
                [lowerScore, titleLater, titleEarlier, sameTitleLowerId]);

            // score 降序 → title 升序 → id 升序
            Assert.Equal(new[] { "a", "d", "c", "b" }, ordered.ConvertAll(r => r.Release.Id));
        }

        [Fact]
        public void Order_IgnoresCountry_AfterCountryPreferenceMovedToReleaseGroupScoring()
        {
            // 搜索阶段不再做国家偏好:候选的国家只作为普通字段,不参与排序。
            // 两者状态/主类型/日期/score/标题全同,只剩 id 决定次序 —— 与请求顺序无关。
            var gb = CreateScored("zzz", "Official", "Album", "2020-01-01", "GB", 100, "Alpha");
            var us = CreateScored("aaa", "Official", "Album", "2020-01-01", "US", 100, "Alpha");

            var ordered = SearchCandidateOrderingPolicy.Order([gb, us]);

            Assert.Equal(new[] { "aaa", "zzz" }, ordered.ConvertAll(r => r.Release.Id));
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
