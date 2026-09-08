using MusicStrmExtract.Online;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class AlbumSearchTests
    {
        [Theory]
        [InlineData("叶惠美 (2003)", "叶惠美")]
        [InlineData("七里香 (2004)", "七里香")]
        [InlineData("七里香-2004", "七里香")]
        [InlineData("The Best of Jay Chou [2003]", "The Best of Jay Chou")]
        [InlineData("七里香", "七里香")]
        [InlineData(" 叶惠美 2003 ", "叶惠美")]
        [InlineData(" ", null)]
        // 版本标签不被剥离,仅年份被剥离
        [InlineData("1989 (Taylor's Version) (2023)", "1989 (Taylor's Version)")]
        [InlineData("Midnights (3am Edition) (2022)", "Midnights (3am Edition)")]
        [InlineData("1989 D.L.X.", "1989 D.L.X")]
        public void CleanAlbumName_StripsYearSuffix(string raw, string? expected)
        {
            Assert.Equal(expected, AlbumSearch.CleanAlbumName(raw));
        }

        [Fact]
        public void CleanAlbumName_KeepsPureYearTitleAndArtistSelfTitle()
        {
            Assert.Equal("2001", AlbumSearch.CleanAlbumName("2001"));
            Assert.Equal("The 1975", AlbumSearch.CleanAlbumName("The 1975", "The 1975"));
            Assert.Equal("The 1975", AlbumSearch.CleanAlbumName("The 1975 (2013)", "The 1975"));
        }

        [Theory]
        [InlineData("1989 (Taylor's Version) (2023)", "Taylor Swift", 2023)]
        [InlineData("The 1975 (2013)", "The 1975", 2013)]
        [InlineData("叶惠美 (2003)", "周杰伦", 2003)]
        [InlineData("The 1975", "The 1975", null)]
        [InlineData("2001", "Dr. Dre", null)]
        [InlineData("No year here", "Artist", null)]
        public void ParseFolderYear_UsesCleanedReleaseYearSuffix(
            string folderName,
            string artistName,
            int? expected)
        {
            Assert.Equal(expected, AlbumSearch.ParseFolderYear(folderName, artistName));
        }
    }
}
