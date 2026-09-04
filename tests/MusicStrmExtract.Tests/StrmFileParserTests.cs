using MusicStrmExtract.Providers;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class StrmFileParserTests
    {
        [Theory]
        [InlineData("01 - Lavender Haze.m4a.strm", null, 1, false)]
        [InlineData("02 - Maroon.flac.strm", null, 2, false)]
        [InlineData("1-01 - Lavender Haze.strm", 1, 1, false)]
        [InlineData("CD1-01 - Lavender Haze.strm", 1, 1, false)]
        [InlineData("Disc 1 - 01 - Lavender Haze.strm", 1, 1, false)]
        [InlineData("01.01 - Lavender Haze.strm", 1, 1, false)]
        [InlineData("01 - 7 Rings.strm", null, 1, false)]
        [InlineData("01 - Welcome to New York (Commentary).flac.strm", null, 1, true)]
        [InlineData("02 - Welcome To New York.flac.strm", null, 2, false)]
        [InlineData("03 - Blank Space Commentary.flac.strm", null, 3, true)]
        [InlineData("01 - 评论轨.flac.strm", null, 1, true)]
        public void ParseFileName_ReturnsDiscTrackAndCommentaryFlag(string name, int? disc, int track, bool isCommentary)
        {
            var (actualDisc, actualTrack, actualCommentary) = StrmFileParser.ParseFileName(name);

            Assert.Equal(disc, actualDisc);
            Assert.Equal(track, actualTrack);
            Assert.Equal(isCommentary, actualCommentary);
        }

        [Fact]
        public void ParseFileName_FourDigitYear_IsNotATrackNumber()
        {
            var (disc, track, isCommentary) = StrmFileParser.ParseFileName("2013 - Song.strm");

            Assert.Null(disc);
            Assert.Equal(0, track);
            Assert.False(isCommentary);
        }

        [Fact]
        public void MapCommentaryTrackNumber_InterleavesOddCommentaryWithEvenRegular()
        {
            var commentary = new[] { 1, 3, 5, 7, 9, 11, 13, 15, 17, 19, 21, 23, 25 };
            var regular = new[] { 2, 4, 6, 8, 10, 12, 14, 16, 18, 20, 22, 24, 26 };

            Assert.Equal(8, StrmFileParser.MapCommentaryTrackNumber(15, true, commentary, regular));
            Assert.Equal(8, StrmFileParser.MapCommentaryTrackNumber(16, false, commentary, regular));
            Assert.Equal(13, StrmFileParser.MapCommentaryTrackNumber(25, true, commentary, regular));
            Assert.Equal(13, StrmFileParser.MapCommentaryTrackNumber(26, false, commentary, regular));
        }

        [Fact]
        public void MapCommentaryTrackNumber_KeepsSameTrackNumbers()
        {
            var commentary = new[] { 1, 2, 3 };
            var regular = new[] { 1, 2, 3 };

            Assert.Equal(2, StrmFileParser.MapCommentaryTrackNumber(2, true, commentary, regular));
            Assert.Equal(2, StrmFileParser.MapCommentaryTrackNumber(2, false, commentary, regular));
        }

        [Fact]
        public void MapCommentaryTrackNumber_KeepsCommentaryAfterRegularInOrder()
        {
            var commentary = new[] { 4, 5, 6 };
            var regular = new[] { 1, 2, 3 };

            Assert.Equal(3, StrmFileParser.MapCommentaryTrackNumber(6, true, commentary, regular));
            Assert.Equal(2, StrmFileParser.MapCommentaryTrackNumber(2, false, commentary, regular));
        }

        [Fact]
        public void MapCommentaryTrackNumber_ReturnsRawWhenNoCommentaryPairing()
        {
            Assert.Equal(7, StrmFileParser.MapCommentaryTrackNumber(7, false, new[] { 1 }, new[] { 2 }));
            Assert.Equal(5, StrmFileParser.MapCommentaryTrackNumber(5, true, new[] { 5 }, new[] { 2 }));
        }

        [Theory]
        [InlineData("Disc 1", 1)]
        [InlineData("CD2", 2)]
        [InlineData("Disk 03", 3)]
        [InlineData("Midnights (2022)", null)]
        [InlineData("Taylor Swift", null)]
        public void ParseDiscFolderName_ReturnsDiscForDiscFolders(string name, int? expected)
        {
            Assert.Equal(expected, StrmFileParser.ParseDiscFolderName(name));
        }
    }
}
