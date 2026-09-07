using System;
using System.Threading;
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
        private CancellationTokenSource? _repairCts;
        private bool _repairRunning;

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
                if (_repairRunning)
                {
                    ContentData.ResultLabel.Text = "修复正在运行，请等待当前任务结束后再执行。";
                    RaiseInfoChanged();
                    return Task.FromResult((IPluginUIView)this);
                }

                _repairCts?.Dispose();
                _repairCts = new CancellationTokenSource();
                _repairRunning = true;
                ContentData.ResultLabel.Text = "修复已开始，正在读取媒体库...";
                RunRepairInBackground(_repairCts.Token);
            }

            RaiseInfoChanged();
            return Task.FromResult((IPluginUIView)this);
        }

        public Task<IPluginUIView> OnSaveCommand(string itemId, string commandId, string data)
        {
            var options = _loadOptions();
            ContentData.ApplyTo(options);
            _saveOptions(options);
            RaiseInfoChanged();
            return Task.FromResult((IPluginUIView)this);
        }

        public Task Cancel()
        {
            _repairCts?.Cancel();
            _repairCts?.Dispose();
            _repairCts = null;
            return Task.CompletedTask;
        }

        public void OnDialogResult(IPluginUIView dialogView, bool completedOk, object data)
        {
        }

        private void RaiseInfoChanged()
        {
            UIViewInfoChanged?.Invoke(this, new GenericEventArgs<IPluginUIView>(this));
        }

        private void RunRepairInBackground(CancellationToken ct)
        {
            _ = Task.Run(
                () =>
                {
                    try
                    {
                        var progress = new Progress<string>(message =>
                        {
                            if (!ct.IsCancellationRequested)
                            {
                                SetResultLabel(message);
                            }
                        });

                        var result = _repairService.Run(progress, ct);
                        if (!ct.IsCancellationRequested)
                        {
                            SetResultLabel(result);
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                    }
                    catch (Exception ex)
                    {
                        if (!ct.IsCancellationRequested)
                        {
                            SetResultLabel("修复失败: " + ex.Message);
                        }
                    }
                    finally
                    {
                        _repairRunning = false;
                    }
                },
                CancellationToken.None);
        }

        private void SetResultLabel(string message)
        {
            ContentData.ResultLabel.Text = message;
            RaiseInfoChanged();
        }
    }
}
