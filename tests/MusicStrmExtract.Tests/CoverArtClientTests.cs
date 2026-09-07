using MusicStrmExtract.Online;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class CoverArtClientTests
    {
        [Fact]
        public void ParseCoverArtCount_PrefersFrontAndCounts()
        {
            var frontThree = TestReleaseJson.BuildCoverArt(3, front: true);
            var noFrontFive = TestReleaseJson.BuildCoverArt(5, front: false);
            var singleFront = TestReleaseJson.BuildCoverArt(1, front: true);

            Assert.Equal(10003, CoverArtClient.ParseCoverArtCount(frontThree)); // 有正面 + 3 图
            Assert.Equal(5, CoverArtClient.ParseCoverArtCount(noFrontFive));    // 无正面 + 5 图
            Assert.Equal(10001, CoverArtClient.ParseCoverArtCount(singleFront)); // 有正面 + 1 图
        }

        [Fact]
        public void BuildFrontImageUrl_RespectsConfiguredBase()
        {
            var client = new CoverArtClient("https://mirror.example/release");

            Assert.Equal(
                "https://mirror.example/release/release-1/front-500",
                client.BuildFrontImageUrl("release-1"));
        }
    }
}
