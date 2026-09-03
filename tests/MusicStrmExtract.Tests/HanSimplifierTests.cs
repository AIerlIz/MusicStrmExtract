using MusicStrmExtract.Metadata;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class HanSimplifierTests
    {
        [Theory]
        [InlineData("周杰倫", "周杰伦")]
        [InlineData("葉惠美", "叶惠美")]
        [InlineData("無與倫比", "无与伦比")]
        [InlineData("亂舞春秋", "乱舞春秋")]
        [InlineData("雙刀", "双刀")]
        public void Simplify_ConvertsCommonTraditionalToSimplified(string input, string expected)
        {
            Assert.Equal(expected, HanSimplifier.Simplify(input));
        }

        [Fact]
        public void Simplify_LeavesUnmappedAndAsciiUntouched()
        {
            Assert.Equal("七里香 Qi-Li-Xiang", HanSimplifier.Simplify("七里香 Qi-Li-Xiang"));
        }

        [Fact]
        public void Simplify_NullOrEmpty_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, HanSimplifier.Simplify(null));
            Assert.Equal(string.Empty, HanSimplifier.Simplify(string.Empty));
        }
    }
}