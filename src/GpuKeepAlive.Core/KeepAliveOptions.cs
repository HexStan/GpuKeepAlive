namespace GpuKeepAlive.Core;

/// <summary>
/// 保活服务配置。目标显卡以 <see cref="GpuAdapterId"/>（序号 + 名称）指定：
/// <see cref="Adapters"/> 为 null 表示交由 Windows 图形首选项自动调度（单个 worker）；
/// 否则为列表中每个标识各创建一个 worker。
/// </summary>
public sealed record KeepAliveOptions(int Fps, int Intensity, IReadOnlyList<GpuAdapterId>? Adapters);
