using System;
using System.Threading.Tasks;

using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.GenericEdit;
using MediaBrowser.Model.Plugins.UI.Views;

namespace MusicStrmExtract.Ui
{
    /// <summary>完整 Plugin UI 页面:负责把 UI 值保存回现有 JSON 配置,并转发按钮命令到修复服务。</summary>
    internal sealed class MusicStrmPageView : IPluginPageView
    {
        private readonly Func<PluginConfiguration> _loadOptions;
        private readonly Action<PluginConfiguration> _saveOptions;
        private readonly StaleMusicAlbumRepairService _repairService;

        public MusicStrmPageView(
            string pluginId,
            MusicStrmPageOptions contentData,
            Func<PluginConfiguration> loadOptions,
            Action<PluginConfiguration> saveOptions,
            StaleMusicAlbumRepairService repairService)
        {
            PluginId = pluginId;
            ContentData = contentData;
            _loadOptions = loadOptions;
            _saveOptions = saveOptions;
            _repairService = repairService;
        }

        public MusicStrmPageOptions ContentData { get; set; }

        IEditableObject IPluginUIView.ContentData
        {
            get => ContentData;
            set => ContentData = value as MusicStrmPageOptions ?? throw new InvalidOperationException("Unexpected page data type.");
        }

        public string Caption => ContentData.EditorTitle;

        public string SubCaption => ContentData.EditorDescription;

        public string PluginId { get; }

        public UserDto User { get; set; } = new UserDto();

        public string RedirectViewUrl { get; set; } = string.Empty;

        public bool ShowSave { get; set; } = true;

        public bool ShowBack { get; set; }

        public bool AllowSave { get; set; } = true;

        public bool AllowBack { get; set; } = true;

        public event EventHandler<GenericEventArgs<IPluginUIView>>? UIViewInfoChanged;

        public bool IsCommandAllowed(string commandKey)
        {
            return true;
        }

        public Task<IPluginUIView> RunCommand(string itemId, string commandId, string data)
        {
            if (string.Equals(commandId, MusicStrmPageOptions.RepairCommand, StringComparison.Ordinal))
            {
                try
                {
                    ContentData.ResultLabel.Text = _repairService.Run();
                }
                catch (Exception ex)
                {
                    ContentData.ResultLabel.Text = "修复失败: " + ex.Message;
                }
            }

            RaiseInfoChanged();
            return Task.FromResult((IPluginUIView)this);
        }

        public Task<IPluginUIView> OnSaveCommand(string itemId, string commandId, string data)
        {
            var options = _loadOptions();
            options.MusicBrainzBaseUrl = ContentData.MusicBrainzBaseUrl;
            options.CoverArtBaseUrl = ContentData.CoverArtBaseUrl;
            _saveOptions(options);
            RaiseInfoChanged();
            return Task.FromResult((IPluginUIView)this);
        }

        public Task Cancel()
        {
            return Task.CompletedTask;
        }

        public void OnDialogResult(IPluginUIView dialogView, bool completedOk, object data)
        {
        }

        private void RaiseInfoChanged()
        {
            UIViewInfoChanged?.Invoke(this, new GenericEventArgs<IPluginUIView>(this));
        }
    }
}
