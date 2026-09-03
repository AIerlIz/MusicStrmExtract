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
        public void CleanAlbumName_StripsYearSuffix(string raw, string? expected)
        {
            Assert.Equal(expected, AlbumSearch.CleanAlbumName(raw));
        }
    }
}