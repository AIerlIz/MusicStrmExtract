using System;
using System.Reflection;
using System.Threading.Tasks;

using MediaBrowser.Model.Logging;

using MusicStrmExtract.Ui;

using Xunit;

namespace MusicStrmExtract.Tests
{
    public class MusicStrmPageViewTests
    {
        [Fact]
        public async Task RunCommand_PageSavePersistsEditedMusicBrainzBaseUrl()
        {
            PluginConfiguration? saved = null;
            var view = CreateView(
                loadOptions: () => new PluginConfiguration
                {
                    MusicBrainzBaseUrl = "https://old.example"
                },
                saveOptions: options => saved = options);
            view.ContentData.MusicBrainzBaseUrl = "https://musicbrainz.emby.tv";

            _ = await view.RunCommand(string.Empty, "PageSave", string.Empty);

            Assert.NotNull(saved);
            Assert.Equal("https://musicbrainz.emby.tv", saved.MusicBrainzBaseUrl);
        }

        [Fact]
        public async Task RunCommand_UnknownCommandReturnsNullForEmbyFallback()
        {
            var view = CreateView();

            var result = await view.RunCommand(string.Empty, "PageBack", string.Empty);

            Assert.Null(result);
        }

        private static MusicStrmPageView CreateView(
            Func<PluginConfiguration>? loadOptions = null,
            Action<PluginConfiguration>? saveOptions = null)
        {
            var repairService = new LegacyAlbumRepairService(
                CreateLogger(),
                () => false,
                () => [],
                () => [],
                _ => { },
                (_, _) => { });

            return new MusicStrmPageView(
                "plugin",
                new MusicStrmPageOptions(),
                loadOptions ?? (() => new PluginConfiguration()),
                saveOptions ?? (_ => { }),
                repairService);
        }

        private static ILogger CreateLogger()
        {
            return DispatchProxy.Create<ILogger, NoOpLogger>();
        }

        private class NoOpLogger : DispatchProxy
        {
            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                return null;
            }
        }
    }
}
