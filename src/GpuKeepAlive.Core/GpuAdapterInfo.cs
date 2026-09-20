namespace GpuKeepAlive.Core;

/// <summary>系统检测到的物理 GPU 适配器（已过滤基于 CPU 的软件渲染器）。</summary>
public sealed record GpuAdapterInfo(
    int Index,
    string Name,
    uint VendorId,
    long DedicatedMemoryMB,
    long SharedMemoryMB)
{
    /// <summary>该适配器的持久化标识（序号 + 名称）。</summary>
    public GpuAdapterId Id => new(Index, Name);
}
