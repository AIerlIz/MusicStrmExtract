using System.Collections.Generic;

using MusicStrmExtract.Metadata;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class TagParserTests
    {
        [Fact]
        public void Parse_VorbisMixedCase_AsProbed()
        {
            // 与实测 ffprobe 输出一致:键大小写混合
            var tags = new Dictionary<string, string>
            {
                ["ALBUM"] = "Common Jasmine Orange (七里香)",
                ["album_artist"] = "Jay Chou (周杰倫)",
                ["ARTIST"] = "Jay Chou (周杰倫)",
                ["COMMENT"] = "Hong Kong Release",
                ["disc"] = "1",
                ["GENRE"] = "Mandopop",
                ["TITLE"] = "我的地盤",
                ["DISCTOTAL"] = "1",
                ["TRACKTOTAL"] = "10",
                ["DATE"] = "2004",
                ["track"] = "01",
                ["musicbrainz_trackid"] = "f13d05fa-0e8d-3731-be94-2ca5cfaf5dc0"
            };

            var md = TagParser.Parse(tags);

            Assert.Equal("我的地盤", md.Title);
            Assert.Equal("Common Jasmine Orange (七里香)", md.Album);
            Assert.Single(md.Artists);
            Assert.Equal("Jay Chou (周杰倫)", md.Artists[0]);
            Assert.Single(md.AlbumArtists);
            Assert.Equal("Jay Chou (周杰倫)", md.AlbumArtists[0]);
            Assert.Single(md.Genres);
            Assert.Equal("Mandopop", md.Genres[0]);
            Assert.Equal(2004, md.Year);
            Assert.Equal(1, md.IndexNumber);
            Assert.Equal(1, md.ParentIndexNumber);
            Assert.Equal("f13d05fa-0e8d-3731-be94-2ca5cfaf5dc0", md.MusicBrainzTrackId);
            Assert.True(md.HasAnyMbid);
        }

        [Fact]
        public void Parse_Id3StyleKeys_WithSpacesAndAliases()
        {
            var tags = new Dictionary<string, string>
            {
                ["title"] = "晴天",
                ["artist"] = "周杰伦",
                ["album"] = "叶惠美",
                ["MusicBrainz Album Id"] = "5f3c9a11-1111-2222-3333-444455556666",
                ["MusicBrainz Track Id"] = "ab12cd34-1111-2222-3333-444455556666",
                ["MusicBrainz Artist Id"] = "cd12ef34-1111-2222-3333-444455556666",
                ["TRACKNUMBER"] = "3/12",
                ["date"] = "2003-07-31"
            };

            var md = TagParser.Parse(tags);

            Assert.Equal("晴天", md.Title);
            Assert.Equal("叶惠美", md.Album);
            Assert.Equal(3, md.IndexNumber);
            Assert.Equal(2003, md.Year);
            Assert.Equal("5f3c9a11-1111-2222-3333-444455556666", md.MusicBrainzAlbumId);
            Assert.Equal("ab12cd34-1111-2222-3333-444455556666", md.MusicBrainzTrackId);
            Assert.Equal("cd12ef34-1111-2222-3333-444455556666", md.MusicBrainzArtistId);
        }

        [Fact]
        public void Parse_MultiValueAlbumArtist_DoesNotSplitArtistNameWithSlashInsideName()
        {
            // 含 "/" 仅在带空格分隔符时拆分;"AC/DC" 不应被拆分
            var tags = new Dictionary<string, string>
            {
                ["album_artist"] = "AC/DC",
                ["TITLE"] = "Back in Black",
                ["ARTIST"] = "A / B"
            };

            var md = TagParser.Parse(tags);

            Assert.Single(md.AlbumArtists);
            Assert.Equal("AC/DC", md.AlbumArtists[0]);
            Assert.Equal(2, md.Artists.Count);
            Assert.Equal("A", md.Artists[0]);
            Assert.Equal("B", md.Artists[1]);
        }

        [Fact]
        public void Parse_EmptyOrNull_ReturnsEmptyMetadata()
        {
            Assert.True(TagParser.Parse(new Dictionary<string, string>()).IsEmpty);
            Assert.True(TagParser.Parse(null!).IsEmpty);
        }

        [Fact]
        public void NormalizeKey_IgnoresCaseAndSeparators()
        {
            Assert.Equal("MUSICBRAINZTRACKID", TagParser.NormalizeKey("MusicBrainz Track Id"));
            Assert.Equal("MUSICBRAINZTRACKID", TagParser.NormalizeKey("musicbrainz_trackid"));
            Assert.Equal("MUSICBRAINZTRACKID", TagParser.NormalizeKey("MUSICBRAINZ-TRACK.ID"));
        }
    }
}
