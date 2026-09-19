using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Vortice.DXGI;

namespace GpuKeepAlive.Core;

public static class GpuAdapterEnumerator
{
    /// <summary>Microsoft 厂商 ID（WARP 软件渲染器，基于 CPU，为它保活无意义）。</summary>
    private const uint MicrosoftVendorId = 0x1414;

    /// <summary>GPU 性能计数器实例名中的 LUID 前缀，如 luid_0x00000000_0x0000f6b5_phys_0。</summary>
    private static readonly Regex CounterLuidRegex =
        new(@"luid_0x(?<high>[0-9A-Fa-f]{8})_0x(?<low>[0-9A-Fa-f]{8})", RegexOptions.Compiled);

    /// <summary>
    /// 枚举系统中可保活的 GPU 适配器：
    /// 过滤 WARP 软件渲染器，并过滤虚拟显示适配器（串流/远控软件创建的 IddCx 设备——
    /// 它们在 DXGI 中伪装成真实显卡，但负载实际都转发到真实 GPU 上，为它们保活毫无意义且极易误导选择）。
    /// 虚拟适配器的判定依据：其 LUID 不出现在任何 GPU 性能计数器中（与任务管理器的 GPU 列表口径一致）。
    /// 索引在过滤后连续编号，GUI 与 CLI 共用同一套编号。
    /// </summary>
    public static IReadOnlyList<GpuAdapterInfo> ListAdapters()
    {
        var adapters = new List<GpuAdapterInfo>();
        var realLuids = GetGpuCounterLuids();
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        for (uint i = 0; factory.EnumAdapters1(i, out var adapter).Success; i++)
        {
            using (adapter)
            {
                var desc = adapter.Description1;
                if (IsHidden(desc, realLuids))
                    continue;
                adapters.Add(new GpuAdapterInfo(
                    adapters.Count,
                    desc.Description,
                    desc.VendorId,
                    (long)(ulong)desc.DedicatedVideoMemory / (1024 * 1024),
                    (long)(ulong)desc.SharedSystemMemory / (1024 * 1024),
                    FormatLuid(desc.Luid)));
            }
        }
        return adapters;
    }

    /// <summary>枚举时被过滤的适配器数量（WARP + 虚拟显示适配器），用于向用户说明。</summary>
    public static int CountHiddenAdapters()
    {
        var realLuids = GetGpuCounterLuids();
        int hidden = 0;
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        for (uint i = 0; factory.EnumAdapters1(i, out var adapter).Success; i++)
        {
            using (adapter)
            {
                if (IsHidden(adapter.Description1, realLuids))
                    hidden++;
            }
        }
        return hidden;
    }

    /// <summary>
    /// 按 LUID 重新枚举并取出 DXGI 适配器 COM 对象。
    /// 返回的对象由调用方负责释放；LUID 无效（显卡列表已变化）时返回 null。
    /// </summary>
    internal static IDXGIAdapter1? OpenAdapterByLuid(string adapterLuid)
    {
        var realLuids = GetGpuCounterLuids();
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        for (uint i = 0; factory.EnumAdapters1(i, out var adapter).Success; i++)
        {
            if (IsHidden(adapter.Description1, realLuids))
            {
                adapter.Dispose();
                continue;
            }
            if (FormatLuid(adapter.Description1.Luid) == adapterLuid)
                return adapter;
            adapter.Dispose();
        }
        return null;
    }

    /// <summary>
    /// LUID 是显卡适配器的唯一标识（同型号多卡也互不相同），格式与 GPU 性能计数器
    /// 实例名 (luid_0x..._0x...) 一致，便于对照验证保活落在哪块卡上。
    /// </summary>
    public static string FormatLuid(Vortice.Luid luid)
        => luid.HighPart == 0
            ? $"0x{luid.LowPart:x8}"
            : $"0x{luid.HighPart:x8}_0x{luid.LowPart:x8}";

    private static bool IsHidden(AdapterDescription1 desc, HashSet<ulong>? realLuids)
    {
        if (desc.VendorId == MicrosoftVendorId || desc.Flags.HasFlag(AdapterFlags.Software))
            return true;
        // 计数器不可用时（异常环境）保守起见不过滤虚拟适配器。
        if (realLuids is null)
            return false;
        return !realLuids.Contains(LuidKey(desc.Luid));
    }

    /// <summary>
    /// 收集 GPU 性能计数器中出现的适配器 LUID 集合。
    /// 真实 GPU（无论是否连接显示器）都会注册计数器；虚拟显示适配器不会。
    /// 读取失败（计数器缺失/权限受限）时返回 null，调用方应跳过虚拟过滤。
    /// </summary>
    private static HashSet<ulong>? GetGpuCounterLuids()
    {
        try
        {
            if (!PerformanceCounterCategory.Exists("GPU Adapter Memory"))
                return null;
            var luids = new HashSet<ulong>();
            foreach (string instance in new PerformanceCounterCategory("GPU Adapter Memory").GetInstanceNames())
            {
                var match = CounterLuidRegex.Match(instance);
                if (!match.Success)
                    continue;
                ulong high = ulong.Parse(match.Groups["high"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                ulong low = ulong.Parse(match.Groups["low"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                luids.Add(high << 32 | low);
            }
            return luids.Count > 0 ? luids : null;
        }
        catch
        {
            return null;
        }
    }

    private static ulong LuidKey(Vortice.Luid luid)
        => (ulong)(uint)luid.HighPart << 32 | luid.LowPart;
}
