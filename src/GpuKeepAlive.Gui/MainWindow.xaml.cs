using System.ComponentModel;
using System.Windows;
using GpuKeepAlive.Gui.Services;
using GpuKeepAlive.Gui.ViewModels;

namespace GpuKeepAlive.Gui;

public partial class MainWindow : Window
{
    /// <summary>宽度“合理上限”：内容极宽（如超长显卡名/错误信息）时到该宽度为止，更宽部分横向滚动。</summary>
    private const double ReasonableMaxWidth = 720;

    /// <summary>高度“合理上限”：内容极长（如大量显卡与 worker 状态行）时到该高度为止，更长部分纵向滚动。</summary>
    private const double ReasonableMaxHeight = 760;

    private bool _reallyClosing;

    public MainWindowViewModel ViewModel { get; }

    public MainWindow(KeepAliveService service, JsonSettingsStore store, GuiSettings initialSettings)
    {
        ViewModel = new MainWindowViewModel(service, store, initialSettings);
        InitializeComponent();
        DataContext = ViewModel;

        ApplySizeLimits();
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
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
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        ViewModel.Dispose();
        base.OnClosed(e);
    }

    /// <summary>尺寸上限取“工作区”与“合理上限”中的较小者：顶满工作区或超出合理大小后窗口不再放大。</summary>
    private void ApplySizeLimits()
    {
        var workArea = SystemParameters.WorkArea;
        MaxWidth = Math.Min(workArea.Width, ReasonableMaxWidth);
        MaxHeight = Math.Min(workArea.Height, ReasonableMaxHeight);
    }

    /// <summary>任务栏位置、屏幕分辨率/缩放变化会引起工作区变化，需动态跟随更新上限。</summary>
    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.WorkArea))
            Dispatcher.InvokeAsync(ApplySizeLimits);
    }
}
