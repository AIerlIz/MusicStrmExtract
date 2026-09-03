using MusicStrmExtract.Probing;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class ProbeResultTests
    {
        [Fact]
        public void FromJson_ParsesTags()
        {
            var json = """
            {
              "format": {
                "filename": "https://example.com/x.flac",
                "format_name": "flac",
                "duration": "242.640000",
                "size": "28955037",
                "tags": { "ALBUM": "Seven", "TITLE": "Song", "musicbrainz_trackid": "abc-123" }
              }
            }
            """;

            var probe = ProbeResult.FromJson(json);

            Assert.True(probe.HasTags);
            Assert.Equal("abc-123", probe.Tags["musicbrainz_trackid"]);
            Assert.Equal("Seven", probe.Tags["ALBUM"]);
        }

        [Fact]
        public void FromJson_NoTags_ReturnsEmpty()
        {
            var probe = ProbeResult.FromJson("""{"format":{"format_name":"mp3"}}""");
            Assert.False(probe.HasTags);
        }

        [Fact]
        public void FromJson_RawControlCharsInTagValue_Survives()
        {
            // 某些标签值含裸 \r/\n 控制字符(ffprobe 未转义), 不应导致解析崩溃
            var json = "{ \"format\": { \"tags\": { \"TITLE\": \"a\u000Db\", \"COMMENT\": \"x\u000Ay\" } } }";
            var probe = ProbeResult.FromJson(json);
            Assert.True(probe.HasTags);
            Assert.Contains("a", probe.Tags["TITLE"]);
            Assert.Contains("b", probe.Tags["TITLE"]);
        }
    }
}
