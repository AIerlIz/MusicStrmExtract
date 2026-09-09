using MusicStrmExtract;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class MusicStrmConfigurationSourceTests
    {
        [Fact]
        public void Default_Instance_ReturnsNewConfigurationWhenNoPluginInstance()
        {
            var source = MusicStrmConfigurationSource.Default;
            var config = source.Current;

            Assert.NotNull(config);
            Assert.Equal(string.Empty, config.MusicBrainzBaseUrl);
        }

        [Fact]
        public void Current_ReturnsValidConfiguration()
        {
            var source = MusicStrmConfigurationSource.Default;
            var config = source.Current;

            Assert.NotNull(config);
            Assert.Equal(string.Empty, config.MusicBrainzBaseUrl);
        }
    }
}
