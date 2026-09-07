using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

using MusicStrmExtract.Providers;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class MusicStrmRemoteProviderTests
    {
        [Fact]
        public async Task GetImages_UsesConfiguredCoverArtMirror()
        {
            var source = new FixedConfigurationSource(new PluginConfiguration
            {
                CoverArtBaseUrl = "https://mirror.example/release"
            });
            using var provider = new MusicStrmRemoteProvider(source);
            var info = new SongInfo
            {
                Path = @"C:\music\Album\01 - Track.flac.strm"
            };
            info.ProviderIds[PluginConstants.MusicBrainzAlbum] = "release-1";

            var images = (await provider.GetImages(info, CancellationToken.None)).ToList();

            var image = Assert.Single(images);
            Assert.Equal("https://mirror.example/release/release-1/front-500", image.Url);
            Assert.Equal(ImageType.Primary, image.Type);
        }

        [Fact]
        public async Task GetImages_ReturnsEmpty_WhenNotStrmOrNoAlbumId()
        {
            var source = new FixedConfigurationSource(new PluginConfiguration());
            using var provider = new MusicStrmRemoteProvider(source);
            var info = new SongInfo
            {
                Path = @"C:\music\Album\01 - Track.flac"
            };

            var images = (await provider.GetImages(info, CancellationToken.None)).ToList();

            Assert.Empty(images);
        }

        private sealed class FixedConfigurationSource : IMusicStrmConfigurationSource
        {
            public FixedConfigurationSource(PluginConfiguration configuration)
            {
                Current = configuration;
            }

            public PluginConfiguration Current { get; }
        }
    }
}
