using System.ComponentModel;
using GpuKeepAlive.Core;

namespace GpuKeepAlive.Gui.ViewModels;

/// <summary>显卡列表中单个适配器的勾选项。</summary>
public sealed class GpuAdapterViewModel : INotifyPropertyChanged
{
    private readonly Action _selectionChanged;
    private bool _isSelected;

    public GpuAdapterViewModel(GpuAdapterInfo info, Action selectionChanged)
    {
        Info = info;
        _selectionChanged = selectionChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public GpuAdapterInfo Info { get; }

    public string Label => $"[{Info.Index}] {Info.Name}";

    /// <summary>LUID 是显卡唯一标识（同型号多卡互不相同），用于精确区分与对照验证。</summary>
    public string LuidText => $"LUID {Info.AdapterLuid}";

    public string MemoryText => Info.DedicatedMemoryMB > 0
        ? $"专用 {Info.DedicatedMemoryMB} MB · 共享 {Info.SharedMemoryMB} MB"
        : $"共享 {Info.SharedMemoryMB} MB";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            _selectionChanged();
        }
    }
}
