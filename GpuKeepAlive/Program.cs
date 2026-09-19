using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace GpuKeepAlive;

public class Program
{
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    private const int SW_HIDE = 0;
    private const int SW_MINIMIZE = 6;

    private const string VertexShaderSource = @"
struct VSOutput {
    float4 Pos : SV_POSITION;
    float2 UV : TEXCOORD0;
};

VSOutput VSMain(uint id : SV_VertexID) {
    VSOutput output;
    output.UV = float2((id << 1) & 2, id & 2);
    output.Pos = float4(output.UV * float2(2.0f, -2.0f) + float2(-1.0f, 1.0f), 0.0f, 1.0f);
    return output;
}
";

    private const string PixelShaderSource = @"
cbuffer TimeBuffer : register(b0) {
    float Time;
    float3 Padding;
};

struct VSOutput {
    float4 Pos : SV_POSITION;
    float2 UV : TEXCOORD0;
};

float4 PSMain(VSOutput input) : SV_Target {
    float2 uv = input.UV;
    float r = 0.5f + 0.5f * sin(uv.x * 10.0f + Time);
    float g = 0.5f + 0.5f * cos(uv.y * 10.0f + Time * 0.7f);
    float b = 0.5f + 0.5f * sin((uv.x + uv.y) * 5.0f + Time * 1.3f);
    return float4(r, g, b, 1.0f);
}
";

    private const string ComputeShaderSource = @"
cbuffer TimeBuffer : register(b0) {
    float Time;
    float3 Padding;
};

RWTexture2D<float4> OutputTexture : register(u0);

[numthreads(8, 8, 1)]
void CSMain(uint3 id : SV_DispatchThreadID) {
    float2 uv = float2(id.xy) / 512.0f;
    float r = sin(uv.x * 20.0f + Time) * cos(uv.y * 20.0f + Time);
    float g = cos(uv.x * 15.0f - Time * 0.8f);
    float b = sin(uv.y * 15.0f + Time * 1.2f);
    OutputTexture[id.xy] = float4(r, g, b, 1.0f);
}
";

