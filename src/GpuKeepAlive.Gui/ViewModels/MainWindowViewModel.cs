using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using GpuKeepAlive.Core;
using GpuKeepAlive.Gui.Services;

namespace GpuKeepAlive.Gui.ViewModels;

/// <summary>
/// 配置窗口主视图模型：
/// - 显卡三态选择（默认/全部/自定义）+ 帧率 + 负载强度；
/// - 服务启停；配置任何有效变更即持久化，服务运行中变更立即以新配置重启 worker；
/// - 每秒刷新各 worker 的运行状态。
/// </summary>
public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly KeepAliveService _service;
    private readonly JsonSettingsStore _store;
    private readonly HashSet<GpuAdapterId> _initialCustomAdapters;
    private readonly DispatcherTimer _timer;

    private bool _hydrating;
    private bool _suppressSettingsChanged;
    private bool _saveErrorNotified;
    private bool _allFailedNotified;
    private GuiSettings? _lastValidSettings;

    private GpuSelectionMode _mode;
    private string _fpsText = "15";
    private int _intensity;
    private bool _isServiceRunning;

    public event Action? ServiceStateChanged;

    /// <summary>请求托盘气泡通知 (title, message)。</summary>
    public event Action<string, string>? NotificationRequested;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindowViewModel(KeepAliveService service, JsonSettingsStore store, GuiSettings initialSettings)
    {
        _service = service;
        _store = store;
        _initialCustomAdapters = [.. initialSettings.CustomAdapters];

        _hydrating = true;
        _mode = initialSettings.Mode;
        _fpsText = initialSettings.Fps.ToString();
        _intensity = initialSettings.Intensity;
        RefreshAdapters();
        _hydrating = false;

        // 初始快照打上当前服务状态，供后续变更比较（窗口打开本身不算配置变更）。
        _lastValidSettings = BuildSettings();
        if (_lastValidSettings is not null)
            _lastValidSettings.ServiceRunning = service.IsRunning;
        _isServiceRunning = service.IsRunning;

        StartCommand = new RelayCommand(StartService, () => !IsServiceRunning && BuildSettings() is not null);
        StopCommand = new RelayCommand(StopService, () => IsServiceRunning);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => OnTimerTick();
        _timer.Start();
    }

    public ObservableCollection<GpuAdapterViewModel> Adapters { get; } = [];

    public ObservableCollection<WorkerStatusViewModel> Workers { get; } = [];

    public ICommand StartCommand { get; }

    public ICommand StopCommand { get; }

    public GpuSelectionMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value)
                return;
            _mode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AdaptersEnabled));
            OnPropertyChanged(nameof(GpuHint));
            OnSettingsChanged();
        }
    }

    public bool AdaptersEnabled => _mode == GpuSelectionMode.Custom;

    public string FpsText
    {
        get => _fpsText;
        set
        {
            if (_fpsText == value)
                return;
            _fpsText = value;
            OnPropertyChanged();
            OnSettingsChanged();
        }
    }

    public int Intensity
    {
        get => _intensity;
        set
        {
            if (_intensity == value)
                return;
            _intensity = value;
            OnPropertyChanged();
            OnSettingsChanged();
        }
    }

    public bool IsServiceRunning
    {
        get => _isServiceRunning;
        private set
        {
            if (_isServiceRunning == value)
                return;
            _isServiceRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ServiceStateText));
        }
    }

    public string ServiceStateText
    {
        get
        {
            if (!_isServiceRunning)
                return "服务未运行";
            var stats = _service.GetStats();
            if (stats.Count == 0)
                return "服务运行中";
            if (stats.All(s => s.Status == KeepAliveWorkerStatus.Failed))
                return "运行异常 · 所有目标均失败";
            int active = stats.Count(s => s.Status is KeepAliveWorkerStatus.Running or KeepAliveWorkerStatus.Starting);
            return $"服务运行中 · {active}/{stats.Count} 个目标";
        }
    }

    public string GpuHint => _mode switch
    {
        GpuSelectionMode.Default => "实际调度到哪块显卡由 Windows 图形首选项决定（设置 → 系统 → 屏幕 → 显示卡）。",
        GpuSelectionMode.All => Adapters.Count == 0
            ? "未检测到可用显卡。"
            : $"将为检测到的 {Adapters.Count} 块显卡同时保活。",
        _ => "勾选要保活的显卡（可多选）。同型号多卡可通过序号区分。",
    };

    /// <summary>配置校验提示；空字符串表示配置有效。</summary>
    public string ConfigHint
    {
        get
        {
            if (_mode == GpuSelectionMode.Custom && !Adapters.Any(a => a.IsSelected))
                return "自定义模式下需至少勾选一块显卡。";
            if (!int.TryParse(_fpsText.Trim(), out int fps) || fps < 1 || fps > 240)
                return "帧率须为 1–240 之间的整数。";
            return "";
        }
    }

    /// <summary>重新枚举显卡并重建列表；勾选状态按标识（序号 + 名称）保留，
    /// 初始化时应用配置文件中的自定义勾选。</summary>
    public void RefreshAdapters()
    {
        IEnumerable<GpuAdapterId> keep = _hydrating
            ? _initialCustomAdapters
            : Adapters.Where(a => a.IsSelected).Select(a => a.Info.Id);

        var keepSet = keep.ToHashSet();

        // 重建列表期间的勾选恢复不视为用户配置变更。
        _suppressSettingsChanged = true;
        try
        {
            Adapters.Clear();
            foreach (var info in GpuAdapterEnumerator.ListAdapters())
                Adapters.Add(new GpuAdapterViewModel(info, OnAdapterSelectionChanged)
                {
                    IsSelected = keepSet.Contains(info.Id),
                });
        }
        finally
        {
            _suppressSettingsChanged = false;
        }

        OnPropertyChanged(nameof(GpuHint));
        OnSettingsChanged();
    }

    private void OnAdapterSelectionChanged() => OnSettingsChanged();

    /// <summary>配置变更统一入口：有效则持久化（运行中则热重启生效），无效则仅提示；配置实际未变时不动作。</summary>
    private void OnSettingsChanged()
    {
        if (_hydrating || _suppressSettingsChanged)
            return;

        OnPropertyChanged(nameof(ConfigHint));

        var settings = BuildSettings();
        if (settings is null)
        {
            CommandManager.InvalidateRequerySuggested();
            return;
        }

        settings.ServiceRunning = _service.IsRunning;
        if (_lastValidSettings is not null && SettingsEqual(settings, _lastValidSettings))
        {
            CommandManager.InvalidateRequerySuggested();
            return;
        }

        _lastValidSettings = settings;
        SaveSettings(settings);

        if (_service.IsRunning)
        {
            _service.Restart(settings);
            _allFailedNotified = false;
        }
        CommandManager.InvalidateRequerySuggested();
    }

    private void StartService()
    {
        var settings = BuildSettings();
        if (settings is null || _service.IsRunning)
            return;

        settings.ServiceRunning = true;
        _lastValidSettings = settings;
        SaveSettings(settings);

        _service.Start(settings);
        _allFailedNotified = false;
        UpdateRunState();
    }

    private void StopService()
    {
        if (!_service.IsRunning)
            return;

        var settings = BuildSettings() ?? _lastValidSettings;
        if (settings is not null)
        {
            settings.ServiceRunning = false;
            SaveSettings(settings);
            _lastValidSettings = settings;
        }

        _service.Stop();
        UpdateRunState();
    }

    private void SaveSettings(GuiSettings settings)
    {
        if (_store.TrySave(settings))
        {
            _saveErrorNotified = false;
            return;
        }
        if (!_saveErrorNotified)
        {
            _saveErrorNotified = true;
            NotificationRequested?.Invoke("GPU KeepAlive",
                $"配置保存失败：无法写入 {_store.Path}（目录可能只读）。");
        }
    }

    private static bool SettingsEqual(GuiSettings a, GuiSettings b)
        => a.Mode == b.Mode
           && a.Fps == b.Fps
           && a.Intensity == b.Intensity
           && a.ServiceRunning == b.ServiceRunning
           && a.CustomAdapters.SequenceEqual(b.CustomAdapters);

    /// <summary>由当前 UI 状态构造配置；配置无效（自定义未选显卡 / 帧率越界 / 全部模式无显卡）时返回 null。</summary>
    private GuiSettings? BuildSettings()
    {
        if (!int.TryParse(_fpsText.Trim(), out int fps) || fps < 1 || fps > 240)
            return null;

        var selected = Adapters.Where(a => a.IsSelected).Select(a => a.Info.Id).ToList();
        if (_mode == GpuSelectionMode.Custom && selected.Count == 0)
            return null;
        if (_mode == GpuSelectionMode.All && Adapters.Count == 0)
            return null;

        return new GuiSettings
        {
            Mode = _mode,
            CustomAdapters = selected,
            Fps = fps,
            Intensity = _intensity,
        };
    }

    private void UpdateRunState()
    {
        IsServiceRunning = _service.IsRunning;
        OnPropertyChanged(nameof(ServiceStateText));
        CommandManager.InvalidateRequerySuggested();
        ServiceStateChanged?.Invoke();
    }

    private void OnTimerTick()
    {
        var stats = _service.GetStats();

        if (stats.Count != Workers.Count)
        {
            Workers.Clear();
            for (int i = 0; i < stats.Count; i++)
                Workers.Add(new WorkerStatusViewModel());
        }
        for (int i = 0; i < stats.Count; i++)
            Workers[i].Update(stats[i]);

        if (_service.IsRunning != _isServiceRunning)
        {
            IsServiceRunning = _service.IsRunning;
            ServiceStateChanged?.Invoke();
        }

        if (_isServiceRunning && stats.Count > 0 && stats.All(s => s.Status == KeepAliveWorkerStatus.Failed))
        {
            if (!_allFailedNotified)
            {
                _allFailedNotified = true;
                NotificationRequested?.Invoke("GPU KeepAlive", "保活服务启动失败：所有目标显卡均无法创建设备。");
                ServiceStateChanged?.Invoke();
            }
        }
        else
        {
            _allFailedNotified = false;
        }

        OnPropertyChanged(nameof(ServiceStateText));
    }

    public void Dispose() => _timer.Stop();

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName!));
}
