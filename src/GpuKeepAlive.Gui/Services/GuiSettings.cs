using GpuKeepAlive.Core;

namespace GpuKeepAlive.Gui.Services;

public enum GpuSelectionMode
{
    Default,
    All,
    Custom,
}

/// <summary>GUI 用户配置（持久化到 exe 同级目录的 GpuKeepAlive.config.json）。</summary>
public sealed class GuiSettings
{
    public GpuSelectionMode Mode { get; set; } = GpuSelectionMode.Default;

    /// <summary>
    /// 自定义模式选中的显卡标识列表（序号 + 名称，两者一致才视为同一设备）。
    /// </summary>
    public List<GpuAdapterId> CustomAdapters { get; set; } = [];

    public int Fps { get; set; } = 15;

    public int Intensity { get; set; } = 1;

    /// <summary>上次退出时的服务运行状态；为 true 时下次启动自动恢复服务。</summary>
    public bool ServiceRunning { get; set; }
}
