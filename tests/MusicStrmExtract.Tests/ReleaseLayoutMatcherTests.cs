using System.Linq;

using MusicStrmExtract.Online;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class ReleaseLayoutMatcherTests
    {
        [Fact]
        public void MapLocalDiscsToMedias_MapsExplicitDiscToMediaPosition()
        {
            var disc1 = TestReleaseJson.LocalDisc(1, 1, 2);
            var disc2 = TestReleaseJson.LocalDisc(2, 1);
            var disc3 = TestReleaseJson.LocalDisc(3, 1);
            var root = TestReleaseJson.BuildRelease(
                (1, new[] { (1, "Lavender Haze"), (2, "Maroon") }),
                (2, new[] { (1, "You're Losing Me") }),
                (3, new[] { (1, "Hits Different") }));

            var map = ReleaseLayoutMatcher.MapLocalDiscsToMedias(
                new[] { disc1, disc2, disc3 },
                ReleaseTracklistParser.ParseReleaseMedias(root));

            Assert.NotNull(map);
            Assert.Equal(1, map![disc1].Position);
            Assert.Equal(2, map[disc2].Position);
            Assert.Equal(3, map[disc3].Position);
        }

        [Fact]
        public void MapLocalDiscsToMedias_RejectsWhenDiscPositionMissing()
        {
            var locals = new[] { TestReleaseJson.LocalDisc(1, 1, 2), TestReleaseJson.LocalDisc(2, 1) };
            var root = TestReleaseJson.BuildRelease((1, new[] { (1, "A"), (2, "B") }));

            var map = ReleaseLayoutMatcher.MapLocalDiscsToMedias(locals, ReleaseTracklistParser.ParseReleaseMedias(root));

            Assert.Null(map);
        }

        [Fact]
        public void MapLocalDiscsToMedias_RejectsWhenMediaDoesNotCoverTrackNumbers()
        {
            var locals = new[] { TestReleaseJson.LocalDisc(1, 1, 2, 3) };
            var root = TestReleaseJson.BuildRelease((1, new[] { (1, "A"), (2, "B") }));

            var map = ReleaseLayoutMatcher.MapLocalDiscsToMedias(locals, ReleaseTracklistParser.ParseReleaseMedias(root));

            Assert.Null(map);
        }

        [Fact]
        public void MapLocalDiscsToMedias_RejectsDuplicateDiscPosition()
        {
            var locals = new[] { TestReleaseJson.LocalDisc(1, 1), TestReleaseJson.LocalDisc(1, 2) };
            var root = TestReleaseJson.BuildRelease((1, new[] { (1, "A"), (2, "B") }));

            var map = ReleaseLayoutMatcher.MapLocalDiscsToMedias(locals, ReleaseTracklistParser.ParseReleaseMedias(root));

            Assert.Null(map);
        }

        [Fact]
        public void MapLocalDiscsToMedias_ImplicitGroupUsesLowestCoveringMedia()
        {
            var local = TestReleaseJson.LocalDisc(null, 1, 2, 3);
            var root = TestReleaseJson.BuildRelease(
                (1, new[] { (4, "D"), (5, "E") }),
                (2, new[] { (1, "A"), (2, "B"), (3, "C") }));

            var map = ReleaseLayoutMatcher.MapLocalDiscsToMedias(
                new[] { local },
                ReleaseTracklistParser.ParseReleaseMedias(root));

            Assert.NotNull(map);
            Assert.Equal(2, map![local].Position);
        }

        [Fact]
        public void HasExactTrackCount_TrueWhenEveryMediaMatchesLocalCount()
        {
            var local = new LocalDisc();
            local.TrackNumbers.AddRange(Enumerable.Range(1, 10));
            var root = TestReleaseJson.BuildRelease((1, TestReleaseJson.Tracks(1, 10)));

            var map = ReleaseLayoutMatcher.MapLocalDiscsToMedias(
                new[] { local },
                ReleaseTracklistParser.ParseReleaseMedias(root));

            Assert.NotNull(map);
            Assert.True(ReleaseLayoutMatcher.HasExactTrackCount(new[] { local }, map!));
        }

        [Fact]
        public void HasExactTrackCount_FalseWhenMediaHasBonusTracks()
        {
            var local = new LocalDisc();
            local.TrackNumbers.AddRange(Enumerable.Range(1, 10));
            var bonusTracks = TestReleaseJson.Tracks(1, 10)
                .Concat(new[] { (11, "Bonus A"), (12, "Bonus B") })
                .ToArray();
            var root = TestReleaseJson.BuildRelease((1, bonusTracks));

            var map = ReleaseLayoutMatcher.MapLocalDiscsToMedias(
                new[] { local },
                ReleaseTracklistParser.ParseReleaseMedias(root));

            Assert.NotNull(map);
            Assert.False(ReleaseLayoutMatcher.HasExactTrackCount(new[] { local }, map!));
        }

        [Fact]
        public void HasExactTrackCount_MatchesPerDiscForMultiDiscAlbums()
        {
            var disc1 = new LocalDisc();
            disc1.TrackNumbers.AddRange(Enumerable.Range(1, 17));
            var disc2 = new LocalDisc();
            disc2.TrackNumbers.AddRange(Enumerable.Range(1, 13));
            var root = TestReleaseJson.BuildRelease(
                (1, TestReleaseJson.Tracks(1, 17)),
                (2, TestReleaseJson.Tracks(1, 13)));

            var map = ReleaseLayoutMatcher.MapLocalDiscsToMedias(
                new[] { disc1, disc2 },
                ReleaseTracklistParser.ParseReleaseMedias(root));

            Assert.NotNull(map);
            Assert.True(ReleaseLayoutMatcher.HasExactTrackCount(new[] { disc1, disc2 }, map!));
        }

    }
}
