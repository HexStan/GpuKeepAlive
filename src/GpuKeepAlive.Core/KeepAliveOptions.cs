namespace GpuKeepAlive.Core;

/// <summary>
/// 保活服务配置。目标显卡以 LUID（适配器唯一标识）指定：
/// DXGI 枚举顺序在虚拟显示适配器增删后会重排，索引无法跨运行稳定标识同一块卡。
/// <see cref="AdapterLuids"/> 为 null 表示交由 Windows 图形首选项自动调度（单个 worker）；
/// 否则为列表中每个 LUID 各创建一个 worker。
/// </summary>
public sealed record KeepAliveOptions(int Fps, int Intensity, IReadOnlyList<string>? AdapterLuids);
