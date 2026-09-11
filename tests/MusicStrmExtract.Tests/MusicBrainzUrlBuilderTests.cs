using MusicStrmExtract.Online;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class MusicBrainzUrlBuilderTests
    {
        [Fact]
        public void SearchReleases_EscapesLuceneMetacharacters()
        {
            // 目录名可含 Lucene 元字符;这些字符必须被转义(反斜杠 + 原字符),
            // 转义发生在 URL 编码之前,故 URL 里表现为 %5C + 原字符的百分号编码。
            var cases = new (string Album, string ExpectedFragment)[]
            {
                ("Rock: Live", "Rock%5C%3A%20Live"),      // : 字段分隔符
                ("AC/DC", "AC%5C%2FDC"),                  // / 正则分隔符
                ("C++", "C%5C%2B%5C%2B"),                 // + 必需项运算符
                ("Rock && Roll", "Rock%20%5C%26%5C%26%20Roll"), // && 布尔运算符
                ("Rock*Live", "Rock%5C%2ALive"),          // * 通配符
            };

            foreach (var (album, expected) in cases)
            {
                var url = MusicBrainzUrlBuilder.SearchReleases("https://mb.example", album, null, 10);
                Assert.Contains(expected, url);
            }
        }

        [Fact]
        public void SearchReleases_EscapesQuoteWithoutDeleting()
        {
            // 新实现:双引号只转义(→ \")而不删除,避免"先删后转义"的混乱语义。
            var url = MusicBrainzUrlBuilder.SearchReleases("https://mb.example", "A\"B", null, 10);

            Assert.Contains("A%5C%22B", url);         // 引号被转义保留
            Assert.DoesNotContain("A%22B", url);      // 未被删除后又原样出现
        }

        [Fact]
        public void SearchReleases_EncodesArtistAndKeepsAndClause()
        {
            var url = MusicBrainzUrlBuilder.SearchReleases(
                "https://mb.example",
                "1989",
                "Jay \"Chou\"",
                10);

            Assert.Contains("/ws/2/release?query=", url);
            // AND artist: 子句保留;artist 中的引号被转义而非删除
            Assert.Contains("AND%20artist", url);
            Assert.Contains("Jay%20%5C%22Chou%5C%22", url);
            // 原断言在新实现下仍成立:旧实现删除引号会产出连续 %22%22,新实现产出 %5C%22
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
