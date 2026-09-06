using MusicStrmExtract.Providers;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class MusicStrmRemoteProviderTests
    {
        [Theory]
        [InlineData(@"C:\music\Album\01 - Track.flac.strm", true)]
        [InlineData(@"C:\music\Album\01 - Track.m4a.strm", true)]
        [InlineData(@"C:\music\Album\01 - Track.flac", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsStrmPath_LimitsProviderToStrmAudio(string? path, bool expected)
        {
            Assert.Equal(expected, MusicStrmRemoteProvider.IsStrmPath(path));
        }
    }
}
