namespace GpuKeepAlive.Core;

/// <summary>
/// GPU 适配器的持久化标识：序号 + 名称，两者完全一致才视为同一设备。
/// 适配器 LUID 每次重启都会变化，不适合持久化；序号与名称的组合在重启间基本稳定。
/// 序号为过滤后可见列表（排除 WARP 与虚拟显示适配器）中的 0 起始索引，与 GUI / CLI --list 显示的编号一致。
/// </summary>
public sealed record GpuAdapterId(int Index, string Name);
