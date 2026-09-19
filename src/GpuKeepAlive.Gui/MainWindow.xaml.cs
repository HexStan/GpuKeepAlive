using System.ComponentModel;
using System.Windows;
using GpuKeepAlive.Gui.Services;
using GpuKeepAlive.Gui.ViewModels;

namespace GpuKeepAlive.Gui;

public partial class MainWindow : Window
{
    private bool _reallyClosing;

    public MainWindowViewModel ViewModel { get; }

    public MainWindow(KeepAliveService service, JsonSettingsStore store, GuiSettings initialSettings)
    {
        ViewModel = new MainWindowViewModel(service, store, initialSettings);
        InitializeComponent();
        DataContext = ViewModel;
    }

    /// <summary>应用退出时真正关闭窗口（绕过"关闭即隐藏到托盘"）。</summary>
    public void ReallyClose()
    {
        _reallyClosing = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_reallyClosing)
        {
            // 关闭窗口只是隐藏到托盘，服务继续运行；退出入口仅托盘右键菜单。
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        ViewModel.Dispose();
        base.OnClosed(e);
    }
}
