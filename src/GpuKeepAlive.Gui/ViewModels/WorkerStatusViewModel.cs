using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using GpuKeepAlive.Core;

namespace GpuKeepAlive.Gui.ViewModels;

/// <summary>服务状态区中单个保活 worker 的展示行。</summary>
public sealed class WorkerStatusViewModel : INotifyPropertyChanged
{
    private string _name = "";
    private string _statusText = "";
    private string _detailText = "";
    private Brush _statusBrush = Brushes.Gray;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        private set => Set(ref _name, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public string DetailText
    {
        get => _detailText;
        private set => Set(ref _detailText, value);
    }

    public Brush StatusBrush
    {
        get => _statusBrush;
        private set => Set(ref _statusBrush, value);
    }

    public void Update(KeepAliveStats stats)
    {
        Name = stats.DisplayName;
        if (stats.DisplayName == KeepAliveManager.SystemDefaultLabel && stats.ActualAdapterName is not null)
            Name = $"系统默认调度 → {stats.ActualAdapterName}";
        if (stats.ActualAdapterLuid is not null)
            Name = $"{Name} · {stats.ActualAdapterLuid}";

        switch (stats.Status)
        {
            case KeepAliveWorkerStatus.Running:
                StatusText = "运行中";
                StatusBrush = Brushes.SeaGreen;
                DetailText = $"{stats.ActualFps:F1} FPS · {stats.ElapsedText} · {stats.FramesText}";
                break;
            case KeepAliveWorkerStatus.Starting:
                StatusText = "启动中…";
                StatusBrush = Brushes.Gray;
                DetailText = "";
                break;
            case KeepAliveWorkerStatus.Failed:
                StatusText = "失败";
                StatusBrush = Brushes.IndianRed;
                DetailText = stats.ErrorMessage ?? "";
                break;
            default:
                StatusText = "已停止";
                StatusBrush = Brushes.Gray;
                DetailText = "";
                break;
        }
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName!));
    }
}
