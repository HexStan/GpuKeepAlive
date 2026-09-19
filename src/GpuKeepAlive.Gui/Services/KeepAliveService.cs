using GpuKeepAlive.Core;

namespace GpuKeepAlive.Gui.Services;

/// <summary>
/// GUI 侧保活服务：把 <see cref="GuiSettings"/> 翻译为核心 KeepAliveOptions 并托管运行。
/// GUI 的 默认/全部/自定义 三态在这里转换为「系统默认调度 / 显式索引列表」两种核心形态。
/// </summary>
public sealed class KeepAliveService : IDisposable
{
    private readonly KeepAliveManager _manager = new();

    public bool IsRunning => _manager.IsRunning;

    public IReadOnlyList<KeepAliveStats> GetStats() => _manager.GetStats();

    public void Start(GuiSettings settings)
    {
        _manager.Start(ToOptions(settings));
    }

    /// <summary>服务运行中以新配置重启全部 worker（配置变更立即生效）。</summary>
    public void Restart(GuiSettings settings)
    {
        _manager.Stop();
        _manager.Start(ToOptions(settings));
    }

    public void Stop() => _manager.Stop();

    public void Dispose() => _manager.Dispose();

    private static KeepAliveOptions ToOptions(GuiSettings s) => s.Mode switch
    {
        GpuSelectionMode.Default => new KeepAliveOptions(s.Fps, s.Intensity, null),
        GpuSelectionMode.All => new KeepAliveOptions(s.Fps, s.Intensity, AllAdapterLuids()),
        GpuSelectionMode.Custom => new KeepAliveOptions(s.Fps, s.Intensity, [.. s.CustomLuids]),
        _ => throw new ArgumentOutOfRangeException(nameof(s)),
    };

    private static List<string> AllAdapterLuids()
    {
        var luids = GpuAdapterEnumerator.ListAdapters().Select(a => a.AdapterLuid).ToList();
        if (luids.Count == 0)
            throw new InvalidOperationException("未检测到可用的硬件显卡。");
        return luids;
    }
}