    static void PrintHelp()
    {
        Console.WriteLine(@"
========================================================================
 GPU KeepAlive - 轻量级 GPU 3D 引擎防休眠保活工具
========================================================================
用途:
  通过向指定的显卡持续提交微量 3D/Compute 渲染任务，
  使其维持活跃状态，防止因性能调度深度降频/休眠而导致的画面卡顿。

用法:
  GpuKeepAlive.exe [参数]

参数选项:
  --list, -l
      列出系统当前所有可用的 GPU 显卡适配器及其索引。

  --adapter <索引或名称>, -a <索引或名称>
      指定要保活的目标显卡。
      - 若不指定 (默认 auto): 交由系统 Windows 图形首选项自动调度。
        (推荐: 您可以在 Windows 设置 -> 系统 -> 屏幕 -> 图形 中将本程序的
         首选项设为'节能'或'高性能'，由系统决定调度到哪块显卡)
      - 若指定数字: 绑定指定索引的显卡 (如 -a 1)。
      - 若指定文本: 模糊匹配显卡名称 (如 -a 4090，不区分大小写)。
        支持的主流品牌/系列关键字: intel、arc、nvidia、geforce、rtx、amd、radeon、rx 等。

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

示例:
  GpuKeepAlive.exe -l
      查看所有显卡列表及编号。

  GpuKeepAlive.exe
      默认模式运行 (调度目标由 Windows 图形首选项决定)。

  GpuKeepAlive.exe -a 1 -f 15 -i 1
      指定索引为 1 的显卡，以 15 FPS、轻量模式保活。

  GpuKeepAlive.exe -a radeon
      按品牌/系列关键字模糊匹配显卡 (如 intel、arc、nvidia、geforce、amd、radeon)。

  GpuKeepAlive.exe -a 1 -i 2 --hide
      指定索引为 1 的显卡，中等负载，后台隐藏运行。
========================================================================
");
    }

    static void Main(string[] args)
    {
        string? adapterArg = null;
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
                return;
            }
            if (arg is "--list" or "-l")
            {
                listOnly = true;
            }
            else if (arg is "--adapter" or "-a" && i + 1 < args.Length)
            {
                adapterArg = args[++i];
            }
            else if (arg is "--fps" or "-f" && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out int f) && f > 0 && f <= 240)
                    targetFps = f;
            }
            else if (arg is "--intensity" or "-i" && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out int lvl) && lvl >= 1 && lvl <= 3)
                    intensity = lvl;
            }
            else if (arg is "--hide" or "-h")
            {
                hideWindow = true;
            }
        }

        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        var adapters = new System.Collections.Generic.List<IDXGIAdapter1>();
        for (uint idx = 0; factory.EnumAdapters1(idx, out var ad).Success; idx++)
        {
            adapters.Add(ad);
        }

        if (listOnly)
        {
            Console.WriteLine("\n[系统检测到的显卡适配器列表]");
            for (int i = 0; i < adapters.Count; i++)
            {
                var desc = adapters[i].Description;
                Console.WriteLine($"  [{i}] {desc.Description}");
                Console.WriteLine($"      厂商ID: 0x{desc.VendorId:X4} | 专用显存: {desc.DedicatedVideoMemory / (1024 * 1024)} MB | 共享显存: {desc.SharedSystemMemory / (1024 * 1024)} MB");
            }
            Console.WriteLine();
            return;
        }

        IDXGIAdapter1? selectedAdapter = null;
        string adapterMatchReason = "系统自动调度 (跟随 Windows 屏幕图形首选项)";

        if (!string.IsNullOrEmpty(adapterArg))
        {
            if (int.TryParse(adapterArg, out int chosenIndex) && chosenIndex >= 0 && chosenIndex < adapters.Count)
            {
                selectedAdapter = adapters[chosenIndex];
                adapterMatchReason = $"命令行指定索引 [{chosenIndex}]";
            }
            else
            {
                string search = adapterArg.ToLowerInvariant();
                foreach (var a in adapters)
                {
                    if (a.Description.Description.ToLowerInvariant().Contains(search))
                    {
                        selectedAdapter = a;
                        adapterMatchReason = $"命令行名称匹配 \"{adapterArg}\"";
                        break;
                    }
                }
            }

            if (selectedAdapter == null)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"警告: 未找到匹配 \"{adapterArg}\" 的显卡，将使用系统默认配置。");
                Console.ResetColor();
            }
        }

        DriverType driverType = selectedAdapter == null ? DriverType.Hardware : DriverType.Unknown;
        DeviceCreationFlags creationFlags = DeviceCreationFlags.BgraSupport;

        var hr = D3D11.D3D11CreateDevice(
            selectedAdapter,
            driverType,
            creationFlags,
            new FeatureLevel[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1 },
            out ID3D11Device device,
            out ID3D11DeviceContext context
        );

        if (hr.Failure)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"错误: 无法创建 Direct3D 11 设备: {hr}");
            Console.ResetColor();
            return;
        }

        using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
        dxgiDevice.GetAdapter(out var actualAdapter);
        var actualDesc = actualAdapter.Description;

        Console.Title = $"GPU KeepAlive [{actualDesc.Description}]";
        Console.WriteLine("========================================================================");
        Console.WriteLine(" GPU KeepAlive - 运行中");
        Console.WriteLine("========================================================================");
        Console.WriteLine($"绑定显卡: {actualDesc.Description}");
        Console.WriteLine($"调度模式: {adapterMatchReason}");
        Console.WriteLine($"运行帧率: {targetFps} FPS");
        Console.WriteLine($"负载等级: 等级 {intensity} ({(intensity == 1 ? "轻微 Clear" : intensity == 2 ? "中等 2D光栅化着色" : "进阶 Compute 计算")})");
        Console.WriteLine($"进程PID : {Environment.ProcessId}");
        Console.WriteLine("------------------------------------------------------------------------");
        Console.WriteLine("提示: 按 Ctrl+C 或在窗口中按 'Q' 可停止保活程序。");
        Console.WriteLine("========================================================================\n");

        if (hideWindow)
        {
            IntPtr hWnd = GetConsoleWindow();
            if (hWnd != IntPtr.Zero)
            {
                ShowWindow(hWnd, SW_HIDE);
            }
        }

        int texSize = 512;
        var texDesc = new Texture2DDescription
        {
            Width = (uint)texSize,
            Height = (uint)texSize,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.R8G8B8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource | (intensity == 3 ? BindFlags.UnorderedAccess : BindFlags.None)
        };

        using var renderTargetTexture = device.CreateTexture2D(texDesc);
        using var rtv = device.CreateRenderTargetView(renderTargetTexture);

        ID3D11VertexShader? vs = null;
        ID3D11PixelShader? ps = null;
        ID3D11ComputeShader? cs = null;
        ID3D11UnorderedAccessView? uav = null;
        ID3D11Buffer? constantBuffer = null;

        if (intensity == 2)
        {
            var vsBytecode = Compiler.Compile(VertexShaderSource, "VSMain", "vs.hlsl", "vs_5_0", ShaderFlags.OptimizationLevel3, EffectFlags.None);
            vs = device.CreateVertexShader(vsBytecode.Span);

            var psBytecode = Compiler.Compile(PixelShaderSource, "PSMain", "ps.hlsl", "ps_5_0", ShaderFlags.OptimizationLevel3, EffectFlags.None);
            ps = device.CreatePixelShader(psBytecode.Span);

            constantBuffer = device.CreateBuffer(new BufferDescription
            {
                ByteWidth = 16,
                Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.ConstantBuffer,
                CPUAccessFlags = CpuAccessFlags.Write
            });
        }
        else if (intensity == 3)
        {
            var csBytecode = Compiler.Compile(ComputeShaderSource, "CSMain", "cs.hlsl", "cs_5_0", ShaderFlags.OptimizationLevel3, EffectFlags.None);
            cs = device.CreateComputeShader(csBytecode.Span);
            uav = device.CreateUnorderedAccessView(renderTargetTexture);

            constantBuffer = device.CreateBuffer(new BufferDescription
            {
                ByteWidth = 16,
                Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.ConstantBuffer,
                CPUAccessFlags = CpuAccessFlags.Write
            });
        }

        bool running = true;
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            running = false;
        };

        long frameIntervalTicks = Stopwatch.Frequency / targetFps;
        var stopwatch = Stopwatch.StartNew();
        long nextFrameTicks = stopwatch.ElapsedTicks;

        long frameCount = 0;
        float simTime = 0.0f;
        float dt = 1.0f / targetFps;

        var statsWatch = Stopwatch.StartNew();
        long lastStatFrames = 0;

        bool hasConsole = !Console.IsInputRedirected;

        while (running)
        {
            if (hasConsole)
            {
                try
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true);
                        if (key.Key is ConsoleKey.Q or ConsoleKey.Escape)
                        {
                            break;
                        }
                    }
                }
                catch
                {
                    hasConsole = false;
                }
            }

            simTime += dt;
            frameCount++;

            if (intensity == 1)
            {
                float r = 0.5f + 0.5f * MathF.Sin(simTime);
                float g = 0.5f + 0.5f * MathF.Cos(simTime * 0.7f);
                context.ClearRenderTargetView(rtv, new Color4(r, g, 0.5f, 1.0f));
            }
            else if (intensity == 2 && vs != null && ps != null && constantBuffer != null)
            {
                var mapped = context.Map(constantBuffer, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
                Marshal.StructureToPtr(new Vector4(simTime, 0, 0, 0), mapped.DataPointer, false);
                context.Unmap(constantBuffer, 0);

                context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
                context.IASetInputLayout(null);
                context.VSSetShader(vs);
                context.PSSetShader(ps);
                context.PSSetConstantBuffer(0, constantBuffer);
                context.RSSetViewport(new Viewport(0, 0, texSize, texSize));
                context.OMSetRenderTargets(rtv);

                context.Draw(3, 0);
            }
            else if (intensity == 3 && cs != null && uav != null && constantBuffer != null)
            {
                var mapped = context.Map(constantBuffer, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
                Marshal.StructureToPtr(new Vector4(simTime, 0, 0, 0), mapped.DataPointer, false);
                context.Unmap(constantBuffer, 0);

                context.CSSetShader(cs);
                context.CSSetConstantBuffer(0, constantBuffer);
                context.CSSetUnorderedAccessView(0, uav);

                context.Dispatch((uint)(texSize / 8), (uint)(texSize / 8), 1);
            }

            context.Flush();

            if (!hideWindow && statsWatch.ElapsedMilliseconds >= 3000)
            {
                double elapsedSec = statsWatch.Elapsed.TotalSeconds;
                double actualFps = (frameCount - lastStatFrames) / elapsedSec;
                lastStatFrames = frameCount;
                statsWatch.Restart();

                Console.Write($"\r[运行状态] 运行时间: {stopwatch.Elapsed:hh\\:mm\\:ss} | 已提交: {frameCount} 帧 | 实时帧率: {actualFps:F1} FPS   ");
            }

            nextFrameTicks += frameIntervalTicks;
            long currentTicks = stopwatch.ElapsedTicks;
            long sleepMs = (nextFrameTicks - currentTicks) * 1000 / Stopwatch.Frequency;
            if (sleepMs > 1)
            {
                Thread.Sleep((int)sleepMs);
            }
            else if (sleepMs < -frameIntervalTicks * 2)
            {
                nextFrameTicks = stopwatch.ElapsedTicks;
            }
        }

        Console.WriteLine("\n正在清理资源并退出...");
        vs?.Dispose();
        ps?.Dispose();
        cs?.Dispose();
        uav?.Dispose();
        constantBuffer?.Dispose();
        rtv.Dispose();
        renderTargetTexture.Dispose();
        context.Dispose();
        device.Dispose();
        foreach (var a in adapters)
        {
            a.Dispose();
        }
        Console.WriteLine("保活程序已安全退出。");
    }
}
