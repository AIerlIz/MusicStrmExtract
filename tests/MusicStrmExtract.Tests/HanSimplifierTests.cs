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
        [InlineData("應對繁體選擇", "应对繁体选择")]
        [InlineData("遠方的燈", "远方的灯")]
        [InlineData("經過這週", "经过这周")]
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
        public void Simplify_OpenCcCoversRareWords()
        {
            // 表外生僻字:OpenCC 词级全覆盖;词典未部署时回退手写表(跳过)
            if (!HanSimplifier.IsOpenCcAvailable)
            {
                return;
            }

            Assert.Equal("郁闷", HanSimplifier.Simplify("鬱悶"));
            Assert.Equal("啰嗦", HanSimplifier.Simplify("囉嗦"));
        }

        [Fact]
        public void Simplify_NullOrEmpty_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, HanSimplifier.Simplify(null));
            Assert.Equal(string.Empty, HanSimplifier.Simplify(string.Empty));
        }
    }
}