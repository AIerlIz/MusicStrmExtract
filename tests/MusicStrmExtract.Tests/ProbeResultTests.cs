using System.Collections.Generic;

using MusicStrmExtract.Probing;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class ProbeResultTests
    {
        [Fact]
        public void FromJson_ParsesTagsAndAttachedCover()
        {
            var json = """
            {
              "streams": [
                { "index": 0, "codec_type": "audio", "codec_name": "flac",
                  "disposition": { "attached_pic": 0 } },
                { "index": 1, "codec_type": "video", "codec_name": "mjpeg", "width": 500,
                  "disposition": { "attached_pic": 1 }, "tags": { "comment": "Cover (front)" } }
              ],
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
            Assert.Equal("flac", probe.Container);
            Assert.Equal(242.64, probe.DurationSeconds!.Value, 2);
            Assert.Equal(28955037, probe.SizeBytes);
            Assert.True(probe.HasEmbeddedCover);
            Assert.Equal("abc-123", probe.Tags["musicbrainz_trackid"]);
            Assert.Equal("Seven", probe.Tags["ALBUM"]);
        }

        [Fact]
        public void FromJson_NoTags_ReturnsEmpty()
        {
            var probe = ProbeResult.FromJson("""{"streams":[],"format":{"format_name":"mp3"}}""");
            Assert.False(probe.HasTags);
            Assert.Equal("mp3", probe.Container);
        }

        [Fact]
        public void FromJson_RawControlCharsInTagValue_Survives()
        {
            // 某些标签值含裸 \r/\n 控制字符(ffprobe 未转义), 不应导致解析崩溃
            var json = "{ \"streams\": [], \"format\": { \"tags\": { \"TITLE\": \"a\u000Db\", \"COMMENT\": \"x\u000Ay\" } } }";
            var probe = ProbeResult.FromJson(json);
            Assert.True(probe.HasTags);
            Assert.Contains("a", probe.Tags["TITLE"]);
            Assert.Contains("b", probe.Tags["TITLE"]);
        }
    }
}
