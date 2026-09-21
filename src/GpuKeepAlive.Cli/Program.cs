using System.Runtime.InteropServices;
using System.Text;
using GpuKeepAlive.Core;

namespace GpuKeepAlive.Cli;

public static class Program
{
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll")]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out int lpMode);

    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, int dwMode);

    private const int SW_HIDE = 0;
    private const int STD_OUTPUT_HANDLE = -11;
    private const int ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

    private const string SingletonMutexName = @"Local\GpuKeepAlive.Cli";

    public static int Main(string[] args)
    {
        var adapterTokens = new List<string>();
        int targetFps = 15;
        int intensity = 1;
        bool hideWindow = false;
        bool listOnly = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i].ToLowerInvariant();
            if (arg is "--help" or "-?" or "/?")
            {
                PrintHelp();
                return 0;
            }
            if (arg is "--list" or "-l")
            {
                listOnly = true;
            }
            else if (arg is "--adapter" or "-a" && i + 1 < args.Length)
            {
                adapterTokens.AddRange(args[++i].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
            }
            else if (arg is "--fps" or "-f" && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out int f) && f > 0 && f <= 240)
                    targetFps = f;
                else
                    Console.WriteLine("警告: --fps 值无效 (须为 1~240)，使用默认值 15。");
            }
            else if (arg is "--intensity" or "-i" && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out int lvl) && lvl >= 1 && lvl <= 3)
                    intensity = lvl;
                else
                    Console.WriteLine("警告: --intensity 值无效 (须为 1~3)，使用默认值 1。");
            }
            else if (arg is "--hide" or "-h")
            {
                hideWindow = true;
            }
        }

        var adapters = GpuAdapterEnumerator.ListAdapters();

        if (listOnly)
        {
            PrintAdapters(adapters);
            return 0;
        }

        // 解析目标显卡：每项可为索引或名称关键字，允许逗号分隔与重复传参。
        // 内部以 序号+名称 标识目标。
        List<GpuAdapterId>? targetAdapters = null;
        if (adapterTokens.Count > 0)
        {
            var resolved = new List<GpuAdapterInfo>();
            var failedTokens = new List<string>();
            foreach (string token in adapterTokens)
            {
                if (int.TryParse(token, out int index) && index >= 0 && index < adapters.Count)
                {
                    resolved.Add(adapters[index]);
                    continue;
                }
                var match = adapters.FirstOrDefault(a => a.Name.Contains(token, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    resolved.Add(match);
                }
                else
                {
                    failedTokens.Add(token);
                }
            }
            resolved = resolved.DistinctBy(a => a.Id).ToList();

            foreach (string token in failedTokens)
                Console.WriteLine($"警告: 未找到匹配 \"{token}\" 的显卡，已跳过。");

            if (resolved.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("错误: 指定的所有显卡均无法匹配，未启动保活。使用 -l 查看可用显卡列表。");
                Console.ResetColor();
                return 1;
            }

            targetAdapters = [.. resolved.Select(a => a.Id)];
        }

        // 单实例锁（仅约束保活模式；--list / --help 不受限制）。
        using var singleton = new Mutex(true, SingletonMutexName, out bool createdNew);
        bool owned = createdNew;
        if (!owned)
        {
            try { owned = singleton.WaitOne(TimeSpan.Zero); }
            catch (AbandonedMutexException) { owned = true; }
        }
        if (!owned)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("错误: 已有一个 GpuKeepAliveCli 实例正在运行。如需同时保活多块显卡，请用 -a 传入逗号分隔的多个显卡。");
            Console.ResetColor();
            return 1;
        }

        string targetDescription = targetAdapters is null
            ? KeepAliveManager.SystemDefaultLabel
            : string.Join(", ", targetAdapters.Select(id => $"[{id.Index}] {id.Name}"));

        if (!Console.IsOutputRedirected)
            Console.Title = $"GPU KeepAliveCli v{AppVersion.Current}";

        Console.WriteLine("========================================================================");
        Console.WriteLine($" GPU KeepAliveCli v{AppVersion.Current} - 运行中");
        Console.WriteLine("========================================================================");
        Console.WriteLine($"保活目标: {targetDescription}");
        Console.WriteLine($"运行帧率: {targetFps} FPS");
        Console.WriteLine($"负载等级: {intensity} ({(intensity == 1 ? "轻微 Clear 渲染目标" : intensity == 2 ? "中等 全屏着色器光栅化" : "进阶 Compute 计算")})");
        Console.WriteLine($"进程PID : {Environment.ProcessId}");
        Console.WriteLine("------------------------------------------------------------------------");
        Console.WriteLine("提示: 按 Ctrl+C 或 Q 可停止保活程序。");
        Console.WriteLine("========================================================================\n");

        if (hideWindow)
        {
            IntPtr hWnd = GetConsoleWindow();
            if (hWnd != IntPtr.Zero)
                ShowWindow(hWnd, SW_HIDE);
        }

        using var manager = new KeepAliveManager();
        manager.Start(new KeepAliveOptions(targetFps, intensity, targetAdapters));

        bool stopping = false;
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stopping = true;
        };

        var renderer = new StatusRenderer();
        bool hasInput = !Console.IsInputRedirected;
        bool canDrawStatus = !hideWindow && !Console.IsOutputRedirected;
        var lastDraw = DateTime.MinValue;

        while (!stopping)
        {
            if (hasInput)
            {
                try
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true);
                        if (key.Key is ConsoleKey.Q or ConsoleKey.Escape)
                            break;
                    }
                }
                catch (IOException)
                {
                    hasInput = false;
                }
            }

            var stats = manager.GetStats();
            if (stats.Count > 0 && stats.All(s => s.Status == KeepAliveWorkerStatus.Failed))
            {
                Console.WriteLine();
                foreach (var s in stats)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"错误: {s.DisplayName} — {s.ErrorMessage}");
                    Console.ResetColor();
                }
                return 1;
            }

            if (canDrawStatus && (DateTime.UtcNow - lastDraw).TotalMilliseconds >= 500)
            {
                lastDraw = DateTime.UtcNow;
                renderer.Draw(stats.Select(FormatStatusLine).ToList());
            }

            Thread.Sleep(100);
        }

        Console.WriteLine("\n正在停止保活并清理资源...");
        manager.Stop();
        Console.WriteLine("保活程序已安全退出。");
        return 0;
    }

    private static string FormatStatusLine(KeepAliveStats s)
    {
        string name = s.DisplayName;
        if (s.DisplayName == KeepAliveManager.SystemDefaultLabel && s.ActualAdapterName is not null)
            name = $"系统默认调度 → {s.ActualAdapterName}";

        string state = s.Status switch
        {
            KeepAliveWorkerStatus.Running => $"{s.ActualFps:F1} FPS · 已运行 {s.ElapsedText} · 已提交 {s.FramesText}",
            KeepAliveWorkerStatus.Starting => "启动中…",
            KeepAliveWorkerStatus.Failed => $"失败: {s.ErrorMessage}",
            _ => "已停止",
        };
        return $"{name}  {state}";
    }

    private static void PrintAdapters(IReadOnlyList<GpuAdapterInfo> adapters)
    {
        Console.WriteLine("\n[系统检测到的显卡适配器列表]");
        if (adapters.Count == 0)
        {
            Console.WriteLine("  (未检测到可用的硬件显卡适配器)");
        }
        for (int i = 0; i < adapters.Count; i++)
        {
            var a = adapters[i];
            Console.WriteLine($"  [{i}] {a.Name}");
            Console.WriteLine($"      厂商ID: 0x{a.VendorId:X4} | 专用显存: {a.DedicatedMemoryMB} MB | 共享显存: {a.SharedMemoryMB} MB");
        }
        int hidden = GpuAdapterEnumerator.CountHiddenAdapters();
        Console.WriteLine($"\n(注: 已过滤 {hidden} 个不可保活适配器：WARP 软件渲染器及串流/远控软件创建的虚拟显示适配器)\n");
    }

    private static void PrintHelp()
    {
        Console.WriteLine($@"
========================================================================
 GPU KeepAliveCli v{AppVersion.Current} - 轻量级 GPU 3D 引擎防休眠保活工具 (命令行版)
========================================================================
用途:
  通过向指定的显卡持续提交微量 3D/Compute 渲染任务，
  使其维持活跃状态，防止因性能调度深度降频/休眠而导致的画面卡顿。

用法:
  GpuKeepAliveCli.exe [参数]

参数选项:
  --list, -l
      列出系统当前所有可用的 GPU 显卡适配器及其索引。
      (注: 已自动过滤 WARP 软件渲染器及串流/远控软件创建的虚拟显示适配器)

  --adapter <项[,项...]>, -a <项[,项...]>
      指定要保活的目标显卡，支持同时保活多块：
      - 逗号分隔多个目标 (如 -a 0,1)，参数可重复出现 (如 -a 0 -a 1)。
      - 若不指定 (默认): 交由系统 Windows 图形首选项自动调度。
        (推荐: 您可以在 Windows 设置 -> 系统 -> 屏幕 -> 图形 中将本程序的
         首选项设为'节能'或'高性能'，由系统决定调度到哪块显卡)
      - 每项若为数字: 绑定指定索引的显卡 (如 -a 1)。
      - 每项若为文本: 模糊匹配显卡名称 (如 -a 4090，不区分大小写)。
        支持的主流品牌/系列关键字: intel、arc、nvidia、geforce、rtx、amd、radeon、rx 等。
      - 部分项匹配失败: 警告并跳过，继续保活其余显卡。
      - 全部项匹配失败: 报错退出，不会回退到系统默认调度。

  --fps <数值>, -f <数值>
      渲染保活帧率 (默认: 15 FPS)。
      推荐范围: 15 ~ 60。帧率越低 CPU 开销越小，15 FPS 兼顾灵敏度与极低功耗。

  --intensity <1|2|3>, -i <1|2|3>
      负载强度等级 (默认: 1):
      1 = 轻微 (Clear 渲染目标): GPU 占用约 0.2%~0.5%，足以阻止 GPU 深度休眠。
      2 = 中等 (全屏着色器光栅化): GPU 占用约 1%~2%，模拟常规 3D 渲染管线。
      3 = 进阶 (计算着色器 CS 并行计算): GPU 占用约 3%~5%，激活完整计算单元。

  --hide, -h
      启动后自动隐藏控制台窗口，在后台静默运行。

其他说明:
  - 程序为单实例运行: 同一时间仅允许一个 GpuKeepAliveCli 保活进程，
    重复启动会被拒绝 (--list / --help 不受限制)。
  - 本程序不读取也不写入 GUI 版 (GpuKeepAlive.exe) 的用户配置文件。

示例:
  GpuKeepAliveCli.exe -l
      查看所有显卡列表及编号。

  GpuKeepAliveCli.exe
      默认模式运行 (调度目标由 Windows 图形首选项决定)。

  GpuKeepAliveCli.exe -a 0,1 -f 15 -i 1
      同时为 0、1 号两块显卡以 15 FPS、轻量模式保活。

  GpuKeepAliveCli.exe -a intel,nvidia
      按品牌关键字模糊匹配，同时保活 Intel 与 Nvidia 显卡。

  GpuKeepAliveCli.exe -a 1 -i 2 --hide
      指定索引为 1 的显卡，中等负载，后台隐藏运行。
========================================================================
");
    }

    /// <summary>
    /// 运行状态渲染器：单/多 GPU 统一按块刷新。
    /// 控制台支持 ANSI 转义时整块原地重绘；否则降级为每 3 秒追加一轮状态行。
    /// </summary>
    private sealed class StatusRenderer
    {
        private bool _firstDraw = true;
        private readonly bool _ansiEnabled;
        private DateTime _lastAppend = DateTime.MinValue;

        public StatusRenderer()
        {
            try
            {
                IntPtr handle = GetStdHandle(STD_OUTPUT_HANDLE);
                if (handle != IntPtr.Zero && GetConsoleMode(handle, out int mode))
                {
                    if (SetConsoleMode(handle, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING))
                        _ansiEnabled = true;
                }
            }
            catch
            {
                // 无控制台或权限受限时保持追加模式。
            }
        }

        public void Draw(IReadOnlyList<string> lines)
        {
            if (lines.Count == 0)
                return;

            if (_ansiEnabled && Console.WindowHeight > lines.Count)
            {
                var sb = new StringBuilder();
                if (!_firstDraw)
                    sb.Append($"\x1b[{lines.Count}A");
                foreach (string line in lines)
                    sb.Append('\r').Append(line).Append("\x1b[K\n");
                Console.Write(sb.ToString());
                _firstDraw = false;
            }
            else
            {
                if ((DateTime.UtcNow - _lastAppend).TotalSeconds < 3)
                    return;
                _lastAppend = DateTime.UtcNow;
                foreach (string line in lines)
                    Console.WriteLine(line);
            }
        }
    }
}
