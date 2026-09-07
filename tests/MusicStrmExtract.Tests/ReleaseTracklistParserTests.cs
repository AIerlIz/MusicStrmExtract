using System.Linq;

using MusicStrmExtract.Online;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class ReleaseTracklistParserTests
    {
        [Fact]
        public void ParseReleaseMedias_ParsesTracksWithRecordingData()
        {
            var root = TestReleaseJson.BuildRelease(
                (1, new[] { (1, "我的地盤"), (2, "七里香") }),
                (2, new[] { (1, "七里香MV") }));

            var medias = ReleaseTracklistParser.ParseReleaseMedias(root);

            Assert.Equal(2, medias.Count);
            Assert.Equal(1, medias[0].Position);
            Assert.Equal(2, medias[0].Tracks.Count);
            Assert.Equal(1, medias[0].Tracks[0].Number);
            Assert.Equal("我的地盤", medias[0].Tracks[0].Title);
            Assert.Equal("rec-1-1", medias[0].Tracks[0].RecordingMbid);
            Assert.Equal("周杰倫", medias[0].Tracks[0].Artists.First());
            Assert.Equal("art-1", medias[0].Tracks[0].ArtistMbid);
            Assert.Equal(2, medias[1].Position);
            Assert.Single(medias[1].Tracks);
        }
    }
}
