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
    /// 自定义模式选中的显卡 LUID 列表。
    /// 以 LUID 而非索引持久化：DXGI 枚举顺序在虚拟显示适配器增删后会重排，
    /// 索引无法跨运行稳定标识同一块物理显卡。
    /// </summary>
    public List<string> CustomLuids { get; set; } = [];

    public int Fps { get; set; } = 15;

    public int Intensity { get; set; } = 1;

    /// <summary>上次退出时的服务运行状态；为 true 时下次启动自动恢复服务。</summary>
    public bool ServiceRunning { get; set; }
}
