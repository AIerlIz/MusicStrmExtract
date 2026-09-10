using MusicStrmExtract.Online;
using MusicStrmExtract.Providers;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class ResolutionDiagnosticsStoreTests
    {
        [Fact]
        public void GetSummary_CountsAllOutcomesAndShowsLatestEntry()
        {
            var store = new ResolutionDiagnosticsStore(maxEntries: 2);
            store.Record(CreateRequest("Success"), AlbumResolutionOutcome.Found, CreateResult());
            store.Record(CreateRequest("Missing"), AlbumResolutionOutcome.NotFound);
            store.Record(CreateRequest("Unavailable"), AlbumResolutionOutcome.Unavailable, reason: "timeout");

            var summary = store.GetSummary();

            Assert.Contains("成功 1", summary, System.StringComparison.Ordinal);
            Assert.Contains("未命中 1", summary, System.StringComparison.Ordinal);
            Assert.Contains("来源异常 1", summary, System.StringComparison.Ordinal);
            Assert.Contains("'Unavailable'", summary, System.StringComparison.Ordinal);
            Assert.DoesNotContain("'Success'", summary, System.StringComparison.Ordinal);
        }

        [Fact]
        public void Clear_ResetsCountersAndEntries()
        {
            var store = new ResolutionDiagnosticsStore();
            store.Record(CreateRequest("Album"), AlbumResolutionOutcome.NotFound);

            store.Clear();

            Assert.Equal("暂无定位记录。", store.GetSummary());
        }

        private static AlbumResolutionRequest CreateRequest(string album)
        {
            return new AlbumResolutionRequest(album, "Artist", [], string.Empty);
        }

        private static AlbumSearchResult CreateResult()
        {
            return new AlbumSearchResult(
                true,
                "Album",
                2020,
                "release-1",
                "rg-1",
                "Artist",
                "artist-1",
                [new ReleaseMedia(1, "CD", [new AlbumTrack(1, "Song", "rec-1", null, [])])]);
        }
    }
}
