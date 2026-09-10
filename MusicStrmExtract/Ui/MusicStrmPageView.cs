using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.GenericEdit;
using MediaBrowser.Model.Plugins.UI.Views;
using MusicStrmExtract.Online;
using MusicStrmExtract.Providers;

namespace MusicStrmExtract.Ui;

/// <summary>完整 Plugin UI 页面:负责把 UI 值保存回现有 JSON 配置,并转发按钮命令到修复服务。</summary>
internal sealed class MusicStrmPageView : IPluginPageView, IDisposable
{
    private readonly Func<PluginConfiguration> _loadOptions;
    private readonly Action<PluginConfiguration> _saveOptions;
    private readonly PluginCacheManager _cacheManager;
    private readonly ResolutionDiagnosticsStore _diagnostics;
    private readonly MusicBrainzSourceCheckService _sourceCheckService;
    private readonly BackgroundJobRunner _repairRunner;
    private readonly BackgroundJobRunner _sourceCheckRunner;

    public MusicStrmPageView(
        string pluginId,
        MusicStrmPageOptions contentData,
        Func<PluginConfiguration> loadOptions,
        Action<PluginConfiguration> saveOptions,
        LegacyAlbumRepairService repairService,
        PluginCacheManager cacheManager,
        ResolutionDiagnosticsStore diagnostics,
        MusicBrainzSourceCheckService sourceCheckService)
    {
        PluginId = pluginId;
        ContentData = contentData;
        _loadOptions = loadOptions;
        _saveOptions = saveOptions;
        _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _sourceCheckService = sourceCheckService ?? throw new ArgumentNullException(nameof(sourceCheckService));
        _repairRunner = new BackgroundJobRunner(
            (progress, ct) => Task.FromResult(repairService.Run(progress, ct)),
            SetRepairResultLabel,
            "旧库专辑关系修复失败");
        _sourceCheckRunner = new BackgroundJobRunner(
            (_, ct) => _sourceCheckService.CheckAsync(_loadOptions().MusicBrainzBaseUrl, ct),
            SetSourceStatusLabel,
            "MusicBrainz 连接检查失败");
        RefreshCacheStatus();
        RefreshDiagnostics();
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

    public UserDto User { get; set; } = new();

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
        if (string.Equals(commandId, "PageSave", StringComparison.Ordinal))
        {
            return OnSaveCommand(itemId, commandId, data);
        }

        if (string.Equals(commandId, MusicStrmPageOptions.RepairCommand, StringComparison.Ordinal))
        {
            if (!_repairRunner.TryStart("旧库专辑关系修复已开始，正在读取媒体库..."))
            {
                ContentData.ResultLabel.Text = "修复正在运行，请等待当前任务结束后再执行。";
                RaiseInfoChanged();
                return Task.FromResult((IPluginUIView)this);
            }
        }
        else if (string.Equals(commandId, MusicStrmPageOptions.ClearCacheCommand, StringComparison.Ordinal))
        {
            _cacheManager.Clear();
            RefreshCacheStatus();
            RefreshDiagnostics();
        }
        else if (string.Equals(commandId, MusicStrmPageOptions.CheckSourceCommand, StringComparison.Ordinal))
        {
            if (!_sourceCheckRunner.TryStart("正在检查 MusicBrainz 连接..."))
            {
                ContentData.SourceStatusLabel.Text = "连接检查正在运行，请稍候。";
            }
        }
        else if (string.Equals(commandId, MusicStrmPageOptions.RefreshDiagnosticsCommand, StringComparison.Ordinal))
        {
            RefreshDiagnostics();
        }
        else
        {
            return Task.FromResult<IPluginUIView>(null!);
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

    public async Task Cancel()
    {
        await _repairRunner.CancelAsync().ConfigureAwait(false);
        await _sourceCheckRunner.CancelAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        _repairRunner.Dispose();
        _sourceCheckRunner.Dispose();
    }

    public void OnDialogResult(IPluginUIView dialogView, bool completedOk, object data)
    {
    }

    private void RaiseInfoChanged()
    {
        UIViewInfoChanged?.Invoke(this, new GenericEventArgs<IPluginUIView>(this));
    }

    private void SetRepairResultLabel(string message)
    {
        ContentData.ResultLabel.Text = message;
        RaiseInfoChanged();
    }

    private void SetSourceStatusLabel(string message)
    {
        ContentData.SourceStatusLabel.Text = message;
        RaiseInfoChanged();
    }

    private void RefreshCacheStatus()
    {
        ContentData.CacheStatusLabel.Text = _cacheManager.GetStatus();
        RaiseInfoChanged();
    }

    private void RefreshDiagnostics()
    {
        ContentData.DiagnosticsLabel.Text = _diagnostics.GetSummary();
        RaiseInfoChanged();
    }
}
