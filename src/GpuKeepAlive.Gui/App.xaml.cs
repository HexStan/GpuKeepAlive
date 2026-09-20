using System.IO;
using System.Threading;
using System.Windows;
using GpuKeepAlive.Core;
using GpuKeepAlive.Gui.Services;
using H.NotifyIcon;

namespace GpuKeepAlive.Gui;

/// <summary>
/// 托盘常驻应用：启动时不显示窗口，仅托盘图标；
/// 左键单击托盘弹出配置窗口，右键菜单提供唯一退出入口。
/// </summary>
public partial class App : Application
{
    private const string SingletonMutexName = @"Local\GpuKeepAlive.Gui";
    private const string ActivateEventName = @"Local\GpuKeepAlive.Gui.Activate";

    private Mutex? _singleton;
    private EventWaitHandle? _activateSignal;
    private KeepAliveService? _service;
    private JsonSettingsStore? _store;
    private GuiSettings _initialSettings = null!;
    private TaskbarIcon? _trayIcon;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 单实例：GUI 与 CLI 各自独立加锁，互不干涉。
        _singleton = new Mutex(true, SingletonMutexName, out bool createdNew);
        bool owned = createdNew;
        if (!owned)
        {
            try { owned = _singleton.WaitOne(TimeSpan.Zero); }
            catch (AbandonedMutexException) { owned = true; }
        }
        if (!owned)
        {
            // 唤起已有实例的配置窗口后退出。
            using var signal = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
            signal.Set();
            if (TryFindTrayIcon() is { } stray)
                stray.Dispose();
            Shutdown();
            return;
        }

        _store = new JsonSettingsStore(Path.Combine(AppContext.BaseDirectory, "GpuKeepAlive.config.json"));
        _service = new KeepAliveService();
        _trayIcon = TryFindTrayIcon();
        // H.NotifyIcon v2 中资源声明的 TaskbarIcon 不会自动创建托盘图标，必须显式 ForceCreate。
        // 不启用 Efficiency Mode（EcoQoS），避免影响保活渲染线程的调度。
        _trayIcon?.ForceCreate(enablesEfficiencyMode: false);

        _activateSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        new Thread(ListenForActivation)
        {
            IsBackground = true,
            Name = "GpuKeepAlive.Gui: ActivateListener",
        }.Start();

        // 恢复配置：初次运行（无配置文件）默认不启动服务；
        // 后续运行按持久化的 ServiceRunning 自动恢复。
        GuiSettings? persisted = _store.Load();
        _initialSettings = persisted ?? new GuiSettings();
        if (persisted is { ServiceRunning: true })
            TryAutoStart(persisted);
        UpdateTrayStatus();
    }

    private TaskbarIcon? TryFindTrayIcon() => TryFindResource("TrayIcon") as TaskbarIcon;

    /// <summary>按上次配置自动启动服务；配置的显卡已不存在时做保守降级并气泡提示。</summary>
    private void TryAutoStart(GuiSettings settings)
    {
        var adapters = GpuAdapterEnumerator.ListAdapters();

        if (settings.Mode == GpuSelectionMode.Custom)
        {
            var available = adapters.Select(a => a.Id).ToHashSet();
            var validAdapters = settings.CustomAdapters.Where(available.Contains).Distinct().ToList();
            if (validAdapters.Count == 0)
            {
                settings.CustomAdapters = validAdapters;
                settings.ServiceRunning = false;
                _store?.TrySave(settings);
                NotifyTray("GPU KeepAlive", "配置中指定的显卡已不存在，服务未自动启动。");
                return;
            }
            if (validAdapters.Count != settings.CustomAdapters.Count)
            {
                settings.CustomAdapters = validAdapters;
                _store?.TrySave(settings);
            }
        }
        else if (settings.Mode == GpuSelectionMode.All && adapters.Count == 0)
        {
            settings.ServiceRunning = false;
            _store?.TrySave(settings);
            NotifyTray("GPU KeepAlive", "未检测到可用显卡，服务未自动启动。");
            return;
        }

        try
        {
            _service?.Start(settings);
        }
        catch (Exception ex)
        {
            settings.ServiceRunning = false;
            _store?.TrySave(settings);
            NotifyTray("GPU KeepAlive", "服务自动启动失败: " + ex.Message);
        }
    }

    private void ListenForActivation()
    {
        var signal = _activateSignal!;
        while (signal.WaitOne())
            Dispatcher.BeginInvoke(ShowMainWindow);
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null && _service is not null && _store is not null)
        {
            _mainWindow = new MainWindow(_service, _store, _initialSettings);
            _mainWindow.ViewModel.ServiceStateChanged += UpdateTrayStatus;
            _mainWindow.ViewModel.NotificationRequested += (title, message) => NotifyTray(title, message);
        }
        _mainWindow?.Show();
        if (_mainWindow?.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
        _mainWindow?.Activate();
        // 每次显示都刷新显卡列表（虚拟显示适配器可能增删导致枚举变化）；勾选按标识（序号 + 名称）保留。
        _mainWindow?.ViewModel.RefreshAdapters();
    }

    private void UpdateTrayStatus()
    {
        if (_trayIcon is null || _service is null)
            return;
        var stats = _service.GetStats();
        bool allFailed = stats.Count > 0 && stats.All(s => s.Status == KeepAliveWorkerStatus.Failed);
        _trayIcon.ToolTipText = _service.IsRunning
            ? allFailed
                ? "GPU KeepAlive — 运行异常（所有目标失败）"
                : $"GPU KeepAlive — 运行中 · {stats.Count} 个目标"
            : "GPU KeepAlive — 已停止";
    }

    public void NotifyTray(string title, string message)
    {
        try
        {
            _trayIcon?.ShowNotification(title, message);
        }
        catch
        {
            // 通知失败不影响主流程。
        }
    }

    private void OnTrayLeftClick(object sender, RoutedEventArgs e) => ShowMainWindow();

    private void OnMenuExitClick(object sender, RoutedEventArgs e) => ExitApplication();

    public void ExitApplication()
    {
        // 配置（含 ServiceRunning）已随每次变更实时持久化：退出时服务在运行，
        // 配置文件中 ServiceRunning 保持 true，下次启动自动恢复。
        _service?.Stop();
        _mainWindow?.ReallyClose();
        _trayIcon?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _service?.Dispose();
        _singleton?.Dispose();
        base.OnExit(e);
    }
}
