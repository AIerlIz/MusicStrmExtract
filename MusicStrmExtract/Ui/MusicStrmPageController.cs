using System;
using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Common;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI;
using MediaBrowser.Model.Plugins.UI.Views;

namespace MusicStrmExtract.Ui
{
    internal sealed class MusicStrmPageController : IPluginUIPageController
    {
        private readonly string _pluginId;
        private readonly Func<PluginConfiguration> _loadOptions;
        private readonly Action<PluginConfiguration> _saveOptions;
        private readonly StaleMusicAlbumRepairService _repairService;

        public MusicStrmPageController(
            IApplicationHost applicationHost,
            string pluginId,
            Func<PluginConfiguration> loadOptions,
            Action<PluginConfiguration> saveOptions)
        {
            _pluginId = pluginId;
            _loadOptions = loadOptions;
            _saveOptions = saveOptions;
            _repairService = new StaleMusicAlbumRepairService(
                applicationHost.Resolve<ILogManager>(),
                applicationHost.Resolve<MediaBrowser.Controller.Library.ILibraryManager>(),
                applicationHost.Resolve<MediaBrowser.Controller.Providers.IProviderManager>(),
                applicationHost.Resolve<IFileSystem>());

            PageInfo = new PluginPageInfo
            {
                Name = "Settings",
                DisplayName = pluginId,
                IsMainConfigPage = true,
                EnableInMainMenu = false
            };
        }

        public PluginPageInfo PageInfo { get; }

        public Task Initialize(CancellationToken token)
        {
            return Task.CompletedTask;
        }

        public Task<IPluginUIView> CreateDefaultPageView()
        {
            var persisted = _loadOptions();
            var options = MusicStrmPageOptions.From(persisted);

            return Task.FromResult((IPluginUIView)new MusicStrmPageView(
                _pluginId,
                options,
                _loadOptions,
                _saveOptions,
                _repairService));
        }
    }
}
