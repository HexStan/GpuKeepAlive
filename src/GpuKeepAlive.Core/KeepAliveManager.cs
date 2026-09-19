namespace GpuKeepAlive.Core;

/// <summary>
/// 保活服务编排器：根据 <see cref="KeepAliveOptions"/> 创建一组 worker（每目标显卡一个，或系统默认调度单个），
/// 统一启停并聚合状态快照。纯后台线程驱动，不依赖任何 UI。
/// </summary>
public sealed class KeepAliveManager : IDisposable
{
    /// <summary>系统默认调度模式的显示名（实际调度目标由 Windows 图形首选项决定）。</summary>
    public const string SystemDefaultLabel = "系统默认调度 (跟随 Windows 图形首选项)";

    private readonly object _gate = new();
    private List<KeepAliveWorker> _workers = [];

    public bool IsRunning
    {
        get { lock (_gate) return _workers.Count > 0; }
    }

    public IReadOnlyList<KeepAliveStats> GetStats()
    {
        lock (_gate) return _workers.Select(w => w.Stats).ToArray();
    }

    public void Start(KeepAliveOptions options)
    {
        lock (_gate)
        {
            if (_workers.Count > 0)
                throw new InvalidOperationException("保活服务已在运行，请先调用 Stop。");

            int fps = Math.Clamp(options.Fps, 1, 240);
            int intensity = Math.Clamp(options.Intensity, 1, 3);

            if (options.AdapterLuids is { } luids)
            {
                var infos = GpuAdapterEnumerator.ListAdapters().ToDictionary(a => a.AdapterLuid);
                foreach (string luid in luids.Distinct())
                {
                    string displayName = infos.TryGetValue(luid, out var info)
                        ? $"[{info.Index}] {info.Name}"
                        : $"未知显卡 (LUID {luid})";
                    _workers.Add(new KeepAliveWorker(luid, fps, intensity, displayName));
                }
            }
            else
            {
                _workers.Add(new KeepAliveWorker(null, fps, intensity, SystemDefaultLabel));
            }

            foreach (var worker in _workers)
                worker.Start();
        }
    }

    public void Stop()
    {
        List<KeepAliveWorker> workers;
        lock (_gate)
        {
            workers = _workers;
            _workers = [];
        }
        foreach (var worker in workers)
            worker.Stop();
        foreach (var worker in workers)
            worker.Dispose();
    }

    public void Dispose() => Stop();
}
