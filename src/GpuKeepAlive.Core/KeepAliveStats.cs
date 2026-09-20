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
    KeepAliveWorkerStatus Status,
    TimeSpan Elapsed,
    long FramesSubmitted,
    double ActualFps,
    string? ErrorMessage)
{
    /// <summary>运行时长计数器的展示文本：不足一天为 hh:mm:ss；满一天为 "X 天 hh:mm:ss"（小时为扣除天数后的余量）。</summary>
    public string ElapsedText => Elapsed.Days > 0
        ? $"{Elapsed.Days} 天 {Elapsed:hh\\:mm\\:ss}"
        : $"{Elapsed:hh\\:mm\\:ss}";

    /// <summary>帧计数器的展示文本：不足一万显示原数；满一万以“万”、满一亿以“亿”为单位，保留两位小数。</summary>
    public string FramesText
    {
        get
        {
            if (FramesSubmitted < 10_000)
                return $"{FramesSubmitted} 帧";

            // 先按“万”舍入再选单位，避免 9999.995 万被舍入显示为 10000.00 万而不进位到“亿”。
            double wan = Math.Round(FramesSubmitted / 1e4, 2);
            return wan >= 10_000
                ? $"{wan / 1e4:F2} 亿帧"
                : $"{wan:F2} 万帧";
        }
    }
}
