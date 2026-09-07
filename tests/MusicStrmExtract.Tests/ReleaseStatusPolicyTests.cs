using MusicStrmExtract.Online;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class ReleaseStatusPolicyTests
    {
        [Theory]
        [InlineData("Official", 0)]
        [InlineData("Promotional", 1)]
        [InlineData("Unknown", 1)]
        [InlineData("Bootleg", 2)]
        [InlineData("Withdrawn", 2)]
        [InlineData("Pseudo-Release", 3)]
        public void SearchPriority_KeepsSearchOrder(string status, int expected)
        {
            Assert.Equal(expected, ReleaseStatusPolicy.SearchPriority(status));
        }

        [Theory]
        [InlineData("Official", 40)]
        [InlineData("Promotional", 0)]
        [InlineData("Unknown", 0)]
        [InlineData("Bootleg", -40)]
        [InlineData("Withdrawn", -40)]
        [InlineData("Pseudo-Release", -10)]
        public void ScoreWeight_MatchesReleaseGroupScoring(string status, int expected)
        {
            Assert.Equal(expected, ReleaseStatusPolicy.ScoreWeight(status));
        }
    }
}
