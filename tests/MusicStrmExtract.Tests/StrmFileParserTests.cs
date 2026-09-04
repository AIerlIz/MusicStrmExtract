using MusicStrmExtract.Providers;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class StrmFileParserTests
    {
        [Theory]
        [InlineData("01 - Lavender Haze.m4a.strm", null, 1)]
        [InlineData("02 - Maroon.flac.strm", null, 2)]
        [InlineData("1-01 - Lavender Haze.strm", 1, 1)]
        [InlineData("CD1-01 - Lavender Haze.strm", 1, 1)]
        [InlineData("Disc 1 - 01 - Lavender Haze.strm", 1, 1)]
        [InlineData("01.01 - Lavender Haze.strm", 1, 1)]
        [InlineData("01 - 7 Rings.strm", null, 1)]
        public void ParseFileName_ReturnsDiscAndTrack(string name, int? disc, int track)
        {
            var (actualDisc, actualTrack) = StrmFileParser.ParseFileName(name);

            Assert.Equal(disc, actualDisc);
            Assert.Equal(track, actualTrack);
        }

        [Fact]
        public void ParseFileName_FourDigitYear_IsNotATrackNumber()
        {
            var (disc, track) = StrmFileParser.ParseFileName("2013 - Song.strm");

            Assert.Null(disc);
            Assert.Equal(0, track);
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
