namespace GpuKeepAlive.Core;

public enum KeepAliveWorkerStatus
{
    Starting,
    Running,
    Failed,
    Stopped,
}

/// <summary>单个保活 worker 的状态快照，线程安全读取。</summary>
public sealed record KeepAliveStats(
    string DisplayName,
    string? ActualAdapterName,
    string? ActualAdapterLuid,
    KeepAliveWorkerStatus Status,
    TimeSpan Elapsed,
    long FramesSubmitted,
    double ActualFps,
    string? ErrorMessage);
